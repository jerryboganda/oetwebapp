import { readFileSync } from 'node:fs';
import path from 'node:path';
import vm from 'node:vm';
import { describe, expect, it, vi } from 'vitest';

type FetchEvent = { request: unknown; respondWith: (response: unknown) => void };

/** Loads public/sw.js in a sandbox and returns its fetch handler. */
function loadFetchHandler(): (event: FetchEvent) => void {
  const listeners = new Map<string, (event: FetchEvent) => void>();
  const sandbox = {
    self: {
      addEventListener: (type: string, handler: (event: FetchEvent) => void) => listeners.set(type, handler),
      location: { origin: 'https://app.example.test' },
    },
    URL,
    fetch: async () => ({ ok: false }),
    caches: { match: async () => undefined, open: async () => ({ put: () => undefined }) },
  };
  vm.runInNewContext(readFileSync(path.join(process.cwd(), 'public/sw.js'), 'utf8'), sandbox);
  const handler = listeners.get('fetch');
  if (!handler) throw new Error('sw.js registered no fetch handler');
  return handler;
}

function apiGet(pathname: string) {
  const respondWith = vi.fn();
  loadFetchHandler()({
    request: {
      method: 'GET',
      url: `https://app.example.test${pathname}`,
      mode: 'cors',
      headers: { get: () => 'application/json' },
    },
    respondWith,
  });
  return respondWith;
}

describe('service worker', () => {
  it('never intercepts Writing draft reads, so a stale cached draft can not be restored', () => {
    expect(apiGet('/api/backend/v1/writing/drafts/7f1c2a1e-0000-4000-8000-000000000001/practice')).not.toHaveBeenCalled();
    expect(apiGet('/v1/writing/drafts/7f1c2a1e-0000-4000-8000-000000000001/revision')).not.toHaveBeenCalled();
  });

  it('still serves other API reads network-first', () => {
    expect(apiGet('/api/backend/v1/writing/my-work')).toHaveBeenCalledTimes(1);
  });

  it('never intercepts SignalR hub traffic, so long-polls are not cached one entry per poll', () => {
    // Every long-poll URL is unique (`&_=<timestamp>`), so a cached poll is never reused.
    expect(apiGet('/api/backend/v1/notifications/hub?id=abc&_=1767225600000')).not.toHaveBeenCalled();
    expect(apiGet('/api/backend/v1/ai-assistant/hub?id=abc&_=1767225600001')).not.toHaveBeenCalled();
    expect(apiGet('/api/backend/v1/conversations/hub')).not.toHaveBeenCalled();
    expect(apiGet('/api/backend/v1/speaking/live-rooms/hub?id=abc')).not.toHaveBeenCalled();
    // Direct-to-API hubs (Writing, mock live room) are matched on any origin.
    expect(apiGet('/v1/mocks/live-room/hub')).not.toHaveBeenCalled();
    expect(apiGet('/hubs/writing-submissions')).not.toHaveBeenCalled();
  });

  it('only treats a whole path segment named hub as a SignalR hub', () => {
    expect(apiGet('/api/backend/v1/github/hubspot-sync')).toHaveBeenCalledTimes(1);
    expect(apiGet('/api/backend/v1/hubble/status')).toHaveBeenCalledTimes(1);
  });

  it('uses a fresh cache version so v7 hub long-poll entries are purged on activate', () => {
    const source = readFileSync(path.join(process.cwd(), 'public/sw.js'), 'utf8');
    expect(source).toContain("const CACHE_VERSION = 'oet-v8';");
  });
});
