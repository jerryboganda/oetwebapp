import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';

// Test the exported utilities from api.ts by importing them
// We need to mock firebase before importing api
vi.mock('../firebase', () => ({
  auth: null,
  isFirebaseConfigured: false,
}));

// Import after mocks
const { ApiError } = await import('../api');

describe('ApiError', () => {
  it('creates an error with all fields', () => {
    const error = new ApiError(400, 'validation_error', 'Invalid input', false, [
      { field: 'email', code: 'required', message: 'Email is required' },
    ]);

    expect(error).toBeInstanceOf(Error);
    expect(error.name).toBe('ApiError');
    expect(error.status).toBe(400);
    expect(error.code).toBe('validation_error');
    expect(error.retryable).toBe(false);
    expect(error.fieldErrors).toHaveLength(1);
    expect(error.fieldErrors[0].field).toBe('email');
  });

  it('maps known error codes to user-friendly messages', () => {
    const error = new ApiError(409, 'draft_version_conflict', 'Conflict', false);
    expect(error.userMessage).toBe('Your draft was updated in another tab. Please refresh and try again.');
  });

  it('maps rate_limited to friendly message', () => {
    const error = new ApiError(429, 'rate_limited', 'Too many requests', true);
    expect(error.userMessage).toBe('Too many requests. Please wait a moment and try again.');
  });

  it('falls back to server message for unknown codes', () => {
    const error = new ApiError(500, 'some_custom_error', 'Server exploded', true);
    expect(error.userMessage).toBe('Server exploded');
  });

  it('defaults fieldErrors to empty array', () => {
    const error = new ApiError(500, 'internal_server_error', 'fail', false);
    expect(error.fieldErrors).toEqual([]);
  });
});

describe('API retry logic', () => {
  const originalFetch = globalThis.fetch;

  afterEach(() => {
    globalThis.fetch = originalFetch;
  });

  it('retries on 500 errors up to MAX_RETRIES times', async () => {
    let callCount = 0;
    globalThis.fetch = vi.fn(async () => {
      callCount++;
      return new Response(JSON.stringify({ code: 'internal_server_error', message: 'fail', retryable: true }), {
        status: 500,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchUserProfile } = await import('../api');

    try {
      await fetchUserProfile();
    } catch (e) {
      // expected to fail after all retries exhausted
    }

    // Should have been called 3 times (1 initial + 2 retries)
    expect(callCount).toBe(3);
  }, 15000);

  it('does not retry on 4xx errors', async () => {
    let callCount = 0;
    globalThis.fetch = vi.fn(async () => {
      callCount++;
      return new Response(JSON.stringify({ code: 'not_found', message: 'Not found', retryable: false }), {
        status: 404,
        headers: { 'content-type': 'application/json' },
      });
    });

    const { fetchUserProfile } = await import('../api');

    try {
      await fetchUserProfile();
    } catch (e) {
      expect(e).toBeInstanceOf(ApiError);
      expect((e as InstanceType<typeof ApiError>).status).toBe(404);
    }

    expect(callCount).toBe(1);
  });

  it('retries on network errors', async () => {
    let callCount = 0;
    globalThis.fetch = vi.fn(async () => {
      callCount++;
      throw new TypeError('Failed to fetch');
    });

    const { fetchUserProfile } = await import('../api');

    try {
      await fetchUserProfile();
    } catch (e) {
      expect(e).toBeInstanceOf(ApiError);
      expect((e as InstanceType<typeof ApiError>).code).toBe('network_error');
    }

    expect(callCount).toBe(3);
  }, 15000);
});
