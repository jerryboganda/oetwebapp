'use client';

import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { describeMicrophoneError } from '@/lib/mobile/speaking-recorder';
import {
  createGeminiLiveToken,
  createOpenAiLiveOffer,
  getLiveVoicePreflight,
  persistLiveVoiceTranscript,
  persistLiveVoiceTurn,
  type LiveVoicePreflight,
  type LiveVoiceProvider,
  type LiveVoiceTranscriptSegmentInput,
} from '@/lib/api/speaking-live-voice';

export type RealtimeVoiceConnection = 'idle' | 'preparing' | 'ready' | 'connecting' | 'connected' | 'ending' | 'ended' | 'error';
export type RealtimeVoicePhase = 'idle' | 'listening' | 'speaking';
export type RealtimeVoiceSpeaker = 'candidate' | 'patient';

export interface RealtimeVoiceCaption {
  id: string;
  speaker: RealtimeVoiceSpeaker;
  text: string;
}

export interface UseSpeakingRealtimeVoiceResult {
  connection: RealtimeVoiceConnection;
  phase: RealtimeVoicePhase;
  preflight: LiveVoicePreflight | null;
  captions: RealtimeVoiceCaption[];
  micLevel: number;
  micEnabled: boolean;
  awaitingCandidateStart: boolean;
  error: string | null;
  /** True when the last start failed because microphone permission was refused (drives the app-settings recovery path). */
  micPermissionDenied: boolean;
  ended: boolean;
  /** The provider whose link is live (null before that). Diagnostics and the QA harness only; never shown to learners. */
  provider: LiveVoiceProvider | null;
  /** True when an earlier provider failed before this one connected. Diagnostics only. */
  failedOver: boolean;
  audioRef: RefObject<HTMLAudioElement | null>;
  prepare: () => Promise<void>;
  /** Consent (incl. the provider disclosure) is recorded on the Rules + consent step before prep. */
  start: () => Promise<boolean>;
  /**
   * Resolves true when the caller may move on: the transcript was saved, nothing was said, or the server
   * will never accept it (a retry cannot help and must not strand the learner). False only for a failure
   * worth retrying (network, 5xx, rate limit); calling stop() again retries the same transcript.
   */
  stop: () => Promise<boolean>;
}

const TARGET_SAMPLE_RATE = 16_000;

/** How long one provider gets to go live once its session exists (the create call has its own timeout). */
export const CONNECT_TIMEOUT_MS = 15_000;
// The API's per-user limiter admits one live-voice request at a time; a 429 is our own limiter, not the provider.
const RATE_LIMIT_RETRY_DELAY_MS = 1_500;
/** The one learner-facing start failure: provider names, provider bodies and transport detail never reach the UI. */
export const LIVE_VOICE_UNAVAILABLE = 'The live AI patient could not start. Please try again.';
const LIVE_VOICE_INTERRUPTED = 'The live conversation was interrupted.';
const LIVE_VOICE_ENDED = 'The live conversation ended. End the role-play to save the completed transcript.';
const LIVE_VOICE_UNREADABLE = 'The live voice stream returned an unreadable event.';
const TRANSCRIPT_NOT_SAVED = 'The live voice transcript could not be saved. Please try again.';

/** A provider leg failed before it was live (create refused, transport fault, deadline). Its message is for logs and tests, never for learners. */
export class ProviderConnectError extends Error {
  constructor(message: string) {
    super(message);
    this.name = 'ProviderConnectError';
  }
}

interface ApiErrorInfo {
  status: number;
  code: string;
  message: string;
}

// Duck-typed on `status`: it holds for the real ApiError and for the stubs page tests install.
function apiErrorInfo(error: unknown): ApiErrorInfo | null {
  if (!error || typeof error !== 'object') return null;
  const { status, code, userMessage, message } = error as Record<string, unknown>;
  if (typeof status !== 'number') return null;
  return {
    status,
    code: typeof code === 'string' ? code : '',
    message: typeof userMessage === 'string' && userMessage ? userMessage : typeof message === 'string' ? message : '',
  };
}

const isLiveVoiceProvider = (value: unknown): value is LiveVoiceProvider => value === 'openai' || value === 'gemini';

/**
 * The providers to try, in order. The server names them healthiest first (`candidates`); an older
 * server sends none, so there is a single attempt with `provider`. A forced provider (`?voiceProvider=`
 * on the page, the server's `pinned`) never fails over: comparison and QA runs must measure the
 * provider they asked for.
 */
export function planProviders(
  preflight: Pick<LiveVoicePreflight, 'provider' | 'candidates' | 'pinned'>,
  forced?: LiveVoiceProvider,
): LiveVoiceProvider[] {
  if (forced || preflight.pinned) return isLiveVoiceProvider(preflight.provider) ? [preflight.provider] : [];
  const listed = (preflight.candidates ?? []).filter(isLiveVoiceProvider);
  const order = listed.length > 0 ? listed : [preflight.provider].filter(isLiveVoiceProvider);
  return [...new Set(order)];
}

/**
 * True when the next provider is worth trying: the leg failed before it was live (transport, deadline,
 * refusal), or the create call failed the way a provider outage looks (network, timeout, 5xx). Any 4xx
 * is a definite answer about this session (consent, session state, content, time over) that another
 * provider would repeat, and the local 429 rate limit is our own limiter, so neither fails over.
 */
export function isProviderFailure(error: unknown): boolean {
  if (error instanceof ProviderConnectError) return true;
  const info = apiErrorInfo(error);
  return info !== null && (info.status === 0 || info.status === 408 || info.status >= 500);
}

/**
 * A 4xx the server will give again for the same request: retrying cannot help. Not a sign-in expiry (401: the same
 * request succeeds after re-auth), a timeout (408) or the rate limit (429).
 */
export function isClientRejection(error: unknown): boolean {
  const info = apiErrorInfo(error);
  return info !== null && info.status >= 400 && info.status < 500
    && info.status !== 401 && info.status !== 408 && info.status !== 429;
}

