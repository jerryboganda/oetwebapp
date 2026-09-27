import { describe, expect, it } from 'vitest';
import {
  apiKeySourceProblem,
  assessClaudeAuth,
  AsyncQueue,
  buildClaudeEnv,
  ClaudeMessageMapper,
  computeTurnCost,
  hookOutputFor,
  mapClaudeModels,
  mapClaudeRateLimit,
  normalizeUtilization,
  permissionModeFor,
  toolCallRequestFromHook,
  turnStatusFor,
} from '../../src/engines/claude.js';
import type { EngineEvent } from '../../src/engines/types.js';

const paths = { homeDir: '/home/agent', configDir: '/home/agent/.claude' };

describe('env hygiene', () => {
  it('strips API-key and third-party provider variables and pins the client app', () => {
    const env = buildClaudeEnv(
      {
        PATH: '/usr/bin',
        HOME: '/home/agent',
        ANTHROPIC_API_KEY: 'test-not-a-key',
        ANTHROPIC_BASE_URL: 'https://example.invalid',
        CLAUDE_CODE_USE_BEDROCK: '1',
        CLAUDE_CODE_OAUTH_TOKEN: 'test-not-a-token',
        OWNER_AGENT_INTERNAL_TOKEN: 'test-not-a-token',
        OPENAI_API_KEY: 'test-not-a-key',
        HTTPS_PROXY: 'http://01J8Z0000000000000000000S1:x@oet-agent-egress:3128',
      },
      paths,
    );
    expect(env).toEqual({
      PATH: '/usr/bin',
      HOME: '/home/agent',
      HTTPS_PROXY: 'http://01J8Z0000000000000000000S1:x@oet-agent-egress:3128',
      CLAUDE_CONFIG_DIR: '/home/agent/.claude',
      DISABLE_AUTOUPDATER: '1',
      CLAUDE_AGENT_SDK_CLIENT_APP: 'oet-agent-console',
    });
  });

  it('keeps an explicit CLAUDE_CONFIG_DIR / HOME from src/env.ts', () => {
    const env = buildClaudeEnv({ HOME: '/h', CLAUDE_CONFIG_DIR: '/h/.c' }, paths);
    expect(env['HOME']).toBe('/h');
    expect(env['CLAUDE_CONFIG_DIR']).toBe('/h/.c');
  });
});

describe('modes', () => {
  it('uses dontAsk for read_only and default otherwise — never bypass/acceptEdits', () => {
    expect(permissionModeFor('read_only')).toBe('dontAsk');
    expect(permissionModeFor('guarded')).toBe('default');
    expect(permissionModeFor('autopilot')).toBe('default');
  });
});

describe('supportedModels() mapping', () => {
  it('maps effort levels, keeps ids opaque and de-duplicates', () => {
    expect(
      mapClaudeModels([
        { value: 'default', displayName: 'Default (recommended)', description: 'Opus for complex work', supportsEffort: true, supportedEffortLevels: ['low', 'medium', 'high', 'xhigh', 'max'] },
        { value: 'haiku', displayName: 'Haiku', description: 'Fast', supportsEffort: false },
        { value: 'sonnet[1m]', displayName: 'Sonnet (1M context)', description: '', supportedEffortLevels: ['low', 'high'] },
        { value: 'default', displayName: 'dup' },
        { displayName: 'no value' },
      ]),
    ).toEqual([
      {
        value: 'default',
        displayName: 'Default (recommended)',
        description: 'Opus for complex work',
        supportsEffort: true,
        efforts: ['low', 'medium', 'high', 'xhigh', 'max'],
        defaultEffort: 'high',
      },
      { value: 'haiku', displayName: 'Haiku', description: 'Fast', supportsEffort: false, efforts: [] },
      { value: 'sonnet[1m]', displayName: 'Sonnet (1M context)', supportsEffort: true, efforts: ['low', 'high'], defaultEffort: 'high' },
    ]);
    expect(mapClaudeModels(undefined)).toEqual([]);
  });
});

