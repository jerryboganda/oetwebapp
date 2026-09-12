import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

import { clearStepUpTokens, getStepUpToken, isStepUpRequiredError, requestStepUp } from '@/lib/api/step-up';

function jsonResponse(payload: unknown, status = 200): Response {
  return new Response(JSON.stringify(payload), {
    status,
    headers: { 'Content-Type': 'application/json' },
  });
}

describe('step-up token cache', () => {
  beforeEach(() => {
    clearStepUpTokens();
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-09-12T10:00:00Z'));
  });

  afterEach(() => {
    clearStepUpTokens();
    vi.useRealTimers();
    vi.restoreAllMocks();
  });

  it('returns null before any proof has been requested', () => {
    expect(getStepUpToken('billing.refund')).toBeNull();
  });

  it('caches a proof per scope and does not leak it across scopes', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({
          stepUpToken: 'tok-refund',
          expiresAt: new Date('2026-09-12T10:05:00Z').toISOString(),
          scope: 'billing.refund',
        }),
      ),
    );

    await requestStepUp('billing.refund', '123456');

    expect(getStepUpToken('billing.refund')).toBe('tok-refund');
    expect(getStepUpToken('billing.mark_paid')).toBeNull();
  });

  it('treats a proof as expired inside the safety margin', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({
          stepUpToken: 'tok-refund',
          expiresAt: new Date('2026-09-12T10:05:00Z').toISOString(),
          scope: 'billing.refund',
        }),
      ),
    );

    await requestStepUp('billing.refund', '123456');
    expect(getStepUpToken('billing.refund')).toBe('tok-refund');

    vi.setSystemTime(new Date('2026-09-12T10:04:55Z'));
    expect(getStepUpToken('billing.refund')).toBeNull();
  });

  it('posts the code and scope to the step-up endpoint', async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        stepUpToken: 'tok',
        expiresAt: new Date('2026-09-12T10:05:00Z').toISOString(),
        scope: 'billing.mark_paid',
      }),
    );
    vi.stubGlobal('fetch', fetchMock);

    await requestStepUp('billing.mark_paid', '654321');

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(String(url)).toContain('/v1/auth/step-up');
    expect(init.method).toBe('POST');
    expect(JSON.parse(String(init.body))).toEqual({ code: '654321', scope: 'billing.mark_paid' });
  });

  it('caches nothing when the code is rejected', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockResolvedValue(
        jsonResponse({ code: 'step_up_invalid_code', message: 'Bad code', retryable: false }, 403),
      ),
    );

    await expect(requestStepUp('billing.refund', '000000')).rejects.toBeTruthy();
    expect(getStepUpToken('billing.refund')).toBeNull();
  });

  it('clears every cached scope', async () => {
    vi.stubGlobal(
      'fetch',
      vi.fn().mockImplementation(async () =>
        jsonResponse({
          stepUpToken: 'tok',
          expiresAt: new Date('2026-09-12T10:05:00Z').toISOString(),
          scope: 'billing.refund',
        }),
      ),
    );

    await requestStepUp('billing.refund', '123456');
    await requestStepUp('billing.mark_paid', '123456');

    clearStepUpTokens();

    expect(getStepUpToken('billing.refund')).toBeNull();
    expect(getStepUpToken('billing.mark_paid')).toBeNull();
  });
});

describe('isStepUpRequiredError', () => {
  it('recognises the step-up error code', () => {
    expect(isStepUpRequiredError(Object.assign(new Error('nope'), { code: 'step_up_required' }))).toBe(true);
  });

  it('rejects other errors and non-errors', () => {
    expect(isStepUpRequiredError(Object.assign(new Error('nope'), { code: 'forbidden' }))).toBe(false);
    expect(isStepUpRequiredError(new Error('plain'))).toBe(false);
    expect(isStepUpRequiredError(null)).toBe(false);
    expect(isStepUpRequiredError('step_up_required')).toBe(false);
  });
});