// A definite server answer (consent missing, time over, ...) is shown as written; a provider or
// transport fault is nothing the learner can act on.
function learnerMessage(error: unknown, fallback: string = LIVE_VOICE_UNAVAILABLE): string {
  const info = apiErrorInfo(error);
  return info !== null && info.message && info.status >= 400 && info.status < 500 && info.status !== 408 ? info.message : fallback;
}

// Anything that is not an API answer or already a connect failure is a browser WebRTC/socket fault: a failed leg.
function asConnectFailure(caught: unknown): unknown {
  if (caught instanceof ProviderConnectError || apiErrorInfo(caught)) return caught;
  return new ProviderConnectError(caught instanceof Error ? caught.message : 'The provider connection failed.');
}

const failureCode = (error: unknown) => apiErrorInfo(error)?.code || (error instanceof ProviderConnectError ? 'connect_failed' : 'unknown');

const delay = (ms: number) => new Promise<void>((resolve) => window.setTimeout(resolve, ms));

/** One provider connection attempt: settles exactly once, on the first of live, failure, deadline or cancel. */
interface ProviderAttempt {
  /** Resolves when the provider link is live; rejects with a ProviderConnectError before that. */
  settled: Promise<void>;
  live: () => void;
  fail: (error: ProviderConnectError) => void;
  /** Starts the connect deadline; called once the provider session exists. */
  armDeadline: () => void;
  /** True until live() or fail() ran: events arriving now belong to a link that is not up yet. */
  pending: () => boolean;
}

function createProviderAttempt(): ProviderAttempt {
  let done = false;
  let timer: number | undefined;
  let resolve!: () => void;
  let reject!: (error: ProviderConnectError) => void;
  const settled = new Promise<void>((res, rej) => {
    resolve = res;
    reject = rej;
  });
  // A failure that lands before anything awaits the promise must not surface as an unhandled rejection.
  settled.catch(() => undefined);
  const finish = (error?: ProviderConnectError) => {
    if (done) return;
    done = true;
    window.clearTimeout(timer);
    if (error) reject(error);
    else resolve();
  };
  return {
    settled,
    live: () => finish(),
    fail: (error) => finish(error),
    armDeadline: () => {
      if (!done) timer = window.setTimeout(() => finish(new ProviderConnectError('The provider did not go live in time.')), CONNECT_TIMEOUT_MS);
    },
    pending: () => !done,
  };
}

function pendingAttemptOf(ref: { current: ProviderAttempt | null }): ProviderAttempt | null {
  const attempt = ref.current;
  return attempt !== null && attempt.pending() ? attempt : null;
}

function decodeBase64(value: string): Uint8Array {
  const binary = window.atob(value);
  const bytes = new Uint8Array(binary.length);
  for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
  return bytes;
}

function encodeBase64(bytes: Uint8Array): string {
  let binary = '';
  const chunkSize = 0x8000;
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    binary += String.fromCharCode(...bytes.subarray(offset, Math.min(offset + chunkSize, bytes.length)));
  }
  return window.btoa(binary);
}

function downsample(input: Float32Array, inputRate: number, outputRate: number): Float32Array {
  if (inputRate === outputRate) return input;
  const ratio = inputRate / outputRate;
  const outputLength = Math.round(input.length / ratio);
  const output = new Float32Array(outputLength);
  let inputOffset = 0;
  for (let outputOffset = 0; outputOffset < outputLength; outputOffset += 1) {
    const nextInputOffset = Math.round((outputOffset + 1) * ratio);
    let sum = 0;
    let count = 0;
    for (; inputOffset < nextInputOffset && inputOffset < input.length; inputOffset += 1) {
      sum += input[inputOffset];
      count += 1;
    }
    output[outputOffset] = count === 0 ? 0 : sum / count;
  }
  return output;
}

function pcm16Base64(input: Float32Array): string {
  const bytes = new Uint8Array(input.length * 2);
  const view = new DataView(bytes.buffer);
  for (let index = 0; index < input.length; index += 1) {
    const sample = Math.max(-1, Math.min(1, input[index]));
    view.setInt16(index * 2, sample < 0 ? sample * 0x8000 : sample * 0x7fff, true);
  }
  return encodeBase64(bytes);
}

function pcmToAudioBuffer(
  context: AudioContext,
  data: string,
  sampleRate: number,
): AudioBuffer {
  const bytes = decodeBase64(data);
  const samples = new Int16Array(bytes.buffer, bytes.byteOffset, Math.floor(bytes.byteLength / 2));
  const buffer = context.createBuffer(1, samples.length, sampleRate);
  const channel = buffer.getChannelData(0);
  for (let index = 0; index < samples.length; index += 1) channel[index] = samples[index] / 0x8000;
  return buffer;
}

function providerTranscriptText(value: unknown): string | null {
  if (typeof value === 'string') return value.trim() || null;
  if (!value || typeof value !== 'object') return null;
  const record = value as Record<string, unknown>;
  for (const key of ['transcript', 'text', 'delta']) {
    if (typeof record[key] === 'string' && record[key].trim()) return (record[key] as string).trim();
  }
  for (const key of ['item', 'content', 'part', 'parts', 'response', 'output', 'serverContent']) {
    const nested = record[key];
    if (Array.isArray(nested)) {
      for (const item of nested) {
        const text = providerTranscriptText(item);
        if (text) return text;
      }
    } else {
      const text = providerTranscriptText(nested);
      if (text) return text;
    }
  }
  return null;
}

function providerEventType(value: Record<string, unknown>): string {
  return typeof value.type === 'string'
    ? value.type
    : value.event && typeof value.event === 'object' && typeof (value.event as Record<string, unknown>).type === 'string'
      ? ((value.event as Record<string, unknown>).type as string)
      : '';
}

function audioRate(mimeType: unknown): number {
  if (typeof mimeType !== 'string') return 24_000;
  const match = mimeType.match(/rate\s*=\s*(\d+)/i);
  return match ? Number(match[1]) || 24_000 : 24_000;
}

// The server rejects any transcript segment or saved turn text over 4,000
// characters; stay well under it. Production 26 Sep 2026: a patient that never
// replied left the candidate's whole 5 minutes in one segment, the save failed
// ("A transcript segment is too long") and the exam could not move on.
export const MAX_SEGMENT_CHARS = 1_500;
const MAX_PENDING_TURN_CHARS = 3_000;

