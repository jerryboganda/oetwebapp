import { act, renderHook } from '@testing-library/react';
import { ApiError } from '@/lib/api/client';
import type { LiveVoicePreflight } from '@/lib/api/speaking-live-voice';

const { mockPreflight, mockOffer, mockToken, mockTurn, mockTranscript } = vi.hoisted(() => ({
  mockPreflight: vi.fn(),
  mockOffer: vi.fn(),
  mockToken: vi.fn(),
  mockTurn: vi.fn(),
  mockTranscript: vi.fn(),
}));

vi.mock('@/lib/api/speaking-live-voice', () => ({
  getLiveVoicePreflight: mockPreflight,
  createOpenAiLiveOffer: mockOffer,
  createGeminiLiveToken: mockToken,
  persistLiveVoiceTurn: mockTurn,
  persistLiveVoiceTranscript: mockTranscript,
}));

import {
  CONNECT_TIMEOUT_MS,
  LIVE_VOICE_UNAVAILABLE,
  useSpeakingRealtimeVoice,
  type UseSpeakingRealtimeVoiceResult,
} from '../useSpeakingRealtimeVoice';

// jsdom has no WebRTC, Web Audio or microphone: these fakes stand in for them. Like a browser, a
// WebSocket delivers its close event after close() returned, and a peer connection fires no event
// when it is closed locally.

class FakeTrack {
  readyState = 'live';
  stop = vi.fn(() => {
    this.readyState = 'ended';
  });
}

class FakeStream {
  tracks = [new FakeTrack()];
  getTracks() {
    return this.tracks;
  }
  getAudioTracks() {
    return this.tracks;
  }
}

class FakeNode {
  fftSize = 256;
  gain = { value: 1 };
  onaudioprocess: unknown = null;
  connect = vi.fn();
  disconnect = vi.fn();
  getByteTimeDomainData(values: Uint8Array) {
    values.fill(128);
  }
}

class FakeAudioContext {
  static instances: FakeAudioContext[] = [];
  // WebKit and WebViews can leave resume() pending: it answers only when releaseResume() runs.
  static holdResume = false;
  static releaseResume: (() => void) | null = null;
  sampleRate = 48_000;
  currentTime = 0;
  destination = {};
  closed = false;
  constructor() {
    FakeAudioContext.instances.push(this);
  }
  resume() {
    if (!FakeAudioContext.holdResume) return Promise.resolve();
    return new Promise<void>((resolve) => {
      FakeAudioContext.releaseResume = resolve;
    });
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
}

class FakeChannel {
  readyState = 'connecting';
  onopen: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: (() => void) | null = null;
  closed = false;
  // GPT-Live answers session.close with session.closed, after the last transcript deltas.
  send = vi.fn((data: string) => {
    if (JSON.parse(data).type === 'session.close') queueMicrotask(() => this.emit({ type: 'session.closed' }));
  });
  close() {
    this.closed = true;
    this.readyState = 'closed';
  }
  emit(event: Record<string, unknown>) {
    this.onmessage?.({ data: JSON.stringify(event) });
  }
}

type PeerMode = 'open' | 'never' | 'fail';

class FakePeer {
  static instances: FakePeer[] = [];
  static mode: PeerMode = 'open';
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
    if (FakePeer.mode === 'open') {
      queueMicrotask(() => {
        if (!this.channel) return;
        this.channel.readyState = 'open';
        this.channel.onopen?.();
      });
    }
    if (FakePeer.mode === 'fail') {
      queueMicrotask(() => {
        this.connectionState = 'failed';
        this.onconnectionstatechange?.();
      });
    }
  }
  close() {
    this.closed = true;
  }
}

type SocketMode = 'ready' | 'error' | 'close';

