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
});
