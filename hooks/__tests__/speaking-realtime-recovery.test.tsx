import { act, cleanup, renderHook } from '@testing-library/react';
import { ApiError } from '@/lib/api/client';
import type { LiveVoicePreflight } from '@/lib/api/speaking-live-voice';
import { FakeAudioContext, FakeNode, FakePeer, FakeSocket, FakeStream } from './helpers/realtime-fakes';

const { mockPreflight, mockOffer, mockToken, mockTurn, mockTranscript, mockAudioCapture } = vi.hoisted(() => ({
  mockPreflight: vi.fn(),
  mockOffer: vi.fn(),
  mockToken: vi.fn(),
  mockTurn: vi.fn(),
  mockTranscript: vi.fn(),
  mockAudioCapture: vi.fn(),
}));

vi.mock('@/lib/api/speaking-live-voice', () => ({
  getLiveVoicePreflight: mockPreflight,
  createOpenAiLiveOffer: mockOffer,
  createGeminiLiveToken: mockToken,
  persistLiveVoiceTurn: mockTurn,
  persistLiveVoiceTranscript: mockTranscript,
  captureLiveVoiceAudioTurn: mockAudioCapture,
}));

import {
  createSpeechTracker,
  GEMINI_NUDGE_MS,
  GEMINI_STOP_DRAIN_MS,
  MAX_RECOVERIES,
  STALL_MS,
  useSpeakingRealtimeVoice,
  type UseSpeakingRealtimeVoiceResult,
} from '../useSpeakingRealtimeVoice';

let frameCallback: FrameRequestCallback | null = null;
const getUserMedia = vi.fn();

class FakeMediaRecorder {
  static instances: FakeMediaRecorder[] = [];
  state: RecordingState = 'inactive';
  mimeType = 'audio/webm;codecs=opus';
  ondataavailable: ((event: BlobEvent) => void) | null = null;
  onstop: ((event: Event) => void) | null = null;
  onerror: ((event: Event) => void) | null = null;

  constructor(_stream: MediaStream) {
    FakeMediaRecorder.instances.push(this);
  }

  start() {
    this.state = 'recording';
  }

  stop() {
    this.state = 'inactive';
    this.ondataavailable?.({ data: new Blob(['candidate voice'], { type: 'audio/webm' }) } as BlobEvent);
    this.onstop?.(new Event('stop') as Event);
  }
}

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

