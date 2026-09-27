import { describe, expect, it } from 'vitest';
import { ProxyGrants, proxyBaseUrl } from '../src/proxies.js';

describe('proxy grant revocation', () => {
  it('derives proxy base URLs without credentials', () => {
    expect(proxyBaseUrl('http://01J9ZQ4X7V3N8K2M5P6R7S8T9V:x@oet-agent-egress:3128')).toBe('http://oet-agent-egress:3128');
    expect(proxyBaseUrl('tcp://oet-agent-dockerproxy:2375')).toBe('http://oet-agent-dockerproxy:2375');
    expect(proxyBaseUrl('unix:///var/run/docker.sock')).toBeNull();
  });

  it('sends DELETE /internal/sessions[/:id] with the control token to both proxies', async () => {
    const calls: { url: string; token: string | undefined }[] = [];
    const grants = new ProxyGrants({
      egressProxyUrl: 'http://oet-agent-egress:3128',
      dockerHost: 'tcp://oet-agent-dockerproxy:2375',
      controlToken: 'c'.repeat(40),
      httpDelete: async (url, headers) => {
        calls.push({ url, token: headers['X-Oet-Control-Token'] });
        return 204;
      },
    });
    await grants.revokeSession('01J9ZQ4X7V3N8K2M5P6R7S8T9V');
    await grants.revokeAll();
    expect(calls.map((c) => c.url).sort()).toEqual(
      [
        'http://oet-agent-egress:3128/internal/sessions/01J9ZQ4X7V3N8K2M5P6R7S8T9V',
        'http://oet-agent-dockerproxy:2375/internal/sessions/01J9ZQ4X7V3N8K2M5P6R7S8T9V',
        'http://oet-agent-egress:3128/internal/sessions',
        'http://oet-agent-dockerproxy:2375/internal/sessions',
      ].sort(),
    );
    expect(calls.every((c) => c.token === 'c'.repeat(40))).toBe(true);
  });

  it('is a no-op without a proxy token and never throws on proxy errors', async () => {
    let called = 0;
    const silent = new ProxyGrants({
      egressProxyUrl: 'http://oet-agent-egress:3128',
      dockerHost: 'tcp://oet-agent-dockerproxy:2375',
      controlToken: null,
      httpDelete: async () => {
        called += 1;
        return 204;
      },
    });
    await silent.revokeAll();
    expect(called).toBe(0);

    const failing = new ProxyGrants({
      egressProxyUrl: 'http://oet-agent-egress:3128',
      dockerHost: 'tcp://oet-agent-dockerproxy:2375',
      controlToken: 'c'.repeat(40),
      httpDelete: async () => {
        throw new Error('connection refused');
      },
    });
    await expect(failing.revokeAll()).resolves.toBeUndefined();
  });
});
