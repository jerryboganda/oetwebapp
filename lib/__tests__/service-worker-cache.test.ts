import { afterEach, describe, expect, it, vi } from 'vitest';
import { clearServiceWorkerAuthCache } from '../service-worker-cache';

function stubServiceWorker(container: unknown) {
  Object.defineProperty(window.navigator, 'serviceWorker', { configurable: true, value: container });
}

function removeServiceWorker() {
  // `in` must be false for the "no worker" cases (native shells, old browsers).
  delete (window.navigator as unknown as Record<string, unknown>).serviceWorker;
}

describe('clearServiceWorkerAuthCache', () => {
  afterEach(() => {
    removeServiceWorker();
  });

  it('tells the controlling worker to clear its API cache', () => {
    const postMessage = vi.fn();
    stubServiceWorker({ controller: { postMessage }, getRegistration: vi.fn() });

    clearServiceWorkerAuthCache();

    expect(postMessage).toHaveBeenCalledTimes(1);
    expect(postMessage).toHaveBeenCalledWith({ type: 'CLEAR_AUTH_CACHE' });
  });

  it('falls back to the active registration when the page has no controller yet', async () => {
    const postMessage = vi.fn();
    const getRegistration = vi.fn().mockResolvedValue({ active: { postMessage } });
    stubServiceWorker({ controller: null, getRegistration });

    clearServiceWorkerAuthCache();
    await vi.waitFor(() => expect(postMessage).toHaveBeenCalledWith({ type: 'CLEAR_AUTH_CACHE' }));
  });

  it('is a no-op when there is no worker support', () => {
    removeServiceWorker();
    expect(() => clearServiceWorkerAuthCache()).not.toThrow();
  });

  it('never throws into a sign-out, whatever the worker does', async () => {
    stubServiceWorker({
      controller: {
        postMessage: () => {
          throw new Error('worker gone');
        },
      },
      getRegistration: vi.fn(),
    });
    expect(() => clearServiceWorkerAuthCache()).not.toThrow();

    stubServiceWorker({ controller: null, getRegistration: vi.fn().mockRejectedValue(new Error('no registration')) });
    expect(() => clearServiceWorkerAuthCache()).not.toThrow();
    await Promise.resolve();
  });

  it('is triggered by clearStoredSession, the choke point for every sign-out and session loss', async () => {
    const postMessage = vi.fn();
    stubServiceWorker({ controller: { postMessage }, getRegistration: vi.fn() });
    const { clearStoredSession } = await import('../auth-storage');

    clearStoredSession();

    expect(postMessage).toHaveBeenCalledWith({ type: 'CLEAR_AUTH_CACHE' });
  });
});
