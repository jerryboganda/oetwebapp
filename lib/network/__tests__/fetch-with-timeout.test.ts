import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest';
import { fetchWithTimeout } from '../fetch-with-timeout';

describe('fetchWithTimeout', () => {
  let fetchSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.useFakeTimers();
    fetchSpy = vi.spyOn(globalThis, 'fetch');
  });

  afterEach(() => {
    vi.useRealTimers();
    fetchSpy.mockRestore();
  });

  it('forwards the request and returns the response', async () => {
    const expected = new Response('ok');
    fetchSpy.mockResolvedValue(expected);

    const result = await fetchWithTimeout('https://api.example.com/x');
    expect(result).toBe(expected);
    expect(fetchSpy).toHaveBeenCalledTimes(1);
  });

  it('passes a fresh AbortSignal to fetch', async () => {
    fetchSpy.mockResolvedValue(new Response('ok'));

    await fetchWithTimeout('https://api.example.com/x', { method: 'POST' });
    const init = fetchSpy.mock.calls[0]![1] as RequestInit;
    expect(init.method).toBe('POST');
    expect(init.signal).toBeInstanceOf(AbortSignal);
    expect(init.signal!.aborted).toBe(false);
  });

  it('aborts with a timeout error when the timeout elapses', async () => {
    let capturedSignal: AbortSignal | undefined;
    fetchSpy.mockImplementation((_input, init) => {
      capturedSignal = (init as RequestInit | undefined)?.signal as AbortSignal;
      return new Promise<Response>((_resolve, reject) => {
        capturedSignal!.addEventListener('abort', () => {
          reject(capturedSignal!.reason);
        });
      });
    });

    const promise = fetchWithTimeout('https://api.example.com/x', {}, 1_000);
    // Attach a catch handler before advancing timers to avoid unhandled rejection warnings.
    const settled = promise.catch((err) => err);
    await vi.advanceTimersByTimeAsync(1_000);
    const err = (await settled) as DOMException;
    expect(err).toBeInstanceOf(DOMException);
    expect(err.name).toBe('AbortError');
    expect(err.message).toBe('The request timed out.');
  });

  it('aborts immediately when the external signal is already aborted', async () => {
    const externalController = new AbortController();
    externalController.abort(new DOMException('user cancelled', 'AbortError'));

    let capturedSignal: AbortSignal | undefined;
    fetchSpy.mockImplementation((_input, init) => {
      capturedSignal = (init as RequestInit | undefined)?.signal as AbortSignal;
      // The internal signal should already be aborted by the time fetch runs.
      return Promise.reject(capturedSignal!.reason);
    });

    await expect(
      fetchWithTimeout('https://api.example.com/x', { signal: externalController.signal }),
    ).rejects.toMatchObject({ name: 'AbortError', message: 'user cancelled' });

    expect(capturedSignal!.aborted).toBe(true);
  });

  it('propagates an external abort fired mid-flight', async () => {
    const externalController = new AbortController();

    let capturedSignal: AbortSignal | undefined;
    fetchSpy.mockImplementation((_input, init) => {
      capturedSignal = (init as RequestInit | undefined)?.signal as AbortSignal;
      return new Promise<Response>((_resolve, reject) => {
        capturedSignal!.addEventListener('abort', () => reject(capturedSignal!.reason));
      });
    });

    const promise = fetchWithTimeout('https://api.example.com/x', {
      signal: externalController.signal,
    });
    const settled = promise.catch((err) => err);
    externalController.abort(new DOMException('cancelled', 'AbortError'));
    const err = (await settled) as DOMException;
    expect(err.name).toBe('AbortError');
    expect(err.message).toBe('cancelled');
  });

  it('clears the timeout after a successful response', async () => {
    fetchSpy.mockResolvedValue(new Response('ok'));
    const clearSpy = vi.spyOn(globalThis, 'clearTimeout');

    await fetchWithTimeout('https://api.example.com/x', {}, 5_000);

    expect(clearSpy).toHaveBeenCalledTimes(1);
    clearSpy.mockRestore();
  });

  it('clears the timeout when fetch rejects (non-abort error)', async () => {
    fetchSpy.mockRejectedValue(new Error('network down'));
    const clearSpy = vi.spyOn(globalThis, 'clearTimeout');

    await expect(fetchWithTimeout('https://api.example.com/x')).rejects.toThrow('network down');
    expect(clearSpy).toHaveBeenCalledTimes(1);
    clearSpy.mockRestore();
  });

  it('uses a default AbortError reason when external signal aborts without one', async () => {
    const externalController = new AbortController();
    let capturedSignal: AbortSignal | undefined;
    fetchSpy.mockImplementation((_input, init) => {
      capturedSignal = (init as RequestInit | undefined)?.signal as AbortSignal;
      return new Promise<Response>((_resolve, reject) => {
        capturedSignal!.addEventListener('abort', () => reject(capturedSignal!.reason));
      });
    });

    const promise = fetchWithTimeout('https://api.example.com/x', {
      signal: externalController.signal,
    });
    const settled = promise.catch((err) => err);
    externalController.abort(); // no reason supplied
    const err = (await settled) as { name?: string };
    // AbortController in jsdom may surface a DOMException from a different
    // realm than the test's global DOMException, so check by shape.
    expect(err?.name).toBe('AbortError');
  });
});
