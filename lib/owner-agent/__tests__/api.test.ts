import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockRequest } = vi.hoisted(() => ({ mockRequest: vi.fn() }));

vi.mock('@/lib/api', () => ({
  apiClient: { request: (...args: unknown[]) => mockRequest(...args) },
}));

import {
  buildSessionsQuery,
  connectEngine,
  createSession,
  decideApproval,
  describeOwnerAgentError,
  getAudit,
  getStatus,
  isOwnerAgentLockError,
  isOwnerAgentLockMessage,
  isOwnerAgentSessionId,
  isOwnerAgentUserSessionId,
  listSessions,
  logoutEngine,
  patchSession,
  putGithubTokens,
  sendMessage,
  shipSession,
} from '../api';
import { isUnlocked, resetUnlockStoreForTests, setUnlocked } from '../unlock-store';
import { SYSTEM_QUEUE_SESSION_ID } from '../types';

const SESSION = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';

function lastCall(): { path: string; init: RequestInit; options: Record<string, unknown> } {
  const call = mockRequest.mock.calls[mockRequest.mock.calls.length - 1] ?? [];
  return { path: call[0] as string, init: call[1] as RequestInit, options: call[2] as Record<string, unknown> };
}

function sentHeaderNames(): string[] {
  return mockRequest.mock.calls.flatMap((call) => {
    const headers = (call[1] as RequestInit | undefined)?.headers;
    if (!headers) return [];
    return Object.keys(headers as Record<string, string>).map((name) => name.toLowerCase());
  });
}

