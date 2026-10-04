import { act, cleanup, renderHook } from '@testing-library/react';
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
  CHECKPOINT_TTL_MS,
  GEMINI_STOP_DRAIN_MS,
  transcriptCheckpointKey,
  useSpeakingRealtimeVoice,
  type UseSpeakingRealtimeVoiceResult,
} from '../useSpeakingRealtimeVoice';

// A reload mid-card used to lose the transcript (it lived in memory until stop()): only what was said after the reload
// was saved and graded, and the new hook instance re-used the turn ids of the old one. The hook now keeps a copy of the
// conversation in sessionStorage and takes it back when it mounts again for the same Speaking session.

const getUserMedia = vi.fn();
const KEY = transcriptCheckpointKey('s1');

const preflight = (overrides: Partial<LiveVoicePreflight> = {}): LiveVoicePreflight => ({
  provider: 'openai',
  providerDisplayName: 'GPT-Live',
  model: 'gpt-live-1',
  disclosure: 'disclosure',
  retentionDays: 30,
  sessionId: 's1',
  rolePlayCardId: 'c1',
  candidates: ['openai', 'gemini'],
  ...overrides,
});

const offerAnswer = (providerSessionId: string) => ({
  provider: 'openai',
  model: 'gpt-live-1',
  providerSessionId,
  answerSdp: 'v=0 answer',
});

const providerDown = () => new ApiError(503, 'live_voice_provider_unavailable', 'The realtime voice provider could not start this conversation. Please retry.', false);

type Voice = { current: UseSpeakingRealtimeVoiceResult };
type SavedSegment = { speaker: string; startMs: number; endMs: number; text: string };
type StoredCopy = { sessionId: string; segments: SavedSegment[]; turnIndex: number; originEpochMs: number; savedAt: number };

async function advance(ms: number) {
  await act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });
}