describe('subscription-auth assertion', () => {
  const max = { loggedIn: true, authMethod: 'claude.ai', apiProvider: 'firstParty', email: 'owner@example.test', orgName: 'OET', subscriptionType: 'max' };

  it('accepts first-party claude.ai subscription auth', () => {
    expect(assessClaudeAuth(max, { email: 'owner@example.test', organization: 'OET', subscriptionType: 'max', apiProvider: 'firstParty', apiKeySource: 'none' })).toEqual({
      state: 'signed_in',
      account: { plan: 'max', email: 'owner@example.test', workspace: 'OET' },
    });
    expect(assessClaudeAuth(max)).toEqual({ state: 'signed_in', account: { plan: 'max', email: 'owner@example.test', workspace: 'OET' } });
  });

  it('is signed_out when claude auth status says so', () => {
    expect(assessClaudeAuth({ loggedIn: false })).toEqual({ state: 'signed_out' });
  });

  it('refuses API keys, console auth, third-party providers and missing subscriptions', () => {
    expect(assessClaudeAuth(max, { apiKeySource: 'ANTHROPIC_API_KEY', subscriptionType: 'max' })).toMatchObject({ state: 'error', detail: expect.stringMatching(/API key/) });
    expect(assessClaudeAuth({ ...max, authMethod: 'console' })).toMatchObject({ state: 'error', detail: expect.stringMatching(/not a Claude subscription/) });
    expect(assessClaudeAuth(max, { apiProvider: 'bedrock' })).toMatchObject({ state: 'error', detail: expect.stringMatching(/third-party provider/) });
    const { subscriptionType: _drop, ...noPlan } = max;
    expect(assessClaudeAuth(noPlan, {})).toMatchObject({ state: 'error', detail: expect.stringMatching(/no active Claude subscription/) });
  });

  it('classifies apiKeySource values', () => {
    expect(apiKeySourceProblem(undefined)).toBeUndefined();
    expect(apiKeySourceProblem('none')).toBeUndefined();
    expect(apiKeySourceProblem('user')).toMatch(/API key/);
    expect(apiKeySourceProblem('apiKeyHelper')).toMatch(/API key/);
  });
});

describe('rate_limit_event mapping', () => {
  it('maps status, window labels, utilization and reset time', () => {
    expect(mapClaudeRateLimit({ status: 'allowed', resetsAt: 1_790_000_000, rateLimitType: 'five_hour' })).toEqual([
      { label: '5-hour window', status: 'ok', resetsAt: new Date(1_790_000_000_000).toISOString() },
    ]);
    expect(mapClaudeRateLimit({ status: 'allowed_warning', resetsAt: 1_790_000_000, rateLimitType: 'seven_day', utilization: 0.83 })).toEqual([
      { label: '7-day window', status: 'warning', usedPercent: 83, resetsAt: new Date(1_790_000_000_000).toISOString() },
    ]);
    expect(mapClaudeRateLimit({ status: 'rejected', rateLimitType: 'seven_day_opus', utilization: 1 })).toEqual([
      { label: '7-day window (Opus)', status: 'limited', usedPercent: 100 },
    ]);
    expect(mapClaudeRateLimit({ status: 'allowed', rateLimitType: 'brand_new_window', utilization: 0.95 })).toEqual([
      { label: 'brand new window', status: 'warning', usedPercent: 95 },
    ]);
    expect(mapClaudeRateLimit({})).toEqual([]);
  });

  it('includes per-window detail when the CLI sends unifiedWindows', () => {
    const limits = mapClaudeRateLimit({
      status: 'allowed',
      rateLimitType: 'five_hour',
      unifiedWindows: { five_hour: { utilization: 0.1, resetsAt: 1_790_000_000 }, seven_day: { utilization: 0.5 } },
    });
    expect(limits).toEqual([
      { label: '5-hour window', status: 'ok', usedPercent: 10, resetsAt: new Date(1_790_000_000_000).toISOString() },
      { label: '7-day window', status: 'ok', usedPercent: 50 },
    ]);
  });

  it('normalises utilization fractions and percentages', () => {
    expect(normalizeUtilization(0.456)).toBe(46);
    expect(normalizeUtilization(46)).toBe(46);
    expect(normalizeUtilization(-1)).toBeUndefined();
    expect(normalizeUtilization('x')).toBeUndefined();
  });
});

