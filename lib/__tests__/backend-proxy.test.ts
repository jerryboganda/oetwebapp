import { describe, expect, it } from 'vitest';
import { resolveProxyTarget, sanitizeProxyHeaders, validateProxyPathSegments } from '../backend-proxy';

describe('backend proxy helpers', () => {
  it('builds a safe backend target for v1 paths', () => {
    const target = resolveProxyTarget(['v1', 'auth', 'me'], new URLSearchParams('includeProfile=true'));

    expect(target).toBe('http://localhost:5198/v1/auth/me?includeProfile=true');
  });

  it('rejects non-v1 and traversal paths', () => {
    expect(() => validateProxyPathSegments([])).toThrow('Invalid proxy path.');
    expect(() => validateProxyPathSegments(['admin'])).toThrow('Invalid proxy path.');
    expect(() => validateProxyPathSegments(['v1', '..', 'admin'])).toThrow('Invalid proxy path.');
  });

  it('removes debug and forwarding headers before proxying', () => {
    const headers = new Headers({
      Authorization: 'Bearer token',
      'Content-Type': 'application/json',
      'X-Debug-Role': 'admin',
      Host: 'evil.example.test',
      Connection: 'keep-alive',
      'X-Forwarded-For': '1.2.3.4',
    });

    const sanitized = sanitizeProxyHeaders(headers);

    expect(sanitized.get('Authorization')).toBe('Bearer token');
    expect(sanitized.get('Content-Type')).toBe('application/json');
    expect(sanitized.get('X-Debug-Role')).toBeNull();
    expect(sanitized.get('Host')).toBeNull();
    expect(sanitized.get('Connection')).toBeNull();
    expect(sanitized.get('X-Forwarded-For')).toBeNull();
  });
});