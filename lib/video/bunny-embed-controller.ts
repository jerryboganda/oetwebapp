'use client';

type BunnyPlayerEvent =
  | 'ready'
  | 'play'
  | 'pause'
  | 'ended'
  | 'timeupdate'
  | 'progress'
  | 'error';

type BunnyPlayerListener = (value?: unknown) => void;

type OutboundMessage = {
  method: string;
  value?: unknown;
  context?: string;
  version?: string;
};

type InboundMessage = {
  context?: string;
  event?: string;
  value?: unknown;
};

/**
 * Minimal, audited implementation of the open player.js postMessage protocol
 * used by Bunny's embed player. Keeping this tiny avoids loading mutable
 * third-party JavaScript into the first-party application origin.
 */
export class BunnyEmbedController {
  private readonly origin: string;
  private readonly listeners = new Map<BunnyPlayerEvent, Set<BunnyPlayerListener>>();
  private readonly queue: OutboundMessage[] = [];
  private ready = false;

  constructor(private readonly iframe: HTMLIFrameElement) {
    this.origin = new URL(iframe.src).origin;
    window.addEventListener('message', this.handleMessage);
  }

  on(event: BunnyPlayerEvent, listener: BunnyPlayerListener): void {
    const listeners = this.listeners.get(event) ?? new Set<BunnyPlayerListener>();
    listeners.add(listener);
    this.listeners.set(event, listeners);
    this.send({ method: 'addEventListener', value: event });
  }

  pause(): void {
    this.send({ method: 'pause' });
  }

  play(): void {
    this.send({ method: 'play' });
  }

  mute(): void {
    this.send({ method: 'mute' });
  }

  setCurrentTime(seconds: number): void {
    this.send({ method: 'setCurrentTime', value: seconds });
  }

  destroy(): void {
    window.removeEventListener('message', this.handleMessage);
    this.listeners.clear();
    this.queue.length = 0;
  }

  private readonly handleMessage = (event: MessageEvent): void => {
    if (event.source !== this.iframe.contentWindow || event.origin !== this.origin) return;

    let data: InboundMessage;
    try {
      data = typeof event.data === 'string' ? (JSON.parse(event.data) as InboundMessage) : event.data;
    } catch {
      return;
    }
    if (data?.context !== 'player.js' || typeof data.event !== 'string') return;

    if (data.event === 'ready' && !this.ready) {
      this.ready = true;
      for (const queued of this.queue.splice(0)) this.post(queued);
    }

    const eventName = data.event as BunnyPlayerEvent;
    for (const listener of this.listeners.get(eventName) ?? []) {
      listener(data.value);
    }
  };

  private send(message: OutboundMessage): void {
    // player.js requires the ready subscription to be sent before the ready
    // event itself; every other message is queued until that event arrives.
    if (!this.ready && message.value !== 'ready') {
      this.queue.push(message);
      return;
    }
    this.post(message);
  }

  private post(message: OutboundMessage): void {
    this.iframe.contentWindow?.postMessage(
      JSON.stringify({ ...message, context: 'player.js', version: '0.1.0' }),
      this.origin,
    );
  }
}
