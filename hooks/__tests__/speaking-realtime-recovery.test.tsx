import { act, renderHook } from '@testing-library/react';
import { ApiError } from '@/lib/api/client';
import type { LiveVoicePreflight } from '@/lib/api/speaking-live-voice';
import { FakeAudioContext, FakeNode, FakePeer, FakeSocket, FakeStream } from './helpers/realtime-fakes';

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
  createSpeechTracker,
  MAX_RECOVERIES,
  STALL_MS,
  useSpeakingRealtimeVoice,
  type UseSpeakingRealtimeVoiceResult,
} from '../useSpeakingRealtimeVoice';

let frameCallback: FrameRequestCallback | null = null;
const getUserMedia = vi.fn();

const preflight = (overrides: Partial<LiveVoicePreflight> = {}): LiveVoicePreflight => ({
  provider: 'gemini',
  providerDisplayName: 'Gemini Live',
  model: 'models/gemini-live',
  disclosure: 'disclosure',
  retentionDays: 30,
  sessionId: 's1',
  rolePlayCardId: 'c1',
  candidates: ['gemini', 'openai'],
  ...overrides,
});

const token = (providerSessionId: string) => ({
  provider: 'gemini',
  model: 'models/gemini-live',
  providerSessionId,
  webSocketUrl: `wss://live.example.test/socket?access_token=${providerSessionId}`,
  expiresAt: '2026-10-01T12:00:00Z',
});

const offerAnswer = (providerSessionId: string) => ({
  provider: 'openai',
  model: 'gpt-live-1',
  providerSessionId,
  answerSdp: 'v=0 answer',
});

const providerDown = () => new ApiError(503, 'live_voice_provider_unavailable', 'The realtime voice provider could not start this conversation. Please retry.', false);
const timeOver = () => new ApiError(409, 'live_voice_time_limit_reached', 'The time for this role-play has ended.', false);

type Voice = { current: UseSpeakingRealtimeVoiceResult };

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

// Feeds the meter one frame every 100 ms: speech (clearly above the speech level) or silence.
async function mic(kind: 'speech' | 'quiet', ms: number) {
  FakeNode.amplitude = kind === 'speech' ? 20 : 0;
  for (let elapsed = 0; elapsed < ms; elapsed += 100) {
    await act(async () => {
      await vi.advanceTimersByTimeAsync(100);
      frameCallback?.(0);
    });
  }
}

