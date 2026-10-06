/**
 * Best-effort telemetry batching.
 *
 * Product analytics (hundreds of call sites) and Listening attempt telemetry used
 * to cost one authenticated request per event, each passing through the Next proxy
 * to the API. A batcher collects events for a short window and sends them together.
 *
 * Contract:
 * - Telemetry is best effort and is NEVER retried. A failed batch is dropped: a
 *   retry loop on a fire-and-forget channel is how the 30 Sep 2026 incident burst
 *   used up every database connection.
 * - One batch is in flight at a time, oldest first, so a slow request is never
 *   overtaken by a later one. A send that has not settled after `sendTimeoutMs`
 *   is abandoned so one stalled request cannot silence telemetry for the session.
 * - When the page is hidden or unloading, whatever is queued is sent at once with
 *   `keepalive: true` (in parallel, not behind an in-flight batch, because the page
 *   may not be around for that to finish).
 * - Memory is bounded: unsent items never exceed `maxQueueSize`; the oldest are dropped.
 */

export interface EventBatcherOptions<T> {
  /** Sends one non-empty batch, oldest first. A rejection drops the batch. */
  send: (items: T[], context: { keepalive: boolean }) => Promise<void>;
  /** A full batch is sent immediately. Default 20. */
  maxBatchSize?: number;
  /** Longest an event waits for company before it is sent. Default 2000 ms. */
  flushIntervalMs?: number;
  /** Unsent items beyond this are dropped oldest-first. Default 500 (never below maxBatchSize). */
  maxQueueSize?: number;
  /** A send still pending after this long stops blocking the queue. Default 20000 ms. */
  sendTimeoutMs?: number;
  /** Flush with `keepalive` on page hide / unload. Default true. */
  flushOnPageHide?: boolean;
}

export interface EventBatcher<T> {
  /** Queues an item. `flushNow` sends it (and everything queued before it) immediately. */
  enqueue(item: T, options?: { flushNow?: boolean }): void;
  /**
   * Sends everything queued now and resolves once all of it has been sent, including when a batch
   * is already in flight (it waits for that one, then for the items queued behind it). Never rejects.
   */
  flush(options?: { keepalive?: boolean }): Promise<void>;
  /** Items waiting to be sent. */
  readonly size: number;
  /** Stops the timer and page listeners and drops anything still queued. */
  dispose(): void;
}

export function createEventBatcher<T>(options: EventBatcherOptions<T>): EventBatcher<T> {
  const maxBatchSize = Math.max(1, options.maxBatchSize ?? 20);
  const flushIntervalMs = options.flushIntervalMs ?? 2000;
  const maxQueueSize = Math.max(maxBatchSize, options.maxQueueSize ?? 500);
  const sendTimeoutMs = options.sendTimeoutMs ?? 20_000;

  let queue: T[] = [];
  let timer: ReturnType<typeof setTimeout> | null = null;
  let inFlight: Promise<void> | null = null;
  let sendAgain = false;

  const clearTimer = () => {
    if (timer !== null) {
      clearTimeout(timer);
      timer = null;
    }
  };

  /** Resolves when the send settles, fails or times out. Never rejects. */
  const sendBatch = (batch: T[], keepalive: boolean): Promise<void> =>
    new Promise<void>((resolve) => {
      const timeout = setTimeout(resolve, sendTimeoutMs);
      Promise.resolve()
        .then(() => options.send(batch, { keepalive }))
        .then(() => undefined, () => undefined)
        .then(() => {
          clearTimeout(timeout);
          resolve();
        });
    });

  const armTimer = () => {
    if (timer !== null) return;
    timer = setTimeout(() => {
      timer = null;
      void drain(false);
    }, flushIntervalMs);
  };

  /**
   * Sends what is queued right now, one batch at a time. Items that arrive while it
   * is sending are not swept into the same run: they wait for a full batch or for
   * their own window, so a steady trickle of events still goes out in batches
   * instead of one request per previous response.
   */
  const pump = (): Promise<void> => {
    if (inFlight) {
      // A flush was asked for while a run is sending: the run goes around again before it ends,
      // so the promise handed back settles only after what is queued now has been sent too.
      sendAgain = true;
      return inFlight;
    }
    if (queue.length === 0) return Promise.resolve();

    const run = (async () => {
      // Yield once so `inFlight` is assigned before this can finish and clear it.
      await Promise.resolve();
      try {
        do {
          sendAgain = false;
          let toSend = queue.length;
          while (toSend > 0 && queue.length > 0) {
            const batch = queue.splice(0, Math.min(maxBatchSize, toSend));
            toSend -= batch.length;
            await sendBatch(batch, false);
          }
        } while (sendAgain && queue.length > 0);
      } finally {
        inFlight = null;
        sendAgain = false;
        if (queue.length >= maxBatchSize) {
          void pump();
        } else if (queue.length > 0) {
          armTimer();
        }
      }
    })();
    inFlight = run;
    return run;
  };

  const drain = (keepalive: boolean): Promise<void> => {
    clearTimer();
    if (!keepalive) return pump();

    const parallel: Promise<void>[] = [];
    while (queue.length > 0) {
      parallel.push(sendBatch(queue.splice(0, maxBatchSize), true));
    }
    return Promise.all(parallel).then(() => undefined);
  };

  const onVisibilityChange = () => {
    if (document.visibilityState === 'hidden') void drain(true);
  };
  const onPageHide = () => {
    void drain(true);
  };

  const listening = options.flushOnPageHide !== false
    && typeof window !== 'undefined'
    && typeof document !== 'undefined';
  if (listening) {
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('pagehide', onPageHide);
  }

  return {
    enqueue(item, enqueueOptions) {
      queue.push(item);
      if (queue.length > maxQueueSize) {
        queue.splice(0, queue.length - maxQueueSize);
      }

      if (enqueueOptions?.flushNow || queue.length >= maxBatchSize) {
        void drain(false);
        return;
      }

      armTimer();
    },
    flush(flushOptions) {
      return drain(flushOptions?.keepalive === true);
    },
    get size() {
      return queue.length;
    },
    dispose() {
      clearTimer();
      queue = [];
      if (listening) {
        document.removeEventListener('visibilitychange', onVisibilityChange);
        window.removeEventListener('pagehide', onPageHide);
      }
    },
  };
}