// GPT-Live is full duplex and transcribes the candidate a beat behind real time, so a
// sentence's last word can arrive after the patient's "Uh." / "Yeah," has begun. Its own
// start_ms can then place that word at or just after the backchannel, so timing alone
// cannot order it (production 26 Sep 2026, saved transcript: "...How can I help you" /
// patient "Uh." / candidate "today", 12 such splits in one two-card mock). Replaying that
// run: transcription trails the audio by ~1.1 s, so the tail (and the rest of the sentence)
// lands after the backchannel whatever start_ms says.
const BACKCHANNEL_MAX_WORDS = 3;
const MAX_TAIL_GAP_MS = 2_000;
const wordCount = (text: string) => text.trim().split(/\s+/).filter(Boolean).length;

/**
 * True when a fragment belongs to its own speaker's previous segment although the other
 * speaker's latest segment has already started: the provider timeline places it before
 * that segment began, or the other speaker's segment is only a short backchannel and the
 * fragment continues the sentence the speaker was in the middle of (each fragment follows
 * the previous one within MAX_TAIL_GAP_MS, so a whole continued sentence rejoins). A
 * continuation starts lowercase or with punctuation (", I would like..." / ". Could you...");
 * a genuine reply after a backchannel starts a capitalised sentence, so it stays a new
 * segment. Only mid-sentence tails are rejoined; whole sentences keep the provider's order.
 * The closing-punctuation guard matters for patient text (GPT-Live's candidate transcript has none).
 */
export function isLateFragment(
  segments: readonly LiveVoiceTranscriptSegmentInput[],
  speaker: RealtimeVoiceSpeaker,
  fragment: string,
  at: { startMs: number; endMs: number },
): boolean {
  const last = segments[segments.length - 1];
  if (last === undefined || last.speaker === speaker) return false;
  if (at.startMs < last.startMs) return true;
  const previous = segments[segments.length - 2];
  return previous !== undefined
    && previous.speaker === speaker
    && wordCount(last.text) <= BACKCHANNEL_MAX_WORDS
    && /^\s*[a-z,;:.?!]/.test(fragment)
    && !/[.?!]\s*$/.test(previous.text)
    && at.startMs - previous.endMs <= MAX_TAIL_GAP_MS;
}

/**
 * Appends a transcript fragment to the segment list. Consecutive fragments from
 * one speaker extend one segment. With a provider timeline interval (`spoken`),
 * a late fragment (see `isLateFragment`) joins its own speaker's previous segment
 * instead of splitting it. Returns true for such a late fragment.
 */
export function appendTranscriptFragment(
  segments: LiveVoiceTranscriptSegmentInput[],
  speaker: RealtimeVoiceSpeaker,
  fragment: string,
  exact: boolean,
  at: { startMs: number; endMs: number },
  spoken = false,
): boolean {
  const last = segments[segments.length - 1];
  const late = spoken && isLateFragment(segments, speaker, fragment, at);
  const candidate = late
    ? [...segments].reverse().find((segment) => segment.speaker === speaker)
    : last?.speaker === speaker ? last : undefined;
  const target = candidate && candidate.text.length + fragment.length <= MAX_SEGMENT_CHARS ? candidate : undefined;
  if (target) {
    target.text = exact ? target.text + fragment : `${target.text} ${fragment}`;
    target.endMs = Math.max(target.endMs, at.endMs);
  } else {
    segments.push({ speaker, startMs: at.startMs, endMs: at.endMs, text: fragment });
  }
  return late && target !== undefined;
}