async function mount() {
  const rendered = renderHook(() => useSpeakingRealtimeVoice('s1'));
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

const channel = (index: number) => {
  const found = FakePeer.instances[index].channel;
  if (!found) throw new Error('The OpenAI peer has no data channel.');
  return found;
};

// GPT-Live transcript deltas carry start_ms/end_ms counted from the start of THAT provider session.
async function say(index: number, speaker: 'candidate' | 'patient', delta: string, startMs: number, endMs: number) {
  await act(async () => {
    channel(index).emit({
      type: speaker === 'candidate' ? 'session.input_transcript.delta' : 'session.output_transcript.delta',
      delta,
      start_ms: startMs,
      end_ms: endMs,
    });
  });
}

const savedSegments = (call = mockTranscript.mock.calls.length - 1) => mockTranscript.mock.calls[call][1].segments as SavedSegment[];
const readStored = () => JSON.parse(window.sessionStorage.getItem(KEY) ?? 'null') as StoredCopy | null;

// What an earlier page of this card left in sessionStorage (two lines of conversation, ten seconds into the role-play).
const storedCopy = (overrides: Record<string, unknown> = {}) => ({
  sessionId: 's1',
  segments: [
    { speaker: 'candidate', startMs: 1_000, endMs: 2_000, text: 'How can I help you today' },
    { speaker: 'patient', startMs: 2_500, endMs: 3_500, text: 'My chest hurts' },
  ],
  turnIndex: 1,
  originEpochMs: Date.now() - 10_000,
  savedAt: Date.now(),
  ...overrides,
});
const store = (copy: unknown, key = KEY) => window.sessionStorage.setItem(key, typeof copy === 'string' ? copy : JSON.stringify(copy));

function setVisibility(state: 'hidden' | 'visible') {
  Object.defineProperty(document, 'visibilityState', { configurable: true, get: () => state });
  document.dispatchEvent(new Event('visibilitychange'));
}

describe('useSpeakingRealtimeVoice refresh-safe transcript', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.spyOn(console, 'warn').mockImplementation(() => undefined);
    for (const fn of [mockPreflight, mockOffer, mockToken, mockTurn, mockTranscript, getUserMedia]) fn.mockReset();
    mockPreflight.mockResolvedValue(preflight());
    mockOffer.mockImplementation(async () => offerAnswer(`oai-session-${FakePeer.instances.length}`));
    let minted = 0;
    mockToken.mockImplementation(async () => ({
      provider: 'gemini',
      model: 'models/gemini-live',
      providerSessionId: `gem-session-${++minted}`,
      webSocketUrl: `wss://live.example.test/socket?access_token=${minted}`,
      expiresAt: '2026-10-01T12:00:00Z',
    }));
    mockTurn.mockResolvedValue({ sessionId: 's1', sequenceNumber: 1, duplicate: false, advisoryStatus: null });
    mockTranscript.mockResolvedValue({ transcriptId: 't1', provider: 'realtime-openai', wordCount: 3, meanConfidence: 0, generatedAt: '2026-10-01T12:00:00Z' });
    getUserMedia.mockImplementation(async () => new FakeStream());
    FakePeer.instances = [];
    FakeSocket.instances = [];
    FakeAudioContext.instances = [];
    FakeNode.amplitude = 0;
    vi.stubGlobal('RTCPeerConnection', FakePeer);
    vi.stubGlobal('WebSocket', FakeSocket);
    vi.stubGlobal('AudioContext', FakeAudioContext);
    vi.stubGlobal('requestAnimationFrame', vi.fn(() => 1));
    vi.stubGlobal('cancelAnimationFrame', vi.fn());
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: { getUserMedia } });
    window.sessionStorage.clear();
  });

  afterEach(async () => {
    // Unmount while the fake clock still runs: the stop() on the way out must finish here, not inside the next test.
    cleanup();
    await vi.advanceTimersByTimeAsync(10_000);
    vi.useRealTimers();
    vi.unstubAllGlobals();
    vi.restoreAllMocks();
    Object.defineProperty(navigator, 'mediaDevices', { configurable: true, value: undefined });
    delete (document as unknown as { visibilityState?: string }).visibilityState;
    window.sessionStorage.clear();
  });

  it('brings the conversation back after a reload: restored segments and turn numbers, one transcript saved under the new provider session', async () => {
    const first = await mount();
    await startVoice(first.result); // provider session oai-session-1; the role-play clock starts here
    await advance(14_000);
    await say(0, 'candidate', 'How can I help you today', 10_000, 11_000);
    await say(0, 'patient', 'My chest hurts', 11_300, 12_000);
    await say(0, 'candidate', 'Where is the pain', 12_500, 13_000); // closes turn 1
    await advance(1_100); // the timed write

    // A reload gives the page no chance to clean up: pagehide is the last thing it hears, and sessionStorage survives it.
    window.dispatchEvent(new Event('pagehide'));
    const snapshot = window.sessionStorage.getItem(KEY);
    expect(snapshot).not.toBeNull();
    expect(readStored()?.turnIndex).toBe(1);
    first.unmount(); // stands in for the old page going away
    await advance(6_000);
    window.sessionStorage.setItem(KEY, snapshot as string); // the real reload never ran the cleanup that removed it
    await advance(10_000); // the time the reload took

    const second = await mount();
    await startVoice(second.result); // a NEW provider session: oai-session-2
    await say(1, 'patient', 'In the middle', 1_000, 2_500); // its own clock starts at 0 again
    await say(1, 'candidate', 'Since yesterday', 3_000, 4_000);
    expect(await stopVoice(second.result)).toBe(true);

    const last = mockTranscript.mock.calls.at(-1);
    expect(last?.[0]).toBe('s1');
    expect(last?.[1]).toEqual({ provider: 'openai', providerSessionId: 'oai-session-2', segments: expect.any(Array) });
    const segments = savedSegments();
    expect(segments.map((segment) => [segment.speaker, segment.text])).toEqual([
      ['candidate', 'How can I help you today'],
      ['patient', 'My chest hurts'],
      ['candidate', 'Where is the pain'],
      ['patient', 'In the middle'],
      ['candidate', 'Since yesterday'],
    ]);
    // One timeline across the reload: the new provider session lands after everything that was restored.
    segments.slice(1).forEach((segment, index) => expect(segment.startMs).toBeGreaterThanOrEqual(segments[index].startMs));
    expect(segments[3].startMs).toBeGreaterThanOrEqual(31_100);

    // The turn numbers carry on from the restored ones, and no turn id is ever used twice (the server drops a repeated one).
    const turns = mockTurn.mock.calls.map(([, turn]) => turn as { clientTurnId: string; turnIndex: number; providerSessionId: string });
    const ids = turns.map((turn) => turn.clientTurnId);
    expect(new Set(ids).size).toBe(ids.length);
    expect(turns.filter((turn) => turn.providerSessionId === 'oai-session-2').map((turn) => turn.turnIndex)).toEqual([2, 3]);

    // Saved: the copy has done its job.
    expect(window.sessionStorage.getItem(KEY)).toBeNull();
  });

  it('writes the copy at most once a second while the conversation changes, and keeps it current as it goes', async () => {
    const { result } = await mount();
    await startVoice(result);
    await say(0, 'candidate', 'How can I help', 0, 800);
    expect(window.sessionStorage.getItem(KEY)).toBeNull(); // the write waits for up to a second
    await advance(999);
    expect(window.sessionStorage.getItem(KEY)).toBeNull();
    await advance(1);
    expect(readStored()?.segments).toEqual([expect.objectContaining({ speaker: 'candidate', text: 'How can I help' })]);

    await say(0, 'candidate', ' you today', 800, 1_500);
    await say(0, 'patient', 'My chest hurts', 1_800, 2_600);
    await advance(1_000);
    expect(readStored()?.segments.map((segment) => segment.text)).toEqual(['How can I help you today', 'My chest hurts']);
    // Nothing but the conversation and the numbers to carry on with: no provider session id, no token.
    expect(Object.keys(readStored() ?? {}).sort()).toEqual(['audioRecordings', 'originEpochMs', 'savedAt', 'segments', 'sessionId', 'turnIndex']);
    expect(readStored()).toMatchObject({ audioRecordings: [] });
    expect(window.sessionStorage.getItem(KEY)).not.toMatch(/oai-session|access_token/);
  });

  it('writes the copy the moment the page is hidden, without waiting for the timer', async () => {
    const { result } = await mount();
    await startVoice(result);
    await say(0, 'candidate', 'How can I help you today', 0, 1_000);
    expect(window.sessionStorage.getItem(KEY)).toBeNull();

    window.dispatchEvent(new Event('pagehide'));
    expect(readStored()?.segments).toEqual([expect.objectContaining({ text: 'How can I help you today' })]);

    window.sessionStorage.removeItem(KEY);
    await say(0, 'patient', 'My chest hurts', 1_500, 2_500);
    setVisibility('visible'); // coming back is not leaving
    expect(window.sessionStorage.getItem(KEY)).toBeNull();
    setVisibility('hidden');
    expect(readStored()?.segments.map((segment) => segment.text)).toEqual(['How can I help you today', 'My chest hurts']);
  });

  it('does not bring the copy back after the transcript was saved', async () => {
    const { result } = await mount();
    await startVoice(result);
    await say(0, 'candidate', 'How can I help you today', 0, 1_000);
    expect(await stopVoice(result)).toBe(true);
    expect(window.sessionStorage.getItem(KEY)).toBeNull();

    window.dispatchEvent(new Event('pagehide'));
    setVisibility('hidden');
    await advance(2_000);

    expect(window.sessionStorage.getItem(KEY)).toBeNull();
  });

  it('keeps the copy when the save fails for a reason worth retrying, and drops it once the server will never take the transcript', async () => {
    const { result } = await mount();
    await startVoice(result);
    await say(0, 'candidate', 'How can I help you today', 0, 1_000);

    mockTranscript.mockRejectedValueOnce(new ApiError(0, 'network_error', 'Unable to connect to the server.', true));
    expect(await stopVoice(result)).toBe(false);
    // A reload now can still bring the conversation back.
    window.dispatchEvent(new Event('pagehide'));
    expect(readStored()?.segments).toEqual([expect.objectContaining({ text: 'How can I help you today' })]);

    mockTranscript.mockRejectedValueOnce(new ApiError(409, 'live_voice_transcript_window_closed', 'The time for this role-play has ended.', false));
    expect(await stopVoice(result)).toBe(true);
    expect(window.sessionStorage.getItem(KEY)).toBeNull();
  });

  it('never restores a copy that belongs to another session', async () => {
    store(storedCopy({ sessionId: 'other' }), transcriptCheckpointKey('other'));
    store(storedCopy({ sessionId: 'other' })); // filed under this session's key, but not this session's
    const { result } = await mount();
    await startVoice(result);
    await say(0, 'candidate', 'Good morning', 5_000, 6_000);
    expect(await stopVoice(result)).toBe(true);

    expect(savedSegments().map((segment) => segment.text)).toEqual(['Good morning']);
    // The other session's own copy is none of this hook's business.
    expect(window.sessionStorage.getItem(transcriptCheckpointKey('other'))).not.toBeNull();
  });

  it('ignores a stored copy older than 15 minutes, and restores one just inside the limit', async () => {
    store(storedCopy({ savedAt: Date.now() - CHECKPOINT_TTL_MS - 1 }));
    const stale = await mount();
    await startVoice(stale.result);
    await say(0, 'candidate', 'Good morning', 5_000, 6_000);
    expect(await stopVoice(stale.result)).toBe(true);
    expect(savedSegments().map((segment) => segment.text)).toEqual(['Good morning']);
    stale.unmount();

    store(storedCopy({ savedAt: Date.now() - CHECKPOINT_TTL_MS + 1_000 }));
    const fresh = await mount();
    await startVoice(fresh.result);
    await say(1, 'candidate', 'Good morning', 5_000, 6_000);
    expect(await stopVoice(fresh.result)).toBe(true);
    expect(savedSegments().map((segment) => segment.text)).toEqual(['How can I help you today', 'My chest hurts', 'Good morning']);
  });

  it.each([
    ['is not JSON', '{oops'],
    ['is not an object', '"just text"'],
    ['has no segments', storedCopy({ segments: [] })],
    ['has a segment with an unknown speaker', storedCopy({ segments: [{ speaker: 'doctor', startMs: 0, endMs: 1, text: 'x' }] })],
    ['has a blank segment', storedCopy({ segments: [{ speaker: 'candidate', startMs: 0, endMs: 1, text: '   ' }] })],
    ['has a segment without times', storedCopy({ segments: [{ speaker: 'candidate', text: 'x' }] })],
    ['has a negative turn number', storedCopy({ turnIndex: -1 })],
    ['has no usable origin', storedCopy({ originEpochMs: 'yesterday' })],
  ])('ignores a stored copy that %s', async (_name, copy) => {
    store(copy);
    const { result } = await mount();
    await startVoice(result);
    await say(0, 'candidate', 'Good morning', 5_000, 6_000);
    expect(await stopVoice(result)).toBe(true);

    expect(savedSegments().map((segment) => segment.text)).toEqual(['Good morning']);
  });

  it('lets the first connect after a reload fail over, as it would without a restored conversation', async () => {
    // The restored words are not words this mount heard: the server replays the saved turns into either provider.
    store(storedCopy());
    mockOffer.mockRejectedValue(providerDown());
    const { result } = await mount();

    expect(await startVoice(result)).toBe(true);

    expect(result.current.provider).toBe('gemini');
    expect(mockToken).toHaveBeenCalledTimes(1);
    expect(await stopVoice(result)).toBe(true);
    expect(mockTranscript.mock.calls[0][1]).toEqual(expect.objectContaining({ provider: 'gemini', providerSessionId: 'gem-session-1' }));
    expect(savedSegments().map((segment) => segment.text)).toEqual(['How can I help you today', 'My chest hurts']);
  });

  it('works exactly as before when sessionStorage is unavailable', async () => {
    const original = Object.getOwnPropertyDescriptor(window, 'sessionStorage');
    Object.defineProperty(window, 'sessionStorage', {
      configurable: true,
      get() {
        throw new DOMException('Storage is blocked.', 'SecurityError');
      },
    });
    try {
      const { result } = await mount();
      await startVoice(result);
      await say(0, 'candidate', 'How can I help you today', 0, 1_000);
      await advance(1_500); // the timed write is attempted and swallowed
      window.dispatchEvent(new Event('pagehide')); // so is this one
      await say(0, 'patient', 'My chest hurts', 1_500, 2_500);

      expect(await stopVoice(result)).toBe(true);

      expect(savedSegments().map((segment) => segment.text)).toEqual(['How can I help you today', 'My chest hurts']);
      expect(result.current.error).toBeNull();
    } finally {
      if (original) Object.defineProperty(window, 'sessionStorage', original);
    }
  });
});