describe('PreToolUse hook mapping', () => {
  it('builds the Guard request from hook input (Bash and file writes)', () => {
    expect(
      toolCallRequestFromHook(
        { hook_event_name: 'PreToolUse', session_id: 's', cwd: '/workspace/sessions/S1', tool_name: 'Bash', tool_input: { command: 'git push origin agent/x', description: 'push' } },
        'toolu_1',
      ),
    ).toEqual({
      toolCallId: 'toolu_1',
      name: 'Bash',
      input: { command: 'git push origin agent/x', description: 'push' },
      command: 'git push origin agent/x',
      cwd: '/workspace/sessions/S1',
    });
    expect(toolCallRequestFromHook({ tool_name: 'Edit', tool_input: { file_path: '/workspace/sessions/S1/.github/workflows/deploy.yml' } }, 'toolu_2')).toMatchObject({
      name: 'Edit',
      writePaths: ['/workspace/sessions/S1/.github/workflows/deploy.yml'],
    });
    expect(toolCallRequestFromHook({ tool_name: 'NotebookEdit', tool_input: { notebook_path: '/w/a.ipynb' } }, 'toolu_3')?.writePaths).toEqual(['/w/a.ipynb']);
    expect(toolCallRequestFromHook({ tool_name: 'Read', tool_input: { file_path: '/w/a' } }, undefined)?.toolCallId).toMatch(/^hook-/);
    expect(toolCallRequestFromHook({ hook_event_name: 'PostToolUse', tool_name: 'Bash' }, 'x')).toBeUndefined();
    expect(toolCallRequestFromHook({}, 'x')).toBeUndefined();
  });

  it('maps Guard decisions onto permissionDecision allow/deny', () => {
    expect(hookOutputFor({ behavior: 'allow' })).toEqual({
      hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: 'allow', permissionDecisionReason: 'Approved by the OET console guard.' },
    });
    expect(hookOutputFor({ behavior: 'deny', message: 'Destructive command denied by the owner.' })).toEqual({
      hookSpecificOutput: { hookEventName: 'PreToolUse', permissionDecision: 'deny', permissionDecisionReason: 'Destructive command denied by the owner.' },
    });
    expect(hookOutputFor({ behavior: 'deny', message: '' })).toMatchObject({ hookSpecificOutput: { permissionDecisionReason: 'Denied by the OET console guard.' } });
  });
});

describe('per-turn cost', () => {
  it('uses deltas of the running total and handles unknown baselines and resets', () => {
    expect(computeTurnCost(0, 0.12)).toEqual({ costUsd: 0.12, baseline: 0.12 });
    expect(computeTurnCost(0.12, 0.2)).toEqual({ costUsd: 0.08, baseline: 0.2 });
    expect(computeTurnCost(undefined, 1.5)).toEqual({ costUsd: undefined, baseline: 1.5 });
    expect(computeTurnCost(1.5, 0.3)).toEqual({ costUsd: 0.3, baseline: 0.3 });
    expect(computeTurnCost(0.4, undefined)).toEqual({ costUsd: undefined, baseline: 0.4 });
  });

  it('maps result subtypes to turn statuses', () => {
    const base = { isError: false, inputTokens: 0, outputTokens: 0, cacheReadTokens: 0, errors: [] };
    expect(turnStatusFor({ ...base, subtype: 'success' }, false)).toBe('ok');
    expect(turnStatusFor({ ...base, subtype: 'success', isError: true }, false)).toBe('error');
    expect(turnStatusFor({ ...base, subtype: 'error_max_turns' }, false)).toBe('max_turns');
    expect(turnStatusFor({ ...base, subtype: 'error_during_execution' }, false)).toBe('error');
    expect(turnStatusFor({ ...base, subtype: 'error_during_execution' }, true)).toBe('interrupted');
  });
});