export function useSpeakingRealtimeVoice(
  sessionId: string,
  requestedProvider?: LiveVoiceProvider,
): UseSpeakingRealtimeVoiceResult {
  const [connection, setConnection] = useState<RealtimeVoiceConnection>('idle');
  const [phase, setPhase] = useState<RealtimeVoicePhase>('idle');
  const [preflight, setPreflight] = useState<LiveVoicePreflight | null>(null);
  const [captions, setCaptions] = useState<RealtimeVoiceCaption[]>([]);
  const [micLevel, setMicLevel] = useState(0);
  const [micEnabled, setMicEnabled] = useState(false);
  const [awaitingCandidateStart, setAwaitingCandidateStart] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [micPermissionDenied, setMicPermissionDenied] = useState(false);
  const [ended, setEnded] = useState(false);
  const [activeProvider, setActiveProvider] = useState<LiveVoiceProvider | null>(null);
  const [failedOver, setFailedOver] = useState(false);

  const audioRef = useRef<HTMLAudioElement | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const peerRef = useRef<RTCPeerConnection | null>(null);
  const dataChannelRef = useRef<RTCDataChannel | null>(null);
  const socketRef = useRef<WebSocket | null>(null);
  const inputContextRef = useRef<AudioContext | null>(null);
  const outputContextRef = useRef<AudioContext | null>(null);
  const inputSourceRef = useRef<MediaStreamAudioSourceNode | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const processorRef = useRef<ScriptProcessorNode | null>(null);
  const silentGainRef = useRef<GainNode | null>(null);
  const playbackSourcesRef = useRef<Set<AudioBufferSourceNode>>(new Set());
  const meterFrameRef = useRef<number | null>(null);
  const nextPlaybackTimeRef = useRef(0);
  // The provider whose link is live and its session id, committed together and only once the link is up:
  // a leg that never connected must not leave an id behind that stop() would save a transcript under.
  const providerRef = useRef<LiveVoiceProvider | null>(null);
  const providerSessionIdRef = useRef<string | null>(null);
  // The providers to try, from the last preflight (cleared after a failed start so a retry asks the server again).
  const plannedRef = useRef<LiveVoiceProvider[]>([]);
  // Bumped by stop(), unmount and a session change: a start() that resumes after an await sees it and lets go.
  const runRef = useRef(0);
  const prepareRunRef = useRef(0);
  const startPromiseRef = useRef<{ run: number; promise: Promise<boolean> } | null>(null);
  const stopPromiseRef = useRef<Promise<boolean> | null>(null);
  const attemptRef = useRef<ProviderAttempt | null>(null);
  const pendingCandidateRef = useRef('');
  const pendingPatientRef = useRef('');
  const pendingStartedAtRef = useRef<number | null>(null);
  const turnIndexRef = useRef(0);
  const segmentsRef = useRef<LiveVoiceTranscriptSegmentInput[]>([]);
  const flushPromiseRef = useRef(Promise.resolve());
  const geminiReadyRef = useRef(false);
  const stoppingRef = useRef(false);
  const openAiClosedRef = useRef<(() => void) | null>(null);

  // Per-provider teardown between failover attempts. Handlers are detached BEFORE close(): a WebSocket
  // closes asynchronously, and a stale onclose would flip the next attempt to 'error'. The microphone,
  // its AudioContext and the level meter stay, so one permission prompt serves every attempt.
  const resetProviderTransport = useCallback(() => {
    attemptRef.current?.fail(new ProviderConnectError('The connection attempt was cancelled.'));
    attemptRef.current = null;
    openAiClosedRef.current = null;
    geminiReadyRef.current = false;
    playbackSourcesRef.current.forEach((source) => {
      try {
        source.stop();
      } catch {}
    });
    playbackSourcesRef.current.clear();
    const channel = dataChannelRef.current;
    if (channel) {
      channel.onopen = null;
      channel.onmessage = null;
      channel.onerror = null;
      channel.onclose = null;
      channel.close();
    }
    dataChannelRef.current = null;
    const peer = peerRef.current;
    if (peer) {
      peer.ontrack = null;
      peer.onconnectionstatechange = null;
      peer.close();
    }
    peerRef.current = null;
    const socket = socketRef.current;
    if (socket) {
      socket.onopen = null;
      socket.onmessage = null;
      socket.onerror = null;
      socket.onclose = null;
      socket.close();
    }
    socketRef.current = null;
    const processor = processorRef.current;
    if (processor) {
      // Its edge from the shared microphone source goes too, or it would keep sending on a later socket.
      processor.onaudioprocess = null;
      try {
        inputSourceRef.current?.disconnect(processor);
      } catch {}
      processor.disconnect();
    }
    processorRef.current = null;
    silentGainRef.current?.disconnect();
    silentGainRef.current = null;
    outputContextRef.current = null; // the input context: closeTransport closes it
    nextPlaybackTimeRef.current = 0;
    if (audioRef.current) audioRef.current.srcObject = null;
  }, []);

  const closeTransport = useCallback(() => {
    resetProviderTransport();
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    inputSourceRef.current?.disconnect();
    inputSourceRef.current = null;
    analyserRef.current?.disconnect();
    analyserRef.current = null;
    if (meterFrameRef.current !== null) window.cancelAnimationFrame(meterFrameRef.current);
    meterFrameRef.current = null;
    void inputContextRef.current?.close().catch(() => undefined);
    inputContextRef.current = null;
    setMicEnabled(false);
    setMicLevel(0);
    setPhase('idle');
  }, [resetProviderTransport]);

  const interruptPlayback = useCallback(() => {
    playbackSourcesRef.current.forEach((source) => {
      try {
        source.stop();
      } catch {}
    });
    playbackSourcesRef.current.clear();
    nextPlaybackTimeRef.current = outputContextRef.current?.currentTime ?? 0;
  }, []);

  const prepare = useCallback(async () => {
    if (!sessionId) return;
    const run = ++prepareRunRef.current;
    setConnection('preparing');
    setError(null);
    try {
      const result = await getLiveVoicePreflight(sessionId, requestedProvider);
      // A late answer (the page re-mounts the hook when it learns ?voiceProvider=) must not replace a newer one.
      if (run !== prepareRunRef.current) return;
      const planned = planProviders(result, requestedProvider);
      if (planned.length === 0) throw new ProviderConnectError('The preflight named no usable provider.');
      setPreflight(result);
      plannedRef.current = planned;
      setConnection('ready');
    } catch (caught) {
      if (run !== prepareRunRef.current) return;
      setError(learnerMessage(caught));
      setConnection('error');
    }
  }, [requestedProvider, sessionId]);

  // One caption and one transcript segment per speaker run keeps a 5-minute
  // conversation far below the server's 600-segment limit. `exact` fragments
  // (GPT-Live deltas) carry their own spacing and provider timing; a late one
  // rejoins its speaker's text instead of splitting it (read as disfluency).
  const addCaption = useCallback((
    speaker: RealtimeVoiceSpeaker,
    text: string,
    exact = false,
    spoken?: { startMs: number; endMs: number },
  ) => {
    if (!text.trim()) return;
    const fragment = exact ? text : text.trim();
    const join = (existing: string) => (exact ? existing + fragment : `${existing} ${fragment}`);
    const now = Math.max(0, Math.round(performance.now()));
    if (pendingStartedAtRef.current === null) pendingStartedAtRef.current = now;
    const late = appendTranscriptFragment(segmentsRef.current, speaker, fragment, exact, spoken ?? { startMs: now, endMs: now }, Boolean(spoken));
    setCaptions((current) => {
      const index = late
        ? current.map((caption) => caption.speaker).lastIndexOf(speaker)
        : current[current.length - 1]?.speaker === speaker ? current.length - 1 : -1;
      if (index >= 0) return current.map((caption, i) => (i === index ? { ...caption, text: join(caption.text) } : caption));
      const id = `${speaker}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      return [...current, { id, speaker, text: fragment }].slice(-80);
    });
    if (speaker === 'candidate') setAwaitingCandidateStart(false);
  }, []);

  // Takes the pending turn synchronously, so fragments that arrive while a save
  // is in flight start the next turn instead of being cleared with this one.
  const takePendingTurn = useCallback((): Parameters<typeof persistLiveVoiceTurn>[1] | null => {
    const provider = providerRef.current;
    const providerSessionId = providerSessionIdRef.current;
    const candidateText = pendingCandidateRef.current.trim();
    const patientText = pendingPatientRef.current.trim();
    if (!provider || !providerSessionId || (!candidateText && !patientText)) return null;

    const startedAt = pendingStartedAtRef.current;
    const endedAt = Math.max(0, Math.round(performance.now()));
    turnIndexRef.current += 1;
    pendingCandidateRef.current = '';
    pendingPatientRef.current = '';
    pendingStartedAtRef.current = null;
    return {
      provider,
      providerSessionId,
      candidateText: candidateText || null,
      patientText: patientText || null,
      clientTurnId: `voice-turn:${turnIndexRef.current}`,
      turnIndex: turnIndexRef.current,
      startedAt: startedAt === null ? undefined : new Date(Date.now() - Math.max(0, endedAt - startedAt)).toISOString(),
      endedAt: new Date().toISOString(),
    };
  }, []);

  const flushPendingTurn = useCallback(async () => {
    const turn = takePendingTurn();
    if (turn) await persistLiveVoiceTurn(sessionId, turn);
  }, [sessionId, takePendingTurn]);

  // The per-turn rows are advisory (stop() saves the whole transcript), so a row that will not save is logged, never
  // shown to the learner and never thrown: every caller is fire-and-forget. The chain therefore never rejects.
  const queueFlush = useCallback(() => {
    const turn = takePendingTurn();
    flushPromiseRef.current = flushPromiseRef.current.then(async () => {
      if (!turn) return;
      try {
        await persistLiveVoiceTurn(sessionId, turn);
      } catch (caught) {
        console.warn('Live voice turn row not saved.', failureCode(caught));
      }
    });
    return flushPromiseRef.current;
  }, [sessionId, takePendingTurn]);

  // A turn with no reply (or a very long monologue) is saved in parts rather
  // than growing past the server's per-turn text limit.
  const flushIfLong = useCallback(() => {
    if (pendingCandidateRef.current.length > MAX_PENDING_TURN_CHARS || pendingPatientRef.current.length > MAX_PENDING_TURN_CHARS) {
      void queueFlush();
    }
  }, [queueFlush]);

  // Gemini streams incremental chunks: append, never de-duplicate.
  const captureTranscript = useCallback((speaker: RealtimeVoiceSpeaker, text: string) => {
    const chunk = text.trim();
    if (!chunk) return;
    addCaption(speaker, chunk);
    if (speaker === 'candidate') pendingCandidateRef.current = `${pendingCandidateRef.current} ${chunk}`.trim();
    else pendingPatientRef.current = `${pendingPatientRef.current} ${chunk}`.trim();
    flushIfLong();
  }, [addCaption, flushIfLong]);

  const hasTranscriptText = useCallback(
    () => segmentsRef.current.length > 0 || pendingCandidateRef.current.trim() !== '' || pendingPatientRef.current.trim() !== '',
    [],
  );

  const handleOpenAiEvent = useCallback((value: Record<string, unknown>) => {
    const type = providerEventType(value).toLowerCase();
    // Before the link is live an event ends the attempt (so the next provider can be tried); after, it is a mid-conversation fault.
    const pending = pendingAttemptOf(attemptRef);
    if (type.includes('error')) {
      if (pending) {
        pending.fail(new ProviderConnectError('The provider reported an error before going live.'));
        return;
      }
      setError(LIVE_VOICE_INTERRUPTED);
      setConnection('error');
      return;
    }
    if (type.includes('session.closed') || type.includes('session.ended')) {
      if (pending) {
        pending.fail(new ProviderConnectError('The provider closed the session before going live.'));
        return;
      }
      if (stoppingRef.current) {
        openAiClosedRef.current?.();
        return;
      }
      setError(LIVE_VOICE_ENDED);
      setConnection('error');
      return;
    }
    if (type.includes('session.started') || type.includes('session.created')) {
      pending?.live();
      return;
    }

    // GPT-Live sends exact transcript fragments and no turn-complete event: a
    // turn (candidate, then patient) closes when the candidate speaks again.
    const speaker: RealtimeVoiceSpeaker | null = type === 'session.input_transcript.delta'
      ? 'candidate'
      : type === 'session.output_transcript.delta' ? 'patient' : null;
    const delta = typeof value.delta === 'string' ? value.delta : '';
    if (!speaker || !delta) return;
    const spoken = typeof value.start_ms === 'number' && typeof value.end_ms === 'number'
      ? { startMs: value.start_ms, endMs: value.end_ms }
      : undefined;
    // A late candidate fragment (the tail of a sentence the patient's backchannel or
    // reply already overtook) still belongs to the current turn; only new candidate
    // speech after the patient has spoken closes it.
    const lastSegment = segmentsRef.current[segmentsRef.current.length - 1];
    const lateCandidate = spoken !== undefined && isLateFragment(segmentsRef.current, speaker, delta, spoken);
    if (speaker === 'candidate' && pendingPatientRef.current.trim() && lastSegment?.speaker === 'patient' && !lateCandidate) void queueFlush();
    addCaption(speaker, delta, true, spoken);
    if (speaker === 'candidate') pendingCandidateRef.current += delta;
    else pendingPatientRef.current += delta;
    flushIfLong();
    setPhase(speaker === 'patient' ? 'speaking' : 'listening');
  }, [addCaption, flushIfLong, queueFlush]);

  const handleGeminiMessage = useCallback((value: Record<string, unknown>) => {
    if (value.setupComplete || value.setup_complete) {
      geminiReadyRef.current = true;
      pendingAttemptOf(attemptRef)?.live();
      return;
    }
    if (value.error) {
      const pending = pendingAttemptOf(attemptRef);
      if (pending) {
        pending.fail(new ProviderConnectError('The provider reported an error before going live.'));
        return;
      }
      setError(LIVE_VOICE_INTERRUPTED);
      setConnection('error');
      return;
    }

    const serverContent = (value.serverContent ?? value.server_content) as Record<string, unknown> | undefined;
    if (!serverContent) return;
    if (serverContent.interrupted === true) {
      interruptPlayback();
      setPhase('listening');
      return;
    }
    const inputTranscription = (serverContent.inputTranscription ?? serverContent.input_transcription) as unknown;
    const outputTranscription = (serverContent.outputTranscription ?? serverContent.output_transcription) as unknown;
    const inputText = providerTranscriptText(inputTranscription);
    const outputText = providerTranscriptText(outputTranscription);
    if (inputText) captureTranscript('candidate', inputText);
    if (outputText) {
      setPhase('speaking');
      captureTranscript('patient', outputText);
    }

    const modelTurn = (serverContent.modelTurn ?? serverContent.model_turn) as Record<string, unknown> | undefined;
    const parts = modelTurn?.parts;
    if (Array.isArray(parts)) {
      for (const part of parts) {
        const partRecord = part as Record<string, unknown>;
        const inline = (partRecord.inlineData ?? partRecord.inline_data) as Record<string, unknown> | undefined;
        const data = inline?.data;
        if (typeof data !== 'string') continue;
        const outputContext = outputContextRef.current;
        if (!outputContext) continue;
        const buffer = pcmToAudioBuffer(outputContext, data, audioRate(inline?.mimeType ?? inline?.mime_type));
        const source = outputContext.createBufferSource();
        source.buffer = buffer;
        source.connect(outputContext.destination);
        playbackSourcesRef.current.add(source);
        source.addEventListener('ended', () => playbackSourcesRef.current.delete(source), { once: true });
        const startAt = Math.max(outputContext.currentTime, nextPlaybackTimeRef.current);
        source.start(startAt);
        nextPlaybackTimeRef.current = startAt + buffer.duration;
      }
    }

    if (serverContent.turnComplete || serverContent.turn_complete) {
      void queueFlush();
      setPhase('listening');
    }
  }, [captureTranscript, interruptPlayback, queueFlush]);

  const configureMeter = useCallback((stream: MediaStream, context: AudioContext) => {
    const source = context.createMediaStreamSource(stream);
    const analyser = context.createAnalyser();
    analyser.fftSize = 256;
    source.connect(analyser);
    inputSourceRef.current = source;
    analyserRef.current = analyser;
    const values = new Uint8Array(analyser.fftSize);
    const tick = () => {
      if (!analyserRef.current) return;
      analyser.getByteTimeDomainData(values);
      let sum = 0;
      for (const value of values) {
        const normalized = (value - 128) / 128;
        sum += normalized * normalized;
      }
      setMicLevel(Math.min(1, Math.sqrt(sum / values.length) * 3));
      meterFrameRef.current = window.requestAnimationFrame(tick);
    };
    meterFrameRef.current = window.requestAnimationFrame(tick);
  }, []);

  const waitForIce = useCallback(async (peer: RTCPeerConnection) => {
    if (peer.iceGatheringState === 'complete') return;
    await new Promise<void>((resolve) => {
      let timer: number | undefined;
      const finish = () => {
        window.clearTimeout(timer);
        peer.removeEventListener('icegatheringstatechange', handleState);
        resolve();
      };
      const handleState = () => {
        if (peer.iceGatheringState === 'complete') finish();
      };
      peer.addEventListener('icegatheringstatechange', handleState);
      timer = window.setTimeout(finish, 5_000);
    });
  }, []);

  // Resolves with the provider session id once the link is LIVE; rejects before that with the create
  // call's ApiError or a ProviderConnectError. `alive` is false once stop(), unmount or a newer start
  // took over: nothing more may be created then.
  const connectOpenAi = useCallback(async (stream: MediaStream, alive: () => boolean): Promise<string> => {
    if (typeof RTCPeerConnection === 'undefined') throw new ProviderConnectError('WebRTC is not available in this browser.');
    const attempt = createProviderAttempt();
    attemptRef.current = attempt;
    try {
      const peer = new RTCPeerConnection();
      peerRef.current = peer;
      stream.getAudioTracks().forEach((track) => peer.addTrack(track, stream));
      peer.ontrack = (event) => {
        const remoteStream = event.streams[0] ?? new MediaStream([event.track]);
        if (audioRef.current) {
          audioRef.current.srcObject = remoteStream;
          void audioRef.current.play().catch(() => undefined);
        }
      };
      peer.onconnectionstatechange = () => {
        const state = peer.connectionState;
        if (attempt.pending()) {
          if (state === 'connected') attempt.live();
          else if (state === 'failed' || state === 'disconnected' || state === 'closed') attempt.fail(new ProviderConnectError(`The peer connection ${state}.`));
          return;
        }
        if (state === 'connected') setConnection('connected');
        if (!stoppingRef.current && (state === 'failed' || state === 'disconnected')) {
          setError(LIVE_VOICE_INTERRUPTED);
          setConnection('error');
        }
      };
      const dataChannel = peer.createDataChannel('oai-events');
      dataChannelRef.current = dataChannel;
      dataChannel.onopen = () => attempt.live();
      dataChannel.onmessage = (event) => {
        try {
          const value = JSON.parse(typeof event.data === 'string' ? event.data : '') as Record<string, unknown>;
          handleOpenAiEvent(value);
        } catch {
          if (attempt.pending()) {
            attempt.fail(new ProviderConnectError('The provider stream was unreadable.'));
            return;
          }
          setError(LIVE_VOICE_UNREADABLE);
          setConnection('error');
        }
      };

      const offer = await peer.createOffer();
      await peer.setLocalDescription(offer);
      await waitForIce(peer);
      if (!alive()) throw new ProviderConnectError('The connection attempt was cancelled.');
      const localSdp = peer.localDescription?.sdp;
      if (!localSdp) throw new ProviderConnectError('The browser did not produce a WebRTC offer.');
      const answer = await createOpenAiLiveOffer(sessionId, localSdp);
      if (!alive()) throw new ProviderConnectError('The connection attempt was cancelled.');
      await peer.setRemoteDescription({ type: 'answer', sdp: answer.answerSdp });
      attempt.armDeadline();
      await attempt.settled;
      return answer.providerSessionId;
    } catch (caught) {
      throw asConnectFailure(caught);
    }
  }, [handleOpenAiEvent, sessionId, waitForIce]);

  const configureGeminiInput = useCallback((stream: MediaStream, context: AudioContext) => {
    const source = inputSourceRef.current ?? context.createMediaStreamSource(stream);
    const processor = context.createScriptProcessor(4096, 1, 1);
    const silentGain = context.createGain();
    silentGain.gain.value = 0;
    processor.onaudioprocess = (event) => {
      const socket = socketRef.current;
      if (!geminiReadyRef.current || !socket || socket.readyState !== WebSocket.OPEN) return;
      const input = event.inputBuffer.getChannelData(0);
      const samples = downsample(input, context.sampleRate, TARGET_SAMPLE_RATE);
      socket.send(JSON.stringify({
        realtimeInput: {
          audio: {
            data: pcm16Base64(samples),
            mimeType: `audio/pcm;rate=${TARGET_SAMPLE_RATE}`,
          },
        },
      }));
    };
    source.connect(processor);
    processor.connect(silentGain);
    silentGain.connect(context.destination);
    processorRef.current = processor;
    silentGainRef.current = silentGain;
  }, []);

  const connectGemini = useCallback(async (stream: MediaStream, context: AudioContext, alive: () => boolean): Promise<string> => {
    const attempt = createProviderAttempt();
    attemptRef.current = attempt;
    try {
      const token = await createGeminiLiveToken(sessionId);
      if (!alive()) throw new ProviderConnectError('The connection attempt was cancelled.');
      const socket = new WebSocket(token.webSocketUrl);
      // Gemini Live sends every server message as a binary frame.
      socket.binaryType = 'arraybuffer';
      socketRef.current = socket;
      outputContextRef.current = context;
      attempt.armDeadline();
      socket.onopen = () => {
        // The ephemeral token already locks the full setup (persona, audio
        // modality, transcription); the client may only name the model.
        socket.send(JSON.stringify({ setup: { model: token.model } }));
        configureGeminiInput(stream, context);
      };
      socket.onmessage = (event) => {
        try {
          const raw = typeof event.data === 'string' ? event.data : new TextDecoder().decode(event.data as ArrayBuffer);
          handleGeminiMessage(JSON.parse(raw) as Record<string, unknown>);
        } catch {
          attempt.fail(new ProviderConnectError('The provider stream was unreadable.'));
        }
      };
      socket.onerror = () => attempt.fail(new ProviderConnectError('The provider socket could not be established.'));
      socket.onclose = (event) => {
        // A socket closed by the provider before setup finished (auth, quota, handshake) ends the attempt at once.
        if (attempt.pending()) {
          attempt.fail(new ProviderConnectError(`The provider socket closed before setup (code ${event.code}).`));
          return;
        }
        if (!stoppingRef.current) {
          setError(LIVE_VOICE_INTERRUPTED);
          setConnection('error');
        }
      };
      await attempt.settled;
      return token.providerSessionId;
    } catch (caught) {
      throw asConnectFailure(caught);
    }
  }, [configureGeminiInput, handleGeminiMessage, sessionId]);

  // Asks for the microphone once for the whole start (every failover attempt reuses it). Null when the
  // run was cancelled meanwhile; whatever this call opened is released before returning. The stream and its
  // context are registered the moment a live run owns them, so stop() and unmount release the microphone even
  // while AudioContext.resume() has not answered (it can stay pending on WebKit and WebViews).
  const acquireMic = useCallback(async (alive: () => boolean): Promise<{ stream: MediaStream; context: AudioContext } | null> => {
    if (!navigator.mediaDevices?.getUserMedia) {
      throw new Error('This browser cannot capture audio. Use an updated Chrome, Edge, Safari or the mobile app.');
    }
    const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, channelCount: 1 } });
    let context: AudioContext | null = null;
    // Releases only what this call opened; a ref is cleared only while it still points at it (a newer run may own it).
    const release = () => {
      stream.getTracks().forEach((track) => track.stop());
      void context?.close().catch(() => undefined);
      if (streamRef.current === stream) streamRef.current = null;
      if (context && inputContextRef.current === context) inputContextRef.current = null;
    };
    try {
      if (alive()) {
        streamRef.current = stream;
        context = new AudioContext();
        inputContextRef.current = context;
        await context.resume();
      }
    } catch (caught) {
      release();
      throw caught;
    }
    if (!context || !alive()) {
      release();
      return null;
    }
    configureMeter(stream, context);
    return { stream, context };
  }, [configureMeter]);

  const runStart = useCallback(async (run: number): Promise<boolean> => {
    if (!sessionId) return false;
    const alive = () => runRef.current === run;
    if (plannedRef.current.length === 0) {
      await prepare();
      // prepare() already reported why when it produced no plan.
      if (!alive() || plannedRef.current.length === 0) return false;
    }
    const order = plannedRef.current;
    setError(null);
    setMicPermissionDenied(false);
    setEnded(false);
    setActiveProvider(null);
    setFailedOver(false);
    stoppingRef.current = false;
    setConnection('connecting');
    // A retry must not stack a second microphone, peer or socket on what a failed try left behind.
    closeTransport();

    let media: { stream: MediaStream; context: AudioContext } | null;
    try {
      media = await acquireMic(alive);
    } catch (caught) {
      if (!alive()) return false;
      closeTransport();
      const micError = describeMicrophoneError(caught);
      setMicPermissionDenied(micError.permissionDenied);
      // The shared microphone copy names "Start recording"; the control on this panel is "Start speaking".
      setError(micError.message.replace('Start recording', 'Start speaking'));
      setConnection('error');
      return false;
    }
    if (!media) return false;
    const { stream, context } = media;

    // Strictly one provider at a time: the API admits a single in-flight live-voice request per user.
    let failure: unknown = null;
    for (let index = 0; index < order.length; index += 1) {
      const provider = order[index];
      let retriedRateLimit = false;
      for (;;) {
        // Checked right before anything is created: after a cancel nothing new may be opened.
        if (!alive()) return false;
        try {
          const providerSessionId = provider === 'openai'
            ? await connectOpenAi(stream, alive)
            : await connectGemini(stream, context, alive);
          if (!alive()) return false;
          providerRef.current = provider;
          providerSessionIdRef.current = providerSessionId;
          setActiveProvider(provider);
          setFailedOver(index > 0);
          setMicEnabled(true);
          setPhase('listening');
          setConnection('connected');
          return true;
        } catch (caught) {
          if (!alive()) return false;
          resetProviderTransport();
          failure = caught;
          // The local per-user limiter is not the provider's fault: one retry on the same provider.
          if (apiErrorInfo(caught)?.status === 429 && !retriedRateLimit) {
            retriedRateLimit = true;
            await delay(RATE_LIMIT_RETRY_DELAY_MS);
            if (!alive()) return false;
            continue;
          }
          break;
        }
      }
      // Never switch providers on a definite answer, or once any of the conversation exists.
      if (!isProviderFailure(failure) || hasTranscriptText()) break;
      if (index < order.length - 1) console.warn('Live voice provider failed before going live; trying the next one.', failureCode(failure));
    }
    closeTransport();
    // The server's health order may have changed by now: a retry asks it again.
    plannedRef.current = [];
    setError(learnerMessage(failure));
    setConnection('error');
    return false;
  }, [acquireMic, closeTransport, connectGemini, connectOpenAi, hasTranscriptText, prepare, resetProviderTransport, sessionId]);

  // Single-flight: the auto-start and a tap on "Start speaking" are one start.
  const start = useCallback((): Promise<boolean> => {
    const inFlight = startPromiseRef.current;
    if (inFlight && inFlight.run === runRef.current) return inFlight.promise;
    const run = ++runRef.current;
    const promise: Promise<boolean> = runStart(run)
      .catch((caught) => {
        console.warn('Live voice start failed unexpectedly.', failureCode(caught));
        if (runRef.current === run) {
          closeTransport();
          setError(LIVE_VOICE_UNAVAILABLE);
          setConnection('error');
        }
        return false;
      })
      .finally(() => {
        if (startPromiseRef.current?.promise === promise) startPromiseRef.current = null;
      });
    startPromiseRef.current = { run, promise };
    return promise;
  }, [closeTransport, runStart]);

  const runStop = useCallback(async (): Promise<boolean> => {
    const run = ++runRef.current; // abandons a start() that is still connecting
    // False once unmount, a session change or a newer start took over: the refs are theirs then.
    const current = () => runRef.current === run;
    const provider = providerRef.current;
    const providerSessionId = providerSessionIdRef.current;
    const segments = segmentsRef.current;
    if (!provider || !providerSessionId) {
      // Nothing ever went live, so there is nothing to save: leaving must never wait on a failed or cancelled start.
      stoppingRef.current = true;
      closeTransport();
      setConnection((state) => (state === 'connecting' ? 'ready' : state));
      return true;
    }
    stoppingRef.current = true;
    setConnection('ending');
    setMicEnabled(false);
    setPhase('idle');
    const releaseSession = () => {
      if (providerSessionIdRef.current !== providerSessionId) return;
      providerRef.current = null;
      providerSessionIdRef.current = null;
    };
    try {
      // GPT-Live bills until the session closes and confirms final usage only on session.closed;
      // closing first also drains the last transcript deltas.
      const channel = dataChannelRef.current;
      if (provider === 'openai' && channel?.readyState === 'open') {
        await new Promise<void>((resolve) => {
          const timer = window.setTimeout(resolve, 5_000);
          openAiClosedRef.current = () => {
            window.clearTimeout(timer);
            resolve();
          };
          try {
            channel.send(JSON.stringify({ type: 'session.close' }));
          } catch {
            openAiClosedRef.current();
          }
        });
        openAiClosedRef.current = null;
      }
      // The per-turn rows are advisory: a turn that cannot be saved must not keep the transcript from being saved.
      await flushPromiseRef.current.catch(() => undefined);
      await flushPendingTurn().catch(() => undefined);
      // The conversation is over: release the microphone and provider before the (retryable) save.
      if (current()) closeTransport();
      // The server rejects an empty transcript, and there is nothing to grade in one.
      if (segments.length > 0) {
        await persistLiveVoiceTranscript(sessionId, { provider, providerSessionId, segments });
      }
      releaseSession();
      if (current()) {
        if (segments.length > 0) setEnded(true);
        setConnection('ended');
      }
      return true;
    } catch (caught) {
      if (isClientRejection(caught)) {
        // The server will never take this transcript (window closed, session no longer active, invalid):
        // a retry cannot help and must not strand the learner.
        console.warn('Live voice transcript rejected by the server.', failureCode(caught));
        releaseSession();
        if (current()) {
          setError(learnerMessage(caught, TRANSCRIPT_NOT_SAVED));
          setConnection('ended');
        }
        return true;
      }
      if (current()) {
        stoppingRef.current = false;
        setError(learnerMessage(caught, TRANSCRIPT_NOT_SAVED));
        setConnection('error');
      }
      return false;
    }
  }, [closeTransport, flushPendingTurn, sessionId]);

  // Single-flight: overlapping callers (the exam page's 3 s polls, the unmount cleanup) share one save.
  const stop = useCallback((): Promise<boolean> => {
    if (stopPromiseRef.current) return stopPromiseRef.current;
    const promise: Promise<boolean> = runStop().finally(() => {
      if (stopPromiseRef.current === promise) stopPromiseRef.current = null;
    });
    stopPromiseRef.current = promise;
    return promise;
  }, [runStop]);

  useEffect(() => {
    return () => {
      void stop();
    };
  }, [stop]);

  useEffect(() => {
    setCaptions([]);
    setError(null);
    setPreflight(null);
    setEnded(false);
    setMicPermissionDenied(false);
    setActiveProvider(null);
    setFailedOver(false);
    providerRef.current = null;
    providerSessionIdRef.current = null;
    plannedRef.current = [];
    pendingCandidateRef.current = '';
    pendingPatientRef.current = '';
    pendingStartedAtRef.current = null;
    segmentsRef.current = [];
    turnIndexRef.current = 0;
    flushPromiseRef.current = Promise.resolve();
    startPromiseRef.current = null;
    stopPromiseRef.current = null;
    stoppingRef.current = false;
    if (!sessionId) {
      setConnection('idle');
      return undefined;
    }
    void prepare();
    return () => {
      runRef.current += 1;
      prepareRunRef.current += 1;
      stoppingRef.current = true;
      closeTransport();
    };
  }, [closeTransport, prepare, sessionId]);

  return {
    connection,
    phase,
    preflight,
    captions,
    micLevel,
    micEnabled,
    awaitingCandidateStart,
    error,
    micPermissionDenied,
    ended,
    provider: activeProvider,
    failedOver,
    audioRef,
    prepare,
    start,
    stop,
  };
}
