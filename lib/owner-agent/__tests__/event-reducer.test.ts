import { describe, expect, it } from 'vitest';
import {
  applyAgentEvents,
  createInitialRenderModel,
  sessionRenderReducer,
  type ConsoleItem,
  type SessionRenderModel,
} from '../event-reducer';
import type { AgentEvent } from '../types';

const SESSION = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';

let clock = 0;
function ev(seq: number, type: string, data: object, turnId = 'turn-1'): AgentEvent {
  clock += 1;
  return { seq, sessionId: SESSION, turnId, ts: new Date(Date.UTC(2026, 8, 27, 10, 0, clock)).toISOString(), type, data };
}

function apply(events: AgentEvent[], model: SessionRenderModel = createInitialRenderModel(SESSION)): SessionRenderModel {
  return sessionRenderReducer(model, { type: 'events', events });
}

function itemsOfKind<K extends ConsoleItem['kind']>(model: SessionRenderModel, kind: K) {
  return model.items.filter((item): item is Extract<ConsoleItem, { kind: K }> => item.kind === kind);
}

const approval = {
  approvalId: 'ap-1',
  nonce: 'nonce-1',
  toolCallId: 'tc-1',
  summary: 'Run psql',
  command: 'psql -c "DELETE FROM scratch WHERE true"',
  cwd: '/workspace/sessions/x',
  uid: 10002,
  target: 'oet-postgres',
  reasons: ['DELETE without a real WHERE'],
  tainted: false,
  expiresAt: '2026-09-27T10:10:00.000Z',
};

