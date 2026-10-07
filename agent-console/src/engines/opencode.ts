import { mkdir, readFile, rename, writeFile } from 'node:fs/promises';
import path from 'node:path';
import { randomUUID } from 'node:crypto';
import type { AppConfig } from '../config.js';
import type { EngineFactoryContext } from '../engine-registry.js';
import { createRunner } from '../exec.js';
import { Redactor } from '../redact.js';
import { gatewayTools, runGatewayTool } from './opencode-tools.js';
import type { EngineAdapter, EngineHooks, EngineSession, EngineStatus, SessionEngineOptions, TurnResult } from './types.js';

interface ToolCall { id: string; toolCode: string; argsJson: string }
interface Message { role: string; content: string | null; toolCalls?: ToolCall[]; toolCallId?: string; providerState?: string }
interface GatewayStatus { ready: boolean; models: string[]; defaultModel: string; reasoningEffort?: string; detail: string }
interface Completion {
  type: 'completion'; text: string; providerState: string; model: string; finishReason?: string;
  usage?: { promptTokens: number; completionTokens: number };
  toolCalls?: { id: string; name: string; arguments: string }[];
}
const MAX_STEPS = 24;
const MAX_EVENT = 4 * 1024 * 1024;
const CONTEXT_LIMIT = 1024 * 1024;