async function mount(forced?: 'openai' | 'gemini') {
  const rendered = renderHook(() => useSpeakingRealtimeVoice('s1', forced));
  await advance(0);
  return rendered;
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

const socket = (index: number) => FakeSocket.instances[index];
const channel = (index: number) => {
  const found = FakePeer.instances[index].channel;
  if (!found) throw new Error('The OpenAI peer has no data channel.');
  return found;
};

// The provider kills whichever link is live.
async function dropLink(voice: Voice) {
  await act(async () => {
    if (voice.current.provider === 'openai') channel(FakePeer.instances.length - 1).onclose?.();
    else socket(FakeSocket.instances.length - 1).serverClose(1011);
  });
  await advance(0);
}

async function geminiSays(index: number, serverContent: Record<string, unknown>) {
  await act(async () => {
    socket(index).message({ serverContent });
  });
}

describe('useSpeakingRealtimeVoice mid-session recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    for (const fn of [mockPreflight, mockOffer, mockToken, mockTurn, mockTranscript, getUserMedia]) fn.mockReset();
    mockPreflight.mockResolvedValue(preflight());
    mockOffer.mockImplementation(async () => offerAnswer(`oai-session-${FakePeer.instances.length}`));
    let minted = 0;
    mockToken.mockImplementation(async () => token(`gem-session-${++minted}`));
    mockTurn.mockResolvedValue({ sessionId: 's1', sequenceNumber: 1, duplicate: false, advisoryStatus: null });
    mockTranscript.mockResolvedValue({ transcriptId: 't1', provider: 'realtime-gemini', wordCount: 3, meanConfidence: 0, generatedAt: '2026-10-01T12:00:00Z' });
    getUserMedia.mockImplementation(async () => new FakeStream());
    FakePeer.instances = [];
    FakeSocket.instances = [];
    FakeAudioContext.instances = [];
    FakeNode.amplitude = 0;
    frameCallback = null;
    vi.stubGlobal('RTCPeerConnection', FakePeer);
    vi.stubGlobal('WebSocket', FakeSocket);
    vi.stubGlobal('AudioContext', FakeAudioContext);
    vi.stubGlobal('requestAnimationFrame', vi.fn((callback: FrameRequestCallback) => {
      frameCallback = callback;
      return 1;
    }));
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia } });
  });

  afterEach(() => {
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: undefined });
  });

  it('restores a Gemini link that closed mid-conversation on a new provider session and keeps the transcript', async () => {
    const { result } = await mount();
    expect(await startVoice(result)).toBe(true);
    await geminiSays(0, { inputTranscription: { text: 'How can I help you today' } });
    await geminiSays(0, { outputTranscription: { text: 'My chest hurts' }, turnComplete: true });

    await act(async () => {
      socket(0).serverClose(1011);
    });
    await advance(0);

    expect(mockToken).toHaveBeenCalledTimes(2);
    expect(FakeSocket.instances).toHaveLength(2);
    expect(result.current.connection).toBe('connected');
    expect(result.current.error).toBeNull();
    expect(result.current.recovering).toBe(false);
    expect(result.current.recoveries).toBe(1);
    // The turn in progress is saved BEFORE the new session is minted, so the server can replay it.
    expect(mockTurn.mock.invocationCallOrder[0]).toBeLessThan(mockToken.mock.invocationCallOrder[1]);
    // The microphone was never released and feeds the new link.
    expect(getUserMedia).toHaveBeenCalledTimes(1);

    await geminiSays(1, { inputTranscription: { text: 'Where is the pain' } });
    await geminiSays(1, { outputTranscription: { text: 'In the middle' }, turnComplete: true });
    expect(await stopVoice(result)).toBe(true);

    expect(mockTranscript).toHaveBeenCalledTimes(1);
    expect(mockTranscript).toHaveBeenCalledWith('s1', {
      provider: 'gemini',
      providerSessionId: 'gem-session-2',
      segments: [
        expect.objectContaining({ speaker: 'candidate', text: expect.stringContaining('How can I help you today') }),
        expect.objectContaining({ speaker: 'patient', text: expect.stringContaining('My chest hurts') }),
        expect.objectContaining({ speaker: 'candidate', text: expect.stringContaining('Where is the pain') }),
        expect.objectContaining({ speaker: 'patient', text: expect.stringContaining('In the middle') }),
      ],
    });
  });

  it('restores an OpenAI link whose data channel closed', async () => {
    mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }));
    const { result } = await mount();
    expect(await startVoice(result)).toBe(true);
    expect(result.current.provider).toBe('openai');

    await act(async () => {
      channel(0).onclose?.();
    });
    await advance(0);

    expect(mockOffer).toHaveBeenCalledTimes(2);
    expect(FakePeer.instances).toHaveLength(2);
    expect(FakePeer.instances[0].closed).toBe(true);
    expect(result.current.connection).toBe('connected');
    expect(result.current.provider).toBe('openai');
    expect(result.current.failedOver).toBe(false);
    expect(result.current.recoveries).toBe(1);
  });

  it('gives a dropped peer connection 5 s to heal before restoring it, and restores a failed one at once', async () => {
    mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }));
    const { result } = await mount();
    await startVoice(result);

    await act(async () => {
      FakePeer.instances[0].setState('disconnected');
    });
    await advance(3_000);
    await act(async () => {
      FakePeer.instances[0].setState('connected');
    });
    await advance(10_000);
    expect(mockOffer).toHaveBeenCalledTimes(1);
    expect(result.current.recoveries).toBe(0);

    await act(async () => {
      FakePeer.instances[0].setState('disconnected');
    });
    await advance(5_100);
    await advance(0);
    expect(mockOffer).toHaveBeenCalledTimes(2);
    expect(result.current.recoveries).toBe(1);

    await advance(0);
    await act(async () => {
      FakePeer.instances[1].setState('failed');
    });
    await advance(0);
    // The second restore of a role-play tries the other provider first.
    expect(mockOffer).toHaveBeenCalledTimes(2);
    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(result.current.provider).toBe('gemini');
    expect(result.current.recoveries).toBe(2);
    expect(result.current.connection).toBe('connected');
  });

  it("restores on GPT-Live's connection_lost but never after a session the provider ended for time or content", async () => {
    mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }));
    const { result } = await mount();
    await startVoice(result);

    await act(async () => {
      channel(0).emit({ type: 'session.closed', reason: 'connection_lost' });
    });
    await advance(0);
    expect(result.current.recoveries).toBe(1);
    expect(result.current.connection).toBe('connected');

    await act(async () => {
      channel(1).emit({ type: 'session.closed', reason: 'expired' });
    });
    await advance(0);
    expect(result.current.recoveries).toBe(1);
    expect(mockOffer).toHaveBeenCalledTimes(2);
    expect(result.current.connection).toBe('error');
    expect(result.current.error).toMatch(/ended/i);
  });

  it('never restores a provider that was forced, so comparison runs measure it raw', async () => {
    const { result } = await mount('gemini');
    expect(await startVoice(result)).toBe(true);

    await act(async () => {
      socket(0).serverClose(1011);
    });
    await advance(0);

    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(result.current.recoveries).toBe(0);
    expect(result.current.connection).toBe('error');
    expect(result.current.error).not.toBeNull();
  });

  it(`restores at most ${MAX_RECOVERIES} times per role-play, then shows the error`, async () => {
    const { result } = await mount();
    await startVoice(result);

    for (let loss = 0; loss < MAX_RECOVERIES; loss += 1) {
      await dropLink(result);
      expect(result.current.connection).toBe('connected');
    }
    await dropLink(result);

    expect(result.current.recoveries).toBe(MAX_RECOVERIES);
    expect(mockToken.mock.calls.length + mockOffer.mock.calls.length).toBe(MAX_RECOVERIES + 1);
    expect(result.current.connection).toBe('error');
    expect(result.current.error).not.toBeNull();
  });

  it('on the second restore of a role-play tries the other provider first', async () => {
    const { result } = await mount();
    await startVoice(result);

    await act(async () => {
      socket(0).serverClose(1011);
    });
    await advance(0);
    expect(result.current.provider).toBe('gemini');
    mockOffer.mockResolvedValue(offerAnswer('oai-session-x'));

    await act(async () => {
      socket(1).serverClose(1011);
    });
    await advance(0);

    expect(mockOffer).toHaveBeenCalledTimes(1);
    expect(result.current.provider).toBe('openai');
    expect(result.current.failedOver).toBe(true);
    expect(result.current.connection).toBe('connected');
  });

  it('ends in one error when no provider can be restored, and does not loop', async () => {
    const { result } = await mount();
    await startVoice(result);
    mockToken.mockRejectedValue(providerDown());
    mockOffer.mockRejectedValue(providerDown());

    await act(async () => {
      socket(0).serverClose(1011);
    });
    await advance(0);

    expect(mockToken).toHaveBeenCalledTimes(2);
    expect(mockOffer).toHaveBeenCalledTimes(1);
    expect(result.current.recovering).toBe(false);
    expect(result.current.connection).toBe('error');
    expect(result.current.error).not.toBeNull();
  });

  it('does not try another provider after a definite server answer such as time over', async () => {
    const { result } = await mount();
    await startVoice(result);
    mockToken.mockRejectedValue(timeOver());

    await act(async () => {
      socket(0).serverClose(1011);
    });
    await advance(0);

    expect(mockToken).toHaveBeenCalledTimes(2);
    expect(mockOffer).not.toHaveBeenCalled();
    expect(result.current.connection).toBe('error');
    expect(result.current.error).toMatch(/ended/i);
  });

  it('saves the transcript under the provider session that was live when the learner leaves mid-restore', async () => {
    const { result } = await mount();
    await startVoice(result);
    await geminiSays(0, { inputTranscription: { text: 'How can I help you today' } });
    await geminiSays(0, { outputTranscription: { text: 'My chest hurts' }, turnComplete: true });
    let release!: (value: ReturnType<typeof token>) => void;
    mockToken.mockImplementationOnce(() => new Promise((resolve) => {
      release = resolve;
    }));

    await act(async () => {
      socket(0).serverClose(1011);
    });
    await advance(0);
    expect(result.current.recovering).toBe(true);

    expect(await stopVoice(result)).toBe(true);
    await act(async () => {
      release(token('gem-session-late'));
    });
    await advance(0);

    expect(FakeSocket.instances).toHaveLength(1);
    expect(mockTranscript).toHaveBeenCalledWith('s1', expect.objectContaining({ providerSessionId: 'gem-session-1' }));
    expect(result.current.recovering).toBe(false);
  });

  describe('a patient that stops answering', () => {
    async function candidateSpeaks(ms: number) {
      await mic('speech', ms);
      await mic('quiet', 800); // closes the burst
    }

    it(`restores the link when the patient is silent ${STALL_MS / 1000} s after the candidate finished a sentence`, async () => {
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(2_000);

      await advance(STALL_MS - 2_000);
      expect(result.current.recoveries).toBe(0);
      await advance(2_500);

      expect(mockToken).toHaveBeenCalledTimes(2);
      expect(result.current.recoveries).toBe(1);
      expect(result.current.connection).toBe('connected');
    });

    it('does not restore a patient who answered in time, and waits for the next sentence before it judges again', async () => {
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(2_000);
      await advance(6_000);
      await geminiSays(0, { outputTranscription: { text: 'My chest hurts' }, turnComplete: true });

      await advance(STALL_MS * 2);
      expect(result.current.recoveries).toBe(0);

      await candidateSpeaks(2_000);
      await advance(STALL_MS + 1_500);
      expect(result.current.recoveries).toBe(1);
    });

    it('counts the provider audio as an answer even before its transcript arrives', async () => {
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(2_000);
      await advance(5_000);
      await geminiSays(0, { modelTurn: { parts: [{ inlineData: { data: 'AAAA', mimeType: 'audio/pcm;rate=24000' } }] } });

      await advance(STALL_MS * 2);
      expect(result.current.recoveries).toBe(0);
    });

    it('ignores a cough, a click or a short word: the patient owes no answer to less than a sentence', async () => {
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(300);
      await candidateSpeaks(800);

      await advance(STALL_MS * 3);
      expect(result.current.recoveries).toBe(0);
      expect(mockToken).toHaveBeenCalledTimes(1);
    });

    it('never restores a forced provider, however long the patient stays silent', async () => {
      const { result } = await mount('gemini');
      await startVoice(result);
      await candidateSpeaks(2_000);

      await advance(STALL_MS * 3);
      expect(result.current.recoveries).toBe(0);
      expect(mockToken).toHaveBeenCalledTimes(1);
      expect(result.current.connection).toBe('connected');
    });
  });

  describe('Gemini candidate timing', () => {
    it('gives a candidate sentence the span of its microphone burst instead of a zero-length arrival time', async () => {
      const { result } = await mount();
      await startVoice(result);
      await mic('quiet', 500);
      await mic('speech', 1_500);
      await mic('quiet', 900);
      await geminiSays(0, { inputTranscription: { text: 'Good morning, how can I help you today' } });
      await geminiSays(0, { outputTranscription: { text: 'My chest hurts' }, turnComplete: true });
      expect(await stopVoice(result)).toBe(true);

      const segments = mockTranscript.mock.calls[0][1].segments as { speaker: string; startMs: number; endMs: number }[];
      const candidate = segments.find((segment) => segment.speaker === 'candidate');
      expect(candidate).toBeDefined();
      expect(candidate!.endMs - candidate!.startMs).toBeGreaterThanOrEqual(1_300);
      expect(candidate!.endMs - candidate!.startMs).toBeLessThanOrEqual(1_700);
      // In the saved order the candidate finished before the patient began.
      const patient = segments.find((segment) => segment.speaker === 'patient');
      expect(candidate!.endMs).toBeLessThanOrEqual(patient!.startMs);
    });

    it('stretches a sentence whose transcript arrives while the candidate is still talking, and does not reuse that burst for the next one', async () => {
      const { result } = await mount();
      await startVoice(result);
      await mic('speech', 1_000);
      await geminiSays(0, { inputTranscription: { text: 'Good morning' } });
      await mic('speech', 500);
      await mic('quiet', 900);
      await geminiSays(0, { outputTranscription: { text: 'Hello doctor' }, turnComplete: true });
      await mic('quiet', 4_000);
      await mic('speech', 1_200);
      await mic('quiet', 900);
      await geminiSays(0, { inputTranscription: { text: 'Where does it hurt' } });
      expect(await stopVoice(result)).toBe(true);

      const segments = mockTranscript.mock.calls[0][1].segments as { speaker: string; startMs: number; endMs: number }[];
      const candidates = segments.filter((segment) => segment.speaker === 'candidate');
      expect(candidates).toHaveLength(2);
      expect(candidates[0].endMs - candidates[0].startMs).toBeGreaterThanOrEqual(900);
      expect(candidates[1].startMs).toBeGreaterThan(candidates[0].endMs);
      expect(candidates[1].endMs - candidates[1].startMs).toBeGreaterThanOrEqual(1_000);
    });
  });
});

describe('createSpeechTracker', () => {
  it('reports a burst only after 700 ms of quiet, dated by its first and last loud sample', () => {
    const tracker = createSpeechTracker();
    expect(tracker.update(0.3, 1_000)).toBeNull();
    expect(tracker.update(0.02, 1_100)).toBeNull();
    expect(tracker.update(0.3, 1_500)).toBeNull();
    expect(tracker.active()).toEqual({ startMs: 1_000 });
    expect(tracker.update(0.01, 2_000)).toBeNull();
    expect(tracker.update(0.01, 2_250)).toEqual({ startMs: 1_000, endMs: 1_500 });
    expect(tracker.active()).toBeNull();
  });

  it('drops a burst shorter than 400 ms and forgets everything on reset', () => {
    const tracker = createSpeechTracker();
    tracker.update(0.3, 100);
    tracker.update(0.3, 300);
    expect(tracker.update(0, 1_200)).toBeNull();
    tracker.update(0.3, 2_000);
    tracker.reset();
    expect(tracker.active()).toBeNull();
    expect(tracker.update(0, 5_000)).toBeNull();
  });
});
