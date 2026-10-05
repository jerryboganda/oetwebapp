import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { createEventBatcher, type EventBatcher } from '../event-batcher';

let visibility: 'visible' | 'hidden' = 'visible';

/** A send that never answers until released. */
function holdingSend() {
  const releases: Array<() => void> = [];
  const send = vi.fn().mockImplementation(() => new Promise<void>((resolve) => { releases.push(resolve); }));
  return { send, releases };
}

describe('createEventBatcher', () => {
  let batcher: EventBatcher<number> | null = null;

  beforeEach(() => {
    vi.useFakeTimers();
    visibility = 'visible';
    Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => visibility });
  });

  afterEach(() => {
    batcher?.dispose();
    batcher = null;
    vi.useRealTimers();
  });

  it('sends events that arrive inside the window together, oldest first', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 2000 });

    batcher.enqueue(1);
    batcher.enqueue(2);
    batcher.enqueue(3);
    expect(send).not.toHaveBeenCalled();
    expect(batcher.size).toBe(3);

    await vi.advanceTimersByTimeAsync(2000);

    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith([1, 2, 3], { keepalive: false });
    expect(batcher.size).toBe(0);
  });

  it('does not push an event out past the window because later events keep arriving', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 2000 });

    batcher.enqueue(1);
    await vi.advanceTimersByTimeAsync(1500);
    batcher.enqueue(2);
    await vi.advanceTimersByTimeAsync(500);

    expect(send).toHaveBeenCalledWith([1, 2], { keepalive: false });
  });

  it('sends as soon as a full batch is waiting, splitting a larger backlog into full batches', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, maxBatchSize: 3, flushIntervalMs: 60_000 });

    [1, 2, 3, 4, 5, 6, 7].forEach((n) => batcher!.enqueue(n));
    await vi.advanceTimersByTimeAsync(0);

    expect(send.mock.calls.map(([items]) => items)).toEqual([[1, 2, 3], [4, 5, 6], [7]]);
    expect(send).toHaveBeenCalledWith([1, 2, 3], { keepalive: false });
    expect(batcher.size).toBe(0);
  });

  it('keeps a steady trickle in batches instead of one request per previous response', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 2000 });

    for (let n = 1; n <= 10; n += 1) {
      batcher.enqueue(n);
      await vi.advanceTimersByTimeAsync(300); // 10 events over 3 s, each send answers instantly
    }
    await vi.advanceTimersByTimeAsync(2000);

    // Two windows (events 1-7, then 8-10) rather than a request per event.
    expect(send.mock.calls.length).toBeLessThanOrEqual(3);
    expect(send.mock.calls.flatMap(([items]) => items)).toEqual([1, 2, 3, 4, 5, 6, 7, 8, 9, 10]);
  });

  it('flushNow sends the item together with everything queued before it, in order', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1);
    batcher.enqueue(2);
    batcher.enqueue(3, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);

    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith([1, 2, 3], { keepalive: false });
  });

  it('sends one batch at a time, so a slow request is never overtaken', async () => {
    const { send, releases } = holdingSend();
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);
    batcher.enqueue(2, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledTimes(1);

    releases[0]();
    await vi.advanceTimersByTimeAsync(0);

    // The flush that was asked for while the first send was out goes as soon as it ends.
    expect(send).toHaveBeenCalledTimes(2);
    expect(send).toHaveBeenLastCalledWith([2], { keepalive: false });
  });

  it('flush asked for during an in-flight send resolves only after the items queued behind it were sent', async () => {
    const { send, releases } = holdingSend();
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledTimes(1);

    batcher.enqueue(2);
    batcher.enqueue(3);
    let flushed = false;
    const flushing = batcher.flush().then(() => {
      flushed = true;
    });

    releases[0]();
    await vi.advanceTimersByTimeAsync(0);
    // The first send is done, but the flush is not: the queued items are only now going out.
    expect(send).toHaveBeenCalledTimes(2);
    expect(send).toHaveBeenLastCalledWith([2, 3], { keepalive: false });
    expect(flushed).toBe(false);

    releases[1]();
    await flushing;
    expect(flushed).toBe(true);
    expect(batcher.size).toBe(0);
  });

  it('drops a failed batch and carries on: telemetry is never retried', async () => {
    const send = vi.fn()
      .mockRejectedValueOnce(new Error('server down'))
      .mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);
    batcher.enqueue(2, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);

    expect(send).toHaveBeenCalledTimes(2);
    expect(send).toHaveBeenNthCalledWith(1, [1], { keepalive: false });
    expect(send).toHaveBeenNthCalledWith(2, [2], { keepalive: false });
  });

  it('survives a send that throws synchronously', async () => {
    const send = vi.fn().mockImplementationOnce(() => {
      throw new Error('boom');
    });
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1);
    await expect(batcher.flush()).resolves.toBeUndefined();
    expect(send).toHaveBeenCalledTimes(1);
  });

  it('stops waiting for a send that never answers, so telemetry is not silenced for the session', async () => {
    const { send } = holdingSend();
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000, sendTimeoutMs: 5000 });

    batcher.enqueue(1, { flushNow: true });
    await vi.advanceTimersByTimeAsync(0);
    batcher.enqueue(2, { flushNow: true });
    await vi.advanceTimersByTimeAsync(4999);
    expect(send).toHaveBeenCalledTimes(1);

    await vi.advanceTimersByTimeAsync(1);
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenCalledTimes(2);
    expect(send).toHaveBeenLastCalledWith([2], { keepalive: false });
  });

  it('sends what is queued with keepalive when the page is hidden', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1);
    batcher.enqueue(2);
    visibility = 'hidden';
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);

    expect(send).toHaveBeenCalledTimes(1);
    expect(send).toHaveBeenCalledWith([1, 2], { keepalive: true });
  });

  it('does nothing when the page becomes visible again', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1);
    document.dispatchEvent(new Event('visibilitychange'));
    await vi.advanceTimersByTimeAsync(0);

    expect(send).not.toHaveBeenCalled();
  });

  it('sends what is queued with keepalive on pagehide, without waiting for an in-flight batch', async () => {
    const { send } = holdingSend();
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000 });

    batcher.enqueue(1, { flushNow: true }); // goes out and does not answer
    await vi.advanceTimersByTimeAsync(0);
    batcher.enqueue(2);
    batcher.enqueue(3);
    window.dispatchEvent(new Event('pagehide'));
    await vi.advanceTimersByTimeAsync(0);

    expect(send).toHaveBeenCalledWith([2, 3], { keepalive: true });
  });

  it('does not listen for page lifecycle events when told not to', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 60_000, flushOnPageHide: false });

    batcher.enqueue(1);
    window.dispatchEvent(new Event('pagehide'));
    await vi.advanceTimersByTimeAsync(0);

    expect(send).not.toHaveBeenCalled();
  });

  it('keeps memory bounded: while a send is stuck, only the newest unsent items are kept', async () => {
    const { send, releases } = holdingSend();
    batcher = createEventBatcher<number>({ send, maxBatchSize: 2, maxQueueSize: 3, flushIntervalMs: 60_000 });

    batcher.enqueue(1);
    batcher.enqueue(2); // a full batch goes out and stays out
    await vi.advanceTimersByTimeAsync(0);
    [3, 4, 5, 6].forEach((n) => batcher!.enqueue(n));

    expect(batcher.size).toBe(3);

    releases[0]();
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenNthCalledWith(2, [4, 5], { keepalive: false });
    releases[1]();
    await vi.advanceTimersByTimeAsync(0);
    expect(send).toHaveBeenNthCalledWith(3, [6], { keepalive: false });
    expect(send.mock.calls.flatMap(([items]) => items)).not.toContain(3);
  });

  it('flush on an empty queue sends nothing, and dispose drops pending items and listeners', async () => {
    const send = vi.fn().mockResolvedValue(undefined);
    batcher = createEventBatcher<number>({ send, flushIntervalMs: 2000 });

    await batcher.flush();
    expect(send).not.toHaveBeenCalled();

    batcher.enqueue(1);
    batcher.dispose();
    await vi.advanceTimersByTimeAsync(5000);
    window.dispatchEvent(new Event('pagehide'));
    await vi.advanceTimersByTimeAsync(0);

    expect(send).not.toHaveBeenCalled();
  });
});
