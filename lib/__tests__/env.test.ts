import { describe, it, expect } from 'vitest';
import { resolveApiBaseUrl } from '../env';

describe('resolveApiBaseUrl', () => {
  it('uses NEXT_PUBLIC_API_BASE_URL when set', () => {
    expect(resolveApiBaseUrl({ NEXT_PUBLIC_API_BASE_URL: 'https://api.test' })).toBe(
      'https://api.test',
    );
  });

  it('strips a trailing slash from NEXT_PUBLIC_API_BASE_URL', () => {
    expect(resolveApiBaseUrl({ NEXT_PUBLIC_API_BASE_URL: 'https://api.test/' })).toBe(
      'https://api.test',
    );
  });

  it('treats empty / whitespace NEXT_PUBLIC_API_BASE_URL as unset', () => {
    expect(resolveApiBaseUrl({ NEXT_PUBLIC_API_BASE_URL: '   ' })).toBe('/api/backend');
    expect(resolveApiBaseUrl({ NEXT_PUBLIC_API_BASE_URL: '' })).toBe('/api/backend');
  });

  it('returns the same-origin proxy path in a browser context', () => {
    // jsdom => window is defined in this test environment.
    expect(resolveApiBaseUrl({})).toBe('/api/backend');
  });

  it('falls back to PUBLIC_API_BASE_URL when running server-side', () => {
    const originalWindow = (globalThis as { window?: unknown }).window;
    delete (globalThis as { window?: unknown }).window;
    try {
      expect(
        resolveApiBaseUrl({ PUBLIC_API_BASE_URL: 'https://ssr.test/' }),
      ).toBe('https://ssr.test');
    } finally {
      (globalThis as { window?: unknown }).window = originalWindow;
    }
  });

  it('falls back to API_PROXY_TARGET_URL when PUBLIC_API_BASE_URL absent (server)', () => {
    const originalWindow = (globalThis as { window?: unknown }).window;
    delete (globalThis as { window?: unknown }).window;
    try {
      expect(resolveApiBaseUrl({ API_PROXY_TARGET_URL: 'https://upstream.test' })).toBe(
        'https://upstream.test',
      );
    } finally {
      (globalThis as { window?: unknown }).window = originalWindow;
    }
  });

  it('falls back to default proxy target when nothing is configured (server)', () => {
    const originalWindow = (globalThis as { window?: unknown }).window;
    delete (globalThis as { window?: unknown }).window;
    try {
      expect(resolveApiBaseUrl({})).toBe('http://127.0.0.1:5198');
    } finally {
      (globalThis as { window?: unknown }).window = originalWindow;
    }
  });

  it('NEXT_PUBLIC_API_BASE_URL takes precedence on the server too', () => {
    const originalWindow = (globalThis as { window?: unknown }).window;
    delete (globalThis as { window?: unknown }).window;
    try {
      expect(
        resolveApiBaseUrl({
          NEXT_PUBLIC_API_BASE_URL: 'https://public.test',
          PUBLIC_API_BASE_URL: 'https://internal.test',
          API_PROXY_TARGET_URL: 'https://proxy.test',
        }),
      ).toBe('https://public.test');
    } finally {
      (globalThis as { window?: unknown }).window = originalWindow;
    }
  });
});
