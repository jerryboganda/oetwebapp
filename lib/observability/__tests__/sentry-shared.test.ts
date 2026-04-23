import { describe, it, expect } from 'vitest';
import type { Event } from '@sentry/nextjs';

import {
  readSampleRate,
  readSentryDsn,
  scrubPii,
  SENSITIVE_HEADER_NAMES,
} from '@/lib/observability/sentry-shared';

describe('sentry-shared scrubPii', () => {
  it('drops user email/ip/username but keeps the opaque id', () => {
    const event: Event = {
      user: {
        id: 'user_123',
        email: 'learner@example.com',
        ip_address: '203.0.113.5',
        username: 'learner123',
      },
    };

    const scrubbed = scrubPii(event);

    expect(scrubbed).not.toBeNull();
    expect(scrubbed!.user).toEqual({ id: 'user_123' });
  });

  it('nulls cookies + query-string and removes sensitive headers case-insensitively', () => {
    const event: Event = {
      request: {
        method: 'POST',
        url: 'https://api.example.com/v1/auth/sign-in',
        cookies: { session: 'abc', oet_rt: 'refresh' },
        query_string: 'next=/dashboard&token=secret',
        headers: {
          Authorization: 'Bearer leaked-jwt',
          cookie: 'session=abc',
          'X-CSRF-Token': 'csrf-xyz',
          'x-forwarded-for': '203.0.113.5',
          'Content-Type': 'application/json',
          'User-Agent': 'Mozilla/5.0',
        },
      },
    };

    const scrubbed = scrubPii(event);

    expect(scrubbed).not.toBeNull();
    const req = scrubbed!.request!;
    expect(req.cookies).toBeUndefined();
    expect(req.query_string).toBeUndefined();

    const headers = req.headers as Record<string, string>;
    // Sensitive headers gone regardless of original casing.
    expect(headers.Authorization).toBeUndefined();
    expect(headers.cookie).toBeUndefined();
    expect(headers['X-CSRF-Token']).toBeUndefined();
    expect(headers['x-forwarded-for']).toBeUndefined();
    // Benign headers survive so stack grouping / triage still works.
    expect(headers['Content-Type']).toBe('application/json');
    expect(headers['User-Agent']).toBe('Mozilla/5.0');
  });

  it('is a no-op when user / request are absent', () => {
    const event: Event = { message: 'boom' };
    const scrubbed = scrubPii(event);
    expect(scrubbed).not.toBeNull();
    expect(scrubbed!.message).toBe('boom');
  });

  it('declares the documented sensitive header set', () => {
    expect(SENSITIVE_HEADER_NAMES).toContain('Authorization');
    expect(SENSITIVE_HEADER_NAMES).toContain('Cookie');
    expect(SENSITIVE_HEADER_NAMES).toContain('X-CSRF-Token');
    expect(SENSITIVE_HEADER_NAMES).toContain('X-Forwarded-For');
  });
});

describe('sentry-shared env helpers', () => {
  it('readSentryDsn returns null when DSN env vars are unset', () => {
    const saved = {
      pub: process.env.NEXT_PUBLIC_SENTRY_DSN,
      srv: process.env.SENTRY_DSN,
    };
    delete process.env.NEXT_PUBLIC_SENTRY_DSN;
    delete process.env.SENTRY_DSN;
    try {
      expect(readSentryDsn()).toBeNull();
    } finally {
      if (saved.pub !== undefined) process.env.NEXT_PUBLIC_SENTRY_DSN = saved.pub;
      if (saved.srv !== undefined) process.env.SENTRY_DSN = saved.srv;
    }
  });

  it('readSampleRate clamps to [0,1] and rejects non-finite values', () => {
    process.env.__TEST_RATE = '0.25';
    expect(readSampleRate('__TEST_RATE')).toBe(0.25);

    process.env.__TEST_RATE = '-5';
    expect(readSampleRate('__TEST_RATE')).toBe(0);

    process.env.__TEST_RATE = '42';
    expect(readSampleRate('__TEST_RATE')).toBe(1);

    process.env.__TEST_RATE = 'not-a-number';
    expect(readSampleRate('__TEST_RATE')).toBe(0);

    delete process.env.__TEST_RATE;
    expect(readSampleRate('__TEST_RATE')).toBe(0);
  });
});
