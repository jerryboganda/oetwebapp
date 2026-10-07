'use client';

import { useCallback, useEffect, useRef, useState, type RefObject } from 'react';
import { describeMicrophoneError } from '@/lib/mobile/speaking-recorder';
import {
  createGeminiLiveToken,
  createOpenAiLiveOffer,
  captureLiveVoiceAudioTurn,
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
  /** True while a live link that dropped or went silent is being restored (the conversation continues on the new link). */
  recovering: boolean;
  /** How many times the live link was restored in this role-play. Diagnostics and the QA harness only. */
  recoveries: number;
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
/**
 * How long stop() keeps a Gemini link up before it closes it. Gemini transcribes a sentence 1.4-1.7 s after it was
 * spoken (production 1 Oct 2026) and has no close handshake, so a candidate still talking at the buzzer would otherwise
 * lose the end of the last sentence. GPT-Live drains through its own session.closed.
 */
export const GEMINI_STOP_DRAIN_MS = 2_000;
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
 * The providers to try, in order. The server names them in the order to try (`candidates`: the
 * configured primary first; health only filters out providers that are known to be down); an older
 * server sends none, so there is a single attempt with `provider`. Only the server's `pinned` pins:
 * it is true for a flagged QA account that asked for one provider (`?voiceProvider=` on the page), and
 * that run never fails over, so comparison and QA runs measure the provider they asked for. A provider
 * the page merely asked for never shrinks the plan: for everybody else the server ignores the request.
 */
export function planProviders(
  preflight: Pick<LiveVoicePreflight, 'provider' | 'candidates' | 'pinned'>,
): LiveVoiceProvider[] {
  if (preflight.pinned) return isLiveVoiceProvider(preflight.provider) ? [preflight.provider] : [];
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

/**
 * Runs a live-voice write, retrying it when the per-user limiter (one live-voice request at a time) answered 429 because another
 * request of this learner was still in flight. Any other failure, and a 429 that outlasts the retries, is the caller's.
 */
async function withRateLimitRetry<T>(run: () => Promise<T>): Promise<T> {
  for (let attempt = 0; ; attempt += 1) {
    try {
      return await run();
    } catch (caught) {
      if (apiErrorInfo(caught)?.status !== 429 || attempt >= LIVE_VOICE_429_RETRIES) throw caught;
      await delay(LIVE_VOICE_429_RETRY_DELAY_MS * (attempt + 1));
    }
  }
}

/**
 * Mid-session recovery. A live link that dies after it was live, or a patient that stays silent after the
 * candidate stopped speaking, is restored on a NEW provider session for the same role-play (the server
 * replays the saved turns into its instructions). The server allows three provider sessions per role-play:
 * the first plus two restores. A run the server pinned (a flagged QA account's ?voiceProvider=) never recovers,
 * so comparison and QA runs still measure the raw stability of the provider they asked for.
 */
export const MAX_RECOVERIES = 2;
/** The patient normally answers within ~3 s (slowest healthy replies seen ~19 s); the silent sessions of 30 Sep 2026 never answered. */
export const STALL_MS = 20_000;
/** Gemini only: an unanswered sentence first gets one end-of-audio nudge this long after it ended; a restore follows at STALL_MS. */
export const GEMINI_NUDGE_MS = 5_000;
const STALL_CHECK_MS = 1_000;
/** A dropped WebRTC link that comes back by itself within this time is not a loss. */
const PEER_DISCONNECT_GRACE_MS = 5_000;
/** Only a real sentence (not a cough or a click) is something the patient owes an answer to. */
const MIN_ANSWERABLE_SPEECH_MS = 1_000;
// GPT-Live's own session.closed reasons: the learner asked (close_requested), the time or content policy
// ended it (expired, content), or the link died (remote_hangup, connection_lost).
const UNRECOVERABLE_CLOSE_REASONS = new Set(['close_requested', 'expired', 'content']);

/** A burst of speech on the microphone, in performance.now() milliseconds. */
export interface SpeechSpan {
  startMs: number;
  endMs: number;
}

// The meter's 0..1 scale is RMS x 3: 0.05 is about -36 dBFS, well above room noise and below quiet speech.
const SPEECH_LEVEL = 0.05;
const SPEECH_END_SILENCE_MS = 700;
const MIN_SPEECH_BURST_MS = 400;
const PATIENT_AUDIO_TAIL_MS = 750;
// GPT-Live reports the patient only as transcript deltas: no audio-done or turn-complete event ever clears the "patient is
// speaking" flag (7 Oct 2026 pilot: capture stayed suppressed after the first reply, one clip stored for 3.5 minutes). The
// patient therefore counts as finished this long after their last output; buffered Gemini audio still defers it.
export const PATIENT_OUTPUT_IDLE_MS = 2_000;
// A candidate clip covers one stretch of speech: it ends this long after the burst ended (a new burst inside it extends the
// same clip) and is rotated at MAX_CLIP_MS so a long monologue is stored in parts. The clip keeps SPEECH_END_SILENCE_MS plus
// this much trailing silence; the audio judge joins clips with a 600 ms gap and counts a pause of about 2 s as a long pause,
// so keep the sum below that or every clip boundary reads as a hesitation.
export const CLIP_HANGOVER_MS = 400;
export const MAX_CLIP_MS = 30_000;
const CLIP_TIMESLICE_MS = 1_000;
// The clip uploads share one queue, and the API admits one live-voice request per user at a time (a second one is answered 429
// at once, and the API client never retries a write's 429). A 429 is therefore retried here with a short backoff; everything else
// the API client already retries itself, and a clip that still fails is parked for the final retry at stop().
const LIVE_VOICE_429_RETRIES = 2;
const LIVE_VOICE_429_RETRY_DELAY_MS = 1_500;
// OpenAI's transcript timeline starts when its session was created (1-6 s before the page clock starts), so a short turn can
// sit just outside its own clip: the final link pass takes the nearest clip within this slack.
export const CLIP_LINK_TOLERANCE_MS = 5_000;

export interface SpeechTracker {
  /** Feeds one meter sample; returns the burst that just ended (after SPEECH_END_SILENCE_MS of quiet), if any. */
  update: (level: number, nowMs: number) => SpeechSpan | null;
  /** The burst in progress, if any. */
  active: () => { startMs: number } | null;
  reset: () => void;
}

interface ActiveCandidateAudioCapture {
  recorder: MediaRecorder;
  chunks: Blob[];
  /** True once a burst of real speech (not a click) has run during this clip; a clip without one is not stored. */
  hadSpeech: boolean;
  startMs: number;
  providerSessionId: string;
  sessionId: string;
}

interface FailedCandidateAudioCapture {
  audio: Blob;
  durationMs: number;
  startMs: number;
  endMs: number;
  providerSessionId: string;
  sessionId: string;
}

/**
 * Finds the candidate's speech bursts from the microphone level. Gemini gives the candidate's transcript no
 * timing (every segment was zero-length on 30 Sep 2026, which the grader reads as a capture error), and the
 * stall watchdog needs to know when the candidate stopped talking.
 */
export function createSpeechTracker(): SpeechTracker {
  let startedAt: number | null = null;
  let lastLoudAt = 0;
  return {
    update(level, nowMs) {
      if (level >= SPEECH_LEVEL) {
        if (startedAt === null) startedAt = nowMs;
        lastLoudAt = nowMs;
        return null;
      }
      if (startedAt === null || nowMs - lastLoudAt < SPEECH_END_SILENCE_MS) return null;
      const span = { startMs: startedAt, endMs: lastLoudAt };
      startedAt = null;
      return span.endMs - span.startMs >= MIN_SPEECH_BURST_MS ? span : null;
    },
    active: () => (startedAt === null ? null : { startMs: startedAt }),
    reset() {
      startedAt = null;
      lastLoudAt = 0;
    },
  };
}

type LinkLoss = 'closed' | 'stall';

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
// A speaker who resumes after more than this much silence starts a new segment, so the pause stays visible to the
// grader instead of hiding inside one long segment (production 1 Oct 2026: 45 s of silence sat inside one 54 s
// candidate segment, which also inflated the candidate's talk time about 2x).
export const MAX_SAME_SPEAKER_GAP_MS = 10_000;

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
 * one speaker extend one segment, unless the speaker was silent for longer than
 * MAX_SAME_SPEAKER_GAP_MS. With a provider timeline interval (`spoken`),
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
  if (!fragment.trim()) {
    // GPT-Live sends the space between two words as a delta of its own ("Doctor." + " " + "Well,"). It keeps
    // the words apart on the end of the same speaker's last segment, but it never starts a segment (the server
    // rejects a blank one and fails the whole save), never takes the late path and never moves the segment's
    // end (a space after a long silence must not hide the silence from the gap rule).
    if (exact && last !== undefined && last.speaker === speaker) last.text += fragment;
    return false;
  }
  const late = spoken && isLateFragment(segments, speaker, fragment, at);
  const candidate = late
    ? [...segments].reverse().find((segment) => segment.speaker === speaker)
    : last !== undefined && last.speaker === speaker && at.startMs - last.endMs <= MAX_SAME_SPEAKER_GAP_MS ? last : undefined;
  const target = candidate && candidate.text.length + fragment.length <= MAX_SEGMENT_CHARS ? candidate : undefined;
  if (target) {
    target.text = exact ? target.text + fragment : `${target.text} ${fragment}`;
    target.endMs = Math.max(target.endMs, at.endMs);
  } else {
    segments.push({ speaker, startMs: at.startMs, endMs: at.endMs, text: fragment });
  }
  return late && target !== undefined;
}

/**
 * The segments in the order they started, for the saved transcript. Gemini transcribes the candidate ~1.5 s after the words
 * were spoken but the patient's text arrives at once, so a quick reply (or a late first one, production 1 Oct 2026) is appended
 * ahead of the line it answers. The sort is stable: segments that start together keep the order they arrived in.
 */
export function inOrderOfStart(segments: readonly LiveVoiceTranscriptSegmentInput[]): LiveVoiceTranscriptSegmentInput[] {
  return [...segments].sort((a, b) => a.startMs - b.startMs);
}

export interface CandidateAudioRecordingSpan {
  startMs: number;
  endMs: number;
  recordingId: string;
}

/**
 * Links each unlinked candidate segment to the clip it overlaps most. With `toleranceMs` (the final pass at stop only: while
 * the role-play runs the clip a segment belongs to may simply not be uploaded yet, and the first link is kept) a segment that
 * overlaps no clip takes the nearest one within the slack; a zero-length segment (Gemini gives none timing) is a point.
 */
export function linkCandidateAudioToTranscript(
  segments: readonly LiveVoiceTranscriptSegmentInput[],
  recordings: readonly CandidateAudioRecordingSpan[],
  toleranceMs = 0,
): LiveVoiceTranscriptSegmentInput[] {
  return segments.map((segment) => {
    if (segment.speaker !== 'candidate' || segment.sourceRecordingId) return segment;
    const matches = recordings.map((candidate) => ({
      candidate,
      overlapMs: Math.max(
        0,
        Math.min(segment.endMs, candidate.endMs) - Math.max(segment.startMs, candidate.startMs),
      ),
      gapMs: Math.max(0, candidate.startMs - segment.endMs, segment.startMs - candidate.endMs),
    }));
    const overlapping = matches.filter((match) => match.overlapMs > 0).sort((left, right) => right.overlapMs - left.overlapMs)[0];
    const nearest = toleranceMs > 0
      ? matches.filter((match) => match.gapMs <= toleranceMs).sort((left, right) => left.gapMs - right.gapMs)[0]
      : undefined;
    const recording = (overlapping ?? nearest)?.candidate;
    return recording ? { ...segment, sourceRecordingId: recording.recordingId } : segment;
  });
}

/**
 * The patient is a person in a consultation, never an assistant. Gemini nevertheless appends a safety disclaimer to some
 * replies ("This information is for educational purposes and is not medical advice; please see a healthcare professional"),
 * whatever the prompt says (production 2 Oct 2026: 1-3 per run, with or without a restore). The prompt forbids it; this is the
 * guard for when the model does it anyway: the sentence is kept out of the saved transcript the grader reads.
 */
const PATIENT_DISCLAIMER = /not (?:a substitute for )?medical advice|for educational (?:purposes|and informational)|(?:consult|see|speak (?:to|with)) (?:a|your) (?:healthcare|health care|medical)\b[^.!?]*\b(?:professional|provider)/i;
export const isPatientDisclaimer = (text: string): boolean => PATIENT_DISCLAIMER.test(text);
export function withoutDisclaimerSentences(text: string): string {
  return text.split(/(?<=[.!?])\s+/).filter((sentence) => !isPatientDisclaimer(sentence)).join(' ').trim();
}
export function withoutPatientDisclaimers(segments: readonly LiveVoiceTranscriptSegmentInput[]): LiveVoiceTranscriptSegmentInput[] {
  return segments
    .map((segment) => (segment.speaker === 'patient' ? { ...segment, text: withoutDisclaimerSentences(segment.text) } : segment))
    .filter((segment) => segment.text.trim() !== '');
}

// Refresh-safe transcript. The conversation lives in memory until stop() saves it, so a reload or a crashed tab mid-card
// would lose it (after a reload only what is said afterwards would be saved and graded). A copy per Speaking session is
// kept in sessionStorage (this tab only, nothing but that session's own conversation, no tokens) and taken back when the
// hook mounts again for the same session. Storage can be missing, blocked or full: everything below then does nothing
// and the transcript lives in memory only, as before.
const CHECKPOINT_KEY_PREFIX = 'oet.speaking.live.';
/** A stored copy older than this is not restored: the role-play (5 minutes plus the save window) is long over. */
export const CHECKPOINT_TTL_MS = 15 * 60_000;
/** At most one write a second while the conversation changes, so a long monologue is still kept as it goes. */
const CHECKPOINT_WRITE_DELAY_MS = 1_000;
/** A stored stamp may run this far ahead of this device's clock (a tab's clock is not exact) before it is distrusted. */
const CHECKPOINT_CLOCK_SKEW_MS = 60_000;
/** A role-play is about 5 minutes; a clock origin older than the age limit plus this is not from one. */
const CHECKPOINT_MAX_ROLEPLAY_MS = 60 * 60_000;

export const transcriptCheckpointKey = (sessionId: string) => `${CHECKPOINT_KEY_PREFIX}${sessionId}`;

interface TranscriptCheckpoint {
  sessionId: string;
  segments: LiveVoiceTranscriptSegmentInput[];
  audioRecordings: CandidateAudioRecordingSpan[];
  /** The last turn number sent, so a restored hook carries on counting. */
  turnIndex: number;
  /** Date.now() when the role-play's clock started (the first provider session went live): a reload rebuilds the clock from it. */
  originEpochMs: number;
  /** Date.now() of this write; the copy is only restored while it is younger than CHECKPOINT_TTL_MS. */
  savedAt: number;
}

function isStoredSegment(value: unknown): boolean {
  if (!value || typeof value !== 'object') return false;
  const segment = value as Record<string, unknown>;
  return (segment.speaker === 'candidate' || segment.speaker === 'patient')
    && typeof segment.text === 'string' && segment.text.trim() !== ''
    && typeof segment.startMs === 'number' && Number.isFinite(segment.startMs)
    && typeof segment.endMs === 'number' && Number.isFinite(segment.endMs)
    && (segment.sourceRecordingId === undefined
      || (typeof segment.sourceRecordingId === 'string' && segment.sourceRecordingId.length <= 64));
}

function isStoredAudioRecording(value: unknown): value is CandidateAudioRecordingSpan {
  if (!value || typeof value !== 'object') return false;
  const recording = value as Record<string, unknown>;
  return typeof recording.recordingId === 'string' && recording.recordingId.length <= 64
    && typeof recording.startMs === 'number' && Number.isFinite(recording.startMs)
    && typeof recording.endMs === 'number' && Number.isFinite(recording.endMs)
    && recording.endMs >= recording.startMs;
}

/** The stored copy of this session's conversation; null when there is none, it is not this session's, it is too old or malformed, or storage is unavailable. */
function readTranscriptCheckpoint(sessionId: string): TranscriptCheckpoint | null {
  try {
    const key = transcriptCheckpointKey(sessionId);
    const raw = window.sessionStorage.getItem(key);
    if (!raw) return null;
    const stored: unknown = JSON.parse(raw);
    const value: Record<string, unknown> = stored !== null && typeof stored === 'object' ? (stored as Record<string, unknown>) : {};
    const { segments, turnIndex, originEpochMs, savedAt } = value;
    const audioRecordings = value.audioRecordings ?? [];
    const now = Date.now();
    if (
      value.sessionId !== sessionId
      // Recent, and not from the future: a tampered or skewed stamp must not slip past the age limit.
      || typeof savedAt !== 'number' || !(savedAt >= now - CHECKPOINT_TTL_MS && savedAt <= now + CHECKPOINT_CLOCK_SKEW_MS)
      || !Array.isArray(segments) || segments.length === 0 || !segments.every(isStoredSegment)
      || !Array.isArray(audioRecordings) || !audioRecordings.every(isStoredAudioRecording)
      || typeof turnIndex !== 'number' || !Number.isInteger(turnIndex) || turnIndex < 0
      // The clock origin is rebuilt from this: an absurd value (0, the future) would put every new segment time out of range.
      || typeof originEpochMs !== 'number' || !Number.isFinite(originEpochMs)
      || originEpochMs > now + CHECKPOINT_CLOCK_SKEW_MS || now - originEpochMs > CHECKPOINT_TTL_MS + CHECKPOINT_MAX_ROLEPLAY_MS
    ) {
      window.sessionStorage.removeItem(key); // a copy that cannot be used must not linger
      return null;
    }
    return {
      sessionId,
      // Only the fields the server takes: whatever else sits in storage is never sent.
      segments: (segments as LiveVoiceTranscriptSegmentInput[]).map(
        ({ speaker, startMs, endMs, text, sourceRecordingId }) => ({
          speaker, startMs, endMs, text, ...(sourceRecordingId ? { sourceRecordingId } : {}),
        }),
      ),
      audioRecordings: audioRecordings as CandidateAudioRecordingSpan[],
      turnIndex,
      originEpochMs,
      savedAt,
    };
  } catch {
    // Not valid JSON (or storage threw): drop it, a copy that cannot be read must not linger either.
    clearTranscriptCheckpoint(sessionId);
    return null;
  }
}

function writeTranscriptCheckpoint(checkpoint: TranscriptCheckpoint): void {
  try {
    window.sessionStorage.setItem(transcriptCheckpointKey(checkpoint.sessionId), JSON.stringify(checkpoint));
  } catch {
    // Storage is blocked or full: the transcript lives in memory only, exactly as before.
  }
}

function clearTranscriptCheckpoint(sessionId: string): void {
  try {
    window.sessionStorage.removeItem(transcriptCheckpointKey(sessionId));
  } catch {
    // Storage is blocked: nothing was kept that could be removed.
  }
}

/**
 * `requestedProvider` only ASKS the server to pin one provider (comparison and QA runs, `?voiceProvider=` on the page).
 * The server honours it for a flagged QA account and answers `pinned: true`; for everybody else it is ignored and the
 * automatic order, failover and recovery apply. Only the server's answer pins, never this argument.
 */
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
  const [recovering, setRecovering] = useState(false);
  const [recoveries, setRecoveries] = useState(0);

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
  // Tags the turn ids of one mount: after a reload the same Speaking session gets a new hook instance, and the server
  // drops a repeated (session, clientTurnId) as a duplicate, so the ids of two instances must never meet.
  const runTagRef = useRef('');
  const activeSessionIdRef = useRef(sessionId);
  activeSessionIdRef.current = sessionId;
  const segmentsRef = useRef<LiveVoiceTranscriptSegmentInput[]>([]);
  // True once THIS mount took in words. A conversation restored after a reload does not count: a first connect that
  // fails then may still fail over to the other provider (the server replays the saved turns into either one).
  const heardRef = useRef(false);
  const flushPromiseRef = useRef(Promise.resolve());
  const geminiReadyRef = useRef(false);
  const stoppingRef = useRef(false);
  const openAiClosedRef = useRef<(() => void) | null>(null);
  // Mid-session recovery (see MAX_RECOVERIES). The handlers declared before startRecovery report a lost link
  // through onLinkLostRef, which says whether a restore took over (false: show the error as before).
  const recoveringRef = useRef(false);
  const recoveryCountRef = useRef(0);
  const firstProviderRef = useRef<LiveVoiceProvider | null>(null);
  // A run the server pinned (a flagged QA account's ?voiceProvider=) never recovers: comparison and QA runs measure the
  // provider raw. Set from the server's answer only; the page's own request never pins.
  const pinnedRef = useRef(false);
  const onLinkLostRef = useRef<(reason: LinkLoss) => boolean>(() => false);
  const peerGraceTimerRef = useRef<number | undefined>(undefined);
  // performance.now() of the patient's last output (audio or transcript) and of the end of the candidate's last
  // real sentence; the candidate's speech bursts come from the microphone level (see createSpeechTracker).
  const lastPatientOutputAtRef = useRef(0);
  const candidateSpokeUntilRef = useRef<number | null>(null);
  // The candidateSpokeUntilRef value the Gemini end-of-audio nudge was already sent for (one nudge per unanswered sentence).
  const nudgedForRef = useRef<number | null>(null);
  // What the patient has said so far in the turn Gemini is generating, and whether the rest of it is muted (a disclaimer began).
  const turnPatientTextRef = useRef('');
  const muteTurnRef = useRef(false);
  const speechTrackerRef = useRef<SpeechTracker>(createSpeechTracker());
  const candidateSpansRef = useRef<SpeechSpan[]>([]);
  const activeCandidateAudioRef = useRef<ActiveCandidateAudioCapture | null>(null);
  const candidateAudioRecordingsRef = useRef<CandidateAudioRecordingSpan[]>([]);
  const pendingCandidateAudioRef = useRef<Promise<boolean>[]>([]);
  const failedCandidateAudioRef = useRef<FailedCandidateAudioCapture[]>([]);
  const candidateAudioUnavailableRef = useRef(false);
  const candidateAudioSuppressedRef = useRef(false);
  const patientAudioPlaybackRef = useRef(false);
  const patientAudioTailTimerRef = useRef<number | undefined>(undefined);
  const stopCandidateAudioRef = useRef<(() => Promise<boolean>) | null>(null);
  const patientOutputIdleTimerRef = useRef<number | undefined>(undefined);
  const clipHangoverTimerRef = useRef<number | undefined>(undefined);
  // Every clip upload waits for the one before it; the chain never rejects.
  const clipUploadChainRef = useRef<Promise<unknown>>(Promise.resolve());
  // Diagnostics only: which event types the OpenAI link sent (logged once at stop()).
  const openAiEventTypesRef = useRef<Set<string>>(new Set());
  const assignedBurstStartRef = useRef(-1);
  // One clock for the whole role-play (see markSessionLive): performance.now() when the FIRST provider session went live,
  // the same instant as Date.now() (a reload rebuilds the clock from it), and how long after the origin the CURRENT
  // provider session went live.
  const originRef = useRef<number | null>(null);
  const originEpochRef = useRef<number | null>(null);
  const sessionOffsetMsRef = useRef(0);
  const checkpointTimerRef = useRef<number | undefined>(undefined);

  const finishPatientAudioPlayback = useCallback(() => {
    window.clearTimeout(patientOutputIdleTimerRef.current);
    patientOutputIdleTimerRef.current = undefined;
    if (!patientAudioPlaybackRef.current) return;
    patientAudioPlaybackRef.current = false;
    window.clearTimeout(patientAudioTailTimerRef.current);
    // Once the tail is over the patient is silent, so a burst already running is the candidate's: capture starts mid-burst
    // (the meter loop re-checks every frame) instead of losing the whole burst.
    patientAudioTailTimerRef.current = window.setTimeout(() => {
      patientAudioTailTimerRef.current = undefined;
      candidateAudioSuppressedRef.current = false;
    }, PATIENT_AUDIO_TAIL_MS);
  }, []);

  // Per-provider teardown between failover attempts. Handlers are detached BEFORE close(): a WebSocket
  // closes asynchronously, and a stale onclose would flip the next attempt to 'error'. The microphone,
  // its AudioContext and the level meter stay, so one permission prompt serves every attempt.
  const resetProviderTransport = useCallback(() => {
    attemptRef.current?.fail(new ProviderConnectError('The connection attempt was cancelled.'));
    attemptRef.current = null;
    openAiClosedRef.current = null;
    window.clearTimeout(peerGraceTimerRef.current);
    geminiReadyRef.current = false;
    playbackSourcesRef.current.forEach((source) => {
      try {
        source.stop();
      } catch {}
    });
    playbackSourcesRef.current.clear();
    finishPatientAudioPlayback();
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
    void stopCandidateAudioRef.current?.();
    window.clearTimeout(clipHangoverTimerRef.current);
    clipHangoverTimerRef.current = undefined;
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
    finishPatientAudioPlayback();
  }, [finishPatientAudioPlayback]);

  const prepare = useCallback(async () => {
    if (!sessionId) return;
    const run = ++prepareRunRef.current;
    setConnection('preparing');
    setError(null);
    try {
      const result = await getLiveVoicePreflight(sessionId, requestedProvider);
      // A late answer (the page re-mounts the hook when it learns ?voiceProvider=) must not replace a newer one.
      if (run !== prepareRunRef.current) return;
      // requestedProvider only asked: the plan and the pin follow the server's answer (`pinned` is true for a flagged QA account only).
      const planned = planProviders(result);
      if (planned.length === 0) throw new ProviderConnectError('The preflight named no usable provider.');
      setPreflight(result);
      plannedRef.current = planned;
      pinnedRef.current = Boolean(result.pinned);
      setConnection('ready');
    } catch (caught) {
      if (run !== prepareRunRef.current) return;
      setError(learnerMessage(caught));
      setConnection('error');
    }
  }, [requestedProvider, sessionId]);

  // GPT-Live's start_ms/end_ms are relative to its own provider session and restart at 0 in every restore, and Gemini
  // gives the candidate no timing (page time was used). Both are put on ONE clock, "ms since the first provider session
  // went live", so a restore keeps the segments in order and nothing is merged across it (production 1 Oct 2026: after a
  // restore the new session's first lines were fused into earlier segments in all three recovery runs). A restore, or a
  // manual restart, keeps the origin; a reload rebuilds it from the stored one (see the checkpoint); only a different
  // Speaking session starts a clock of its own.
  const markSessionLive = useCallback(() => {
    const now = performance.now();
    const origin = originRef.current ?? now;
    if (originRef.current === null) {
      originRef.current = origin;
      originEpochRef.current = Date.now();
    }
    sessionOffsetMsRef.current = Math.max(0, Math.round(now - origin));
  }, []);
  const sinceOrigin = useCallback((at: number) => Math.max(0, Math.round(at - (originRef.current ?? 0))), []);

  // Writes the refresh-safe copy now and cancels a pending timed write. Only while a provider session is live: before it
  // (a reload that has not reconnected yet) storage already holds exactly what memory holds, and after the save there is
  // nothing left to protect.
  const flushCheckpoint = useCallback(() => {
    window.clearTimeout(checkpointTimerRef.current);
    checkpointTimerRef.current = undefined;
    const originEpochMs = originEpochRef.current;
    if (!sessionId || !providerSessionIdRef.current || originEpochMs === null || segmentsRef.current.length === 0) return;
    writeTranscriptCheckpoint({
      sessionId,
      segments: segmentsRef.current,
      audioRecordings: candidateAudioRecordingsRef.current,
      turnIndex: turnIndexRef.current,
      originEpochMs,
      savedAt: Date.now(),
    });
  }, [sessionId]);
  const scheduleCheckpoint = useCallback(() => {
    if (checkpointTimerRef.current === undefined) checkpointTimerRef.current = window.setTimeout(flushCheckpoint, CHECKPOINT_WRITE_DELAY_MS);
  }, [flushCheckpoint]);

  // One upload at a time (a clip never collides with another clip at the per-user limiter), each retried on a 429. The chain
  // itself never rejects; the caller gets this upload's own result.
  const uploadCandidateClip = useCallback((capture: FailedCandidateAudioCapture) => {
    const upload = () => withRateLimitRetry(() => captureLiveVoiceAudioTurn(capture.sessionId, {
      providerSessionId: capture.providerSessionId,
      audio: capture.audio,
      durationMs: capture.durationMs,
    }));
    const result = clipUploadChainRef.current.then(upload, upload);
    clipUploadChainRef.current = result.catch(() => undefined);
    return result;
  }, []);

  const saveCandidateAudio = useCallback(async (
    capture: FailedCandidateAudioCapture,
    recordings = candidateAudioRecordingsRef.current,
  ): Promise<boolean> => {
    try {
      const stored = await uploadCandidateClip(capture);
      recordings.push({
        startMs: capture.startMs,
        endMs: capture.endMs,
        recordingId: stored.recordingId,
      });
      if (activeSessionIdRef.current !== capture.sessionId) return true;
      segmentsRef.current = linkCandidateAudioToTranscript(
        segmentsRef.current,
        candidateAudioRecordingsRef.current,
      );
      scheduleCheckpoint();
      return true;
    } catch {
      if (activeSessionIdRef.current === capture.sessionId) {
        failedCandidateAudioRef.current.push(capture);
        setError('Your voice recording could not be saved. Please try again before leaving this role-play.');
      }
      return false;
    }
  }, [scheduleCheckpoint, sessionId, uploadCandidateClip]);

  const startCandidateAudioCapture = useCallback((stream: MediaStream, startedAt: number) => {
    if (stoppingRef.current || activeCandidateAudioRef.current) return;
    const providerSessionId = providerSessionIdRef.current;
    if (!providerSessionId) return;
    if (typeof MediaRecorder === 'undefined') {
      candidateAudioUnavailableRef.current = true;
      setError('This browser cannot securely record your Speaking response. Use an updated Chrome, Edge, Safari or the mobile app.');
      return;
    }

    try {
      const recorder = new MediaRecorder(stream);
      const capture: ActiveCandidateAudioCapture = {
        recorder,
        chunks: [],
        hadSpeech: false,
        startMs: sinceOrigin(startedAt),
        providerSessionId,
        sessionId,
      };
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) capture.chunks.push(event.data);
      };
      // A timeslice keeps what was recorded so far in memory as it goes, so a tab that is suspended mid-clip loses seconds, not the clip.
      recorder.start(CLIP_TIMESLICE_MS);
      activeCandidateAudioRef.current = capture;
    } catch {
      candidateAudioUnavailableRef.current = true;
      setError('This browser could not record your Speaking response. Please use an updated browser and try again.');
    }
  }, [sessionId, sinceOrigin]);

  const stopCandidateAudioCapture = useCallback((): Promise<boolean> => {
    const capture = activeCandidateAudioRef.current;
    if (!capture) return Promise.resolve(true);
    activeCandidateAudioRef.current = null;
    const endMs = sinceOrigin(performance.now());
    const durationMs = Math.max(1, endMs - capture.startMs);
    const recordings = candidateAudioRecordingsRef.current;
    const task = new Promise<boolean>((resolve) => {
      const finish = (audio: Blob) => {
        // A click or a cough (no burst of real speech ran during it): not a clip worth storing, and not an error.
        if (!capture.hadSpeech) {
          resolve(true);
          return;
        }
        if (audio.size === 0) {
          if (activeSessionIdRef.current === capture.sessionId) {
            candidateAudioUnavailableRef.current = true;
            setError('No candidate audio was captured for this response. Please try again before leaving this role-play.');
          }
          resolve(false);
          return;
        }
        void saveCandidateAudio({
          audio,
          durationMs,
          startMs: capture.startMs,
          endMs,
          providerSessionId: capture.providerSessionId,
          sessionId: capture.sessionId,
        }, recordings).then(resolve);
      };

      capture.recorder.onstop = () => {
        finish(new Blob(capture.chunks, { type: capture.recorder.mimeType || 'audio/webm' }));
      };
      capture.recorder.onerror = () => {
        if (activeSessionIdRef.current === capture.sessionId) {
          candidateAudioUnavailableRef.current = true;
          setError('Your voice recording stopped unexpectedly. Please try again before leaving this role-play.');
        }
        resolve(false);
      };
      try {
        if (capture.recorder.state === 'recording') capture.recorder.stop();
        else finish(new Blob(capture.chunks, { type: capture.recorder.mimeType || 'audio/webm' }));
      } catch {
        candidateAudioUnavailableRef.current = true;
        setError('Your voice recording could not be finished. Please try again before leaving this role-play.');
        resolve(false);
      }
    });
    pendingCandidateAudioRef.current.push(task);
    return task;
  }, [saveCandidateAudio, sinceOrigin]);

  const beginPatientAudioPlayback = useCallback(() => {
    window.clearTimeout(patientAudioTailTimerRef.current);
    patientAudioTailTimerRef.current = undefined;
    patientAudioPlaybackRef.current = true;
    candidateAudioSuppressedRef.current = true;
    void stopCandidateAudioCapture();
    // Failsafe: every patient output renews it, so a provider that never says "audio done" cannot keep capture off for good.
    window.clearTimeout(patientOutputIdleTimerRef.current);
    const expire = () => {
      patientOutputIdleTimerRef.current = playbackSourcesRef.current.size > 0
        ? window.setTimeout(expire, PATIENT_OUTPUT_IDLE_MS)
        : undefined;
      if (patientOutputIdleTimerRef.current !== undefined) return;
      // The patient has been silent for the whole idle time, which is longer than the tail the real finish events wait for.
      finishPatientAudioPlayback();
      window.clearTimeout(patientAudioTailTimerRef.current);
      patientAudioTailTimerRef.current = undefined;
      candidateAudioSuppressedRef.current = false;
    };
    patientOutputIdleTimerRef.current = window.setTimeout(expire, PATIENT_OUTPUT_IDLE_MS);
  }, [finishPatientAudioPlayback, stopCandidateAudioCapture]);

  const flushCandidateAudio = useCallback(async (): Promise<boolean> => {
    const stopped = stopCandidateAudioCapture();
    const pendingUploads = pendingCandidateAudioRef.current;
    const failedUploads = failedCandidateAudioRef.current;
    const recordings = candidateAudioRecordingsRef.current;
    const unavailable = candidateAudioUnavailableRef.current;
    await stopped;
    let uploadsSucceeded = true;
    while (pendingUploads.length > 0) {
      const pending = pendingUploads.splice(0);
      if ((await Promise.all(pending)).some((saved) => !saved)) {
        uploadsSucceeded = false;
        break;
      }
    }

    if (failedUploads.length > 0) {
      // Every parked clip gets its retry: one that fails again goes back into the list, the others are not abandoned with it.
      const failed = failedUploads.splice(0);
      let retried = true;
      for (const capture of failed) {
        if (!(await saveCandidateAudio(capture, recordings))) retried = false;
      }
      uploadsSucceeded = retried;
    }

    return !unavailable && uploadsSucceeded
      && failedUploads.length === 0
      && (activeSessionIdRef.current !== sessionId || !candidateAudioUnavailableRef.current);
  }, [saveCandidateAudio, sessionId, stopCandidateAudioCapture]);

  stopCandidateAudioRef.current = stopCandidateAudioCapture;

  // A reload, a closed tab or a backgrounded page can end this document without any cleanup running, so the copy is also
  // written the moment the page is hidden. Leaving the card writes it one last time: a save that then fails must keep it.
  useEffect(() => {
    const onVisibilityChange = () => {
      if (document.visibilityState === 'hidden') flushCheckpoint();
    };
    window.addEventListener('pagehide', flushCheckpoint);
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => {
      window.removeEventListener('pagehide', flushCheckpoint);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      flushCheckpoint();
    };
  }, [flushCheckpoint]);

  // One caption and one transcript segment per speaker run keeps a 5-minute
  // conversation far below the server's 600-segment limit. `exact` fragments
  // (GPT-Live deltas) carry their own spacing and provider timing; a late one
  // rejoins its speaker's text instead of splitting it (read as disfluency).
  const addCaption = useCallback((
    speaker: RealtimeVoiceSpeaker,
    text: string,
    exact = false,
    spoken?: { startMs: number; endMs: number },
    // Where a provider that gives no timing (Gemini) put this speaker's words; never triggers the late-fragment rule.
    timing?: { startMs: number; endMs: number },
  ) => {
    // Gemini's transcription trails its audio, and its audio parts already mark the patient as speaking (and clear it when the
    // last one ends): a transcript chunk that lands after that must not start a new playback with no audio behind it.
    if (speaker === 'patient' && text.trim() && (providerRef.current !== 'gemini' || playbackSourcesRef.current.size > 0)) {
      beginPatientAudioPlayback();
    }
    // GPT-Live sends the space between two words as a delta of its own: it is kept, but only on the end of the same
    // speaker's last segment (see appendTranscriptFragment); anywhere else a blank fragment has nothing to join.
    const lastSegment = segmentsRef.current[segmentsRef.current.length - 1];
    if (!text.trim() && !(exact && lastSegment?.speaker === speaker)) return;
    const fragment = exact ? text : text.trim();
    const join = (existing: string) => (exact ? existing + fragment : `${existing} ${fragment}`);
    const now = Math.max(0, Math.round(performance.now()));
    if (pendingStartedAtRef.current === null) pendingStartedAtRef.current = now;
    // Arrival stamp for a fragment with no provider timing, on the role-play clock.
    const arrived = sinceOrigin(now);
    const late = appendTranscriptFragment(segmentsRef.current, speaker, fragment, exact, spoken ?? timing ?? { startMs: arrived, endMs: arrived }, Boolean(spoken));
    segmentsRef.current = linkCandidateAudioToTranscript(
      segmentsRef.current,
      candidateAudioRecordingsRef.current,
    );
    heardRef.current = true;
    setCaptions((current) => {
      const index = late
        ? current.map((caption) => caption.speaker).lastIndexOf(speaker)
        : current[current.length - 1]?.speaker === speaker ? current.length - 1 : -1;
      if (index >= 0) return current.map((caption, i) => (i === index ? { ...caption, text: join(caption.text) } : caption));
      if (!fragment.trim()) return current; // a lone space never opens a caption
      const id = `${speaker}-${Date.now()}-${Math.random().toString(36).slice(2)}`;
      return [...current, { id, speaker, text: fragment }].slice(-80);
    });
    if (speaker === 'candidate') setAwaitingCandidateStart(false);
    scheduleCheckpoint();
  }, [beginPatientAudioPlayback, scheduleCheckpoint, sinceOrigin]);

  // Takes the pending turn synchronously, so fragments that arrive while a save
  // is in flight start the next turn instead of being cleared with this one.
  const takePendingTurn = useCallback((): Parameters<typeof persistLiveVoiceTurn>[1] | null => {
    const provider = providerRef.current;
    const providerSessionId = providerSessionIdRef.current;
    const candidateText = pendingCandidateRef.current.trim();
    const patientText = withoutDisclaimerSentences(pendingPatientRef.current.trim());
    if (!provider || !providerSessionId || (!candidateText && !patientText)) return null;

    const startedAt = pendingStartedAtRef.current;
    const endedAt = Math.max(0, Math.round(performance.now()));
    turnIndexRef.current += 1;
    pendingCandidateRef.current = '';
    pendingPatientRef.current = '';
    pendingStartedAtRef.current = null;
    scheduleCheckpoint(); // the turn number moved: the refresh-safe copy carries it
    return {
      provider,
      providerSessionId,
      candidateText: candidateText || null,
      patientText: patientText || null,
      clientTurnId: `voice-turn:${runTagRef.current}:${turnIndexRef.current}`,
      turnIndex: turnIndexRef.current,
      startedAt: startedAt === null ? undefined : new Date(Date.now() - Math.max(0, endedAt - startedAt)).toISOString(),
      endedAt: new Date().toISOString(),
    };
  }, [scheduleCheckpoint]);

  const flushPendingTurn = useCallback(async () => {
    const turn = takePendingTurn();
    if (turn) await withRateLimitRetry(() => persistLiveVoiceTurn(sessionId, turn));
  }, [sessionId, takePendingTurn]);

  // The per-turn rows are advisory (stop() saves the whole transcript), so a row that will not save is logged, never
  // shown to the learner and never thrown: every caller is fire-and-forget. The chain therefore never rejects.
  const queueFlush = useCallback(() => {
    const turn = takePendingTurn();
    flushPromiseRef.current = flushPromiseRef.current.then(async () => {
      if (!turn) return;
      try {
        // A clip upload may hold the per-user live-voice permit at this moment; the row waits for it instead of being lost.
        await withRateLimitRetry(() => persistLiveVoiceTurn(sessionId, turn));
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

  // Gemini gives the candidate's transcript no timing (every candidate segment was zero-length on 30 Sep 2026, which
  // the grader reads as a capture error): the span comes from the microphone bursts instead. A fragment that arrives
  // after its burst closed takes the closed bursts; one that arrives mid-burst takes the burst so far and that burst
  // is then not attributed again to the next sentence. The bursts are kept in page time (that is what the tracker and
  // assignedBurstStartRef compare); the returned span is on the role-play clock, like every other segment time.
  const candidateTiming = useCallback((): { startMs: number; endMs: number } => {
    const now = Math.max(0, Math.round(performance.now()));
    const closed = candidateSpansRef.current.filter((span) => span.startMs > assignedBurstStartRef.current);
    candidateSpansRef.current = [];
    if (closed.length > 0) {
      return { startMs: sinceOrigin(closed[0].startMs), endMs: sinceOrigin(closed[closed.length - 1].endMs) };
    }
    const active = speechTrackerRef.current.active();
    if (active) {
      assignedBurstStartRef.current = active.startMs;
      return { startMs: sinceOrigin(active.startMs), endMs: sinceOrigin(now) };
    }
    return { startMs: sinceOrigin(now), endMs: sinceOrigin(now) };
  }, [sinceOrigin]);

  // Gemini streams incremental chunks: append, never de-duplicate.
  const captureTranscript = useCallback((speaker: RealtimeVoiceSpeaker, text: string) => {
    const chunk = text.trim();
    if (!chunk) return;
    addCaption(speaker, chunk, false, undefined, speaker === 'candidate' ? candidateTiming() : undefined);
    if (speaker === 'candidate') pendingCandidateRef.current = `${pendingCandidateRef.current} ${chunk}`.trim();
    else pendingPatientRef.current = `${pendingPatientRef.current} ${chunk}`.trim();
    flushIfLong();
  }, [addCaption, candidateTiming, flushIfLong]);

  const hasTranscriptText = useCallback(
    () => heardRef.current || pendingCandidateRef.current.trim() !== '' || pendingPatientRef.current.trim() !== '',
    [],
  );

  const handleOpenAiEvent = useCallback((value: Record<string, unknown>) => {
    const type = providerEventType(value).toLowerCase();
    if (type && openAiEventTypesRef.current.size < 50) openAiEventTypesRef.current.add(type);
    if (
      (type.includes('output_audio') || type.includes('response.audio'))
      && !type.endsWith('.done')
      && type !== 'response.done'
    ) {
      beginPatientAudioPlayback();
    }
    if (
      type === 'response.done'
      || type === 'response.cancelled'
      || type.includes('output_audio.done')
      || type.includes('response.audio.done')
      || type === 'session.closed'
    ) {
      finishPatientAudioPlayback();
    }
    // Before the link is live an event ends the attempt (so the next provider can be tried); after, it is a mid-conversation fault.
    const pending = pendingAttemptOf(attemptRef);
    if (type.includes('error')) {
      if (pending) {
        pending.fail(new ProviderConnectError('The provider reported an error before going live.'));
        return;
      }
      if (onLinkLostRef.current('closed')) return;
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
      // Only a dead link is restored; the provider's own time or content limit ends the role-play for good.
      const reason = typeof value.reason === 'string' ? value.reason : '';
      if (!UNRECOVERABLE_CLOSE_REASONS.has(reason) && onLinkLostRef.current('closed')) return;
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
    if (speaker === 'patient') lastPatientOutputAtRef.current = performance.now();
    // start_ms/end_ms count from the start of THIS provider session; the offset puts them on the role-play clock (it is 0
    // for the first session and the time between the two session starts after a restore).
    const offsetMs = sessionOffsetMsRef.current;
    const spoken = typeof value.start_ms === 'number' && typeof value.end_ms === 'number'
      ? { startMs: value.start_ms + offsetMs, endMs: value.end_ms + offsetMs }
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
  }, [addCaption, beginPatientAudioPlayback, finishPatientAudioPlayback, flushIfLong, queueFlush]);

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
      if (onLinkLostRef.current('closed')) return;
      setError(LIVE_VOICE_INTERRUPTED);
      setConnection('error');
      return;
    }

    // Gemini announces its own disconnect: restore now instead of waiting for the socket to die mid-sentence.
    if (value.goAway || value.go_away) {
      onLinkLostRef.current('closed');
      return;
    }

    const serverContent = (value.serverContent ?? value.server_content) as Record<string, unknown> | undefined;
    if (!serverContent) return;
    if (serverContent.interrupted === true) {
      interruptPlayback();
      turnPatientTextRef.current = '';
      muteTurnRef.current = false;
      setPhase('listening');
      return;
    }
    const inputTranscription = (serverContent.inputTranscription ?? serverContent.input_transcription) as unknown;
    const outputTranscription = (serverContent.outputTranscription ?? serverContent.output_transcription) as unknown;
    const inputText = providerTranscriptText(inputTranscription);
    const outputText = providerTranscriptText(outputTranscription);
    if (inputText) captureTranscript('candidate', inputText);
    if (outputText) {
      lastPatientOutputAtRef.current = performance.now();
      setPhase('speaking');
      captureTranscript('patient', outputText);
      turnPatientTextRef.current += outputText;
      if (!muteTurnRef.current && isPatientDisclaimer(turnPatientTextRef.current)) {
        // Cut the rest of a disclaimer short; what already played cannot be taken back.
        muteTurnRef.current = true;
        interruptPlayback();
      }
    }

    const modelTurn = (serverContent.modelTurn ?? serverContent.model_turn) as Record<string, unknown> | undefined;
    const parts = modelTurn?.parts;
    if (Array.isArray(parts)) {
      for (const part of parts) {
        const partRecord = part as Record<string, unknown>;
        const inline = (partRecord.inlineData ?? partRecord.inline_data) as Record<string, unknown> | undefined;
        const data = inline?.data;
        if (typeof data !== 'string') continue;
        beginPatientAudioPlayback();
        lastPatientOutputAtRef.current = performance.now();
        if (muteTurnRef.current) continue;
        const outputContext = outputContextRef.current;
        if (!outputContext) continue;
        const buffer = pcmToAudioBuffer(outputContext, data, audioRate(inline?.mimeType ?? inline?.mime_type));
        const source = outputContext.createBufferSource();
        source.buffer = buffer;
        source.connect(outputContext.destination);
        playbackSourcesRef.current.add(source);
        source.addEventListener('ended', () => {
          playbackSourcesRef.current.delete(source);
          if (playbackSourcesRef.current.size === 0) finishPatientAudioPlayback();
        }, { once: true });
        const startAt = Math.max(outputContext.currentTime, nextPlaybackTimeRef.current);
        source.start(startAt);
        nextPlaybackTimeRef.current = startAt + buffer.duration;
      }
    }

    if (serverContent.turnComplete || serverContent.turn_complete) {
      turnPatientTextRef.current = '';
      muteTurnRef.current = false;
      if (playbackSourcesRef.current.size === 0) finishPatientAudioPlayback();
      void queueFlush();
      setPhase('listening');
    }
  }, [beginPatientAudioPlayback, captureTranscript, finishPatientAudioPlayback, interruptPlayback, queueFlush]);

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
      const level = Math.min(1, Math.sqrt(sum / values.length) * 3);
      setMicLevel(level);
      const activeBefore = speechTrackerRef.current.active();
      const span = speechTrackerRef.current.update(level, performance.now());
      const activeNow = speechTrackerRef.current.active();
      if (
        activeBefore && !activeNow
        && !patientAudioPlaybackRef.current
        && patientAudioTailTimerRef.current === undefined
      ) {
        candidateAudioSuppressedRef.current = false;
      }
      // One clip per stretch of speech: it closes CLIP_HANGOVER_MS after the burst ended, unless a new burst starts first.
      if (activeNow) {
        window.clearTimeout(clipHangoverTimerRef.current);
        clipHangoverTimerRef.current = undefined;
      } else if (activeBefore) {
        window.clearTimeout(clipHangoverTimerRef.current);
        clipHangoverTimerRef.current = window.setTimeout(() => {
          clipHangoverTimerRef.current = undefined;
          void stopCandidateAudioRef.current?.();
        }, CLIP_HANGOVER_MS);
      }
      const running = activeCandidateAudioRef.current;
      if (running && activeNow && performance.now() - activeNow.startMs >= MIN_SPEECH_BURST_MS) running.hadSpeech = true;
      if (running && sinceOrigin(performance.now()) - running.startMs >= MAX_CLIP_MS) {
        void stopCandidateAudioRef.current?.(); // a long monologue is stored in parts; the capture below starts the next part at once
      }
      if (activeNow && !candidateAudioSuppressedRef.current && !patientAudioPlaybackRef.current) {
        // The clip starts now, which is the burst start unless suppression only just lifted in the middle of the burst.
        startCandidateAudioCapture(stream, performance.now());
      }
      if (span) {
        candidateSpansRef.current.push(span);
        if (candidateSpansRef.current.length > 40) candidateSpansRef.current.shift();
        if (span.endMs - span.startMs >= MIN_ANSWERABLE_SPEECH_MS) {
          // The stall clock runs from the OLDEST sentence the patient still owes an answer to: a candidate who
          // keeps talking to a silent patient must not keep pushing the restore back (94 s in production on 1 Oct 2026).
          const owed = candidateSpokeUntilRef.current;
          if (owed === null || lastPatientOutputAtRef.current >= owed) candidateSpokeUntilRef.current = span.endMs;
        }
      }
      meterFrameRef.current = window.requestAnimationFrame(tick);
    };
    meterFrameRef.current = window.requestAnimationFrame(tick);
  }, [sinceOrigin, startCandidateAudioCapture]);

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
        if (state === 'connected') {
          window.clearTimeout(peerGraceTimerRef.current);
          setConnection('connected');
        }
        if (!stoppingRef.current && (state === 'failed' || state === 'disconnected')) {
          const lost = () => {
            if (onLinkLostRef.current('closed')) return;
            setError(LIVE_VOICE_INTERRUPTED);
            setConnection('error');
          };
          if (state === 'failed') {
            lost();
          } else {
            // 'disconnected' often heals by itself within seconds: only a link that stays down is lost.
            window.clearTimeout(peerGraceTimerRef.current);
            peerGraceTimerRef.current = window.setTimeout(() => {
              if (peerRef.current === peer && peer.connectionState !== 'connected' && !stoppingRef.current) lost();
            }, PEER_DISCONNECT_GRACE_MS);
          }
        }
      };
      const dataChannel = peer.createDataChannel('oai-events');
      dataChannelRef.current = dataChannel;
      dataChannel.onopen = () => attempt.live();
      dataChannel.onclose = () => {
        if (attempt.pending()) {
          attempt.fail(new ProviderConnectError('The data channel closed before the session started.'));
          return;
        }
        if (stoppingRef.current) return;
        if (onLinkLostRef.current('closed')) return;
        setError(LIVE_VOICE_INTERRUPTED);
        setConnection('error');
      };
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
        if (!stoppingRef.current && !onLinkLostRef.current('closed')) {
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

  // Restores a live link that died after it was live, or a patient that stopped answering, on a NEW provider
  // session for the same role-play; the server gives that session the conversation so far. Returns true when a
  // restore took over (the caller then shows nothing) and false when the link is lost for good (the caller shows the
  // error as before). The microphone, its context and the transcript carry on untouched.
  const startRecovery = useCallback((reason: LinkLoss): boolean => {
    const stream = streamRef.current;
    const context = inputContextRef.current;
    const previous = providerRef.current;
    if (
      !previous || !stream || !context || pinnedRef.current || stoppingRef.current || recoveringRef.current
      || recoveryCountRef.current >= MAX_RECOVERIES
    ) {
      return false;
    }
    recoveringRef.current = true;
    recoveryCountRef.current += 1;
    candidateSpokeUntilRef.current = null;
    // Words spoken into a dead link are never transcribed. Left in place, the new session's first transcript
    // would consume those bursts and start ~STALL_MS early (a 20-30 s segment, a timeline jump).
    candidateSpansRef.current = [];
    assignedBurstStartRef.current = -1;
    nudgedForRef.current = null;
    setRecoveries(recoveryCountRef.current);
    setRecovering(true);
    const run = runRef.current;
    const alive = () => runRef.current === run && !stoppingRef.current;
    console.warn('Live voice link lost; restoring it on a new provider session.', reason, previous);

    void (async () => {
      let failure: unknown = null;
      try {
        // The new session learns the conversation from the turns saved on the server: save the turn in progress first.
        await queueFlush();
        if (!alive()) return;
        resetProviderTransport();
        // The same provider first (a blip is the common case); after a restore that already failed once, the other one.
        const others = plannedRef.current.filter((candidate) => candidate !== previous);
        const order = recoveryCountRef.current > 1 ? [...others, previous] : [previous, ...others];
        for (const provider of order) {
          let retriedRateLimit = false;
          for (;;) {
            if (!alive()) return;
            try {
              const providerSessionId = provider === 'openai'
                ? await connectOpenAi(stream, alive)
                : await connectGemini(stream, context, alive);
              if (!alive()) return;
              providerRef.current = provider;
              providerSessionIdRef.current = providerSessionId;
              markSessionLive(); // the new session's own clock starts here; the role-play clock carries on
              lastPatientOutputAtRef.current = performance.now(); // a new link gets a full stall window
              setActiveProvider(provider);
              if (provider !== firstProviderRef.current) setFailedOver(true);
              setError(null);
              setPhase('listening');
              setConnection('connected');
              return;
            } catch (caught) {
              if (!alive()) return;
              resetProviderTransport();
              failure = caught;
              if (apiErrorInfo(caught)?.status === 429 && !retriedRateLimit) {
                retriedRateLimit = true;
                await delay(RATE_LIMIT_RETRY_DELAY_MS);
                continue;
              }
              break;
            }
          }
          // A definite server answer (time over, session limit) would be given again by the next provider.
          if (!isProviderFailure(failure)) break;
        }
        resetProviderTransport();
        setError(isClientRejection(failure) ? LIVE_VOICE_ENDED : LIVE_VOICE_INTERRUPTED);
        setConnection('error');
      } finally {
        recoveringRef.current = false;
        setRecovering(false);
      }
    })();
    return true;
  }, [connectGemini, connectOpenAi, markSessionLive, queueFlush, resetProviderTransport]);
  useEffect(() => {
    onLinkLostRef.current = startRecovery;
  }, [startRecovery]);

  // A patient that never answers: the candidate finished a real sentence and the provider stayed silent.
  useEffect(() => {
    if (connection !== 'connected') return undefined;
    const timer = window.setInterval(() => {
      const spoke = candidateSpokeUntilRef.current;
      if (spoke === null || pinnedRef.current || stoppingRef.current || recoveringRef.current) return;
      if (lastPatientOutputAtRef.current >= spoke) return;
      const silentMs = performance.now() - spoke;
      const socket = socketRef.current;
      if (
        silentMs >= GEMINI_NUDGE_MS && silentMs < STALL_MS && providerRef.current === 'gemini' && nudgedForRef.current !== spoke
        && geminiReadyRef.current && socket?.readyState === WebSocket.OPEN
      ) {
        nudgedForRef.current = spoke;
        socket.send(JSON.stringify({ realtimeInput: { audioStreamEnd: true } }));
      }
      if (silentMs < STALL_MS) return;
      candidateSpokeUntilRef.current = null; // one trigger per unanswered sentence
      onLinkLostRef.current('stall');
    }, STALL_CHECK_MS);
    return () => window.clearInterval(timer);
  }, [connection]);

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
    recoveringRef.current = false;
    recoveryCountRef.current = 0;
    setRecovering(false);
    setRecoveries(0);
    speechTrackerRef.current.reset();
    candidateSpansRef.current = [];
    assignedBurstStartRef.current = -1;
    candidateSpokeUntilRef.current = null;
    setConnection('connecting');
    // A retry must not stack a second microphone, peer or socket on what a failed try left behind.
    closeTransport();

    let media: { stream: MediaStream; context: AudioContext } | null;
    try {
      media = await acquireMic(alive);
    } catch (caught) {
      if (!alive()) return false;
      closeTransport();
      // The live variant of the shared microphone copy: the learner speaks to the AI patient and the control reads "Start speaking".
      const micError = describeMicrophoneError(caught, undefined, { live: true });
      setMicPermissionDenied(micError.permissionDenied);
      setError(micError.message);
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
          firstProviderRef.current = provider;
          markSessionLive(); // the role-play clock starts at the first live link; a later start keeps it
          lastPatientOutputAtRef.current = performance.now();
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
    // The server's healthy-candidate list may have changed by now: a retry asks it again.
    plannedRef.current = [];
    setError(learnerMessage(failure));
    setConnection('error');
    return false;
  }, [acquireMic, closeTransport, connectGemini, connectOpenAi, hasTranscriptText, markSessionLive, prepare, resetProviderTransport, sessionId]);

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
    // A card switch resets the refs while this card's provider close is still in flight.
    const departingSegments = segmentsRef.current.map((segment) => ({ ...segment }));
    const departingRecordings = candidateAudioRecordingsRef.current;
    const departingTurns = flushPromiseRef.current;
    const departingEventTypes = [...openAiEventTypesRef.current];
    const departingSocket = socketRef.current;
    if (!provider || !providerSessionId) {
      // Nothing ever went live, so there is nothing to save: leaving must never wait on a failed or cancelled start.
      stoppingRef.current = true;
      closeTransport();
      setConnection((state) => (state === 'connecting' ? 'ready' : state));
      return true;
    }
    stoppingRef.current = true;
    const departingCapture = stopCandidateAudioCapture();
    const departingUploads = [...pendingCandidateAudioRef.current];
    const departingAudioUnavailable = candidateAudioUnavailableRef.current;
    const departingFailedUploads = failedCandidateAudioRef.current;
    setConnection('ending');
    setMicEnabled(false);
    setPhase('idle');
    const releaseSession = () => {
      if (!current()) return;
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
        if (current()) openAiClosedRef.current = null;
      }
      // Gemini has no close handshake and transcribes a sentence ~1.5 s after it was spoken: its link (and its handlers)
      // stays up for a moment, so a candidate still talking at the buzzer keeps the end of the last sentence.
      if (provider === 'gemini' && departingSocket?.readyState === WebSocket.OPEN) await delay(GEMINI_STOP_DRAIN_MS);
      // The per-turn rows are advisory: a turn that cannot be saved must not keep the transcript from being saved.
      await departingTurns.catch(() => undefined);
      if (current()) await flushPendingTurn().catch(() => undefined);
      const audioSaved = current()
        ? await flushCandidateAudio()
        : (await Promise.all([departingCapture, ...departingUploads])).every(Boolean)
          && !departingAudioUnavailable && departingFailedUploads.length === 0;
      // A clip that still cannot be stored (after the queue's retries and one more at the end) must not cost the learner the
      // whole transcript: it is saved with the clips that were stored, and the audio stage reports what it could hear.
      if (!audioSaved) console.warn('Live voice candidate audio is incomplete; saving the transcript with the clips that were stored.');
      // The conversation is over: release the microphone and provider before the (retryable) save.
      if (current()) closeTransport();
      // The server rejects an empty transcript, and there is nothing to grade in one.
      const finalSegments = linkCandidateAudioToTranscript(
        current() ? segmentsRef.current : departingSegments,
        current() ? candidateAudioRecordingsRef.current : departingRecordings,
        CLIP_LINK_TOLERANCE_MS,
      );
      if (current()) segmentsRef.current = finalSegments;
      console.info('Live voice candidate audio summary.', {
        clips: (current() ? candidateAudioRecordingsRef.current : departingRecordings).length,
        candidateSegments: finalSegments.filter((segment) => segment.speaker === 'candidate').length,
        linkedCandidateSegments: finalSegments.filter((segment) => segment.speaker === 'candidate' && segment.sourceRecordingId).length,
        audioSaved,
        openAiEventTypes: departingEventTypes,
      });
      if (finalSegments.length > 0) {
        await persistLiveVoiceTranscript(sessionId, {
          provider,
          providerSessionId,
          segments: inOrderOfStart(withoutPatientDisclaimers(finalSegments)),
        });
      }
      // Saved: the refresh-safe copy has done its job (a failed save keeps it, so a reload can still bring it back).
      clearTranscriptCheckpoint(sessionId);
      releaseSession();
      if (current()) {
        if (finalSegments.length > 0) setEnded(true);
        // A "try again before leaving" banner left by a clip that failed mid-role-play is stale now: the transcript is saved.
        setError(null);
        setConnection('ended');
      }
      return true;
    } catch (caught) {
      if (isClientRejection(caught)) {
        // The server will never take this transcript (window closed, session no longer active, invalid):
        // a retry cannot help and must not strand the learner. The card is over for good, so is its stored copy.
        console.warn('Live voice transcript rejected by the server.', failureCode(caught));
        clearTranscriptCheckpoint(sessionId);
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
  }, [closeTransport, flushCandidateAudio, flushPendingTurn, sessionId, stopCandidateAudioCapture]);

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
    setRecovering(false);
    setRecoveries(0);
    recoveringRef.current = false;
    recoveryCountRef.current = 0;
    pinnedRef.current = false;
    speechTrackerRef.current.reset();
    candidateSpansRef.current = [];
    activeCandidateAudioRef.current = null;
    pendingCandidateAudioRef.current = [];
    failedCandidateAudioRef.current = [];
    window.clearTimeout(patientAudioTailTimerRef.current);
    patientAudioTailTimerRef.current = undefined;
    window.clearTimeout(patientOutputIdleTimerRef.current);
    patientOutputIdleTimerRef.current = undefined;
    window.clearTimeout(clipHangoverTimerRef.current);
    clipHangoverTimerRef.current = undefined;
    openAiEventTypesRef.current = new Set();
    // A departing card's uploads keep running on their own; the next card's must not queue behind them.
    clipUploadChainRef.current = Promise.resolve();
    patientAudioPlaybackRef.current = false;
    candidateAudioUnavailableRef.current = false;
    candidateAudioSuppressedRef.current = false;
    assignedBurstStartRef.current = -1;
    candidateSpokeUntilRef.current = null;
    providerRef.current = null;
    providerSessionIdRef.current = null;
    plannedRef.current = [];
    pendingCandidateRef.current = '';
    pendingPatientRef.current = '';
    pendingStartedAtRef.current = null;
    // A reload mid-card mounts this hook again for the same Speaking session: what was said before it comes back from
    // the refresh-safe copy, the turn numbers carry on, and the role-play clock is rebuilt from the stored origin so the
    // new provider session lands after everything that was saved (see markSessionLive). Any other session starts empty.
    const restored = sessionId ? readTranscriptCheckpoint(sessionId) : null;
    segmentsRef.current = restored?.segments ?? [];
    candidateAudioRecordingsRef.current = restored?.audioRecordings ?? [];
    turnIndexRef.current = restored?.turnIndex ?? 0;
    originEpochRef.current = restored?.originEpochMs ?? null;
    originRef.current = restored ? performance.now() - Math.max(0, Date.now() - restored.originEpochMs) : null;
    sessionOffsetMsRef.current = 0;
    heardRef.current = false;
    runTagRef.current = Math.random().toString(36).slice(2, 8);
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
    recovering,
    recoveries,
    audioRef,
    prepare,
    start,
    stop,
  };
}