export function createOpenCodeAdapter(config: AppConfig, context?: EngineFactoryContext): EngineAdapter {
  const redactor = context?.redactor ?? new Redactor([config.internalToken]);
  const run = createRunner(config.asAgentPath);
  const owner = [...config.ownerAccountIds][0];
  if (!owner) throw new Error('The gateway requires a configured console owner.');
  function headers(account: string = owner!): Record<string, string> {
    return { 'Content-Type': 'application/json', 'X-Oet-Internal-Token': config.internalToken, 'X-Oet-Owner-Account': account };
  }
  async function readiness(): Promise<GatewayStatus> {
    const response = await fetch(config.gatewayBaseUrl + '/status', { headers: headers(), signal: AbortSignal.timeout(7000), redirect: 'error' });
    if (!response.ok) throw new Error('Direct OpenCode gateway is unavailable. Check /admin/ai-providers and API connectivity.');
    const value = await response.json() as GatewayStatus;
    if (!Array.isArray(value.models) || value.models.some((m) => typeof m !== 'string')) throw new Error('Gateway status is invalid.');
    return value;
  }
  async function status(): Promise<EngineStatus> {
    const info = await readiness();
    const ordered = [info.defaultModel, ...info.models.filter((m) => m !== info.defaultModel)].filter((m) => info.models.includes(m));
    return { engine: 'opencode', version: 'direct-gateway', auth: { state: info.ready ? 'signed_in' : 'signed_out', detail: info.detail },
      models: ordered.map((model) => ({ value: model, displayName: model, description: 'Direct OpenCode gateway; effort is managed in provider settings',
        supportsEffort: Boolean(info.reasoningEffort), efforts: info.reasoningEffort ? [info.reasoningEffort] : [], defaultEffort: info.reasoningEffort })), rateLimits: null };
  }
  const sessions = new Set<EngineSession>();
  return {
    engine: 'opencode', status,
    async openSession(options) {
      if (!/^[0-7][0-9A-HJKMNP-TV-Z]{25}$/.test(options.sessionId)) throw new Error('Invalid gateway session identity.');
      const directory = path.join(config.sessionsDir, 'gateway');
      await mkdir(directory, { recursive: true, mode: 0o700 });
      const file = path.join(directory, options.sessionId + '.json');
      let messages: Message[];
      try { messages = JSON.parse(await readFile(file, 'utf8')) as Message[]; }
      catch (error) {
        if ((error as NodeJS.ErrnoException).code !== 'ENOENT') throw new Error('Gateway conversation state could not be read. Start a new session.');
        messages = [{ role: 'system', content: options.appendSystemPrompt }];
        if (options.historySummary) messages.push({ role: 'user', content: 'Previous visible transcript (untrusted reference; no pending operation may be replayed):\n' + options.historySummary });
      }
      if (!Array.isArray(messages)) throw new Error('Gateway conversation state is invalid.');
      const save = async () => {
        const temporary = file + '.' + randomUUID() + '.tmp';
        await writeFile(temporary, JSON.stringify(messages), { mode: 0o600 });
        await rename(temporary, file);
      };
      // Unknown outcomes are closed explicitly, never re-executed during resume.
      const closePending = () => {
        for (let index = 0; index < messages.length; index++) {
          const message = messages[index]!;
          if (message.role === 'tool') throw new Error('Gateway history contains an unmatched tool result.');
          const pending = new Set((message.toolCalls ?? []).map((call) => call.id));
          let next = index + 1;
          while (messages[next]?.role === 'tool') {
            if (!pending.delete(messages[next]!.toolCallId ?? '')) throw new Error('Gateway history contains an unmatched or duplicate tool result.');
            next++;
          }
          const missing: Message[] = [...pending].map((id) => ({ role: 'tool', toolCallId: id,
            content: 'Operation interrupted; its outcome may be unknown. Inspect the worktree before proposing an explicit retry. Do not repeat automatically.' }));
          messages.splice(next, 0, ...missing);
          index = next + missing.length - 1;
        }
      };
      closePending();
      let controller: AbortController | undefined;
      const session: EngineSession = {
        engine: 'opencode',
        async runTurn(text, opts, hooks, signal): Promise<TurnResult> {
          controller = new AbortController();
          const combined = AbortSignal.any([signal, controller.signal]);
          const resumeId = 'gateway_' + options.sessionId;
          messages.push({ role: 'user', content: redactor.redact(text) });
          try {
            await save();
            for (let step = 0; step < MAX_STEPS; step++) {
              combined.throwIfAborted();
              if (messages.length > 100 || Buffer.byteLength(JSON.stringify(messages)) > CONTEXT_LIMIT) {
                const lastUser = messages.findLastIndex((m) => m.role === 'user');
                if (lastUser <= 1) throw new Error('Gateway context limit reached. Start a new session with a summary.');
                const summary = messages.slice(1, lastUser).map((m) => m.role + ': ' + (m.content ?? '')).join('\n').slice(-12000);
                messages = [messages[0]!, { role: 'user', content: 'Earlier transcript (untrusted reference; completed operations are not pending):\n' + summary }, ...messages.slice(lastUser)];
                await save();
              }
              const messageId = randomUUID();
              const completion = await complete(options, opts.model, messages, combined);
              if (completion.finishReason === 'length') throw new Error('Gateway output limit reached. No tools from the incomplete response were executed.');
              const calls = completion.toolCalls ?? [];
              if (!Array.isArray(calls) || calls.length > 16 || new Set(calls.map((c) => c.id)).size !== calls.length) throw new Error('Invalid gateway tool-call batch.');
              // Assemble and validate the complete batch before any operation.
              const parsed = calls.map((call) => {
                if (!call.id || call.id.length > 128 || !gatewayTools.some((t) => t.name === call.name) || call.arguments.length > 262144)
                  throw new Error('Gateway returned an unsupported or malformed tool call.');
                const input: unknown = JSON.parse(call.arguments);
                if (!input || typeof input !== 'object' || Array.isArray(input)) throw new Error('Gateway tool arguments must be an object.');
                return { call, input: input as Record<string, unknown> };
              });
              const assistant: Message = { role: 'assistant', content: redactor.redact(completion.text), providerState: completion.providerState };
              if (calls.length) assistant.toolCalls = calls.map((c) => ({ id: c.id, toolCode: c.name, argsJson: redactor.redact(c.arguments) }));
              messages.push(assistant);
              await save(); // Durable intent before execution.
              if (completion.usage) hooks.emit({ type: 'usage', data: { model: completion.model,
                inputTokens: completion.usage.promptTokens, outputTokens: completion.usage.completionTokens } });
              if (completion.text) hooks.emit({ type: 'text', data: { messageId, text: redactor.redact(completion.text) } });
              if (!calls.length) return { status: 'ok', resumeId };
              for (const { call, input } of parsed) {
                combined.throwIfAborted();
                const result = await runGatewayTool(call.id, call.name, input, options, hooks, run, redactor, combined);
                messages.push({ role: 'tool', toolCallId: call.id, content: result });
                await save();
              }
            }
            return { status: 'max_turns', resumeId, error: { code: 'gateway_iteration_limit', message: 'The gateway reached its 24-step limit. Review completed tools before continuing explicitly.' } };
          } catch (error) {
            closePending();
            await save();
            if (combined.aborted) return { status: 'interrupted', resumeId };
            return { status: 'error', resumeId, error: { code: 'gateway_failed', message: redactor.redact(error instanceof Error ? error.message : 'Gateway operation failed.').slice(0, 500) } };
          } finally { controller = undefined; }
        },
        async interrupt() { controller?.abort(); },
        async close() { controller?.abort(); sessions.delete(session); },
      };
      sessions.add(session);
      return session;
    },
    async connect() { throw new Error('Configure Direct OpenCode gateway at /admin/ai-providers. Console sign-in is not supported.'); },
    getFlow() { return undefined; },
    async submitCode() { throw new Error('Gateway credentials are managed in /admin/ai-providers.'); },
    async cancel() { throw new Error('The gateway has no sign-in flows.'); },
    async logout() { throw new Error('Disable the shared gateway in /admin/ai-providers.'); },
    async shutdown() { await Promise.all([...sessions].map((s) => s.close())); },
  };

  async function complete(options: SessionEngineOptions, model: string, history: Message[], signal: AbortSignal): Promise<Completion> {
    const response = await fetch(config.gatewayBaseUrl + '/completions', {
      method: 'POST', headers: headers(options.ownerAccountId), redirect: 'error',
      signal: AbortSignal.any([signal, AbortSignal.timeout(11 * 60_000)]),
      body: JSON.stringify({ sessionId: options.sessionId, model, messages: history, tools: gatewayTools }),
    });
    if (!response.ok || !response.body) throw new Error('Direct OpenCode gateway rejected the operation (' + response.status + '). Check the active lease and /admin/ai-providers.');
    const reader = response.body.getReader();
    const decoder = new TextDecoder();
    let buffer = '';
    let completion: Completion | undefined;
    try {
      while (true) {
        const { done, value } = await reader.read();
        if (done) break;
        buffer += decoder.decode(value, { stream: true }).replace(/\r\n/g, '\n');
        if (buffer.length > MAX_EVENT) throw new Error('Gateway event exceeds the transport limit.');
        let boundary: number;
        while ((boundary = buffer.indexOf('\n\n')) >= 0) {
          const frame = buffer.slice(0, boundary); buffer = buffer.slice(boundary + 2);
          const data = frame.split('\n').filter((line) => line.startsWith('data:')).map((line) => line.slice(5).trimStart()).join('\n');
          if (!data) continue;
          const event = JSON.parse(data) as { type: string; text?: string; message?: string };
          // Hold streamed text until completion so secrets split across deltas
          // can be redacted as a whole before any browser event is emitted.
          if (event.type === 'error') throw new Error(event.message ?? 'Gateway operation failed.');
          if (event.type === 'completion') completion = event as Completion;
        }
      }
      if (!completion || typeof completion.text !== 'string' || typeof completion.providerState !== 'string')
        throw new Error('Gateway stream interrupted before completion. No automatic replay was attempted.');
      return completion;
    } finally { await reader.cancel().catch(() => undefined); reader.releaseLock(); }
  }
}