describe('owner-agent REST client', () => {
  beforeEach(() => {
    mockRequest.mockReset();
    mockRequest.mockResolvedValue({ ok: true });
    resetUnlockStoreForTests();
    setUnlocked({ expiresAt: new Date(Date.now() + 60 * 60_000).toISOString() });
  });

  afterEach(() => {
    resetUnlockStoreForTests();
  });

  it('relies on the unlock cookie: credentials included, no unlock header, retries disabled', async () => {
    await getStatus();
    expect(lastCall().path).toBe('/v1/owner-agent/status');
    expect(lastCall().init.method).toBe('GET');
    expect(lastCall().init.credentials).toBe('include');
    expect(lastCall().init.headers).toBeUndefined();
    expect(lastCall().options).toMatchObject({ maxRetries: 0 });
  });

  it('sends no step-up header anywhere — the former step-up actions need only the unlock', async () => {
    await patchSession(SESSION, { mode: 'autopilot' });
    expect(lastCall().init.method).toBe('PATCH');
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ mode: 'autopilot' });

    await shipSession(SESSION, { prTitle: 'Fix' });
    expect(lastCall().path).toBe(`/v1/owner-agent/sessions/${SESSION}/ship`);

    await connectEngine('claude');
    expect(lastCall().path).toBe('/v1/owner-agent/auth/claude/connect');
    expect(lastCall().init.body).toBeUndefined();

    await connectEngine('opencode', { providerId: 'github-copilot', methodIndex: 0 });
    expect(lastCall().path).toBe('/v1/owner-agent/auth/opencode/connect');
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ providerId: 'github-copilot', methodIndex: 0 });

    await logoutEngine('codex');
    expect(lastCall().path).toBe('/v1/owner-agent/auth/codex/logout');

    await putGithubTokens({ agentToken: ' agent-token-fixture ' });
    expect(lastCall().init.method).toBe('PUT');
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ agentToken: 'agent-token-fixture' });

    await createSession({ engine: 'claude', model: 'opaque', mode: 'autopilot' });
    expect(lastCall().path).toBe('/v1/owner-agent/sessions');

    expect(mockRequest).toHaveBeenCalledTimes(7);
    for (const call of mockRequest.mock.calls) {
      expect((call[1] as RequestInit).credentials).toBe('include');
      expect(String(call[0])).not.toContain('/step-up');
    }
    expect(sentHeaderNames().filter((name) => name.startsWith('x-owner-agent'))).toEqual([]);
  });

  it('encodes the session list filters and paging cursor', async () => {
    mockRequest.mockResolvedValueOnce([]);
    await listSessions();
    expect(lastCall().path).toBe('/v1/owner-agent/sessions?includeArchived=false');

    mockRequest.mockResolvedValueOnce([{ id: SESSION }]);
    const rows = await listSessions({
      q: '  fix T3 & deploy?  ',
      engine: 'codex',
      status: 'archived',
      includeArchived: true,
      before: '2026-09-27T10:00:00.000Z',
      limit: 50,
    });
    expect(rows).toEqual([{ id: SESSION }]);
    const url = new URL(lastCall().path, 'https://app.example.test');
    expect(url.pathname).toBe('/v1/owner-agent/sessions');
    expect(Object.fromEntries(url.searchParams)).toEqual({
      q: 'fix T3 & deploy?',
      engine: 'codex',
      status: 'archived',
      includeArchived: 'true',
      before: '2026-09-27T10:00:00.000Z',
      limit: '50',
    });
  });

  it('drops invalid list params instead of sending them', () => {
    const query = new URLSearchParams(buildSessionsQuery({
      q: 'x'.repeat(250),
      engine: 'gpt' as never,
      status: 'deleted' as never,
      before: 'not-a-date',
      limit: 5000,
    }));
    expect(query.get('q')).toHaveLength(100);
    expect(query.has('engine')).toBe(false);
    expect(query.has('status')).toBe(false);
    expect(query.has('before')).toBe(false);
    expect(query.get('limit')).toBe('200');
    expect(new URLSearchParams(buildSessionsQuery({ q: '   ', limit: 0 })).toString()).toBe('includeArchived=false&limit=1');
  });

  it('reads the audit page shape and tolerates a bare array', async () => {
    mockRequest.mockResolvedValueOnce({ items: [{ id: 'a1', occurredAt: '2026-09-27T10:00:00Z', action: 'unlock' }], chainIntact: true });
    await expect(getAudit(9999)).resolves.toEqual({
      items: [{ id: 'a1', occurredAt: '2026-09-27T10:00:00Z', action: 'unlock' }],
      chainIntact: true,
    });
    expect(lastCall().path).toBe('/v1/owner-agent/audit?take=500');

    mockRequest.mockResolvedValueOnce([{ id: 'a2', occurredAt: '2026-09-27T10:00:00Z', action: 'lock' }]);
    await expect(getAudit()).resolves.toMatchObject({ items: [{ id: 'a2' }], chainIntact: false });

    mockRequest.mockResolvedValueOnce(undefined);
    await expect(getAudit()).resolves.toEqual({ items: [], chainIntact: false });
  });

  it('recognises every API unlock failure code, including inside hub error text', () => {
    for (const code of [
      'owner_agent_unlock_required',
      'owner_agent_unlock_invalid',
      'owner_agent_unlock_expired',
      'owner_agent_unlock_revoked',
      'owner_agent_unlock_session_mismatch',
      'owner_agent_session_revoked',
    ]) {
      expect(isOwnerAgentLockError({ code })).toBe(true);
      expect(isOwnerAgentLockMessage(`An error occurred on the server while streaming results. HubException: ${code}`)).toBe(true);
    }
    expect(isOwnerAgentLockError({ code: 'owner_agent_step_up_required' })).toBe(false);
    expect(isOwnerAgentLockMessage('owner_agent_disabled')).toBe(false);
  });

  it('validates ids before building URLs', async () => {
    expect(isOwnerAgentSessionId(SESSION)).toBe(true);
    expect(isOwnerAgentSessionId('../../admin')).toBe(false);
    expect(isOwnerAgentSessionId(SYSTEM_QUEUE_SESSION_ID)).toBe(true);
    expect(isOwnerAgentUserSessionId(SYSTEM_QUEUE_SESSION_ID)).toBe(false);
    expect(isOwnerAgentUserSessionId(SESSION)).toBe(true);
    await expect(sendMessage('../etc', { text: 'hi' })).rejects.toThrow('Invalid session id.');
    // The system queue only accepts approval decisions.
    await expect(sendMessage(SYSTEM_QUEUE_SESSION_ID, { text: 'hi' })).rejects.toThrow('Invalid session id.');
    await expect(decideApproval(SESSION, 'a/b', { decision: 'approve', nonce: 'n' })).rejects.toThrow('Invalid approval id.');
    expect(mockRequest).not.toHaveBeenCalled();
  });

  it('posts approval decisions with the nonce, including for the system queue', async () => {
    const approvalId = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2F';
    await decideApproval(SYSTEM_QUEUE_SESSION_ID, approvalId, { decision: 'approve_session', nonce: 'nonce-1', note: '  ok  ' });
    expect(lastCall().path).toBe(`/v1/owner-agent/sessions/${SYSTEM_QUEUE_SESSION_ID}/approvals/${approvalId}`);
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ decision: 'approve_session', nonce: 'nonce-1', note: 'ok' });
  });

  it('passes per-turn model and effort with a message', async () => {
    await sendMessage(SESSION, { text: 'do it', model: 'opaque-model', effort: 'opaque-effort' });
    expect(lastCall().path).toBe(`/v1/owner-agent/sessions/${SESSION}/messages`);
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ text: 'do it', model: 'opaque-model', effort: 'opaque-effort' });
  });

  it('clears the unlock state when the API reports it locked', async () => {
    mockRequest.mockRejectedValueOnce(Object.assign(new Error('locked'), { status: 403, code: 'owner_agent_unlock_required' }));
    await expect(getStatus()).rejects.toThrow('locked');
    expect(isUnlocked()).toBe(false);
  });

  it('keeps a newer unlock when a request started under the old one comes back locked', async () => {
    let reject: (error: unknown) => void = () => undefined;
    mockRequest.mockImplementationOnce(() => new Promise((_resolve, rej) => { reject = rej; }));
    const pending = getStatus();
    // The owner unlocks again while the old request is in flight.
    setUnlocked({ expiresAt: new Date(Date.now() + 59 * 60_000).toISOString() });
    reject(Object.assign(new Error('locked'), { status: 403, code: 'owner_agent_unlock_expired' }));
    await expect(pending).rejects.toThrow('locked');
    expect(isUnlocked()).toBe(true);
  });

  it('explains pass-through sidecar statuses the shared client cannot parse', () => {
    const passThrough = Object.assign(new Error('Request failed: 423'), { status: 423, code: 'unknown_error', userMessage: 'Request failed: 423' });
    expect(describeOwnerAgentError(passThrough)).toMatch(/kill switch|draining/);
    const flat = Object.assign(new Error('x'), { status: 409, code: 'session_busy', userMessage: 'Session is busy.' });
    expect(describeOwnerAgentError(flat)).toBe('Session is busy.');
  });

  it('explains a vague-request triage conflict by its code, for the first message and follow-ups alike', async () => {
    const TRIAGE_COPY = 'The request is too vague to triage - add the task, files and expected result, then send again.';
    const conflict = Object.assign(
      new Error('Jev could not establish the task and impact. Clarify the request before continuing.'),
      { status: 409, code: 'jev_review_required', userMessage: 'Jev could not establish the task and impact. Clarify the request before continuing.' },
    );

    expect(describeOwnerAgentError(conflict)).toBe(TRIAGE_COPY);
    expect(describeOwnerAgentError(conflict, 'Message was not sent.')).toBe(TRIAGE_COPY);

    mockRequest.mockRejectedValueOnce(conflict);
    const created = createSession({ engine: 'claude', model: 'opaque', mode: 'guarded', initialMessage: 'make it better somehow' });
    await expect(created).rejects.toBe(conflict);
    expect(lastCall().path).toBe('/v1/owner-agent/sessions');

    mockRequest.mockRejectedValueOnce(conflict);
    await expect(sendMessage(SESSION, { text: 'make it better somehow' })).rejects.toBe(conflict);
    expect(lastCall().path).toBe(`/v1/owner-agent/sessions/${SESSION}/messages`);

    // A triage conflict is not a lock failure, and other 409s keep their own text.
    expect(isOwnerAgentLockError(conflict)).toBe(false);
    expect(isUnlocked()).toBe(true);
    const busy = Object.assign(new Error('Request failed: 409'), { status: 409, code: 'unknown_error', userMessage: 'Request failed: 409' });
    expect(describeOwnerAgentError(busy)).toMatch(/busy/);
  });

  it('keeps the unlock state on unrelated failures', async () => {
    mockRequest.mockRejectedValueOnce(Object.assign(new Error('busy'), { status: 429, code: 'rate_limited' }));
    await expect(getStatus()).rejects.toThrow('busy');
    expect(isUnlocked()).toBe(true);
  });
});
