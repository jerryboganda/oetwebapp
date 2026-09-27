import { NextRequest } from 'next/server';
import { describe, expect, it } from 'vitest';
import { proxy } from '@/proxy';
import { buildOwnerAgentConsoleCsp } from '../csp';
import { isOwnerAgentConsolePath, isOwnerAgentSentryEvent, isOwnerAgentUrl } from '../route-scope';

function directives(csp: string): Map<string, string> {
  const map = new Map<string, string>();
  for (const part of csp.split(';')) {
    const trimmed = part.trim();
    if (!trimmed) continue;
    const [name, ...values] = trimmed.split(/\s+/);
    map.set(name, values.join(' '));
  }
  return map;
}

function authedRequest(pathname: string): NextRequest {
  return new NextRequest(`https://app.oetwithdrhesham.co.uk${pathname}`, {
    headers: { cookie: 'oet_auth=1; oet_csrf=csrf-fixture' },
  });
}

describe('owner-agent console CSP', () => {
  it('serves the strict policy on /admin/agent-console routes', () => {
    for (const path of ['/admin/agent-console', '/admin/agent-console/01J9ZQ3V4W5X6Y7Z8A9B0C1D2E', '/admin/agent-console/settings']) {
      const response = proxy(authedRequest(path));
      const csp = directives(response.headers.get('content-security-policy') ?? '');
      const scriptSrc = csp.get('script-src') ?? '';

      expect(scriptSrc).toMatch(/^'self' 'nonce-[A-Za-z0-9+/=]+'/);
      expect(scriptSrc).not.toContain('https://');
      expect(scriptSrc).not.toContain("'strict-dynamic'");
      expect(csp.get('img-src')).toBe("'self' data:");
      expect(csp.get('connect-src')).toBe("'self'");
      expect(csp.get('frame-src')).toBe("'none'");
      expect(csp.get('frame-ancestors')).toBe("'none'");
      expect(csp.get('object-src')).toBe("'none'");
    }
  });

  it('keeps the broader app policy everywhere else', () => {
    const response = proxy(authedRequest('/admin/ai-providers'));
    const csp = directives(response.headers.get('content-security-policy') ?? '');
    expect(csp.get('connect-src')).toContain('https://video.bunnycdn.com');
    expect(csp.get('frame-ancestors')).toBe("'self'");
  });

  it('builds a nonce policy without unsafe-eval in production', () => {
    const prod = buildOwnerAgentConsoleCsp('abc', false);
    expect(prod).toContain("script-src 'self' 'nonce-abc'");
    expect(prod).not.toContain('unsafe-eval');
    expect(prod).toContain('upgrade-insecure-requests');
    expect(buildOwnerAgentConsoleCsp('abc', true)).toContain("'unsafe-eval'");
  });
});

describe('owner-agent route scope', () => {
  it('matches console paths only', () => {
    expect(isOwnerAgentConsolePath('/admin/agent-console')).toBe(true);
    expect(isOwnerAgentConsolePath('/admin/agent-console/settings?x=1')).toBe(true);
    expect(isOwnerAgentConsolePath('/admin/agent-consoles')).toBe(false);
    expect(isOwnerAgentConsolePath('/admin')).toBe(false);
    expect(isOwnerAgentConsolePath(null)).toBe(false);
  });

  it('recognises console pages and owner-agent API URLs', () => {
    expect(isOwnerAgentUrl('https://app.example.test/admin/agent-console/x')).toBe(true);
    expect(isOwnerAgentUrl('/api/backend/v1/owner-agent/status')).toBe(true);
    expect(isOwnerAgentUrl('http://127.0.0.1:5198/v1/owner-agent/hub/negotiate?negotiateVersion=1')).toBe(true);
    expect(isOwnerAgentUrl('/api/backend/v1/owner-agents')).toBe(false);
    expect(isOwnerAgentUrl('/admin/users')).toBe(false);
  });

  it('flags Sentry events from the console or the owner-agent API', () => {
    expect(isOwnerAgentSentryEvent({ request: { url: 'https://app.example.test/admin/agent-console' } })).toBe(true);
    expect(isOwnerAgentSentryEvent({ transaction: '/api/backend/v1/owner-agent/sessions/x' })).toBe(true);
    expect(isOwnerAgentSentryEvent({ request: { url: 'https://app.example.test/admin/users' } }, '/admin/agent-console/settings')).toBe(true);
    expect(isOwnerAgentSentryEvent({ request: { url: 'https://app.example.test/admin/users' } }, '/admin/users')).toBe(false);
  });
});
