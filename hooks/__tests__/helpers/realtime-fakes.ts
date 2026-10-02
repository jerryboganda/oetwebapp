import { vi } from 'vitest';

// jsdom has no WebRTC, Web Audio or microphone: these fakes stand in for them in the live voice hook tests. Like a
// browser, a WebSocket delivers its close event after close() returned, and a peer connection fires no event when it
// is closed locally.

export class FakeTrack {
  readyState = 'live';
  stop = vi.fn(() => {
    this.readyState = 'ended';
  });
}

export class FakeStream {
  tracks = [new FakeTrack()];
  getTracks() {
    return this.tracks;
  }
  getAudioTracks() {
    return this.tracks;
  }
}

export class FakeNode {
  /** Offset from the silent sample value 128; the meter reads level = 3 x amplitude / 128 (20 is clearly speech). */
  static amplitude = 0;
  fftSize = 256;
  gain = { value: 1 };
  onaudioprocess: unknown = null;
  connect = vi.fn();
  disconnect = vi.fn();
  getByteTimeDomainData(values: Uint8Array) {
    values.fill(128 + FakeNode.amplitude);
  }
}

export class FakeAudioContext {
  static instances: FakeAudioContext[] = [];
  sampleRate = 48_000;
  currentTime = 0;
  destination = {};
  closed = false;
  constructor() {
    FakeAudioContext.instances.push(this);
  }
  resume() {
    return Promise.resolve();
  }
  close() {
    this.closed = true;
    return Promise.resolve();
  }
  createMediaStreamSource() {
    return new FakeNode();
  }
  createAnalyser() {
    return new FakeNode();
  }
  createScriptProcessor() {
    return new FakeNode();
  }
  createGain() {
    return new FakeNode();
  }
  createBuffer(_channels: number, length: number) {
    return { duration: length / 24_000, getChannelData: () => new Float32Array(length) };
  }
  // Every playback source the page created, so a test can tell whether audio was queued.
  sources: { start: ReturnType<typeof vi.fn> }[] = [];
  createBufferSource() {
    const source = { buffer: null, connect: vi.fn(), start: vi.fn(), stop: vi.fn(), addEventListener: vi.fn() };
    this.sources.push(source);
    return source;
  }
}

export class FakeChannel {
  readyState = 'connecting';
  onopen: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;
  closed = false;
  // GPT-Live answers session.close with session.closed, after the last transcript deltas.
  send = vi.fn((data: string) => {
    if (JSON.parse(data).type === 'session.close') queueMicrotask(() => this.emit({ type: 'session.closed', reason: 'close_requested' }));
  });
  close() {
    this.closed = true;
    this.readyState = 'closed';
  }
  emit(event: Record<string, unknown>) {
    this.onmessage?.({ data: JSON.stringify(event) });
  }
}

export class FakePeer {
  static instances: FakePeer[] = [];
  iceGatheringState = 'complete';
  connectionState = 'new';
  localDescription: { sdp: string } | null = null;
  remoteDescription: unknown = null;
  ontrack: unknown = null;
  onconnectionstatechange: (() => void) | null = null;
  channel: FakeChannel | null = null;
  closed = false;
  constructor() {
    FakePeer.instances.push(this);
  }
  addTrack() {}
  addEventListener() {}
  removeEventListener() {}
  createDataChannel() {
    this.channel = new FakeChannel();
    return this.channel;
  }
  async createOffer() {
    return { type: 'offer', sdp: 'v=0 offer' };
  }
  async setLocalDescription(description: { sdp: string }) {
    this.localDescription = description;
  }
  async setRemoteDescription(description: unknown) {
    this.remoteDescription = description;
    queueMicrotask(() => {
      if (!this.channel) return;
      this.channel.readyState = 'open';
      this.channel.onopen?.();
    });
  }
  close() {
    this.closed = true;
  }
  /** The link changes state on its own (the network died), as opposed to a local close(). */
  setState(state: string) {
    this.connectionState = state;
    this.onconnectionstatechange?.();
  }
}

export class FakeSocket {
  static OPEN = 1;
  static CONNECTING = 0;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: FakeSocket[] = [];
  readyState = 0;
  binaryType = '';
  sent: string[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: ((event: { code: number }) => void) | null = null;
  constructor(readonly url: string) {
    FakeSocket.instances.push(this);
    queueMicrotask(() => {
      this.readyState = 1;
      this.onopen?.();
      this.message({ setupComplete: {} });
    });
  }
  message(value: unknown) {
    this.onmessage?.({ data: JSON.stringify(value) });
  }
  send(data: string) {
    this.sent.push(data);
  }
  close() {
    this.readyState = 3;
    queueMicrotask(() => this.onclose?.({ code: 1005 }));
  }
  /** The provider closes the link (the hook's handlers see this like a real drop). */
  serverClose(code = 1011) {
    this.readyState = 3;
    this.onclose?.({ code });
  }
}
