import { describe, expect, it } from 'vitest';
import { getQueryClient, shouldRetryQuery } from '../query-provider';

function apiError(retryable: boolean, status = 503): Error {
  // Same shape lib/api/client.ts gives ApiError (name + retryable); the class itself is not needed here.
  return Object.assign(new Error('Request failed'), { name: 'ApiError', status, retryable });
}

describe('shouldRetryQuery', () => {
  it('retries an unexpected error once', () => {
    expect(shouldRetryQuery(0, new Error('boom'))).toBe(true);
    expect(shouldRetryQuery(1, new Error('boom'))).toBe(false);
  });

  it('does not stack another round on a failure apiRequest already retried with backoff', () => {
    expect(shouldRetryQuery(0, apiError(true, 503))).toBe(false);
    expect(shouldRetryQuery(0, apiError(true, 0))).toBe(false);
    expect(shouldRetryQuery(0, apiError(true, 429))).toBe(false);
  });

  it('keeps the single retry for an ApiError the client did not retry (a 401 caught mid token refresh)', () => {
    expect(shouldRetryQuery(0, apiError(false, 401))).toBe(true);
    expect(shouldRetryQuery(1, apiError(false, 401))).toBe(false);
  });

  it('is the default policy of the shared browser client', () => {
    expect(getQueryClient().getDefaultOptions().queries?.retry).toBe(shouldRetryQuery);
  });
});