describe('owner-agent event reducer', () => {
  it('streams assistant text deltas into one message and finalises it', () => {
    let model = apply([
      ev(1, 'turn_started', { model: 'opaque-model', effort: 'high', mode: 'guarded' }),
      ev(2, 'user_message', { text: 'hello' }),
      ev(3, 'text_delta', { messageId: 'm1', text: 'Hel' }),
    ]);
    model = apply([ev(4, 'text_delta', { messageId: 'm1', text: 'lo ' }), ev(5, 'text_delta', { messageId: 'm1', text: 'there' })], model);

    const [assistant] = itemsOfKind(model, 'assistant');
    expect(assistant.text).toBe('Hello there');
    expect(assistant.streaming).toBe(true);
    expect(model.running).toBe(true);
    expect(model.mode).toBe('guarded');

    model = apply([ev(6, 'text', { messageId: 'm1', text: 'Hello there!' }), ev(7, 'turn_complete', { status: 'ok', durationMs: 1200 })], model);
    const [final] = itemsOfKind(model, 'assistant');
    expect(itemsOfKind(model, 'assistant')).toHaveLength(1);
    expect(final.text).toBe('Hello there!');
    expect(final.streaming).toBe(false);
    expect(model.running).toBe(false);
    expect(model.lastTurnStatus).toBe('ok');
    expect(model.items.map((item) => item.kind)).toEqual(['turn', 'user', 'assistant', 'turn_complete']);
  });

  it('groups consecutive thinking deltas and closes the block when other output starts', () => {
    const model = apply([
      ev(1, 'thinking_delta', { text: 'Plan: ' }),
      ev(2, 'thinking_delta', { text: 'read files' }),
      ev(3, 'text_delta', { messageId: 'm1', text: 'Done.' }),
      ev(4, 'thinking_delta', { text: 'second thought' }),
    ]);
    const thinking = itemsOfKind(model, 'thinking');
    expect(thinking).toHaveLength(2);
    expect(thinking[0].text).toBe('Plan: read files');
    expect(thinking[0].streaming).toBe(false);
    expect(thinking[1].text).toBe('second thought');
    expect(thinking[1].streaming).toBe(true);
  });

  it('accumulates streamed tool output and replaces it with the result', () => {
    let model = apply([
      ev(1, 'tool_call', {
        toolCallId: 'tc-1',
        name: 'Bash',
        input: { command: 'ls' },
        command: 'ls -la',
        cwd: '/workspace',
        classification: { destructive: false, unparseable: false, reasons: [] },
      }),
      ev(2, 'tool_output_delta', { toolCallId: 'tc-1', text: 'a\n' }),
      ev(3, 'tool_output_delta', { toolCallId: 'tc-1', text: 'b\n' }),
    ]);
    let [tool] = itemsOfKind(model, 'tool');
    expect(tool.streamedOutput).toBe('a\nb\n');
    expect(tool.status).toBe('running');
    expect(tool.command).toBe('ls -la');

    model = apply([ev(4, 'tool_result', { toolCallId: 'tc-1', ok: false, output: 'boom', exitCode: 2 })], model);
    [tool] = itemsOfKind(model, 'tool');
    expect(itemsOfKind(model, 'tool')).toHaveLength(1);
    expect(tool.status).toBe('error');
    expect(tool.result).toEqual({ ok: false, output: 'boom', exitCode: 2 });
  });

  it('creates a tool card when output arrives before the call and marks unfinished tools at turn end', () => {
    const model = apply([
      ev(1, 'tool_output_delta', { toolCallId: 'tc-9', text: 'early' }),
      ev(2, 'tool_call', { toolCallId: 'tc-9', name: 'Read', input: { path: 'x' } }),
      ev(3, 'turn_complete', { status: 'interrupted', durationMs: 5 }),
    ]);
    const [tool] = itemsOfKind(model, 'tool');
    expect(tool.name).toBe('Read');
    expect(tool.streamedOutput).toBe('early');
    expect(tool.status).toBe('no_result');
    expect(model.lastTurnStatus).toBe('interrupted');
  });

  it('tracks pending approvals and resolves them', () => {
    let model = apply([ev(1, 'approval_request', approval)]);
    expect(model.pendingApprovals.map((a) => a.approvalId)).toEqual(['ap-1']);
    expect(itemsOfKind(model, 'approval')[0].request.command).toBe(approval.command);

    model = apply([ev(2, 'approval_resolved', { approvalId: 'ap-1', decision: 'deny', by: 'owner' })], model);
    expect(model.pendingApprovals).toEqual([]);
    expect(model.resolvedApprovals['ap-1']).toEqual({ decision: 'deny', by: 'owner' });
    const [card] = itemsOfKind(model, 'approval');
    expect(card.resolution).toMatchObject({ decision: 'deny', by: 'owner', seq: 2 });
  });

  it('does not re-open an approval that was already resolved when the request is replayed', () => {
    const model = apply([
      ev(1, 'approval_resolved', { approvalId: 'ap-1', decision: 'approve', by: 'autopilot' }),
      ev(2, 'approval_request', approval),
    ]);
    expect(model.pendingApprovals).toEqual([]);
    expect(itemsOfKind(model, 'approval')[0].resolution).toMatchObject({ decision: 'approve', by: 'autopilot' });
  });

  it('de-duplicates replayed events by seq after a reconnect', () => {
    const first = apply([
      ev(1, 'user_message', { text: 'go' }),
      ev(2, 'text_delta', { messageId: 'm1', text: 'one ' }),
      ev(3, 'text_delta', { messageId: 'm1', text: 'two ' }),
      ev(4, 'usage', { model: 'opaque-model', inputTokens: 10, outputTokens: 5, costUsd: 0.01 }),
    ]);
    // The stream resumes with afterSeq=3 but the server (or a racing resubscribe) re-sends 2..4.
    const replayed = apply(
      [
        ev(2, 'text_delta', { messageId: 'm1', text: 'one ' }),
        ev(3, 'text_delta', { messageId: 'm1', text: 'two ' }),
        ev(4, 'usage', { model: 'opaque-model', inputTokens: 10, outputTokens: 5, costUsd: 0.01 }),
        ev(5, 'text_delta', { messageId: 'm1', text: 'three' }),
      ],
      first,
    );
    expect(itemsOfKind(replayed, 'assistant')[0].text).toBe('one two three');
    expect(itemsOfKind(replayed, 'user')).toHaveLength(1);
    expect(replayed.usage.inputTokens).toBe(10);
    expect(replayed.lastSeq).toBe(5);
  });

  it('returns the same object for heartbeats and pure duplicates', () => {
    const model = apply([ev(1, 'user_message', { text: 'x' })]);
    const heartbeat = { sessionId: SESSION, ts: new Date().toISOString(), type: 'heartbeat', data: {} } as unknown as AgentEvent;
    expect(applyAgentEvents(model, [heartbeat])).toBe(model);
    expect(applyAgentEvents(model, [ev(1, 'user_message', { text: 'x' })])).toBe(model);
  });

  it('ignores events for another session and unknown event types', () => {
    const foreign = { ...ev(1, 'user_message', { text: 'other' }), sessionId: '01J9ZQ3V4W5X6Y7Z8A9B0C1D2F' };
    const model = apply([foreign, ev(2, 'future_event_type', { anything: true })]);
    expect(model.items).toEqual([]);
    expect(model.lastSeq).toBe(2);
  });

  it('records taint for the turn and resets it when the next turn starts', () => {
    let model = apply([
      ev(1, 'turn_started', { model: 'm', mode: 'autopilot' }),
      ev(2, 'taint', { reason: 'read learner rows', source: 'db' }),
    ]);
    expect(model.tainted).toBe(true);
    expect(model.taints).toHaveLength(1);
    model = apply([ev(3, 'turn_complete', { status: 'ok', durationMs: 1 }), ev(4, 'turn_started', { model: 'm', mode: 'autopilot' }, 'turn-2')], model);
    expect(model.tainted).toBe(false);
    expect(model.taints).toHaveLength(1);
  });

  it('aggregates usage per model, rate limits, mode changes, snapshots, file changes and the ship log', () => {
    const model = apply([
      ev(1, 'usage', { model: 'a', inputTokens: 100, outputTokens: 10, cacheReadTokens: 50 }),
      ev(2, 'usage', { model: 'b', inputTokens: 1, outputTokens: 2, costUsd: 0.5 }),
      ev(3, 'rate_limit', { engine: 'codex', limits: [{ label: '5h', status: 'warning', usedPercent: 80 }] }),
      ev(4, 'mode_changed', { mode: 'guarded', reason: 'lease_expired' }),
      ev(5, 'snapshot', { label: 'agent-snap-1', ok: true, file: 'x.dump' }),
      ev(6, 'file_change', { path: 'README.md', changeKind: 'modify', diff: '+a' }),
      ev(7, 'ship', { phase: 'deploying', message: 'watching run', level: 'info' }),
      ev(8, 'error', { code: 'engine_error', message: 'bad' }),
    ]);
    expect(model.usage.inputTokens).toBe(101);
    expect(model.usage.cacheReadTokens).toBe(50);
    expect(model.usage.costUsd).toBe(0.5);
    expect(model.usage.byModel.a.costUsd).toBeNull();
    expect(model.rateLimits.codex?.[0]).toMatchObject({ label: '5h', status: 'warning' });
    expect(model.mode).toBe('guarded');
    expect(model.fileChanges.map((f) => f.path)).toEqual(['README.md']);
    expect(model.shipLog).toEqual([expect.objectContaining({ phase: 'deploying', message: 'watching run', level: 'info' })]);
    expect(model.lastError).toEqual({ code: 'engine_error', message: 'bad' });
    expect(model.items.map((item) => item.kind)).toEqual(['mode_changed', 'snapshot', 'file_change', 'error']);
  });

  it('resets to an empty model for a new session', () => {
    const model = apply([ev(1, 'user_message', { text: 'x' })]);
    const reset = sessionRenderReducer(model, { type: 'reset', sessionId: 'other' });
    expect(reset.sessionId).toBe('other');
    expect(reset.items).toEqual([]);
    expect(reset.lastSeq).toBe(0);
  });
});