class FakeSocket {
  static OPEN = 1;
  static CONNECTING = 0;
  static CLOSING = 2;
  static CLOSED = 3;
  static instances: FakeSocket[] = [];
  static mode: SocketMode = 'ready';
  readyState = 0;
  binaryType = '';
  sent: string[] = [];
  onopen: (() => void) | null = null;
  onmessage: ((event: { data: string }) => void) | null = null;
  onerror: (() => void) | null = null;
  onclose: ((event: { code: number }) => void) | null = null;
  constructor(readonly url: string) {
    FakeSocket.instances.push(this);
    queueMicrotask(() => this.play());
  }
  private play() {
    if (FakeSocket.mode === 'ready') {
      this.readyState = 1;
      this.onopen?.();
      this.message({ setupComplete: {} });
    } else if (FakeSocket.mode === 'error') {
      this.readyState = 3;
      this.onerror?.();
    } else {
      this.readyState = 3;
      this.onclose?.({ code: 1008 });
    }
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
}

const streams: FakeStream[] = [];
const getUserMedia = vi.fn();
let warn: { mock: { calls: unknown[][] } };

const preflight = (overrides: Partial<LiveVoicePreflight> = {}): LiveVoicePreflight => ({
  provider: 'openai',
  providerDisplayName: 'OpenAI GPT-Live',
  model: 'gpt-live-1',
  disclosure: 'disclosure',
  retentionDays: 30,
  sessionId: 's1',
  rolePlayCardId: 'c1',
  candidates: ['openai', 'gemini'],
  ...overrides,
});

const offerAnswer = (providerSessionId = 'oai-session-1') => ({
  provider: 'openai',
  model: 'gpt-live-1',
  providerSessionId,
  answerSdp: 'v=0 answer',
});

const providerDown = () => new ApiError(
  503,
  'live_voice_provider_unavailable',
  'The realtime voice provider could not start this conversation. Please retry.',
  false,
);

type Voice = { current: UseSpeakingRealtimeVoiceResult };

// Lets everything that is already settled (mocked API calls, fake events) run and React re-render.
async function flush(ms = 0) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

async function mount(sessionId = 's1', forced?: 'openai' | 'gemini') {
  const seen: string[] = [];
  const rendered = renderHook(
    ({ id }: { id: string }) => {
      const voice = useSpeakingRealtimeVoice(id, forced);
      seen.push(voice.connection);
      return voice;
    },
    { initialProps: { id: sessionId } },
  );
  await flush();
  return { ...rendered, seen };
}

async function startVoice(voice: Voice) {
  let started = false;
  await act(async () => {
    started = await voice.current.start();
  });
  return started;
}

async function stopVoice(voice: Voice) {
  let stopped = false;
  await act(async () => {
    stopped = await voice.current.stop();
  });
  return stopped;
}

const openAiChannel = (index = 0) => {
  const channel = FakePeer.instances[index].channel;
  if (!channel) throw new Error('The OpenAI peer has no data channel.');
  return channel;
};

async function candidateSays(text: string, index = 0) {
  await act(async () => {
    openAiChannel(index).emit({ type: 'session.input_transcript.delta', delta: text, start_ms: 0, end_ms: 400 });
  });
}

describe('useSpeakingRealtimeVoice provider failover', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    warn = vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    for (const fn of [mockPreflight, mockOffer, mockToken, mockTurn, mockTranscript, getUserMedia]) fn.mockReset();
    mockPreflight.mockResolvedValue(preflight());
    mockOffer.mockResolvedValue(offerAnswer());
    mockToken.mockResolvedValue({
      provider: 'gemini',
      model: 'models/gemini-live',
      providerSessionId: 'gem-session-1',
      webSocketUrl: 'wss://live.example.test/socket?access_token=secret',
      expiresAt: '2026-09-30T12:00:00Z',
    });
    mockTurn.mockResolvedValue({ sessionId: 's1', sequenceNumber: 1, duplicate: false, advisoryStatus: null });
    mockTranscript.mockResolvedValue({ transcriptId: 't1', provider: 'realtime-openai', wordCount: 3, meanConfidence: 0, generatedAt: '2026-09-30T12:00:00Z' });
    streams.length = 0;
    getUserMedia.mockImplementation(async () => {
      const stream = new FakeStream();
      streams.push(stream);
      return stream;
    });
    FakePeer.instances = [];
    FakePeer.mode = 'open';
    FakeSocket.instances = [];
    FakeSocket.mode = 'ready';
    FakeAudioContext.instances = [];
    FakeAudioContext.holdResume = false;
    FakeAudioContext.releaseResume = null;
    vi.stubGlobal('RTCPeerConnection', FakePeer);
    vi.stubGlobal('WebSocket', FakeSocket);
    vi.stubGlobal('AudioContext', FakeAudioContext);
    vi.stubGlobal('requestAnimationFrame', vi.fn(() => 1));
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia } });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: undefined });
  });

  it('fails over to the second provider after a 503 without an error, with one microphone prompt and one create call each', async () => {
    mockOffer.mockRejectedValue(providerDown());
    const { result, seen } = await mount();
    expect(result.current.connection).toBe('ready');

    expect(await startVoice(result)).toBe(true);

    expect(result.current.connection).toBe('connected');
    expect(result.current.provider).toBe('gemini');
    expect(result.current.failedOver).toBe(true);
    expect(result.current.error).toBeNull();
    expect(seen).not.toContain('error');
    expect(getUserMedia).toHaveBeenCalledTimes(1);
    expect(mockOffer).toHaveBeenCalledTimes(1);
    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(FakePeer.instances).toHaveLength(1);
    expect(FakePeer.instances[0].closed).toBe(true);
    expect(FakeSocket.instances).toHaveLength(1);
    expect(streams[0].tracks[0].stop).not.toHaveBeenCalled();
  });

  it('saves the transcript under the provider that connected and its own session id, never the failed one', async () => {
    mockOffer.mockRejectedValue(providerDown());
    const { result } = await mount();
    await startVoice(result);
    await act(async () => {
      FakeSocket.instances[0].message({ serverContent: { inputTranscription: { text: 'How can I help you today' } } });
      FakeSocket.instances[0].message({ serverContent: { outputTranscription: { text: 'My chest hurts' }, turnComplete: true } });
    });

    expect(await stopVoice(result)).toBe(true);

    expect(mockTranscript).toHaveBeenCalledTimes(1);
    expect(mockTranscript).toHaveBeenCalledWith('s1', {
      provider: 'gemini',
      providerSessionId: 'gem-session-1',
      segments: [
        expect.objectContaining({ speaker: 'candidate', text: 'How can I help you today' }),
        expect.objectContaining({ speaker: 'patient', text: 'My chest hurts' }),
      ],
    });
    expect(mockTurn).toHaveBeenCalledWith('s1', expect.objectContaining({
      provider: 'gemini',
      providerSessionId: 'gem-session-1',
      candidateText: 'How can I help you today',
      patientText: 'My chest hurts',
    }));
    expect(JSON.stringify([...mockTranscript.mock.calls, ...mockTurn.mock.calls])).not.toContain('oai-session-1');
    expect(result.current.ended).toBe(true);
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();
  });

  it('ends in one generic error when every provider fails, with everything released and nothing to save', async () => {
    mockOffer.mockRejectedValue(providerDown());
    mockToken.mockRejectedValue(providerDown());
    const { result } = await mount();

    expect(await startVoice(result)).toBe(false);

    expect(result.current.connection).toBe('error');
    expect(result.current.error).toBe(LIVE_VOICE_UNAVAILABLE);
    expect(result.current.error).not.toMatch(/openai|gemini|provider/i);
    expect(mockOffer).toHaveBeenCalledTimes(1);
    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();
    expect(FakeAudioContext.instances[0].closed).toBe(true);
    expect(FakePeer.instances.every((peer) => peer.closed)).toBe(true);

    // The exam page must be able to move on: there is nothing to save and no request to make.
    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript).not.toHaveBeenCalled();
    expect(mockTurn).not.toHaveBeenCalled();
  });

  it('logs only the error code of a failed provider: no token, no SDP, no server text', async () => {
    mockOffer.mockRejectedValue(providerDown());
    mockToken.mockRejectedValue(providerDown());
    const { result } = await mount();

    await startVoice(result);

    const logged = JSON.stringify(warn.mock.calls);
    expect(logged).toContain('live_voice_provider_unavailable');
    expect(logged).not.toMatch(/access_token|secret|v=0|could not start this conversation/i);
  });

  it('falls back to the other provider when the browser has no WebRTC', async () => {
    vi.stubGlobal('RTCPeerConnection', undefined);
    const { result } = await mount();

    expect(await startVoice(result)).toBe(true);

    expect(mockOffer).not.toHaveBeenCalled();
    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(result.current.provider).toBe('gemini');
    expect(result.current.failedOver).toBe(true);
  });

  it.each([
    { name: 'consent is missing', error: new ApiError(409, 'live_voice_consent_required', 'Consent is required before live voice.', false), text: 'Consent is required before live voice.' },
    { name: 'the role-play time is over', error: new ApiError(409, 'live_voice_time_limit_reached', 'The time for this role-play has ended.', false), text: 'The time for this role-play has ended.' },
    { name: 'the session limit is reached', error: new ApiError(409, 'live_voice_session_limit_reached', 'This role-play cannot open another live session.', false), text: 'This role-play cannot open another live session.' },
    { name: 'the offer is invalid', error: new ApiError(400, 'live_voice_sdp_too_large', 'The connection offer was not valid.', false), text: 'The connection offer was not valid.' },
  ])('does not fail over when $name, and keeps the server text', async ({ error, text }) => {
    mockOffer.mockRejectedValue(error);
    const { result } = await mount();

    expect(await startVoice(result)).toBe(false);

    expect(result.current.connection).toBe('error');
    expect(result.current.error).toBe(text);
    expect(mockToken).not.toHaveBeenCalled();
    expect(FakeSocket.instances).toHaveLength(0);
  });

  it('retries our own 429 once on the same provider after a pause, and never fails over', async () => {
    const limited = () => new ApiError(429, 'rate_limited', 'Too many requests. Please try again later.', true);
    mockOffer.mockRejectedValue(limited());
    const { result } = await mount();

    let started!: Promise<boolean>;
    await act(async () => {
      started = result.current.start();
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(mockOffer).toHaveBeenCalledTimes(1);
    await flush(1_400);
    expect(mockOffer).toHaveBeenCalledTimes(1);
    await flush(100);
    expect(mockOffer).toHaveBeenCalledTimes(2);

    await act(async () => {
      expect(await started).toBe(false);
    });
    expect(mockToken).not.toHaveBeenCalled();
    expect(result.current.error).toBe('Too many requests. Please wait a moment and try again.');
    expect(result.current.connection).toBe('error');
  });

  it('connects on the same provider when the 429 clears on the retry', async () => {
    mockOffer
      .mockRejectedValueOnce(new ApiError(429, 'rate_limited', 'Too many requests. Please try again later.', true))
      .mockResolvedValueOnce(offerAnswer());
    const { result } = await mount();

    let started!: Promise<boolean>;
    await act(async () => {
      started = result.current.start();
      await vi.advanceTimersByTimeAsync(1_500);
    });
    await act(async () => {
      expect(await started).toBe(true);
    });

    expect(mockOffer).toHaveBeenCalledTimes(2);
    expect(mockToken).not.toHaveBeenCalled();
    expect(result.current.provider).toBe('openai');
    expect(result.current.failedOver).toBe(false);
  });

  it.each([
    { name: 'an older server names no candidates', pre: preflight({ candidates: undefined }), forced: undefined },
    { name: 'the server lists a single candidate', pre: preflight({ candidates: ['openai'] }), forced: undefined },
    { name: 'the run is pinned by the server', pre: preflight({ candidates: ['openai'], pinned: true }), forced: undefined },
    { name: 'the page forces a provider', pre: preflight({ candidates: ['openai', 'gemini'] }), forced: 'openai' as const },
  ])('makes a single attempt when $name', async ({ pre, forced }) => {
    mockPreflight.mockResolvedValue(pre);
    mockOffer.mockRejectedValue(providerDown());
    const { result } = await mount('s1', forced);

    expect(await startVoice(result)).toBe(false);

    expect(mockOffer).toHaveBeenCalledTimes(1);
    expect(mockToken).not.toHaveBeenCalled();
    expect(result.current.error).toBe(LIVE_VOICE_UNAVAILABLE);
  });

  it('an abandoned Gemini socket cannot disturb the provider that took over', async () => {
    mockPreflight.mockResolvedValue(preflight({ provider: 'gemini', candidates: ['gemini', 'openai'] }));
    FakeSocket.mode = 'error';
    const { result, seen } = await mount();

    expect(await startVoice(result)).toBe(true);
    expect(result.current.provider).toBe('openai');

    // The closed socket's close event arrives late, as it does in a browser.
    await flush();
    expect(FakeSocket.instances[0].onclose).toBeNull();
    expect(FakeSocket.instances[0].onmessage).toBeNull();
    expect(result.current.connection).toBe('connected');
    expect(result.current.error).toBeNull();
    expect(seen).not.toContain('error');
  });

  it('never saves a transcript under a provider session that did not connect', async () => {
    FakePeer.mode = 'fail';
    mockToken.mockRejectedValue(providerDown());
    const { result } = await mount();

    expect(await startVoice(result)).toBe(false);
    // The OpenAI session was created, but its link never came up.
    expect(mockOffer).toHaveBeenCalledTimes(1);

    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript).not.toHaveBeenCalled();
  });

  it('stop() skips the empty transcript of a connected but silent session, and still closes the provider', async () => {
    const { result } = await mount();
    await startVoice(result);
    const channel = openAiChannel();

    expect(await stopVoice(result)).toBe(true);

    expect(channel.send).toHaveBeenCalledWith(JSON.stringify({ type: 'session.close' }));
    expect(mockTranscript).not.toHaveBeenCalled();
    expect(mockTurn).not.toHaveBeenCalled();
    expect(result.current.connection).toBe('ended');
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();
  });

  it('a start abandoned by unmount opens nothing more and releases the microphone', async () => {
    let resolveOffer!: (value: unknown) => void;
    mockOffer.mockReturnValue(new Promise((resolve) => {
      resolveOffer = resolve;
    }));
    const { result, unmount } = await mount();

    let started!: Promise<boolean>;
    await act(async () => {
      started = result.current.start();
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(mockOffer).toHaveBeenCalledTimes(1);

    unmount();
    await act(async () => {
      resolveOffer(offerAnswer());
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(await started).toBe(false);
    expect(FakePeer.instances[0].remoteDescription).toBeNull();
    expect(FakePeer.instances[0].closed).toBe(true);
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();
    expect(FakeSocket.instances).toHaveLength(0);
    expect(mockToken).not.toHaveBeenCalled();
  });

  it('stop() during a start cancels it, returns true and puts the panel back to ready', async () => {
    let resolveOffer!: (value: unknown) => void;
    mockOffer.mockReturnValue(new Promise((resolve) => {
      resolveOffer = resolve;
    }));
    const { result } = await mount();

    let started!: Promise<boolean>;
    await act(async () => {
      started = result.current.start();
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(result.current.connection).toBe('connecting');

    expect(await stopVoice(result)).toBe(true);
    expect(result.current.connection).toBe('ready');
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();

    await act(async () => {
      resolveOffer(offerAnswer());
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(await started).toBe(false);
    expect(FakePeer.instances[0].remoteDescription).toBeNull();
    expect(mockTranscript).not.toHaveBeenCalled();
  });

  it('a superseded start does not tear down the start that replaced it', async () => {
    let resolveStale!: (value: unknown) => void;
    mockOffer.mockReturnValueOnce(new Promise((resolve) => {
      resolveStale = resolve;
    }));
    const { result } = await mount();

    let first!: Promise<boolean>;
    await act(async () => {
      first = result.current.start();
      await vi.advanceTimersByTimeAsync(0);
    });
    await stopVoice(result);
    expect(await startVoice(result)).toBe(true);

    await act(async () => {
      resolveStale(offerAnswer('oai-stale'));
      await vi.advanceTimersByTimeAsync(0);
    });

    expect(await first).toBe(false);
    expect(result.current.connection).toBe('connected');
    expect(result.current.provider).toBe('openai');
    expect(FakePeer.instances).toHaveLength(2);
    expect(FakePeer.instances[1].closed).toBe(false);
    expect(streams[1].tracks[0].stop).not.toHaveBeenCalled();
  });

  it('moves on when a provider never goes live within the connect deadline', async () => {
    FakePeer.mode = 'never';
    const { result } = await mount();

    let started!: Promise<boolean>;
    await act(async () => {
      started = result.current.start();
      await vi.advanceTimersByTimeAsync(0);
    });
    await flush(CONNECT_TIMEOUT_MS - 1);
    expect(mockToken).not.toHaveBeenCalled();
    await flush(1);
    await act(async () => {
      expect(await started).toBe(true);
    });

    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(result.current.provider).toBe('gemini');
    expect(result.current.failedOver).toBe(true);
    expect(FakePeer.instances[0].closed).toBe(true);
  });

  it('a Gemini socket closed by the provider before setup ends that attempt at once (no 15 s wait)', async () => {
    mockPreflight.mockResolvedValue(preflight({ provider: 'gemini', candidates: ['gemini', 'openai'] }));
    FakeSocket.mode = 'close';
    const { result } = await mount();

    // No timers are advanced: an attempt that waited for its deadline would hang here.
    expect(await startVoice(result)).toBe(true);

    expect(result.current.provider).toBe('openai');
    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(mockOffer).toHaveBeenCalledTimes(1);
  });

  it('overlapping stop() calls share one save under the committed provider and session id', async () => {
    const { result } = await mount();
    await startVoice(result);
    const channel = openAiChannel();
    await candidateSays('Hello doctor');

    let first!: Promise<boolean>;
    let second!: Promise<boolean>;
    await act(async () => {
      first = result.current.stop();
      second = result.current.stop();
      await Promise.all([first, second]);
    });

    expect(first).toBe(second);
    expect(await first).toBe(true);
    expect(channel.send).toHaveBeenCalledTimes(1);
    expect(mockTranscript).toHaveBeenCalledTimes(1);
    expect(mockTranscript).toHaveBeenCalledWith('s1', {
      provider: 'openai',
      providerSessionId: 'oai-session-1',
      segments: [expect.objectContaining({ speaker: 'candidate', text: 'Hello doctor' })],
    });
  });

  it('a start after a mid-conversation error releases the previous connection first', async () => {
    const { result } = await mount();
    await startVoice(result);
    const previous = FakePeer.instances[0];
    await act(async () => {
      previous.connectionState = 'failed';
      previous.onconnectionstatechange?.();
    });
    expect(result.current.connection).toBe('error');
    expect(previous.closed).toBe(false);

    expect(await startVoice(result)).toBe(true);

    expect(previous.closed).toBe(true);
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();
    expect(getUserMedia).toHaveBeenCalledTimes(2);
    expect(FakePeer.instances).toHaveLength(2);
    expect(FakePeer.instances[1].closed).toBe(false);
    expect(result.current.connection).toBe('connected');
  });

  it('does not switch providers once the conversation has begun', async () => {
    const { result } = await mount();
    await startVoice(result);
    await candidateSays('Hello');
    await act(async () => {
      openAiChannel().emit({ type: 'session.closed', reason: 'error' });
    });
    expect(result.current.connection).toBe('error');

    mockOffer.mockRejectedValue(providerDown());
    expect(await startVoice(result)).toBe(false);

    expect(mockToken).not.toHaveBeenCalled();
    expect(result.current.error).toBe(LIVE_VOICE_UNAVAILABLE);
  });

  it('treats a microphone refusal as a device problem, not a provider failure', async () => {
    getUserMedia.mockRejectedValue(new DOMException('denied', 'NotAllowedError'));
    const { result } = await mount();

    expect(await startVoice(result)).toBe(false);

    expect(result.current.connection).toBe('error');
    expect(result.current.micPermissionDenied).toBe(true);
    expect(result.current.error).toMatch(/microphone permission was blocked/i);
    // The live control is "Start speaking"; the shared microphone copy says "Start recording".
    expect(result.current.error).toMatch(/press Start speaking again/);
    expect(result.current.error).not.toMatch(/Start recording/);
    expect(mockOffer).not.toHaveBeenCalled();
    expect(mockToken).not.toHaveBeenCalled();
  });

  it.each(['stop', 'unmount'] as const)('a microphone opened while the audio context is still resuming is released by %s', async (how) => {
    FakeAudioContext.holdResume = true;
    const { result, unmount } = await mount();

    let started!: Promise<boolean>;
    await act(async () => {
      started = result.current.start();
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(streams).toHaveLength(1);
    expect(streams[0].tracks[0].stop).not.toHaveBeenCalled();

    if (how === 'stop') expect(await stopVoice(result)).toBe(true);
    else unmount();

    // resume() has still not answered, yet the microphone and its context are already released.
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();
    expect(FakeAudioContext.instances[0].closed).toBe(true);

    await act(async () => {
      FakeAudioContext.releaseResume?.();
      await vi.advanceTimersByTimeAsync(0);
    });
    expect(await started).toBe(false);
    expect(mockOffer).not.toHaveBeenCalled();
    expect(FakePeer.instances).toHaveLength(0);
  });

  it('stop() resolves true when the server will never take the transcript, false when a retry is worthwhile', async () => {
    const { result } = await mount();
    await startVoice(result);
    await candidateSays('Hello');

    mockTranscript.mockRejectedValueOnce(new ApiError(503, 'internal_server_error', 'Server encountered an issue.', true));
    expect(await stopVoice(result)).toBe(false);
    expect(result.current.error).toBe('The live voice transcript could not be saved. Please try again.');
    expect(result.current.connection).toBe('error');

    mockTranscript.mockRejectedValueOnce(new ApiError(409, 'live_voice_transcript_window_closed', 'The time for this role-play has ended.', false));
    expect(await stopVoice(result)).toBe(true);
    expect(result.current.error).toBe('The time for this role-play has ended.');
    expect(mockTranscript).toHaveBeenCalledTimes(2);
    expect(mockTranscript.mock.calls[1][1]).toEqual(mockTranscript.mock.calls[0][1]);

    // Nothing is left to save, so a further stop() costs no request.
    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript).toHaveBeenCalledTimes(2);
  });

  it('stop() keeps the transcript for a retry when the sign-in has expired (401): the same request succeeds after re-auth', async () => {
    const { result } = await mount();
    await startVoice(result);
    await candidateSays('Hello');

    mockTranscript.mockRejectedValueOnce(new ApiError(401, 'not_authenticated', 'Unauthorized', false));
    expect(await stopVoice(result)).toBe(false);
    expect(result.current.connection).toBe('error');
    expect(result.current.error).toBe('Your session expired. Please sign in again.');
    expect(result.current.ended).toBe(false);

    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript).toHaveBeenCalledTimes(2);
    expect(mockTranscript.mock.calls[1]).toEqual(mockTranscript.mock.calls[0]);
    expect(result.current.ended).toBe(true);
  });

  it('retries the same transcript after a transient failure and then saves it', async () => {
    const { result } = await mount();
    await startVoice(result);
    await candidateSays('Hello');

    mockTranscript.mockRejectedValueOnce(new ApiError(0, 'network_error', 'Unable to connect to the server.', true));
    expect(await stopVoice(result)).toBe(false);
    // The conversation is over: the microphone is already released while the save is retried.
    expect(streams[0].tracks[0].stop).toHaveBeenCalled();

    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript).toHaveBeenCalledTimes(2);
    expect(mockTranscript.mock.calls[1]).toEqual(mockTranscript.mock.calls[0]);
    expect(result.current.ended).toBe(true);
    expect(result.current.connection).toBe('ended');
  });

  it('a turn row that cannot be saved mid-conversation is advisory: no learner error, no unhandled rejection, code-only log', async () => {
    const unhandled: unknown[] = [];
    const onUnhandled = (reason: unknown) => {
      unhandled.push(reason);
    };
    process.on('unhandledRejection', onUnhandled);
    try {
      mockTurn.mockRejectedValue(new ApiError(503, 'internal_server_error', 'Server encountered an issue.', true));
      const { result } = await mount();
      await startVoice(result);
      await candidateSays('How can I help you today');
      await act(async () => {
        openAiChannel().emit({ type: 'session.output_transcript.delta', delta: 'My chest hurts', start_ms: 500, end_ms: 900 });
      });
      // The candidate speaks again after the patient: the finished turn is saved in the background.
      await act(async () => {
        openAiChannel().emit({ type: 'session.input_transcript.delta', delta: 'Since when?', start_ms: 1_000, end_ms: 1_400 });
      });
      await flush();

      expect(mockTurn).toHaveBeenCalledTimes(1);
      expect(result.current.error).toBeNull();
      expect(unhandled).toEqual([]);
      expect(JSON.stringify(warn.mock.calls)).toContain('internal_server_error');

      // The whole transcript is still saved when the conversation ends.
      expect(await stopVoice(result)).toBe(true);
      expect(mockTranscript).toHaveBeenCalledTimes(1);
    } finally {
      process.off('unhandledRejection', onUnhandled);
    }
  });

  it('a turn that cannot be saved does not keep the transcript from being saved', async () => {
    const { result } = await mount();
    await startVoice(result);
    await candidateSays('Hello');
    mockTurn.mockRejectedValue(new ApiError(409, 'live_voice_transcript_window_closed', 'The time for this role-play has ended.', false));

    expect(await stopVoice(result)).toBe(true);

    expect(mockTranscript).toHaveBeenCalledTimes(1);
  });

  it('shows one generic message when the preflight fails, without provider names', async () => {
    mockPreflight.mockRejectedValue(new ApiError(
      503,
      'live_voice_provider_unverified',
      'The openai realtime voice model has not passed the live account probe yet. Please retry shortly.',
      true,
    ));
    const { result } = await mount();

    expect(result.current.connection).toBe('error');
    expect(result.current.preflight).toBeNull();
    expect(result.current.error).toBe(LIVE_VOICE_UNAVAILABLE);
  });

  it('a retry after every provider failed asks the server for a fresh order', async () => {
    mockOffer.mockRejectedValue(providerDown());
    mockToken.mockRejectedValue(providerDown());
    const { result } = await mount();
    await startVoice(result);
    expect(mockPreflight).toHaveBeenCalledTimes(1);

    mockPreflight.mockResolvedValue(preflight({ provider: 'gemini', candidates: ['gemini'] }));
    mockToken.mockResolvedValue({ provider: 'gemini', model: 'm', providerSessionId: 'gem-session-2', webSocketUrl: 'wss://live.example.test/s', expiresAt: '2026-09-30T12:00:00Z' });

    expect(await startVoice(result)).toBe(true);

    expect(mockPreflight).toHaveBeenCalledTimes(2);
    expect(result.current.provider).toBe('gemini');
    expect(mockOffer).toHaveBeenCalledTimes(1);
  });

  it('does not carry one card into the next session: each is saved to its own session id, alone', async () => {
    const { result, rerender } = await mount('card-a');
    await startVoice(result);
    await candidateSays('Card A words');

    mockOffer.mockResolvedValueOnce(offerAnswer('oai-session-2'));
    await act(async () => {
      rerender({ id: 'card-b' });
    });
    // The card that was left is saved on its way out (after the provider's close handshake window).
    await flush(5_000);
    expect(mockTranscript).toHaveBeenCalledWith('card-a', expect.objectContaining({ provider: 'openai', providerSessionId: 'oai-session-1' }));
    expect(result.current.captions).toEqual([]);

    expect(await startVoice(result)).toBe(true);
    await candidateSays('Card B words', 1);
    expect(await stopVoice(result)).toBe(true);

    const last = mockTranscript.mock.calls.at(-1);
    expect(last?.[0]).toBe('card-b');
    expect(last?.[1]).toEqual({
      provider: 'openai',
      providerSessionId: 'oai-session-2',
      segments: [expect.objectContaining({ text: 'Card B words' })],
    });
    expect(JSON.stringify(last)).not.toContain('Card A words');
  });
});
