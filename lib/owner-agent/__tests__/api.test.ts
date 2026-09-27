import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockRequest } = vi.hoisted(() => ({ mockRequest: vi.fn() }));

vi.mock('@/lib/api', () => ({
  apiClient: { request: (...args: unknown[]) => mockRequest(...args) },
}));

import {
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
  logoutEngine,
  patchSession,
  putGithubTokens,
  resetOwnerAgentApiForTests,
  sendMessage,
  shipSession,
} from '../api';
import { getUnlockTicket, resetUnlockStoreForTests, setUnlock } from '../unlock-store';
import { SYSTEM_QUEUE_SESSION_ID } from '../types';

const SESSION = '01J9ZQ3V4W5X6Y7Z8A9B0C1D2E';

function lastCall(): { path: string; init: RequestInit; options: Record<string, unknown> } {
  const call = mockRequest.mock.calls[mockRequest.mock.calls.length - 1] ?? [];
  return { path: call[0] as string, init: call[1] as RequestInit, options: call[2] as Record<string, unknown> };
}

function headers(): Record<string, string> {
  return (lastCall().init.headers ?? {}) as Record<string, string>;
}

describe('owner-agent REST client', () => {
  beforeEach(() => {
    mockRequest.mockReset();
    mockRequest.mockResolvedValue({ ok: true });
    resetUnlockStoreForTests();
    setUnlock({
      ticket: 'unlock-fixture',
      expiresAt: new Date(Date.now() + 45 * 60_000).toISOString(),
      absoluteExpiresAt: new Date(Date.now() + 8 * 60 * 60_000).toISOString(),
    });
  });

  afterEach(() => {
    resetOwnerAgentApiForTests();
    resetUnlockStoreForTests();
  });

  it('attaches the unlock ticket to every call and disables retries', async () => {
    await getStatus();
    expect(lastCall().path).toBe('/v1/owner-agent/status');
    expect(lastCall().init.method).toBe('GET');
    expect(headers()['X-Owner-Agent-Unlock']).toBe('unlock-fixture');
    expect(headers()['X-Owner-Agent-StepUp']).toBeUndefined();
    expect(lastCall().options).toMatchObject({ maxRetries: 0 });
  });

  it('sends the step-up token only where the contract requires it', async () => {
    await patchSession(SESSION, { mode: 'autopilot' }, 'step-up-fixture');
    expect(lastCall().init.method).toBe('PATCH');
    expect(headers()['X-Owner-Agent-StepUp']).toBe('step-up-fixture');

    await patchSession(SESSION, { mode: 'guarded' });
    expect(headers()['X-Owner-Agent-StepUp']).toBeUndefined();

    await shipSession(SESSION, { prTitle: 'Fix' }, 'step-up-ship');
    expect(lastCall().path).toBe(`/v1/owner-agent/sessions/${SESSION}/ship`);
    expect(headers()['X-Owner-Agent-StepUp']).toBe('step-up-ship');

    await connectEngine('claude', 'step-up-connect');
    expect(lastCall().path).toBe('/v1/owner-agent/auth/claude/connect');
    expect(headers()['X-Owner-Agent-StepUp']).toBe('step-up-connect');

    await logoutEngine('codex', 'step-up-logout');
    expect(lastCall().path).toBe('/v1/owner-agent/auth/codex/logout');

    await putGithubTokens({ agentToken: ' agent-token-fixture ' }, 'step-up-tokens');
    expect(lastCall().init.method).toBe('PUT');
    expect(JSON.parse(String(lastCall().init.body))).toEqual({ agentToken: 'agent-token-fixture' });
  });

  it('refuses protected actions without a step-up token', async () => {
    await expect(patchSession(SESSION, { mode: 'autopilot' })).rejects.toThrow(/authenticator/i);
    await expect(shipSession(SESSION, {}, '')).rejects.toThrow(/authenticator/i);
    await expect(connectEngine('claude', '')).rejects.toThrow(/authenticator/i);
    await expect(putGithubTokens({ shipToken: 'x' }, '')).rejects.toThrow(/authenticator/i);
    expect(mockRequest).not.toHaveBeenCalled();
  });

  it('requires a step-up to create a session directly in Autopilot', async () => {
    await expect(
      createSession({ engine: 'claude', model: 'opaque', mode: 'autopilot' }),
    ).rejects.toThrow(/Autopilot.*authenticator/);
    expect(mockRequest).not.toHaveBeenCalled();

    await createSession({ engine: 'claude', model: 'opaque', mode: 'autopilot' }, 'step-up-create');
    expect(lastCall().path).toBe('/v1/owner-agent/sessions');
    expect(headers()['X-Owner-Agent-StepUp']).toBe('step-up-create');

    await createSession({ engine: 'codex', model: 'opaque', mode: 'guarded' });
    expect(headers()['X-Owner-Agent-StepUp']).toBeUndefined();
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

  it('drops the unlock ticket when the API reports it locked', async () => {
    mockRequest.mockRejectedValueOnce(Object.assign(new Error('locked'), { status: 403, code: 'owner_agent_unlock_required' }));
    await expect(getStatus()).rejects.toThrow('locked');
    expect(getUnlockTicket()).toBeNull();
  });

  it('explains pass-through sidecar statuses the shared client cannot parse', () => {
    const passThrough = Object.assign(new Error('Request failed: 423'), { status: 423, code: 'unknown_error', userMessage: 'Request failed: 423' });
    expect(describeOwnerAgentError(passThrough)).toMatch(/kill switch|draining/);
    const flat = Object.assign(new Error('x'), { status: 409, code: 'session_busy', userMessage: 'Session is busy.' });
    expect(describeOwnerAgentError(flat)).toBe('Session is busy.');
  });

  it('keeps the ticket on unrelated failures', async () => {
    mockRequest.mockRejectedValueOnce(Object.assign(new Error('busy'), { status: 429, code: 'rate_limited' }));
    await expect(getStatus()).rejects.toThrow('busy');
    expect(getUnlockTicket()).toBe('unlock-fixture');
  });
});