// `requested` is the provider the page asks the server to pin (?voiceProvider=); only a pinned:true preflight makes it count.
async function mount(requested?: 'openai' | 'gemini', sessionId = 's1') {
  const rendered = renderHook(({ id }) => useSpeakingRealtimeVoice(id, requested), {
    initialProps: { id: sessionId },
  });
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
    const pending = voice.current.stop();
    // A Gemini link is kept up for a moment so the last transcription lands (GEMINI_STOP_DRAIN_MS); a GPT-Live stop is quicker.
    await vi.advanceTimersByTimeAsync(GEMINI_STOP_DRAIN_MS);
    stopped = await pending;
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

// GPT-Live transcript deltas carry start_ms/end_ms counted from the start of THAT provider session.
async function openAiSays(index: number, speaker: 'candidate' | 'patient', delta: string, startMs: number, endMs: number) {
  await act(async () => {
    channel(index).emit({
      type: speaker === 'candidate' ? 'session.input_transcript.delta' : 'session.output_transcript.delta',
      delta,
      start_ms: startMs,
      end_ms: endMs,
    });
  });
}

type SavedSegment = { speaker: string; startMs: number; endMs: number; text: string };
const savedSegments = (call = 0) => mockTranscript.mock.calls[call][1].segments as SavedSegment[];

describe('useSpeakingRealtimeVoice mid-session recovery', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    for (const fn of [mockPreflight, mockOffer, mockToken, mockTurn, mockTranscript, mockAudioCapture, getUserMedia]) fn.mockReset();
    mockPreflight.mockResolvedValue(preflight());
    mockOffer.mockImplementation(async () => offerAnswer(`oai-session-${FakePeer.instances.length}`));
    let minted = 0;
    mockToken.mockImplementation(async () => token(`gem-session-${++minted}`));
    mockTurn.mockResolvedValue({ sessionId: 's1', sequenceNumber: 1, duplicate: false, advisoryStatus: null });
    mockTranscript.mockResolvedValue({ transcriptId: 't1', provider: 'realtime-gemini', wordCount: 3, meanConfidence: 0, generatedAt: '2026-10-01T12:00:00Z' });
    mockAudioCapture.mockResolvedValue({ recordingId: 'clip-1', mimeType: 'audio/webm', durationSeconds: 1 });
    getUserMedia.mockImplementation(async () => new FakeStream());
    FakePeer.instances = [];
    FakeSocket.instances = [];
    FakeAudioContext.instances = [];
    FakeMediaRecorder.instances = [];
    FakeNode.amplitude = 0;
    frameCallback = null;
    vi.stubGlobal('RTCPeerConnection', FakePeer);
    vi.stubGlobal('WebSocket', FakeSocket);
    vi.stubGlobal('AudioContext', FakeAudioContext);
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
    vi.stubGlobal('requestAnimationFrame', vi.fn((callback: FrameRequestCallback) => {
      frameCallback = callback;
      return 1;
    }));
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia } });
    // The hook keeps a refresh-safe copy of the conversation in sessionStorage: no test may inherit another's.
    window.sessionStorage.clear();
  });

  afterEach(async () => {
    // Unmount while the fake clock still runs: the stop() on the way out (the GPT-Live close handshake, the Gemini drain)
    // must finish here, not on a real timer inside the next test.
    cleanup();
    await vi.advanceTimersByTimeAsync(10_000);
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: undefined });
    window.sessionStorage.clear();
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

  it('keeps a departing card audio upload out of the next card while its stop is still draining', async () => {
    let finishOldUpload!: (value: { recordingId: string; mimeType: string; durationSeconds: number }) => void;
    mockAudioCapture.mockImplementationOnce(() => new Promise((resolve) => { finishOldUpload = resolve; }));
    mockAudioCapture.mockResolvedValue({ recordingId: 'card-b-clip', mimeType: 'audio/webm', durationSeconds: 1 });
    const { result, rerender } = await mount(undefined, 'card-a');
    expect(await startVoice(result)).toBe(true);
    await mic('speech', 500);
    await geminiSays(0, { inputTranscription: { text: 'Card A words' } });

    await act(async () => { rerender({ id: 'card-b' }); });
    await advance(0);
    expect(mockAudioCapture).toHaveBeenCalledWith('card-a', expect.anything());
    expect(await startVoice(result)).toBe(true);
    await mic('speech', 500);
    await geminiSays(1, { inputTranscription: { text: 'Card B words' } });
    await act(async () => {
      finishOldUpload({ recordingId: 'card-a-clip', mimeType: 'audio/webm', durationSeconds: 1 });
    });
    await advance(GEMINI_STOP_DRAIN_MS);

    expect(mockTranscript).toHaveBeenCalledWith('card-a', expect.objectContaining({
      segments: [expect.objectContaining({ text: 'Card A words', sourceRecordingId: 'card-a-clip' })],
    }));
    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript).toHaveBeenCalledWith('card-b', expect.objectContaining({
      segments: [expect.objectContaining({ text: 'Card B words', sourceRecordingId: 'card-b-clip' })],
    }));
  });

  it('suppresses mic capture during Gemini patient playback and its tail, then resumes after quiet', async () => {
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
    const { result } = await mount();
    expect(await startVoice(result)).toBe(true);

    await mic('speech', 500);
    expect(FakeMediaRecorder.instances).toHaveLength(1);

    await geminiSays(0, {
      modelTurn: {
        parts: [{ inlineData: { data: 'AAAAAA==', mimeType: 'audio/pcm;rate=24000' } }],
      },
      turnComplete: true,
    });
    expect(FakeMediaRecorder.instances[0].state).toBe('inactive');
    expect(FakeAudioContext.instances.at(-1)?.sources).toHaveLength(1);

    await mic('speech', 500);
    expect(FakeMediaRecorder.instances).toHaveLength(1);

    await act(async () => {
      FakeAudioContext.instances.at(-1)?.sources[0].finish();
    });
    await mic('quiet', 800);
    await advance(800);
    await mic('speech', 500);
    expect(FakeMediaRecorder.instances).toHaveLength(2);

    expect(await stopVoice(result)).toBe(true);
  });

  it('suppresses mic capture during OpenAI patient output and its tail, then resumes after quiet', async () => {
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
    mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }));
    const { result } = await mount();
    expect(await startVoice(result)).toBe(true);

    await mic('speech', 500);
    expect(FakeMediaRecorder.instances).toHaveLength(1);

    await act(async () => {
      channel(0).emit({ type: 'response.output_audio.delta', delta: 'AAAAAA==' });
    });
    expect(FakeMediaRecorder.instances[0].state).toBe('inactive');

    await mic('speech', 500);
    expect(FakeMediaRecorder.instances).toHaveLength(1);

    await act(async () => {
      channel(0).emit({ type: 'response.done' });
    });
    await mic('quiet', 800);
    await advance(800);
    await mic('speech', 500);
    expect(FakeMediaRecorder.instances).toHaveLength(2);

    expect(await stopVoice(result)).toBe(true);
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

  it('never restores a run the server pinned, so comparison runs measure the provider raw', async () => {
    // A flagged QA account asked for Gemini (?voiceProvider=gemini) and the server answered pinned:true.
    mockPreflight.mockResolvedValue(preflight({ provider: 'gemini', candidates: ['gemini'], pinned: true }));
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

  it('still restores a provider the page asked for when the server did not pin it (only a flagged QA account is pinned)', async () => {
    // ?voiceProvider=gemini from an ordinary learner: the server ignores it (pinned false) and serves the automatic order.
    mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'], pinned: false }));
    const { result } = await mount('gemini');
    expect(mockPreflight).toHaveBeenCalledWith('s1', 'gemini');
    expect(await startVoice(result)).toBe(true);
    expect(result.current.provider).toBe('openai');

    await dropLink(result);

    expect(mockOffer).toHaveBeenCalledTimes(2);
    expect(result.current.recoveries).toBe(1);
    expect(result.current.provider).toBe('openai');
    expect(result.current.connection).toBe('connected');
    expect(result.current.error).toBeNull();
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

    it('sends one end-of-audio nudge to a silent Gemini patient before it restores the link', async () => {
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(2_000);
      const nudges = () => socket(0).sent.filter((frame) => frame.includes('audioStreamEnd')).length;

      await advance(GEMINI_NUDGE_MS - 2_000);
      expect(nudges()).toBe(0);
      await advance(3_000);
      expect(nudges()).toBe(1);
      expect(result.current.recoveries).toBe(0);
      await advance(3_000);
      expect(nudges()).toBe(1); // one nudge per unanswered sentence

      await advance(STALL_MS);
      expect(result.current.recoveries).toBe(1);
    });

    it('times the first sentence of a restored Gemini session from its own audio, not from the words spoken into the dead link', async () => {
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(2_000); // never transcribed: the provider is silent
      await advance(9_000);
      await candidateSpeaks(2_000); // "Hello? Can you hear me?", also lost
      await advance(STALL_MS - 9_000);
      expect(result.current.recoveries).toBe(1);

      await mic('quiet', 500);
      await mic('speech', 1_000);
      await mic('quiet', 900);
      await geminiSays(1, { inputTranscription: { text: 'Is it all right if I explain' } });
      await geminiSays(1, { outputTranscription: { text: 'Yes please' }, turnComplete: true });
      expect(await stopVoice(result)).toBe(true);

      const candidate = savedSegments().find((segment) => segment.speaker === 'candidate');
      expect(candidate).toBeDefined();
      expect(candidate!.endMs - candidate!.startMs).toBeLessThanOrEqual(1_700);
    });

    it('restores the link as soon as Gemini announces its own disconnect (goAway)', async () => {
      const { result } = await mount();
      await startVoice(result);
      await act(async () => {
        socket(0).message({ goAway: { timeLeft: '10s' } });
      });
      await advance(0);

      expect(mockToken).toHaveBeenCalledTimes(2);
      expect(result.current.recoveries).toBe(1);
    });

    it('counts the silence from the first sentence nobody answered, however much more the candidate says to a silent patient', async () => {
      // Production, 1 Oct 2026: the scripted candidate kept speaking every ~12 s to a stalled patient, each new
      // sentence restarted the 20 s clock, and the restore only came 94 s after the stall.
      const { result } = await mount();
      await startVoice(result);
      await candidateSpeaks(2_000); // the sentence nobody answers
      await advance(9_000);
      await candidateSpeaks(2_000); // "Hello? Can you hear me?"
      await advance(7_000);
      expect(result.current.recoveries).toBe(0); // still inside the first sentence's window

      await advance(3_000);
      expect(mockToken).toHaveBeenCalledTimes(2);
      expect(result.current.recoveries).toBe(1);
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

    it('never restores a run the server pinned, however long the patient stays silent', async () => {
      mockPreflight.mockResolvedValue(preflight({ provider: 'gemini', candidates: ['gemini'], pinned: true }));
      const { result } = await mount('gemini');
      await startVoice(result);
      await candidateSpeaks(2_000);

      await advance(STALL_MS * 3);
      expect(result.current.recoveries).toBe(0);
      expect(mockToken).toHaveBeenCalledTimes(1);
      expect(result.current.connection).toBe('connected');
    });

    it('still restores a silent patient on a provider the page asked for but the server did not pin', async () => {
      mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'], pinned: false }));
      const { result } = await mount('gemini');
      await startVoice(result);
      await candidateSpeaks(2_000);

      await advance(STALL_MS + 1_500);

      expect(mockOffer).toHaveBeenCalledTimes(2);
      expect(result.current.recoveries).toBe(1);
      expect(result.current.connection).toBe('connected');
    });
  });

  describe('a Gemini disclaimer', () => {
    it('is kept out of the saved transcript and the rest of the turn is not played', async () => {
      const { result } = await mount();
      await startVoice(result);
      await mic('speech', 1_500);
      await mic('quiet', 900);
      await geminiSays(0, { inputTranscription: { text: 'How long did it last' } });
      await geminiSays(0, { outputTranscription: { text: 'It lasted three days.' } });
      const playedBefore = FakeAudioContext.instances.reduce((sum, context) => sum + context.sources.length, 0);
      await geminiSays(0, { outputTranscription: { text: ' This information is not medical advice or diagnosis.' } });
      await geminiSays(0, { modelTurn: { parts: [{ inlineData: { data: 'AAAA', mimeType: 'audio/pcm;rate=24000' } }] } });
      await geminiSays(0, { turnComplete: true });
      const playedAfter = FakeAudioContext.instances.reduce((sum, context) => sum + context.sources.length, 0);
      expect(playedAfter).toBe(playedBefore); // nothing queued after the disclaimer began
      expect(await stopVoice(result)).toBe(true);

      const patient = savedSegments().find((segment) => segment.speaker === 'patient');
      expect(patient?.text).toBe('It lasted three days.');
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

  // GPT-Live's start_ms/end_ms count from the start of its own provider session and restart at 0 in a restored one, and
  // Gemini stamped page time. Production 1 Oct 2026: after a restore the first lines of the new session were fused into
  // earlier segments in all three recovery runs (five scripted candidate lines in one 5 s segment, five patient replies in
  // one 0.4 s segment). Every provider time is now "ms since the first provider session went live".
  describe('one timeline across a restore', () => {
    const expectNonDecreasingStarts = (segments: SavedSegment[]) => {
      segments.slice(1).forEach((segment, index) => expect(segment.startMs).toBeGreaterThanOrEqual(segments[index].startMs));
    };

    it('keeps one monotonic timeline when an OpenAI link is restored, and merges nothing across the restore', async () => {
      mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }));
      const { result } = await mount();
      await startVoice(result);
      await advance(61_000); // the first session's own clock reads 50-60 s when its link drops
      await openAiSays(0, 'candidate', 'How can I help you today', 50_000, 52_000);
      await openAiSays(0, 'patient', 'My chest hurts', 52_500, 55_000);
      await openAiSays(0, 'candidate', 'Where is the pain', 55_500, 57_000);
      await openAiSays(0, 'patient', 'In the middle', 57_500, 60_000);

      await dropLink(result);
      expect(result.current.recoveries).toBe(1);
      expect(FakePeer.instances).toHaveLength(2);

      // The restored session counts from 0 again.
      await openAiSays(1, 'candidate', 'Since yesterday', 2_000, 4_000);
      await openAiSays(1, 'patient', 'Any other symptoms', 4_500, 7_000);
      await openAiSays(1, 'candidate', 'A little nausea', 7_500, 9_000);
      await openAiSays(1, 'patient', 'Thank you', 9_500, 12_000);
      expect(await stopVoice(result)).toBe(true);

      const segments = savedSegments();
      expect(segments.map((segment) => [segment.speaker, segment.text])).toEqual([
        ['candidate', 'How can I help you today'],
        ['patient', 'My chest hurts'],
        ['candidate', 'Where is the pain'],
        ['patient', 'In the middle'],
        ['candidate', 'Since yesterday'],
        ['patient', 'Any other symptoms'],
        ['candidate', 'A little nausea'],
        ['patient', 'Thank you'],
      ]);
      expectNonDecreasingStarts(segments);
      // The restored conversation comes after the old one, on the same clock (the restore happened at ~61 s).
      expect(segments[4].startMs).toBeGreaterThan(segments[3].endMs);
      expect(segments[4].startMs).toBeGreaterThanOrEqual(61_000);
    });

    it('does not collapse the conversation when the second restore switches from Gemini to OpenAI', async () => {
      const { result } = await mount(); // the default preflight puts Gemini first
      await startVoice(result);
      await advance(30_000);
      await geminiSays(0, { inputTranscription: { text: 'How can I help you today' } });
      await geminiSays(0, { outputTranscription: { text: 'My chest hurts' }, turnComplete: true });
      await dropLink(result); // the first restore tries the same provider again
      expect(result.current.provider).toBe('gemini');

      await advance(30_000);
      await geminiSays(1, { inputTranscription: { text: 'Where is the pain' } });
      await geminiSays(1, { outputTranscription: { text: 'In the middle' }, turnComplete: true });
      await dropLink(result); // the second restore tries the other provider first
      expect(result.current.provider).toBe('openai');

      // GPT-Live's clock starts at 0 again, a minute after Gemini stamped its lines.
      await openAiSays(0, 'candidate', 'Since yesterday', 1_000, 3_000);
      await openAiSays(0, 'patient', 'Any other symptoms', 3_500, 6_000);
      expect(await stopVoice(result)).toBe(true);

      const segments = savedSegments();
      expect(segments.map((segment) => [segment.speaker, segment.text])).toEqual([
        ['candidate', 'How can I help you today'],
        ['patient', 'My chest hurts'],
        ['candidate', 'Where is the pain'],
        ['patient', 'In the middle'],
        ['candidate', 'Since yesterday'],
        ['patient', 'Any other symptoms'],
      ]);
      expectNonDecreasingStarts(segments);
    });

    it('puts a Gemini transcript on the role-play clock: the first segment is near 0, not at the page uptime', async () => {
      const { result } = await mount();
      await advance(120_000); // the page has been open for two minutes when the card starts
      await startVoice(result);
      await advance(3_000);
      await geminiSays(0, { inputTranscription: { text: 'Good morning' } });
      await advance(2_000);
      await geminiSays(0, { outputTranscription: { text: 'Hello doctor' }, turnComplete: true });
      expect(await stopVoice(result)).toBe(true);

      const segments = savedSegments();
      expect(segments).toHaveLength(2);
      expect(segments[0].startMs).toBeGreaterThanOrEqual(2_900);
      expect(segments[0].startMs).toBeLessThanOrEqual(3_100);
      expect(segments[1].startMs).toBeGreaterThanOrEqual(4_900);
      expect(segments[1].startMs).toBeLessThanOrEqual(5_100);
    });

    it('keeps the microphone bursts of a Gemini candidate on the same clock', async () => {
      const { result } = await mount();
      await advance(60_000);
      await startVoice(result);
      await mic('quiet', 1_000);
      await mic('speech', 1_500);
      await mic('quiet', 900);
      await geminiSays(0, { inputTranscription: { text: 'Good morning, how can I help you today' } });
      expect(await stopVoice(result)).toBe(true);

      const [candidate] = savedSegments();
      // The burst began about 1 s after the card went live.
      expect(candidate.startMs).toBeGreaterThanOrEqual(900);
      expect(candidate.startMs).toBeLessThanOrEqual(1_300);
      expect(candidate.endMs - candidate.startMs).toBeGreaterThanOrEqual(1_300);
      expect(candidate.endMs - candidate.startMs).toBeLessThanOrEqual(1_700);
    });
  });

  describe('GPT-Live whitespace deltas', () => {
    it('keeps the space it sends as a delta of its own, and never saves whitespace as a segment', async () => {
      // Production 1 Oct 2026: " Doctor." / " " / "Well," was saved as "Doctor.Well,".
      mockPreflight.mockResolvedValue(preflight({ provider: 'openai', candidates: ['openai', 'gemini'] }));
      const { result } = await mount();
      await startVoice(result);

      await openAiSays(0, 'patient', ' ', 100, 150); // nothing to join yet
      await openAiSays(0, 'patient', 'Thanks, Doctor.', 200, 900);
      await openAiSays(0, 'patient', ' ', 900, 950);
      await openAiSays(0, 'patient', 'Well,', 950, 1_200);
      expect(await stopVoice(result)).toBe(true);

      expect(mockTranscript).toHaveBeenCalledTimes(1);
      expect(savedSegments()).toEqual([expect.objectContaining({ speaker: 'patient', text: 'Thanks, Doctor. Well,' })]);
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