describe('SDK message → EngineEvent mapping', () => {
  const streamEvent = (event: Record<string, unknown>) => ({ type: 'stream_event', event, parent_tool_use_id: null, session_id: 'sess-1', uuid: 'u' });

  it('streams text/thinking deltas and numbers text blocks consistently with final messages', () => {
    const emitted = new Set<string>();
    const mapper = new ClaudeMessageMapper(emitted);
    const events: EngineEvent[] = [];
    const push = (m: unknown) => events.push(...mapper.handle(m).events);

    push(streamEvent({ type: 'message_start', message: { id: 'msg_A' } }));
    push(streamEvent({ type: 'content_block_start', index: 0, content_block: { type: 'thinking' } }));
    push(streamEvent({ type: 'content_block_delta', index: 0, delta: { type: 'thinking_delta', thinking: 'Plan: check git.' } }));
    push(streamEvent({ type: 'content_block_delta', index: 1, delta: { type: 'text_delta', text: 'Checking' } }));
    push(streamEvent({ type: 'content_block_delta', index: 1, delta: { type: 'text_delta', text: ' now.' } }));
    push({ type: 'assistant', parent_tool_use_id: null, session_id: 'sess-1', message: { id: 'msg_A', content: [{ type: 'thinking', thinking: 'Plan: check git.' }] } });
    push({ type: 'assistant', parent_tool_use_id: null, session_id: 'sess-1', message: { id: 'msg_A', content: [{ type: 'text', text: 'Checking now.' }] } });
    push(streamEvent({ type: 'content_block_delta', index: 2, delta: { type: 'input_json_delta', partial_json: '{"com' } }));
    push({
      type: 'assistant',
      parent_tool_use_id: null,
      session_id: 'sess-1',
      message: { id: 'msg_A', content: [{ type: 'tool_use', id: 'toolu_1', name: 'Bash', input: { command: 'git status' } }] },
    });
    push(streamEvent({ type: 'content_block_delta', index: 3, delta: { type: 'text_delta', text: 'Second block' } }));
    push({ type: 'assistant', parent_tool_use_id: null, session_id: 'sess-1', message: { id: 'msg_A', content: [{ type: 'text', text: 'Second block' }] } });

    expect(events).toEqual([
      { type: 'thinking_delta', data: { text: 'Plan: check git.' } },
      { type: 'text_delta', data: { messageId: 'msg_A:0', text: 'Checking' } },
      { type: 'text_delta', data: { messageId: 'msg_A:0', text: ' now.' } },
      { type: 'text', data: { messageId: 'msg_A:0', text: 'Checking now.' } },
      { type: 'tool_call', data: { toolCallId: 'toolu_1', name: 'Bash', input: { command: 'git status' }, command: 'git status' } },
      { type: 'text_delta', data: { messageId: 'msg_A:1', text: 'Second block' } },
      { type: 'text', data: { messageId: 'msg_A:1', text: 'Second block' } },
    ]);
    expect(emitted.has('toolu_1')).toBe(true);
  });

  it('emits unstreamed thinking once, skips subagent text but keeps subagent tool calls', () => {
    const mapper = new ClaudeMessageMapper(new Set(['toolu_known']));
    expect(mapper.handle({ type: 'assistant', parent_tool_use_id: null, message: { id: 'msg_B', content: [{ type: 'thinking', thinking: 'Summary of thinking' }] } }).events).toEqual([
      { type: 'thinking_delta', data: { text: 'Summary of thinking' } },
    ]);
    const sub = mapper.handle({
      type: 'assistant',
      parent_tool_use_id: 'toolu_task',
      message: {
        id: 'msg_C',
        content: [
          { type: 'text', text: 'subagent chatter' },
          { type: 'tool_use', id: 'toolu_sub', name: 'Read', input: { file_path: '/w/a.ts' } },
          { type: 'tool_use', id: 'toolu_known', name: 'Bash', input: { command: 'ls' } },
        ],
      },
    }).events;
    expect(sub).toEqual([{ type: 'tool_call', data: { toolCallId: 'toolu_sub', name: 'Read', input: { file_path: '/w/a.ts' } } }]);
  });

  it('maps tool results and file changes (capped output)', () => {
    const mapper = new ClaudeMessageMapper(new Set());
    mapper.handle({
      type: 'assistant',
      parent_tool_use_id: null,
      message: { id: 'msg_D', content: [{ type: 'tool_use', id: 'toolu_w', name: 'Write', input: { file_path: '/w/new.md', content: 'x' } }] },
    });
    mapper.handle({
      type: 'assistant',
      parent_tool_use_id: null,
      message: { id: 'msg_D', content: [{ type: 'tool_use', id: 'toolu_b', name: 'Bash', input: { command: 'cat big.log' } }] },
    });
    expect(
      mapper.handle({
        type: 'user',
        message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: 'toolu_w', content: 'File created successfully', is_error: false }] },
        tool_use_result: { type: 'create', filePath: '/w/new.md' },
      }).events,
    ).toEqual([
      { type: 'tool_result', data: { toolCallId: 'toolu_w', ok: true, output: 'File created successfully' } },
      { type: 'file_change', data: { path: '/w/new.md', changeKind: 'add' } },
    ]);
    const big = mapper.handle({
      type: 'user',
      message: { role: 'user', content: [{ type: 'tool_result', tool_use_id: 'toolu_b', content: [{ type: 'text', text: 'z'.repeat(70_000) }], is_error: true }] },
    }).events;
    expect(big).toHaveLength(1);
    const result = big[0] as { data: { ok: boolean; output: string } };
    expect(result.data.ok).toBe(false);
    expect(Buffer.byteLength(result.data.output)).toBeLessThan(66_000);
    expect(result.data.output).toMatch(/…\[truncated \d+ bytes\]$/);
  });

  it('surfaces session ids, init apiKeySource, rate limits and results', () => {
    const mapper = new ClaudeMessageMapper(new Set());
    expect(mapper.handle({ type: 'system', subtype: 'init', session_id: 'sess-9', apiKeySource: 'none', model: 'x' })).toEqual({
      events: [],
      sessionId: 'sess-9',
      init: { apiKeySource: 'none' },
    });
    const rate = mapper.handle({ type: 'rate_limit_event', session_id: 'sess-9', rate_limit_info: { status: 'allowed_warning', rateLimitType: 'five_hour', utilization: 0.9 } });
    expect(rate.rateLimits).toEqual([{ label: '5-hour window', status: 'warning', usedPercent: 90 }]);
    expect(rate.events).toEqual([{ type: 'rate_limit', data: { engine: 'claude', limits: rate.rateLimits } }]);
    const result = mapper.handle({
      type: 'result',
      subtype: 'success',
      is_error: false,
      session_id: 'sess-9',
      total_cost_usd: 0.42,
      result: 'Done',
      usage: { input_tokens: 1_000, output_tokens: 250, cache_read_input_tokens: 800, cache_creation_input_tokens: 50 },
    });
    expect(result.result).toEqual({
      subtype: 'success',
      isError: false,
      totalCostUsd: 0.42,
      inputTokens: 1_000,
      outputTokens: 250,
      cacheReadTokens: 800,
      errors: [],
      resultText: 'Done',
    });
  });

  it('records assistant API errors for the TurnResult instead of emitting them', () => {
    const mapper = new ClaudeMessageMapper(new Set());
    const mapped = mapper.handle({
      type: 'assistant',
      parent_tool_use_id: null,
      error: 'rate_limit',
      message: { id: 'msg_E', content: [{ type: 'text', text: 'API Error: rate limited' }] },
    });
    expect(mapped.events.map((e) => e.type)).toEqual(['text']);
    expect(mapper.lastError).toEqual({ code: 'claude_rate_limit', message: 'API Error: rate limited' });
  });
});

describe('AsyncQueue', () => {
  it('delivers pushed items in order and ends on close', async () => {
    const queue = new AsyncQueue<number>();
    const iterator = queue[Symbol.asyncIterator]();
    const pending = iterator.next();
    queue.push(1);
    queue.push(2);
    await expect(pending).resolves.toEqual({ value: 1, done: false });
    await expect(iterator.next()).resolves.toEqual({ value: 2, done: false });
    const waiting = iterator.next();
    queue.close();
    await expect(waiting).resolves.toEqual({ value: undefined, done: true });
    expect(() => queue.push(3)).toThrow(/closed/);
  });
});
