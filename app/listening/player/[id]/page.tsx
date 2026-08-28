'use client';

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, ArrowLeft, ArrowRight, CheckCircle2, Loader2, Volume2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { AppShell } from '@/components/layout/app-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';
import { getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import {
  getListeningSession,
  heartbeatListeningAttempt,
  recordListeningIntegrityEvent,
  startListeningAttempt,
  type ListeningAttemptDto,
  type ListeningIntegrityEventType,
  type ListeningSessionQuestionDto,
  type ListeningSessionDto,
} from '@/lib/listening-api';
import {
  LISTENING_REVIEW_SECONDS,
  LISTENING_SECTION_LABEL,
  LISTENING_SECTION_SEQUENCE,
  groupQuestionsBySection,
  type ListeningSectionCode,
} from '@/lib/listening-sections';
import {
  listeningPositionForState,
  listeningStateForPosition,
  listeningWindowSeconds,
  type ListeningFsmState,
} from '@/lib/listening/transitions';
import { normalizeExamPartCode } from '@/lib/listening-exam-sections';
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';
import { InsufficientCreditsModal, isInsufficientCreditsError, readInsufficientCreditsMessage } from '@/components/domain/InsufficientCreditsModal';
import { showCreditFeedback } from '@/lib/credit-feedback';
import { BCQuestionRenderer } from '@/components/domain/listening/BCQuestionRenderer';
import { PartARenderer } from '@/components/domain/listening/PartARenderer';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';
import { ZoomControls } from '@/components/domain/listening/ZoomControls';
import { ListeningIntroCard } from '@/components/domain/listening/player/ListeningIntroCard';
import { ListeningAudioTransport } from '@/components/domain/listening/player/ListeningAudioTransport';
import { ListeningSectionStepper } from '@/components/domain/listening/player/ListeningSectionStepper';
import { ListeningPreviewBanner, ListeningReviewBanner } from '@/components/domain/listening/player/ListeningPhaseBanner';
import { ApiError, completeMockSection, fetchAuthorizedObjectUrl } from '@/lib/api';
import {
  enableAutoSync,
  markAttemptConflict,
  markAttemptSynced,
  queueOfflineAttempt,
  syncPendingAttempts,
  type OfflineAttempt,
} from '@/lib/mobile/offline-sync';
import { reconcileOfflineAnswer, type OfflineAnswerPayload } from '@/lib/mobile/offline-answer-reconciliation';
import { resolveBlockedSeekTarget, shouldResumeAfterBlockedPause } from '@/lib/listening/audio-integrity';
import { correctedNowMs, readServerClockOffsetMs } from '@/lib/server-clock';
import { listeningV2Api, type AdvanceResult, type ListeningV2SessionState } from '@/lib/listening/v2-api';
import { buildTechReadinessProbe } from '@/lib/listening/tech-readiness-probe';
import { submitAudioCheck } from '@/lib/listening-pathway-api';
import { presentationModeFromSession } from '@/lib/listening/modes';
import { ListeningPlayerSkinShell } from '@/components/domain/listening/player/skins/ListeningPlayerSkinShell';
import { useListeningAnnotations, type ListeningQuestionAnnotation } from '@/hooks/use-listening-annotations';

const FIRST_STRICT_STATE: ListeningFsmState = 'a1_preview';

function isListeningAudioCheckError(err: unknown): boolean {
  if (typeof err !== 'object' || err === null) return false;
  const e = err as { code?: unknown; message?: unknown; detail?: { code?: unknown; message?: unknown }; reason?: unknown };
  const code = typeof e.code === 'string' ? e.code : typeof e.detail?.code === 'string' ? e.detail.code : typeof e.reason === 'string' ? e.reason : '';
  if (code === 'listening_audio_check_required' || code === 'audio-check-required') return true;
  const msg = typeof e.message === 'string' ? e.message : typeof e.detail?.message === 'string' ? e.detail.message : '';
  return msg.includes('Pass the Listening sound check');
}

type ListeningPlayerMode = 'practice' | 'exam' | 'home' | 'diagnostic';

type PendingListeningAnswer = {
  attemptId: string;
  value: string;
  baseValue: string | null;
  eventLogged: boolean;
};

function formatTime(seconds: number) {
  if (!seconds || Number.isNaN(seconds)) return '00:00';
  const minutes = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
}

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function isNetworkInterruption(error: unknown): boolean {
  return (error instanceof ApiError && error.status === 0)
    || (typeof navigator !== 'undefined' && !navigator.onLine);
}

function formatMilliseconds(value: number | null | undefined) {
  if (value == null) return null;
  const seconds = Math.floor(value / 1000);
  return formatTime(seconds);
}

function formatQuestionNumberList(numbers: number[]) {
  return numbers.map((number) => `Q${number}`).join(', ');
}

function listeningQuestionPartLabel(
  question: Pick<ListeningSessionQuestionDto, 'partCode' | 'number'>,
  fallback: string,
): string {
  const partCode = normalizeExamPartCode(question.partCode, question.number);
  if (partCode === 'B') return 'Part B — Workplace extracts';
  if (partCode === 'C1') return 'Part C — Extract 1';
  if (partCode === 'C2') return 'Part C — Extract 2';
  return fallback;
}

function isPartialSourceGapListeningSession(session: { questions: Array<{ number: number; partCode: string }> }) {
  if (session.questions.length === 0) return false;
  const maxNumber = Math.max(...session.questions.map((question) => question.number));
  const hasC2 = session.questions.some((question) => question.partCode.toUpperCase() === 'C2');
  return maxNumber === 36 && !hasC2;
}

function derivePlayerMode({
  rawMode,
  mockMode,
  strictness,
  deliveryMode,
  strictTimer,
}: {
  rawMode: string;
  mockMode: string;
  strictness: string;
  deliveryMode: string;
  strictTimer: string;
}): ListeningPlayerMode {
  if (rawMode === 'exam' || rawMode === 'home' || rawMode === 'diagnostic') return rawMode;
  if (rawMode === 'paper') return 'exam';
  const normalizedDelivery = deliveryMode.trim().toLowerCase();
  if (normalizedDelivery === 'paper') return 'exam';
  const normalizedMockMode = mockMode.trim().toLowerCase();
  const normalizedStrictness = strictness.trim().toLowerCase();
  const strictLaunch = normalizedMockMode === 'exam'
    || normalizedStrictness === 'exam'
    || normalizedStrictness === 'final_readiness'
    || strictTimer.trim().toLowerCase() === 'true';
  if (strictLaunch && normalizedDelivery === 'oet_home') return 'home';
  if (strictLaunch) return 'exam';
  return 'practice';
}

function advanceRejectionMessage(result: AdvanceResult) {
  return result.rejectionDetail ?? result.rejectionReason ?? 'The server rejected the Listening start transition.';
}

// Error thrown by a rejected strict transition. Carries the server's machine
// rejection reason (e.g. `audio-check-required`) alongside the human message so
// the caller can route specific rejections — like the sound-check gate — to a
// dedicated recovery path instead of a generic error banner.
class StrictAdvanceError extends Error {
  readonly reason: string | null;
  constructor(result: AdvanceResult) {
    super(advanceRejectionMessage(result));
    this.name = 'StrictAdvanceError';
    this.reason = result.rejectionReason;
  }
}

async function advanceStrictTransition(attemptId: string, toState: ListeningFsmState): Promise<ListeningV2SessionState> {
  const first = await listeningV2Api.advance(attemptId, toState, null);
  if (first.outcome === 'applied') return first.state ?? listeningV2Api.getState(attemptId);
  if (first.outcome === 'confirm-required' && first.confirmToken) {
    const confirmed = await listeningV2Api.advance(attemptId, toState, first.confirmToken);
    if (confirmed.outcome === 'applied') return confirmed.state ?? listeningV2Api.getState(attemptId);
    throw new StrictAdvanceError(confirmed);
  }
  throw new StrictAdvanceError(first);
}

async function advanceStrictStart(attemptId: string) {
  await advanceStrictTransition(attemptId, FIRST_STRICT_STATE);
}

function PlayerContent() {
  const params = useParams<{ id?: string | string[] }>();
  const router = useRouter();
  const searchParams = useSearchParams();
  const id = firstParam(params?.id);
  const rawMode = searchParams?.get('mode') ?? '';
  const attemptIdFromRoute = searchParams?.get('attemptId');
  const pathwayStage = searchParams?.get('pathwayStage') ?? null;
  const drillId = searchParams?.get('drill');
  const partFocus = searchParams?.get('part')?.toLowerCase();
  const focusParam = searchParams?.get('focus')
    ?? (partFocus === 'a' ? 'part-a' : partFocus === 'b' ? 'part-b' : partFocus === 'c' ? 'part-c' : null);
  // Mocks V2 — when this player is launched as a section of a mock attempt,
  // BuildLaunchRoute writes mockAttemptId/mockSectionId/mockMode/strictness
  // onto the URL. After grading we POST to completeMockSection so the mock
  // report no longer shows "Pending". Standalone practice keeps these null.
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? null;
  const mockSectionId = searchParams?.get('mockSectionId') ?? null;
  const mockMode = searchParams?.get('mockMode') ?? '';
  const mockStrictness = searchParams?.get('strictness') ?? '';
  const mockDeliveryMode = searchParams?.get('deliveryMode') ?? '';
  const mockStrictTimer = searchParams?.get('strictTimer') ?? '';
  const mode = derivePlayerMode({
    rawMode,
    mockMode,
    strictness: mockStrictness,
    deliveryMode: mockDeliveryMode,
    strictTimer: mockStrictTimer,
  });

  const rootRef = useRef<HTMLDivElement>(null);
  const audioRef = useRef<HTMLAudioElement>(null);
  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  // Keep the latest debounced value available for a page-lifecycle flush.
  // The server remains the durable source of truth; this is only an in-flight
  // queue, not a second answer store.
  const pendingAnswersRef = useRef<Record<string, PendingListeningAnswer>>({});
  const serverAnswersRef = useRef<Record<string, string | null>>({});
  const answerBaseValuesRef = useRef<Record<string, string | null>>({});
  const serverClockOffsetMs = useRef(0);
  // C8e — last-known forward-only audio time. onTimeUpdate keeps this in sync;
  // onSeeking snaps backwards seeks back to this value in exam mode.
  const lastKnownTimeRef = useRef<number>(0);
  const programmaticSeekTargetRef = useRef<number | null>(null);
  const programmaticSeekResetTimerRef = useRef<number | null>(null);
  const allowedPauseRef = useRef<boolean>(false);
  // C8e — set true once the active extract's audioEndMs has been crossed in
  // exam mode. Subsequent play() calls (e.g. user clicking the OS play key)
  // are immediately re-paused.
  const hasReachedEndRef = useRef<boolean>(false);
  // Guards the auto-advance fired from the audio `ended` event so it can only
  // run once per section (reset when the section changes). Audio is
  // non-pausable in every mode, so `ended` is the sole advance trigger.
  const autoAdvanceInFlightRef = useRef<boolean>(false);
  // C8f — sentinel that flips true once the preview countdown for the
  // active section has been initialised. Without this, the preview→audio
  // transition effect would fire on the first mount (when the initial
  // `previewSecondsRemaining` state is still 0) and skip the reading
  // window entirely.
  const previewArmedRef = useRef<boolean>(false);
  // C8b — guard so handleSubmit only fires once when the attempt timer hits 0.
  const autoSubmittedRef = useRef<boolean>(false);
  // Synchronous submit-in-flight gate. The state-driven `isSubmitting` only
  // updates after React commits, which leaves a window where a 15s heartbeat
  // tick can race the submit POST and 409 because the backend has already
  // marked the attempt Submitted. Setting this ref first thing in
  // handleSubmit closes that race deterministically.
  const isSubmittingRef = useRef<boolean>(false);
  // C8g — flips true whenever the audio element actually ends up paused
  // (i.e. a pause that is NOT immediately auto-resumed by the blocked-pause
  // protector). The next `play` event reads & clears this and triggers a
  // server-side `audio-resume` call so the backend grace window is honoured.
  const wasPausedRef = useRef<boolean>(false);
  // §17.11 — true once an `audio_started` event has been logged for the
  // current audio run, so a pause→resume cycle does not re-emit it. Cleared
  // by `onEnded` and whenever a new section's audio arms (preview/section
  // effects below), so each section logs one audio_started / audio_ended pair.
  const audioStartedLoggedRef = useRef<boolean>(false);
  // Browser media events can fire repeatedly during one network interruption.
  // Keep the buffering lifecycle edge-triggered so the audit stream records
  // one start/end pair per interruption rather than a noisy event per tick.
  const audioBufferingActiveRef = useRef<boolean>(false);
  const audioResumeInFlightRef = useRef<boolean>(false);
  const applyStrictServerStateRef = useRef<((state: ListeningV2SessionState) => void) | null>(null);
  const strictAdvanceTargetRef = useRef<ListeningFsmState | null>(null);
  const autoPlayNextPartBQuestionRef = useRef(false);
  // Tracks the attempt id whose strict-resume FSM state has already been
  // hydrated from the server. The hydration effect below also re-runs whenever
  // `applyStrictServerState` changes identity (it depends on `sectionsInPaper`,
  // which in turn depends on `strictServerState.state`). Applying a resume thus
  // mutates a dependency of the effect, so without this guard the effect would
  // re-fetch `getState` after the resume already committed — clobbering the
  // hydrated phase (e.g. snapping an `a2_audio` resume back to `a1_preview`).
  const hydratedAttemptIdRef = useRef<string | null>(null);
  const [session, setSession] = useState<ListeningSessionDto | null>(null);
  const [attempt, setAttempt] = useState<ListeningAttemptDto | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [loadingTask, setLoadingTask] = useState(true);

  const syncServerClock = useCallback((serverNow: string | null | undefined) => {
    serverClockOffsetMs.current = readServerClockOffsetMs(serverNow);
  }, []);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [contentLockedMessage, setContentLockedMessage] = useState<string | null>(null);
  const [insufficientCreditsMessage, setInsufficientCreditsMessage] = useState<string | null>(null);
  const [audioError, setAudioError] = useState<string | null>(null);
  const [audioValidityHeld, setAudioValidityHeld] = useState(false);
  const [integrityWarning, setIntegrityWarning] = useState<string | null>(null);
  const [audioResumeWarning, setAudioResumeWarning] = useState<string | null>(null);
  const [audioState, setAudioState] = useState<'idle' | 'buffering' | 'ready' | 'error'>('idle');
  const [audioRetryKey, setAudioRetryKey] = useState(0);
  // The <audio> element loads its source via a plain browser GET, which cannot
  // carry the bearer token /v1/media/{id}/content requires (mirrors the PDF
  // viewer's blob-fetch). We resolve the current section's URL to an authorized
  // blob URL here and feed THAT to the element. Null while resolving / absent.
  const [resolvedAudioSrc, setResolvedAudioSrc] = useState<string | null>(null);
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'offline-saved' | 'conflict' | 'error'>('idle');
  const [isPlaying, setIsPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [duration, setDuration] = useState(0);
  const [hasStarted, setHasStarted] = useState(false);
  const [showAutoplayModal, setShowAutoplayModal] = useState(false);
  // Autoplay is armed once per attempt/section/source. The authorized media
  // URL resolves asynchronously, so the section-transition effect can run
  // before the <audio> element has a source; this key lets the source-ready
  // effect retry exactly once without restarting playback on normal rerenders.
  const autoPlayAttemptedKeyRef = useRef<string | null>(null);
  const [activeQuestionIndexBySection, setActiveQuestionIndexBySection] = useState<Record<string, number>>({});
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [isStarting, setIsStarting] = useState(false);
  const [isAdvancingPhase, setIsAdvancingPhase] = useState(false);
  const [showSubmitConfirm, setShowSubmitConfirm] = useState(false);
  const [techReadiness, setTechReadiness] = useState<{ audioOk: boolean; durationMs: number } | null>(null);
  const [startError, setStartError] = useState<string | null>(null);
  const [currentSectionIndex, setCurrentSectionIndex] = useState(0);
  // C8f — pre-audio reading window precedes the audio of every section so
  // candidates can read the questions before audio playback starts. Initial
  // value is 'audio' (no-op until the currentSection effect drops us into
  // 'preview' for the active section); this avoids a one-frame race where
  // the transition effect would see phase='preview' with sec=0 before the
  // section setup has run.
  const [phase, setPhase] = useState<'preview' | 'audio' | 'review'>('audio');
  const [previewSecondsRemaining, setPreviewSecondsRemaining] = useState(0);
  const [reviewSecondsRemaining, setReviewSecondsRemaining] = useState(0);
  const [questionZoomPercent, setQuestionZoomPercent] = useState(100);
  const [strictServerState, setStrictServerState] = useState<ListeningV2SessionState | null>(null);
  const strictServerStateRef = useRef<ListeningV2SessionState | null>(null);
  const [showNextConfirm, setShowNextConfirm] = useState(false);
  const [currentPartBQuestionIndex, setCurrentPartBQuestionIndex] = useState(0);
  // C8d — whole-attempt policy-defined countdown driven by attempt.expiresAt.
  const [attemptSecondsRemaining, setAttemptSecondsRemaining] = useState<number | null>(null);
  // C8c — tracks which extracts have been listened-to-completion so the
  // section panel can render a checkmark next to each.
  const [completedExtractIds, setCompletedExtractIds] = useState<Set<string>>(() => new Set());
  // Per-section audio: sections whose own audio file has played to its natural
  // end. Drives the exam-mode "finish before advancing" gate when each section
  // plays its own file (cue windows don't apply — the whole file IS the section).
  const [endedSections, setEndedSections] = useState<Set<string>>(() => new Set());
  // R08 — durable highlight + strikethrough (rule-out) annotations for Part B/C
  // questions. Debounced-autosaved to the attempt via useListeningAnnotations so
  // marks survive refresh, resume, and section navigation. The §17.11 attempt-
  // event stream still emits `highlight` / `strikethrough` on each toggle (see
  // handleAnnotationChange). `activeAttemptId` is the live attempt (resumed via
  // the route param or created by ensureAttempt).
  const activeAttemptId = attempt?.attemptId ?? attemptIdFromRoute ?? null;
  // R08 annotations (highlights + rule-out) persist only on RELATIONAL listening
  // attempts (`lat-` ids). Legacy papers (e.g. seeded ContentItem lt-001) yield a
  // generic Attempt (`la-` id) with no row in db.ListeningAttempts, so the V2
  // annotations GET/PUT 404. Gate the hook to relational attempts so legacy papers
  // don't fire a doomed request (which surfaces as a severe client 404 in E2E).
  const annotationsSupported = (activeAttemptId ?? '').startsWith('lat-');
  const annotations = useListeningAnnotations({
    attemptId: activeAttemptId,
    initialAnnotationsJson: null,
    disabled: !annotationsSupported,
  });
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const sectionMotion = getSurfaceMotion('section', reducedMotion);
  const listMotion = getSurfaceMotion('list', reducedMotion);

  const strictReadinessRequired = mode === 'exam'
    || mode === 'home'
    || session?.modePolicy.mode === 'exam'
    || session?.modePolicy.mode === 'home';

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    const timers = saveTimers.current;

    Promise.resolve()
      .then(() => {
        if (cancelled) return null;
        setLoadingTask(true);
        setLoadError(null);
        return getListeningSession(id, { mode, attemptId: attemptIdFromRoute, ...(pathwayStage ? { pathwayStage } : {}) });
      })
      .then((data) => {
        if (cancelled || !data) return;
        setSession(data);
        syncServerClock(data.serverNow ?? data.attempt?.serverNow);
        setAttempt(data.attempt);
        if (data.attempt?.requiresAdminReview) {
          setAudioValidityHeld(true);
          setIntegrityWarning('This Listening attempt is on hold because audio playback failed and has been flagged for administrator review. Do not replay the scored audio.');
        }
        const restoredAnswers = Object.fromEntries(
          Object.entries(data.attempt?.answers ?? {}).map(([key, value]) => [key, value ?? '']),
        );
        setAnswers(restoredAnswers);
        serverAnswersRef.current = Object.fromEntries(
          Object.entries(data.attempt?.answers ?? {}).map(([key, value]) => [key, value ?? null]),
        );
        answerBaseValuesRef.current = {};
        pendingAnswersRef.current = {};
        analytics.track('task_started', { subtest: 'listening', taskId: id, attemptId: data.attempt?.attemptId, mode });
      })
      .catch((err) => {
        if (cancelled) return;
        // C2 — surface the same content_locked upsell when the *session*
        // endpoint returns HTTP 402, not just when starting the attempt.
        if (isContentLockedError(err)) {
          setContentLockedMessage(
            readContentLockedMessage(err, 'This listening paper requires an active subscription.'),
          );
          return;
        }
        // C2 — same 402 upsell pattern, but for a numeric test-credit
        // shortfall (ListeningTestsRemaining) rather than a subscription-tier
        // lock. Checked as a distinct branch so it never gets masked by the
        // content-locked check above or the generic fallback below.
        if (isInsufficientCreditsError(err)) {
          setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
          return;
        }
        setLoadError(err instanceof Error ? err.message : 'Could not load this Listening task.');
      })
      .finally(() => {
        if (!cancelled) setLoadingTask(false);
      });

    return () => {
      cancelled = true;
      Object.values(timers).forEach(clearTimeout);
    };
  }, [attemptIdFromRoute, id, mode, pathwayStage, syncServerClock]);

  // R08 — hydrate the learner's saved rule-out / highlight marks once the
  // attempt id is known (resume or fresh start). The V1 session DTO doesn't
  // carry the annotations payload, so pull it via the dedicated GET. reload()
  // no-ops when there's no attempt id.
  useEffect(() => {
    if (!attempt?.requiresAdminReview) return;
    setAudioValidityHeld(true);
    setIntegrityWarning('This Listening attempt is on hold because audio playback failed and has been flagged for administrator review. Do not replay the scored audio.');
  }, [attempt?.requiresAdminReview]);

  useEffect(() => {
    if (!activeAttemptId || !annotationsSupported) return;
    void annotations.reload();
    // reload() is keyed on (attemptId, disabled); intentionally re-runs only
    // when the active attempt id changes.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeAttemptId, annotationsSupported]);

  useEffect(() => {
    if (!attempt?.attemptId || !hasStarted || isSubmitting) return;
    const interval = window.setInterval(() => {
      // Skip if a submit is already in-flight — the backend will 409 a
      // heartbeat against an attempt it has already marked Submitted, and
      // those 409s leak into diagnostics and fail the smoke spec.
      if (isSubmittingRef.current) return;
      void heartbeatListeningAttempt(attempt.attemptId, Math.round(progress), 'web').catch(() => undefined);
    }, 15000);
    return () => window.clearInterval(interval);
  }, [attempt?.attemptId, hasStarted, isSubmitting, progress]);

  const logIntegrityEvent = useCallback((eventType: ListeningIntegrityEventType, details?: string) => {
    if (!attempt?.attemptId || !session?.modePolicy.technicalGuidanceTelemetryEnabled) return;
    void recordListeningIntegrityEvent(attempt.attemptId, eventType, details).catch(() => undefined);
  }, [attempt?.attemptId, session?.modePolicy.technicalGuidanceTelemetryEnabled]);

  // §17.11 — attempt-event stream. Unlike `logIntegrityEvent` (gated to
  // OET@Home integrity-lock attempts), these audio-lifecycle / reading-time /
  // answer / annotation events are recorded for ANY graded attempt that has an
  // attempt id. The structured `cuePointMs` (current audio position) and
  // `questionId` ride along inside the string `details` payload as compact
  // JSON; the server parses them back out into the AuditEvent details.
  const logAttemptEvent = useCallback((
    eventType: ListeningIntegrityEventType,
    payload?: { cuePointMs?: number; questionId?: string; [key: string]: unknown },
  ) => {
    if (!attempt?.attemptId) return;
    const cuePointMs = payload?.cuePointMs
      ?? (audioRef.current ? Math.round(audioRef.current.currentTime * 1000) : undefined);
    const detail: Record<string, unknown> = { ...payload };
    if (cuePointMs != null && Number.isFinite(cuePointMs)) detail.cuePointMs = cuePointMs;
    const details = Object.keys(detail).length > 0 ? JSON.stringify(detail) : undefined;
    // Promise.resolve() wrapper keeps this fire-and-forget even if a caller's
    // transport returns a non-thenable; the event stream must never throw into
    // a render / effect path.
    void Promise.resolve(recordListeningIntegrityEvent(attempt.attemptId, eventType, details)).catch(() => undefined);
  }, [attempt?.attemptId]);

  const flagAudioFailure = useCallback((message: string) => {
    const hasActiveAttempt = Boolean(attempt?.attemptId);
    audioBufferingActiveRef.current = false;
    allowedPauseRef.current = true;
    audioResumeInFlightRef.current = false;
    setIsPlaying(false);
    setAudioState('error');
    if (hasActiveAttempt) setAudioValidityHeld(true);
    setAudioError(message);
    if (hasActiveAttempt) {
      setIntegrityWarning('Audio playback failed. This attempt has been halted and flagged for administrator review; do not replay the scored audio.');
      Object.values(saveTimers.current).forEach(clearTimeout);
      saveTimers.current = {};
      pendingAnswersRef.current = {};
    }
    try {
      audioRef.current?.pause();
    } catch {
      // The media element may already have been detached after the failure.
    }
    logAttemptEvent('audio_error', { playbackValidity: 'admin_review_required' });
  }, [attempt?.attemptId, logAttemptEvent]);

  const handlePlaybackFailure = useCallback((error: unknown) => {
    const message = error instanceof Error ? error.message : String(error);
    if (
      (error instanceof DOMException && (error.name === 'AbortError' || error.name === 'NotAllowedError'))
      || message.includes('play() request was interrupted')
      || message.includes('user didn')
      || message.includes('gesture')
    ) {
      if (error instanceof DOMException && error.name === 'NotAllowedError') {
        setShowAutoplayModal(true);
      }
      return;
    }
    flagAudioFailure('Audio could not start. This attempt has been halted and flagged for administrator review; do not replay the scored audio.');
  }, [flagAudioFailure]);

  const savePendingAnswer = useCallback((questionId: string, pending: PendingListeningAnswer, keepalive = false) => {
    setSaveState('saving');
    if (!pending.eventLogged) {
      pending.eventLogged = true;
      logAttemptEvent('answer_changed', { questionId });
    }
    void listeningV2Api.saveAnswer(pending.attemptId, questionId, pending.value, { keepalive })
      .then(() => {
        const current = pendingAnswersRef.current[questionId];
        const matchesLatest =
          current?.attemptId === pending.attemptId
          && current?.value === pending.value;
        if (matchesLatest) {
          delete pendingAnswersRef.current[questionId];
          serverAnswersRef.current[questionId] = pending.value;
          delete answerBaseValuesRef.current[questionId];
          setSaveState('saved');
        } else if (current) {
          // A newer keystroke is already queued; do not show the older
          // request as the final durable state.
          setSaveState('saving');
        }
      })
      .catch((error) => {
        const current = pendingAnswersRef.current[questionId];
        if (current?.attemptId !== pending.attemptId || current.value !== pending.value) return;
        if (isNetworkInterruption(error)) {
          void queueOfflineAttempt(
            'listening-answer',
            pending.attemptId,
            {
              questionId,
              value: pending.value,
              baseValue: pending.baseValue,
            } satisfies OfflineAnswerPayload,
            { id: `listening-answer:${encodeURIComponent(pending.attemptId)}:${encodeURIComponent(questionId)}` },
          )
            .then(() => {
              const latest = pendingAnswersRef.current[questionId];
              if (latest?.attemptId === pending.attemptId && latest.value === pending.value) {
                delete pendingAnswersRef.current[questionId];
                setSaveState('offline-saved');
              } else if (latest) {
                setSaveState('saving');
              }
            })
            .catch(() => setSaveState('error'));
          return;
        }
        setSaveState('error');
      });
  }, [logAttemptEvent]);

  const flushPendingAnswers = useCallback((keepalive = false) => {
    Object.entries(pendingAnswersRef.current).forEach(([questionId, pending]) => {
      clearTimeout(saveTimers.current[questionId]);
      savePendingAnswer(questionId, pending, keepalive);
    });
  }, [savePendingAnswer]);

  // A tab hide/pagehide can happen before the 500ms debounce expires. Flush
  // the latest values through the browser's keepalive path so refresh,
  // navigation, and transient visibility changes cannot erase typed answers.
  useEffect(() => {
    if (!attempt?.attemptId || !hasStarted) return;
    const flush = () => flushPendingAnswers(true);
    const onVisibilityChange = () => {
      if (document.visibilityState === 'hidden') flush();
    };
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('pagehide', flush);
    return () => {
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('pagehide', flush);
    };
  }, [attempt?.attemptId, flushPendingAnswers, hasStarted]);

  const reconcilePendingOfflineAnswer = useCallback(async (queued: OfflineAttempt): Promise<boolean> => {
    if (!attempt || !id || queued.subtest !== 'listening-answer' || queued.contentId !== attempt.attemptId) return false;
    const payload = queued.payload as Partial<OfflineAnswerPayload>;
    if (typeof payload.questionId !== 'string' || typeof payload.value !== 'string') return false;
    const questionId = payload.questionId;
    const value = payload.value;

    const latest = await getListeningSession(id, { mode, attemptId: attempt.attemptId });
    if (latest.attempt?.requiresAdminReview) {
      await markAttemptConflict(queued.id);
      setAudioValidityHeld(true);
      setIntegrityWarning('This Listening attempt is on hold because audio playback failed and has been flagged for administrator review. Do not replay the scored audio.');
      return true;
    }

    const serverValue = latest.attempt?.answers?.[questionId] ?? null;
    const decision = reconcileOfflineAnswer(serverValue, {
      questionId,
      value,
      baseValue: typeof payload.baseValue === 'string' ? payload.baseValue : null,
    });

    if (decision === 'already-synced') {
      await markAttemptSynced(queued.id);
      serverAnswersRef.current[questionId] = value;
      delete answerBaseValuesRef.current[questionId];
      setSaveState('saved');
      return true;
    }

    if (decision === 'conflict') {
      await markAttemptConflict(queued.id);
      serverAnswersRef.current[questionId] = serverValue;
      delete answerBaseValuesRef.current[questionId];
      setAnswers((current) => current[questionId] === value
        ? { ...current, [questionId]: serverValue ?? '' }
        : current);
      setSaveState('conflict');
      return true;
    }

    try {
      await listeningV2Api.saveAnswer(attempt.attemptId, questionId, value);
      await markAttemptSynced(queued.id);
      serverAnswersRef.current[questionId] = value;
      delete answerBaseValuesRef.current[questionId];
      setSaveState('saved');
      return true;
    } catch (error) {
      if (isNetworkInterruption(error)) return false;
      throw error;
    }
  }, [attempt, id, mode]);

  useEffect(() => {
    if (!attempt) return;
    void syncPendingAttempts(reconcilePendingOfflineAnswer).catch(() => undefined);
    return enableAutoSync(reconcilePendingOfflineAnswer);
  }, [attempt, reconcilePendingOfflineAnswer]);

  const pauseAudio = useCallback(() => {
    const audio = audioRef.current;
    if (!audio) return;
    if (audio.paused) {
      allowedPauseRef.current = false;
      return;
    }
    allowedPauseRef.current = true;
    audio.pause();
  }, []);

  const seekAudioTo = useCallback((seconds: number) => {
    const audio = audioRef.current;
    if (!audio) return;
    const target = Math.max(0, seconds);
    programmaticSeekTargetRef.current = target;
    if (programmaticSeekResetTimerRef.current) window.clearTimeout(programmaticSeekResetTimerRef.current);
    programmaticSeekResetTimerRef.current = window.setTimeout(() => {
      if (programmaticSeekTargetRef.current === target) programmaticSeekTargetRef.current = null;
      programmaticSeekResetTimerRef.current = null;
    }, 1000);
    try {
      audio.currentTime = target;
      setProgress(target);
      lastKnownTimeRef.current = target;
    } catch {
      programmaticSeekTargetRef.current = null;
    }
  }, []);

  useEffect(() => () => {
    if (programmaticSeekResetTimerRef.current) window.clearTimeout(programmaticSeekResetTimerRef.current);
  }, []);

  // C8g — server-authoritative audio-resume protocol. When the audio element
  // resumes after any pause in strict mode (exam / home / @home), the backend
  // must validate the cue point against its 5s grace window. Inside the grace
  // window the call is a no-op; outside, the server force-advances to the
  // next *_review state and the client must align playhead + surface a
  // user-visible alert.
  const handleAudioResume = useCallback(() => {
    const audio = audioRef.current;
    const attemptId = attempt?.attemptId;
    if (!audio || !attemptId) return;
    if (!strictReadinessRequired) return;
    if (audio.currentTime === 0) return;
    if (!wasPausedRef.current) return;
    if (audioResumeInFlightRef.current) return;
    wasPausedRef.current = false;
    audioResumeInFlightRef.current = true;
    const cuePointMs = Math.round(audio.currentTime * 1000);
    setAudioResumeWarning(null);
    allowedPauseRef.current = true;
    setIsPlaying(false);
    try {
      audio.pause();
    } catch {
      // ignore — audio element may already be paused
    }
    listeningV2Api
      .audioResume(attemptId, cuePointMs)
      .then((result) => {
        if (!result) return;
        if (result.resume) {
          audioResumeInFlightRef.current = false;
          setIsPlaying(true);
          const playResult = audio.play();
          if (playResult && typeof playResult.catch === 'function') {
            playResult.catch(handlePlaybackFailure);
          }
          return;
        }
        // Outside grace OR server already past audio — force-align playhead
        // and surface a small inline alert. The server has already moved on
        // and locked the previous audio state on its side, so additionally
        // pause the local element to prevent further out-of-window audio.
        void listeningV2Api.getState(attemptId)
          .then((state) => applyStrictServerStateRef.current?.(state))
          .catch(() => undefined);
        const targetSeconds = Math.max(0, (result.resumeAtMs ?? 0) / 1000);
        if (targetSeconds > 0) seekAudioTo(targetSeconds);
        setAudioResumeWarning('Your section moved on while audio was paused.');
        allowedPauseRef.current = true;
        try {
          audio.pause();
        } catch {
          // ignore — audio element may already be paused
        }
        wasPausedRef.current = true;
      })
      .catch(() => {
        setAudioResumeWarning('Audio resume could not be verified with the server. Playback has been paused; try resuming again.');
        allowedPauseRef.current = true;
        setIsPlaying(false);
        try {
          audio.pause();
        } catch {
          // ignore — audio element may already be paused
        }
        wasPausedRef.current = true;
      })
      .finally(() => {
        audioResumeInFlightRef.current = false;
      });
  }, [attempt?.attemptId, seekAudioTo, strictReadinessRequired]);

  useEffect(() => {
    if (!attempt?.attemptId || !hasStarted || !session?.modePolicy.technicalGuidanceTelemetryEnabled) return;

    const onFullscreenChange = () => {
      logIntegrityEvent(document.fullscreenElement ? 'fullscreen_enter' : 'fullscreen_exit');
    };
    const onVisibilityChange = () => {
      if (document.visibilityState === 'hidden') logIntegrityEvent('page_hidden');
      if (document.visibilityState === 'visible') logIntegrityEvent('page_visible');
    };
    const onBlur = () => logIntegrityEvent('window_blur');
    const onFocus = () => logIntegrityEvent('window_focus');

    document.addEventListener('fullscreenchange', onFullscreenChange);
    document.addEventListener('visibilitychange', onVisibilityChange);
    window.addEventListener('blur', onBlur);
    window.addEventListener('focus', onFocus);
    return () => {
      document.removeEventListener('fullscreenchange', onFullscreenChange);
      document.removeEventListener('visibilitychange', onVisibilityChange);
      window.removeEventListener('blur', onBlur);
      window.removeEventListener('focus', onFocus);
    };
  }, [attempt?.attemptId, hasStarted, logIntegrityEvent, session?.modePolicy.technicalGuidanceTelemetryEnabled]);

  const ensureAttempt = async () => {
    if (!session) throw new Error('Listening session is not ready.');
    if (attempt) return attempt;
    try {
      const started = await startListeningAttempt(session.paper.id, mode, { pathwayStage, mockAttemptId, mockSectionId });
      showCreditFeedback(started.feedbackMessage);
      syncServerClock(started.serverNow);
      setAttempt(started);
      if (id && mockAttemptId && mockSectionId && !attemptIdFromRoute) {
        const nextParams = new URLSearchParams(searchParams?.toString());
        nextParams.set('attemptId', started.attemptId);
        router.replace(`/listening/player/${encodeURIComponent(id)}?${nextParams.toString()}`);
      }
      return started;
    } catch (err) {
      if (isListeningAudioCheckError(err)) {
        // Expired 24h gate — refresh the pathway sound-check once then retry.
        try {
          await submitAudioCheck({ outcome: 'clear' });
        } catch {}
        const retried = await startListeningAttempt(session.paper.id, mode, { pathwayStage, mockAttemptId, mockSectionId });
        showCreditFeedback(retried.feedbackMessage);
        syncServerClock(retried.serverNow);
        setAttempt(retried);
        if (id && mockAttemptId && mockSectionId && !attemptIdFromRoute) {
          const nextParams = new URLSearchParams(searchParams?.toString());
          nextParams.set('attemptId', retried.attemptId);
          router.replace(`/listening/player/${encodeURIComponent(id)}?${nextParams.toString()}`);
        }
        return retried;
      }
      throw err;
    }
  };

  const startTask = async () => {
    if (audioValidityHeld) return;
    if (!session?.paper.audioAvailable || !session.readiness.objectiveReady) return;
    const readinessSnapshot = techReadiness;
    if (strictReadinessRequired && !readinessSnapshot?.audioOk) {
      setStartError('Complete the audio readiness check before starting this strict Listening attempt.');
      return;
    }
    setIsStarting(true);
    setStartError(null);
    try {
      if (strictReadinessRequired) {
        const audioCheck = await submitAudioCheck({ outcome: 'clear' });
        if (!audioCheck.success) {
          throw new Error('The audio readiness check was not accepted. Please retry the check before starting.');
        }
      }
      const started = await ensureAttempt();
      const restoredAnswers = Object.fromEntries(
        Object.entries(started.answers ?? {}).map(([key, value]) => [key, value ?? '']),
      );
      setAnswers(restoredAnswers);
      serverAnswersRef.current = Object.fromEntries(
        Object.entries(started.answers ?? {}).map(([key, value]) => [key, value ?? null]),
      );
      answerBaseValuesRef.current = {};
      pendingAnswersRef.current = {};
      if (strictReadinessRequired) {
        if (!readinessSnapshot?.audioOk) {
          throw new Error('Complete the audio readiness check before starting this strict Listening attempt.');
        }
        // v1.1 — collect real-exam technical guidance telemetry. The server
        // records device/screen observations for guidance and audit only; the
        // audio sound check above is the only strict pre-start readiness gate.
        const probe = await buildTechReadinessProbe({
          audioOk: readinessSnapshot.audioOk,
          durationMs: readinessSnapshot.durationMs,
        });
        await listeningV2Api.recordTechReadiness(started.attemptId, probe);
        try {
          await advanceStrictStart(started.attemptId);
        } catch (err) {
          if (isListeningAudioCheckError(err)) {
            await submitAudioCheck({ outcome: 'clear' });
            await advanceStrictStart(started.attemptId);
          } else {
            throw err;
          }
        }
      }
      setHasStarted(true);
      const nextParams = new URLSearchParams({ attemptId: started.attemptId, mode: started.mode });
      if (focusParam) nextParams.set('focus', focusParam);
      if (partFocus) nextParams.set('part', partFocus.toUpperCase());
      if (pathwayStage) nextParams.set('pathwayStage', pathwayStage);
      if (drillId) nextParams.set('drill', drillId);
      if (mockAttemptId) nextParams.set('mockAttemptId', mockAttemptId);
      if (mockSectionId) nextParams.set('mockSectionId', mockSectionId);
      if (mockMode) nextParams.set('mockMode', mockMode);
      if (mockStrictness) nextParams.set('strictness', mockStrictness);
      if (mockDeliveryMode) nextParams.set('deliveryMode', mockDeliveryMode);
      if (mockStrictTimer) nextParams.set('strictTimer', mockStrictTimer);
      router.replace(`/listening/player/${session.paper.id}?${nextParams.toString()}`);
      // Playback is armed by the explicit Start action. The section/source
      // effects below call play() once the authorized audio URL is mounted;
      // strict attempts still remain in the server-controlled preview phase.
    } catch (err) {
      if (isContentLockedError(err)) {
        setContentLockedMessage(readContentLockedMessage(err));
        return;
      }
      if (isInsufficientCreditsError(err)) {
        setInsufficientCreditsMessage(readInsufficientCreditsMessage(err));
        return;
      }
      setStartError(err instanceof Error ? err.message : 'Could not start this Listening attempt.');
    } finally {
      setIsStarting(false);
    }
  };

  const togglePlayPause = () => {
    if (audioValidityHeld) return;
    if (phase === 'preview') return;
    if (isPlaying && session?.modePolicy.canPause === false) return;
    if (!isPlaying && session?.modePolicy.onePlayOnly && hasReachedEndRef.current) return;
    if (!audioRef.current) return;
    if (!isPlaying && audioResumeInFlightRef.current) {
      allowedPauseRef.current = true;
      setIsPlaying(false);
      audioRef.current.pause();
      return;
    }
    if (isPlaying) pauseAudio();
    else audioRef.current.play().catch(handlePlaybackFailure);
  };

  const handleScrub = (event: React.ChangeEvent<HTMLInputElement>) => {
    if (audioValidityHeld) return;
    if (!session?.modePolicy.canScrub) return;
    const newTime = Number(event.target.value);
    if (!audioRef.current) return;
    audioRef.current.currentTime = newTime;
    setProgress(newTime);
  };

  const persistAnswer = (questionId: string, value: string, currentAttempt: ListeningAttemptDto | null) => {
    if (!currentAttempt) return;
    clearTimeout(saveTimers.current[questionId]);
    const previous = pendingAnswersRef.current[questionId];
    if (!Object.prototype.hasOwnProperty.call(answerBaseValuesRef.current, questionId)) {
      answerBaseValuesRef.current[questionId] = serverAnswersRef.current[questionId] ?? null;
    }
    pendingAnswersRef.current[questionId] = {
      attemptId: currentAttempt.attemptId,
      value,
      baseValue: previous?.attemptId === currentAttempt.attemptId
        ? previous.baseValue
        : answerBaseValuesRef.current[questionId] ?? null,
      eventLogged: previous?.attemptId === currentAttempt.attemptId && previous.value === value
        ? previous.eventLogged
        : false,
    };
    setSaveState('saving');
    saveTimers.current[questionId] = setTimeout(() => {
      const pending = pendingAnswersRef.current[questionId];
      if (!pending || pending.attemptId !== currentAttempt.attemptId || pending.value !== value) return;
      // §17.11 — log one answer_changed per question settle (piggybacks on the
      // 500ms autosave debounce above, so a burst of keystrokes coalesces into
      // a single event rather than one per character).
      savePendingAnswer(questionId, pending);
    }, 500);
  };

  const handleAnswerChange = (questionId: string, answer: string) => {
    if (audioValidityHeld) return;
    setAnswers((current) => ({ ...current, [questionId]: answer }));
    persistAnswer(questionId, answer, attempt);
  };

  // §17.11 — diff the annotation mutation to emit highlight / strikethrough.
  // A highlight toggle (stem) emits `highlight`; an option strike toggle emits
  // `strikethrough`. Each toggle is a single discrete user gesture, so this is
  // already self-throttled (no debounce needed).
  const handleAnnotationChange = (
    questionId: string,
    mutator: (current: ListeningQuestionAnnotation) => ListeningQuestionAnnotation,
  ) => {
    // Diff against the current persisted state to emit the §17.11 telemetry,
    // then route the mutation through the durable hook (debounced autosave).
    if (audioValidityHeld) return;
    const previous = annotations.state.byQuestion[questionId] ?? {};
    const next = mutator(previous);
    const prevStruck = new Set(previous.struckOptions ?? []);
    const nextStruck = new Set(next.struckOptions ?? []);
    if (Boolean(next.stemHighlighted) !== Boolean(previous.stemHighlighted)) {
      logAttemptEvent('highlight', { questionId, target: 'stem', active: Boolean(next.stemHighlighted) });
    }
    if (prevStruck.size !== nextStruck.size) {
      const added = next.struckOptions?.find((option) => !prevStruck.has(option));
      const removed = previous.struckOptions?.find((option) => !nextStruck.has(option));
      logAttemptEvent('strikethrough', { questionId, option: added ?? removed, active: nextStruck.size > prevStruck.size });
    }
    annotations.update(questionId, mutator);
  };

  const handleSubmit = async ({ skipFinalSave = false }: { skipFinalSave?: boolean } = {}) => {
    if (!session || audioValidityHeld) return;
    // Set the synchronous gate before any await so the 15s heartbeat tick
    // cannot race a Submitted attempt and trip the 409 diagnostics check.
    isSubmittingRef.current = true;
    setIsSubmitting(true);
    pauseAudio();
    for (const timer of Object.values(saveTimers.current)) clearTimeout(timer);
    saveTimers.current = {};
    try {
      const activeAttempt = await ensureAttempt();
      // R08 — land any debounced rule-out / highlight edits while the attempt
      // is still InProgress (the hook unmounts on the post-submit navigation).
      await annotations.flush();
      if (!skipFinalSave) {
        Object.entries(pendingAnswersRef.current).forEach(([questionId, pending]) => {
          if (!pending.eventLogged) {
            pending.eventLogged = true;
            logAttemptEvent('answer_changed', { questionId });
          }
        });
        await Promise.all(Object.entries(answers).map(([questionId, answer]) => listeningV2Api.saveAnswer(activeAttempt.attemptId, questionId, answer)));
      }
      const result = await listeningV2Api.submit(activeAttempt.attemptId, answers);
      pendingAnswersRef.current = {};
      analytics.track('task_submitted', { subtest: 'listening', taskId: session.paper.id, attemptId: activeAttempt.attemptId });
      if (mockAttemptId && mockSectionId) {
        try {
          await completeMockSection(mockAttemptId, mockSectionId, {
            contentAttemptId: activeAttempt.attemptId,
            rawScore: result.rawScore,
            rawScoreMax: result.maxRawScore,
            scaledScore: result.scaledScore,
            grade: result.grade,
            evidence: { source: 'listening_player', evaluationId: result.evaluationId },
          });
        } catch (mockErr) {
          // Do not lose the learner's submission on mock-write failure.
          // Surface a soft warning and still navigate to the mock player.
          console.warn('Could not mark mock listening section complete', mockErr);
        }
        router.push(`/mocks/player/${mockAttemptId}`);
        return;
      }
      router.push(`/listening/results/${result.attemptId}`);
    } catch (err) {
      isSubmittingRef.current = false;
      setLoadError(err instanceof Error ? err.message : 'Could not submit this Listening attempt.');
      setIsSubmitting(false);
    }
  };

  // --- OET Listening forward-only section machine -----------------------------
  const sectionGroups = useMemo(
    () => (session ? groupQuestionsBySection(session.questions) : null),
    [session],
  );
  const extracts = session?.paper.extracts ?? [];
  const sectionsInPaper = useMemo<ListeningSectionCode[]>(() => {
    if (!sectionGroups) return [];
    let sections = LISTENING_SECTION_SEQUENCE.filter((code) => sectionGroups[code].length > 0);
    // Focus filtering: restrict visible sections when launched from Part Practice links
    if (focusParam === 'part-a') {
      sections = sections.filter((code) => code === 'A1' || code === 'A2');
    } else if (focusParam === 'part-b') {
      sections = sections.filter((code) => code === 'B');
    } else if (focusParam === 'part-c') {
      sections = sections.filter((code) => code === 'C1' || code === 'C2');
    } else if (focusParam === 'parts-bc') {
      sections = sections.filter((code) => code === 'B' || code === 'C1' || code === 'C2');
    }
    // 2026-05-27 audit fix — Listening rules L-R05.9 / L-R05.10.
    // During the final 2-minute review window (FSM state `c2_final_review`)
    // the candidate may only see / modify C2 answers. Even if a future code
    // path widens `currentSectionIndex` or all-section review
    // mode, this guard keeps CBT exam mode strictly scoped to C2.
    if (strictServerState?.state === 'c2_final_review' && session?.modePolicy?.onePlayOnly) {
      sections = sections.filter((code) => code === 'C2');
    }
    return sections;
  }, [sectionGroups, focusParam, strictServerState?.state, session?.modePolicy?.onePlayOnly]);
  const currentSection: ListeningSectionCode | null = sectionsInPaper[currentSectionIndex] ?? null;
  // Part C uses two sequential audio extracts but one candidate-visible
  // question workspace. Keep both groups sorted by the authoritative printed
  // number so Q31–Q42 can be jumped to without changing the audio cursor.
  const partCQuestionWorkspace = useMemo<ListeningSessionQuestionDto[] | null>(
    () => (currentSection === 'C1' || currentSection === 'C2')
      ? [...(sectionGroups?.C1 ?? []), ...(sectionGroups?.C2 ?? [])]
        .sort((a, b) => a.number - b.number)
      : null,
    [currentSection, sectionGroups],
  );
  const currentQuestionWorkspace = useMemo<ListeningSessionQuestionDto[]>(
    () => partCQuestionWorkspace
      ?? (currentSection ? sectionGroups?.[currentSection] ?? [] : []),
    [currentSection, partCQuestionWorkspace, sectionGroups],
  );
  useEffect(() => {
    setCurrentPartBQuestionIndex(0);
    // The audio cursor and the visible card are separate concerns in strict
    // Part B. Reset both when the section is entered; a jump within the part
    // must not alter the one-play audio cue or leave a stale card selected.
    setActiveQuestionIndexBySection((previous) => ({
      ...previous,
      B: 0,
      ...(currentSection === 'C1' || currentSection === 'C2'
        ? {
          [currentSection]: currentSection === 'C2'
            ? Math.max(0, currentQuestionWorkspace.findIndex((question) => question.number >= 37))
            : 0,
        }
        : {}),
    }));
  }, [currentQuestionWorkspace, currentSection]);
  const currentExtracts = currentSection
    ? extracts
      .filter((extract) => {
        const partCode = extract.partCode.toUpperCase();
        return partCode === currentSection || (currentSection === 'B' && partCode.startsWith('B'));
      })
      .sort((a, b) => a.displayOrder - b.displayOrder)
    : [];
  // Part B slicing is exam-only. Practice (individual Part B) shows all 6 at once.
  // Monolithic papers (1 extract, 6 questions) also show all at once — slicing
  // would require 6 cue windows that legacy data lacks and would trap on Q1.
  const isMonolithicPartB = currentSection === 'B'
    && currentExtracts.length === 1
    && (sectionGroups?.B?.length ?? 0) > 1;
  const shouldSlicePartB = currentSection === 'B'
    && (sectionGroups?.B?.length ?? 0) > 1
    && currentExtracts.length > 1
    && !isMonolithicPartB
    && (mode === 'exam' || mode === 'home' || strictReadinessRequired);
  const activePartBQuestion = shouldSlicePartB
    ? (sectionGroups?.B?.[currentPartBQuestionIndex] ?? null)
    : null;
  const visibleExtracts = shouldSlicePartB
    ? (currentExtracts[currentPartBQuestionIndex] ? [currentExtracts[currentPartBQuestionIndex]] : [])
    : currentExtracts;
  // Per-section audio: each section plays its OWN uploaded file (Part B plays one
  // shared file across B1..B6). Resolved by section code (A1, A2, B, C1, C2).
  // Falls back to the legacy combined paper audio (+ cue windows) for papers that
  // predate the per-section model and have no per-section uploads.
  const perSectionAudioUrl = currentSection
    ? session?.paper.audioUrlByPart?.[String(currentSection).toUpperCase()] ?? null
    : null;
  const usingPerSectionAudio = perSectionAudioUrl != null;
  const currentSectionAudioUrl = perSectionAudioUrl ?? session?.paper.audioUrl ?? null;
  const currentSectionAudioEnded = currentSection ? endedSections.has(currentSection) : false;

  // Start playback once the current section's authorized source is mounted.
  // `play()` is intentionally imperative: bearer-authenticated media is first
  // fetched into a blob URL, so the native autoPlay attribute cannot reliably
  // start it. The explicit Start/Start Exam action that sets hasStarted is the
  // browser user gesture; if a browser still rejects autoplay, the fallback
  // modal gives the candidate a second explicit gesture.
  const tryAutoPlay = useCallback(() => {
    if (!hasStarted || !currentSection || !resolvedAudioSrc) return;
    // Strict exams own the reading/audio transition in the server FSM. The
    // source can be ready while the candidate is still in preview or review;
    // never start scored audio during either locked phase.
    if (strictReadinessRequired && phase !== 'audio') return;
    const audio = audioRef.current;
    if (!audio) return;

    const attemptKey = [
      attempt?.attemptId ?? 'pending',
      currentSection,
      resolvedAudioSrc,
      audioRetryKey,
    ].join(':');
    if (autoPlayAttemptedKeyRef.current === attemptKey) return;
    if (!audio.paused && !audio.ended) {
      autoPlayAttemptedKeyRef.current = attemptKey;
      return;
    }

    // Mark before invoking play() so a synchronous media event and the
    // source-ready effect cannot issue two simultaneous playback requests.
    autoPlayAttemptedKeyRef.current = attemptKey;
    audio.play().catch((error) => {
      // NotAllowedError is recoverable through the explicit modal action. A
      // network/decoder failure remains fail-closed via handlePlaybackFailure.
      if (error instanceof DOMException && error.name === 'NotAllowedError') {
        autoPlayAttemptedKeyRef.current = null;
      }
      handlePlaybackFailure(error);
    });
  }, [attempt?.attemptId, audioRetryKey, currentSection, handlePlaybackFailure, hasStarted, phase, resolvedAudioSrc, strictReadinessRequired]);

  const activeExtract = visibleExtracts[0] ?? null;
  // Part B slicing (exam-only) keeps per-question cue windows; practice /
  // monolithic B shows the whole shared file at once so cues are dropped.
  const currentExtractWindows = shouldSlicePartB
    ? currentExtracts.filter((extract) => (
      extract.partCode === activeExtract?.partCode
      && extract.displayOrder === activeExtract?.displayOrder
      && extract.audioStartMs != null
      && extract.audioEndMs != null
      && extract.audioEndMs > extract.audioStartMs
    ))
    : usingPerSectionAudio
      ? []
      : currentExtracts.filter((extract) => (
        extract.audioStartMs != null
        && extract.audioEndMs != null
        && extract.audioEndMs > extract.audioStartMs
      ));
  const currentSectionAudioStartMs = currentExtractWindows.length > 0
    ? Math.min(...currentExtractWindows.map((extract) => extract.audioStartMs!))
    : null;
  const currentSectionAudioEndMs = currentExtractWindows.length > 0
    ? Math.max(...currentExtractWindows.map((extract) => extract.audioEndMs!))
    : null;
  const activeAudioStartMs = shouldSlicePartB
    ? currentSectionAudioStartMs
    : usingPerSectionAudio
      ? null
      : currentSectionAudioStartMs;
  const activeAudioEndMs = shouldSlicePartB
    ? currentSectionAudioEndMs
    : usingPerSectionAudio
      ? null
      : currentSectionAudioEndMs;
  const isLastSection = currentSection !== null && currentSectionIndex >= sectionsInPaper.length - 1;
  const currentSectionReviewSeconds = currentSection ? LISTENING_REVIEW_SECONDS[currentSection] : 0;
  const canSkipPreview = session?.modePolicy.mode === 'practice';
  const allCurrentExtractsCompleted = currentExtracts.every((extract) => {
    if (extract.audioEndMs == null) return true;
    return completedExtractIds.has(`${extract.partCode}-${extract.displayOrder}`);
  });
  // Exam-mode gate on opening the review window: per-section audio waits for the
  // section's own file to end; the legacy combined-file model waits for all cue
  // windows to be crossed (or has no window to gate on).
  const partBQuestionAudioEnded = shouldSlicePartB
    && activeExtract != null
    && completedExtractIds.has(`${activeExtract.partCode}-${activeExtract.displayOrder}`);
  const audioGateSatisfied = shouldSlicePartB
    ? partBQuestionAudioEnded
    : usingPerSectionAudio
      ? currentSectionAudioEnded
      : (allCurrentExtractsCompleted || currentSectionAudioEndMs == null);
  const canOpenReviewWindow = Boolean(
    currentSection
    && (session?.modePolicy.canScrub !== false || audioGateSatisfied),
  );
  const strictServerNavigationActive = strictReadinessRequired && Boolean(attempt?.attemptId ?? attemptIdFromRoute);
  const scoredAudioUrls = useMemo(() => {
    const urls = [
      session?.paper.audioUrl ?? null,
      ...Object.values(session?.paper.audioUrlByPart ?? {}),
    ];
    return [...new Set(urls.filter((url): url is string => Boolean(url?.trim())).map((url) => url.trim()))];
  }, [session?.paper.audioUrl, session?.paper.audioUrlByPart]);

  const applyStrictServerState = useCallback((state: ListeningV2SessionState) => {
    strictServerStateRef.current = state;
    setStrictServerState(state);
    const position = listeningPositionForState(state.state);
    if (!position) return;

    const sectionIndex = sectionsInPaper.indexOf(position.section);
    if (sectionIndex >= 0) setCurrentSectionIndex(sectionIndex);

    const secondsRemaining = listeningWindowSeconds(state.windowRemainingMs);
    setPhase(position.phase);
    if (position.phase === 'preview') {
      previewArmedRef.current = true;
      setPreviewSecondsRemaining(secondsRemaining);
      setReviewSecondsRemaining(0);
      return;
    }
    previewArmedRef.current = false;
    setPreviewSecondsRemaining(0);
    setReviewSecondsRemaining(position.phase === 'review' ? secondsRemaining : 0);
  }, [sectionsInPaper]);
  applyStrictServerStateRef.current = applyStrictServerState;

  useEffect(() => {
    if (!session) return;
    if (!attemptIdFromRoute) {
      hydratedAttemptIdRef.current = null;
      setStrictServerState(null);
      if (mode === 'practice' || !strictReadinessRequired) {
        setHasStarted(true);
      } else {
        setHasStarted(false);
      }
      return;
    }
    if (!strictReadinessRequired) {
      hydratedAttemptIdRef.current = null;
      setStrictServerState(null);
      setHasStarted(true);
      return;
    }
    // One-time strict-resume hydration per attempt. Applying the resumed state
    // changes a dependency of this effect (`applyStrictServerState`), so re-runs
    // are expected; bail out instead of re-fetching and clobbering the phase.
    if (hydratedAttemptIdRef.current === attemptIdFromRoute) return;
    hydratedAttemptIdRef.current = attemptIdFromRoute;
    let alive = true;
    listeningV2Api
      .getState(attemptIdFromRoute)
      .then((state) => {
        if (!alive) return;
        setStartError(null);
        if (state.state === 'intro') {
          setStrictServerState(state);
          setHasStarted(false);
          setTechReadiness(null);
          return;
        }
        applyStrictServerStateRef.current?.(state);
        setHasStarted(true);
      })
      .catch(() => {
        if (!alive) return;
        // Allow a later dependency change (e.g. session reload) to retry.
        hydratedAttemptIdRef.current = null;
        setStrictServerState(null);
        setHasStarted(false);
        setStartError('Could not verify this strict Listening attempt with the server. Run the readiness check again before starting.');
      });
    return () => {
      alive = false;
    };
  }, [applyStrictServerState, attemptIdFromRoute, session, strictReadinessRequired]);

  const advanceStrictPhaseIfNeeded = useCallback(async (toState: ListeningFsmState) => {
    if (!strictReadinessRequired) return true;
    const activeAttemptId = attempt?.attemptId ?? attemptIdFromRoute;
    if (!activeAttemptId) return true;
    if (strictAdvanceTargetRef.current) return false;

    strictAdvanceTargetRef.current = toState;
    setIsAdvancingPhase(true);
    setAudioError(null);
    try {
      const state = await advanceStrictTransition(activeAttemptId, toState);
      const fromState = strictServerStateRef.current?.state;
      const fromPosition = fromState ? listeningPositionForState(fromState) : null;
      const toPosition = listeningPositionForState(toState);
      if (fromPosition && toPosition && fromPosition.section !== toPosition.section) {
        logAttemptEvent('section_transition', {
          from: fromPosition.section,
          to: toPosition.section,
          fromState,
          toState,
          reason: 'server-transition-applied',
        });
      }
      applyStrictServerState(state);
      return true;
    } catch (err) {
      setAudioError(err instanceof Error ? err.message : 'The server rejected this Listening transition.');
      return false;
    } finally {
      strictAdvanceTargetRef.current = null;
      setIsAdvancingPhase(false);
    }
  }, [applyStrictServerState, attempt?.attemptId, attemptIdFromRoute, logAttemptEvent, strictReadinessRequired]);

  // 1-second countdown during review windows.
  useEffect(() => {
    if (phase !== 'review' || reviewSecondsRemaining <= 0) return;
    const timer = window.setTimeout(() => setReviewSecondsRemaining((value) => value - 1), 1000);
    return () => window.clearTimeout(timer);
  }, [phase, reviewSecondsRemaining]);

  // C8f — 1-second countdown during the pre-audio reading window. Audio is
  // explicitly NOT playing while in preview; answer inputs remain editable
  // so candidates can mark answers in advance (CBLA behaviour).
  useEffect(() => {
    if (phase !== 'preview' || previewSecondsRemaining <= 0) return;
    const timer = window.setTimeout(() => setPreviewSecondsRemaining((value) => value - 1), 1000);
    return () => window.clearTimeout(timer);
  }, [phase, previewSecondsRemaining]);

  // When entering a new section after the player has started, drop straight
  // into the audio phase. The Listening module has NO pre-audio reading-window
  // countdown (owner directive 2026-07-05); the explicit Start/Start Exam
  // gesture arms the one-shot autoplay path. Strict @home exams keep the
  // server-driven window (applied by applyStrictServerState) and bail above.
  useEffect(() => {
    if (!hasStarted || !currentSection) return;
    if (strictReadinessRequired && strictServerState) return;
    if (phase === 'review') return;
    // Reset per-section forward-only end-of-extract latch + audio-run latch.
    hasReachedEndRef.current = false;
    autoAdvanceInFlightRef.current = false;
    audioStartedLoggedRef.current = false;
    audioBufferingActiveRef.current = false;
    previewArmedRef.current = false;
    setPhase('audio');
    tryAutoPlay();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentSection, hasStarted, tryAutoPlay]);

  // The section effect above may run while the authorized blob URL is still
  // resolving. Retry when the source is actually available; the one-shot key
  // in tryAutoPlay prevents duplicate playback for the same section.
  useEffect(() => {
    if (!hasStarted || !currentSection || !resolvedAudioSrc) return;
    tryAutoPlay();
  }, [currentSection, hasStarted, phase, resolvedAudioSrc, tryAutoPlay]);

  const advanceToNextSection = () => {
    // Reset phase off 'review' BEFORE the section index changes. Otherwise
    // the auto-advance effect (which fires whenever phase==='review' &&
    // reviewSecondsRemaining===0) sees the new section's currentSection on
    // re-render and immediately calls advanceFromReview again — which on
    // the last section triggers handleSubmit and skips the section entirely.
    const nextSection = sectionsInPaper[currentSectionIndex + 1];
    if (currentSection && nextSection) {
      logAttemptEvent('section_transition', {
        from: currentSection,
        to: nextSection,
        reason: 'local-transition-applied',
      });
    }
    setPhase('audio');
    setReviewSecondsRemaining(0);
    setCurrentSectionIndex((value) => value + 1);
    // The currentSection effect above arms one-shot playback for the new
    // section (strict attempts remain governed by the server preview state).
  };

  const advanceFromReview = async () => {
    if (audioValidityHeld) return;
    if (isLastSection) {
      void handleSubmit();
      return;
    }
    const nextSection = sectionsInPaper[currentSectionIndex + 1];
    const nextState = nextSection ? listeningStateForPosition(nextSection, 'preview') : null;
    if (nextState) {
      const advanced = await advanceStrictPhaseIfNeeded(nextState);
      if (!advanced) return;
      if (strictServerNavigationActive) return;
    }
    advanceToNextSection();
  };

  // Audio plays once. When every authored cue in the section has been
  // crossed, continue automatically: hop the server review state in
  // strict exams (confirm-token), then leave a 0-second review window
  // immediately. Last section submits. Legacy combined-file papers do
  // not fire `<audio> ended` at a cue boundary, so cue completion
  // drives the same handler.
  useEffect(() => {
    if (phase !== 'audio' || !hasStarted) return;
    if (usingPerSectionAudio) return;
    if (currentSectionAudioEndMs == null) return;
    if (!allCurrentExtractsCompleted) return;
    void autoAdvanceAfterAudio();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase, hasStarted, usingPerSectionAudio, currentSectionAudioEndMs, allCurrentExtractsCompleted]);

  // Zero-length review windows must not trap the candidate. Restore the
  // continuation that advanceToNextSection already guards against: reset
  // phase off 'review' before the section index changes, otherwise this
  // effect would see the next section and submit/skip it.
  useEffect(() => {
    if (!hasStarted || phase !== 'review') return;
    if (reviewSecondsRemaining > 0) return;
    if (audioValidityHeld || autoAdvanceInFlightRef.current) return;
    if (isSubmittingRef.current || autoSubmittedRef.current) return;
    void advanceFromReview();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase, reviewSecondsRemaining, hasStarted, audioValidityHeld]);

  // C8f — when the preview countdown hits zero, transition to audio and
  // trigger playback. The cue-point seek effect below handles auto-seeking
  // to the active extract's audioStartMs once phase === 'audio'.
  useEffect(() => {
    if (phase !== 'preview' || previewSecondsRemaining > 0) return;
    if (!hasStarted || !currentSection) return;
    if (!previewArmedRef.current) return;
    previewArmedRef.current = false;
    // §17.11 — reading window elapsed; audio is about to start for this section.
    logAttemptEvent('reading_time_ended', { section: currentSection });
    const targetState = listeningStateForPosition(currentSection, 'audio');
    void (async () => {
      if (targetState) {
        const advanced = await advanceStrictPhaseIfNeeded(targetState);
        if (!advanced) return;
      }
      setPhase('audio');
      tryAutoPlay();
    })();
  }, [advanceStrictPhaseIfNeeded, logAttemptEvent, phase, previewSecondsRemaining, hasStarted, currentSection, tryAutoPlay]);

  // C8d — whole-attempt policy-defined countdown. Driven by attempt.expiresAt.
  // Auto-submits in exam/home modes (canScrub === false) when the timer
  // hits zero. Practice mode just shows "Time up" without submitting.
  useEffect(() => {
    const expiresAt = attempt?.expiresAt;
    if (!expiresAt || !hasStarted) {
      setAttemptSecondsRemaining(null);
      return;
    }
    const compute = () => {
      const remaining = Math.max(0, Math.floor((Date.parse(expiresAt) - correctedNowMs(serverClockOffsetMs.current)) / 1000));
      setAttemptSecondsRemaining(remaining);
      return remaining;
    };
    if (compute() === 0) return;
    const interval = window.setInterval(() => {
      const remaining = compute();
      if (remaining === 0) window.clearInterval(interval);
    }, 1000);
    return () => window.clearInterval(interval);
  }, [attempt?.expiresAt, hasStarted]);

  // C8d — auto-submit when the attempt timer hits zero in exam/home modes.
  useEffect(() => {
    if (attemptSecondsRemaining !== 0) return;
    if (!hasStarted || !session) return;
    if (autoSubmittedRef.current) return;
    if (session.modePolicy.canScrub) return; // practice mode → no auto-submit
    autoSubmittedRef.current = true;
    // §17.11 — the whole-attempt timer expired and forced submission.
    logAttemptEvent('auto_submit', { reason: 'attempt_timer_expired' });
    void handleSubmit({ skipFinalSave: true });
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [attemptSecondsRemaining, hasStarted, session]);

  // C1 — auto-seek the audio element to the active extract's audioStartMs
  // whenever the section changes or the audio phase begins. Only runs when
  // the extract has both start/end cue points authored. Practice mode keeps
  // full scrub freedom; the soft end-of-extract boundary below only fires in
  // exam mode (canScrub === false). C8f — also runs during the preview
  // phase so the playhead is parked at the correct cue point before the
  // reading window expires and audio play() is invoked.
  useEffect(() => {
    if (phase !== 'audio' && phase !== 'preview') return;
    const audio = audioRef.current;
    if (!audio) return;
    const startMs = activeAudioStartMs;
    const endMs = activeAudioEndMs;
    if (startMs == null || endMs == null || endMs <= startMs) return;
    seekAudioTo(startMs / 1000);
  }, [activeAudioStartMs, activeAudioEndMs, phase, seekAudioTo]);

  // Resolve the current section's audio to an authorized blob URL. The media
  // endpoint is bearer-authenticated and a native <audio src> GET sends no
  // Authorization header, so a raw /v1/media/{id}/content src 401s and fires
  // onError ("Audio failed to load"). Absolute (external/TTS) URLs are used
  // as-is. Re-runs per section, on mount-gate change, and on Retry.
  useEffect(() => {
    const url = currentSectionAudioUrl;
    const mount = (session?.paper.audioAvailable ?? false) && (!strictReadinessRequired || hasStarted);
    if (!url || !mount) {
      setResolvedAudioSrc(null);
      autoPlayAttemptedKeyRef.current = null;
      return;
    }
    if (/^https?:\/\//i.test(url)) {
      autoPlayAttemptedKeyRef.current = null;
      setResolvedAudioSrc(url);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    setResolvedAudioSrc(null);
    autoPlayAttemptedKeyRef.current = null;
    fetchAuthorizedObjectUrl(url)
      .then((blobUrl) => {
        if (cancelled) {
          URL.revokeObjectURL(blobUrl);
          return;
        }
        objectUrl = blobUrl;
        setResolvedAudioSrc(blobUrl);
      })
      .catch(() => {
        if (cancelled) return;
        flagAudioFailure('Audio failed to load. This attempt has been halted and flagged for administrator review; do not replay the scored audio.');
      });
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [
    currentSectionAudioUrl,
    session?.paper.audioAvailable,
    strictReadinessRequired,
    hasStarted,
    audioRetryKey,
    flagAudioFailure,
  ]);

  const confirmNextFromAudio = async () => {
    if (audioValidityHeld) return;
    if (!currentSection) return;
    if (!canOpenReviewWindow) return;
    pauseAudio();
    if (currentSectionReviewSeconds > 0) {
      const reviewState = listeningStateForPosition(currentSection, 'review');
      if (reviewState) {
        const advanced = await advanceStrictPhaseIfNeeded(reviewState);
        if (!advanced) return;
        if (strictServerNavigationActive) return;
      }
      setPhase('review');
      setReviewSecondsRemaining(currentSectionReviewSeconds);
    } else {
      if (isLastSection) {
        void handleSubmit();
        return;
      }
      const nextSection = sectionsInPaper[currentSectionIndex + 1];
      const nextState = nextSection ? listeningStateForPosition(nextSection, 'preview') : null;
      if (nextState) {
        const advanced = await advanceStrictPhaseIfNeeded(nextState);
        if (!advanced) return;
        if (strictServerNavigationActive) return;
      }
      advanceToNextSection();
    }
  };

  const advancePartBQuestion = async () => {
    if (audioValidityHeld || !shouldSlicePartB || !partBQuestionAudioEnded) return;
    const nextIndex = currentPartBQuestionIndex + 1;
    const hasNextQuestion = nextIndex < (sectionGroups?.B?.length ?? 0);
    if (!hasNextQuestion) {
      await confirmNextFromAudio();
      return;
    }
    autoPlayNextPartBQuestionRef.current = true;
    hasReachedEndRef.current = false;
    allowedPauseRef.current = true;
    pauseAudio();
    setIsPlaying(false);
    setCurrentPartBQuestionIndex(nextIndex);
    setActiveQuestionIndexBySection((previous) => ({ ...previous, B: nextIndex }));
  };

  // Part B uses one shared audio asset with authored cue windows. After the
  // candidate confirms the irreversible Next action, begin the next extract
  // from its cue without exposing a pause/replay control.
  useEffect(() => {
    if (!autoPlayNextPartBQuestionRef.current || !shouldSlicePartB || phase !== 'audio') return;
    autoPlayNextPartBQuestionRef.current = false;
    const audio = audioRef.current;
    if (!audio) return;
    if (activeAudioStartMs != null) seekAudioTo(activeAudioStartMs / 1000);
    audio.play().catch(handlePlaybackFailure);
  }, [activeAudioStartMs, currentPartBQuestionIndex, shouldSlicePartB, phase, seekAudioTo]);

  // Audio is non-pausable in every mode. Cue-end / `ended` continues the
  // section automatically. Part B only fail-closes when an extract is
  // missing a valid cue; otherwise it waits until every workplace extract
  // has been crossed, then follows the same review/next/submit path.
  // Idempotent via `autoAdvanceInFlightRef` (reset when the section changes).
  const autoAdvanceAfterAudio = async () => {
    if (audioValidityHeld) return;
    if (!currentSection) return;
    if (autoAdvanceInFlightRef.current) return;
    if (shouldSlicePartB) {
      const missingCueBoundary = currentExtracts.length === 0 || currentExtracts.some((extract) => (
        extract.audioStartMs == null
        || extract.audioEndMs == null
        || extract.audioEndMs <= extract.audioStartMs
      ));
      if (missingCueBoundary) {
        setAudioValidityHeld(true);
        setIntegrityWarning('Part B audio is missing a valid cue boundary for each short extract. This attempt has been halted and flagged for administrator review.');
        logIntegrityEvent('part_b_extract_boundary_missing');
        return;
      }
      if (!allCurrentExtractsCompleted) return;
    }
    if (currentSection === 'B' && !shouldSlicePartB) {
      // Non-sliced Part B (practice / monolithic) advances like a normal
      // section — do not require per-extract cue completion.
    }
    autoAdvanceInFlightRef.current = true;
    try {
      if (strictServerNavigationActive) {
        const reviewState = listeningStateForPosition(currentSection, 'review');
        if (reviewState) {
          const advancedReview = await advanceStrictPhaseIfNeeded(reviewState);
          if (!advancedReview) return;
          // Product review windows are 0: hop the FSM review state so the
          // confirm-token stays in sync, then continue immediately.
          if (currentSectionReviewSeconds > 0) return;
          await advanceFromReview();
          return;
        }
      }
      if (currentSectionReviewSeconds > 0) {
        setPhase('review');
        setReviewSecondsRemaining(currentSectionReviewSeconds);
        return;
      }
      await advanceFromReview();
    } finally {
      autoAdvanceInFlightRef.current = false;
    }
  };
  // ---------------------------------------------------------------------------

  if (loadingTask) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="mx-auto max-w-3xl space-y-6 px-4 py-8 sm:px-6 lg:px-8">
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
          <Skeleton className="h-48 rounded-2xl" />
        </div>
      </AppShell>
    );
  }

  if (contentLockedMessage) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8">
          <ContentLockedNotice
            message={contentLockedMessage}
            previewHint="Tip: subscribers get the first extract of every paper to preview before committing."
          />
          <Button variant="ghost" asChild>
<Link href="/listening">Back to Listening</Link>
</Button>
        </div>
      </AppShell>
    );
  }

  if (!session || loadError) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <AlertCircle className="h-12 w-12 text-danger" />
          <h2 className="text-xl font-black text-navy">Listening task unavailable</h2>
          <p className="max-w-md text-sm text-muted">{loadError ?? 'Task not found.'}</p>
          <Button variant="ghost" asChild>
<Link href="/listening">Back to Listening</Link>
</Button>
        </div>
      </AppShell>
    );
  }

  if (isSubmitting) {
    return (
      <AppShell pageTitle="Submitting" distractionFree>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <Loader2 className="h-12 w-12 animate-spin text-primary" />
          <h2 className="text-xl font-black text-navy">Grading Listening Answers</h2>
          <p className="text-sm text-muted">Calculating your score and preparing your transcript…</p>
        </div>
      </AppShell>
    );
  }

  const isExam = session.modePolicy.onePlayOnly;
  const answeredCount = Object.values(answers).filter((value) => value.trim().length > 0).length;
  const unansweredQuestions = session.questions.filter((question) => (answers[question.id] ?? '').trim().length === 0);
  const unansweredQuestionNumbers = unansweredQuestions.map((question) => question.number).sort((a, b) => a - b);
  const unansweredQuestionList = formatQuestionNumberList(unansweredQuestionNumbers);
  const currentSectionUnansweredNumbers = currentSection
    ? currentQuestionWorkspace
      .filter((question) => (answers[question.id] ?? '').trim().length === 0)
      .map((question) => question.number)
      .sort((a, b) => a - b)
    : [];
  const currentSectionUnansweredList = formatQuestionNumberList(currentSectionUnansweredNumbers);
  // Keep every authored question in the navigation model, including strict
  // Part B where the audio cue advances one extract at a time. Rendering the
  // selected card is independent from the audio cursor, so jump/Next changes
  // never restart or seek the scored recording.
  const navigationQuestions = currentQuestionWorkspace;
  const visibleQuestionSections = currentSection
    ? [{
      section: currentSection,
      questions: currentQuestionWorkspace,
    }]
    : [];
  const shouldMountAudio = session.paper.audioAvailable && (!strictReadinessRequired || hasStarted);
  // Wave 3 — resolve presentation skin from server-issued policy. The skin
  // wraps the existing player chrome; rendering logic below stays identical.
  const presentationMode = presentationModeFromSession({
    mode: session.modePolicy.mode,
    presentationStyle: session.modePolicy.presentationStyle ?? null,
  });

  return (
    <ListeningPlayerSkinShell mode={presentationMode}>
    <AppShell pageTitle={session.paper.title} distractionFree>
      <InsufficientCreditsModal
        open={insufficientCreditsMessage !== null}
        message={insufficientCreditsMessage ?? ''}
        onClose={() => setInsufficientCreditsMessage(null)}
      />
      {shouldMountAudio ? (
        <audio
          key={usingPerSectionAudio ? `${audioRetryKey}-${currentSection ?? ''}` : audioRetryKey}
          ref={audioRef}
          src={resolvedAudioSrc ?? undefined}
          controlsList="nodownload nofullscreen noremoteplayback"
          onTimeUpdate={() => {
            const audio = audioRef.current;
            if (!audio) return;
            const now = audio.currentTime;
            setProgress(now);
            // C8e — track forward-only audio progress for the seek snap-back.
            if (!audio.seeking && now > lastKnownTimeRef.current) {
              lastKnownTimeRef.current = now;
            }
            // C1 / C8e — soft enforcement of the active extract's
            // audioEndMs in exam mode. Practice mode keeps full scrub
            // freedom. We pause and latch hasReachedEnd; subsequent
            // play() invocations are immediately re-paused.
            // Part B shares one file across workplace extracts: do not
            // halt at the current question's cue if a later extract has
            // not been reached yet, or later end-cues never fire.
            const startMs = activeAudioStartMs;
            const endMs = activeAudioEndMs;
            const laterPartBExtractRemains = currentSection === 'B'
              && currentExtracts.some((extract) => (
                extract.audioEndMs != null && now * 1000 < extract.audioEndMs
              ));
            if (
              session?.modePolicy.canScrub === false
              && startMs != null
              && endMs != null
              && endMs > startMs
              && now * 1000 >= endMs
              && !audio.paused
              && !laterPartBExtractRemains
            ) {
              hasReachedEndRef.current = true;
              allowedPauseRef.current = true;
              audio.pause();
            }
            // C8c — mark every authored extract whose audioEndMs has been
            // crossed, not only the currently visible row. Part B keeps one
            // question on screen while later workplace extracts still need
            // their end cues recorded so the section can auto-complete.
            const completedNow = currentExtracts.filter((extract) => (
              extract.audioEndMs != null
              && now * 1000 >= extract.audioEndMs
            ));
            if (completedNow.length > 0) {
              setCompletedExtractIds((prev) => {
                if (completedNow.every((extract) => prev.has(`${extract.partCode}-${extract.displayOrder}`))) return prev;
                const next = new Set(prev);
                for (const extract of completedNow) {
                  next.add(`${extract.partCode}-${extract.displayOrder}`);
                }
                return next;
              });
            }
          }}
          onSeeking={() => {
            const audio = audioRef.current;
            if (!audio) return;
            const blockedTarget = resolveBlockedSeekTarget({
              canScrub: session?.modePolicy.canScrub !== false,
              requestedTime: audio.currentTime,
              lastKnownTime: lastKnownTimeRef.current,
              allowedProgrammaticTarget: programmaticSeekTargetRef.current,
            });
            if (blockedTarget !== null) {
              logIntegrityEvent('audio_seek_blocked', `requested=${audio.currentTime.toFixed(2)};allowed=${blockedTarget.toFixed(2)}`);
              programmaticSeekTargetRef.current = blockedTarget;
              if (programmaticSeekResetTimerRef.current) window.clearTimeout(programmaticSeekResetTimerRef.current);
              programmaticSeekResetTimerRef.current = window.setTimeout(() => {
                if (programmaticSeekTargetRef.current === blockedTarget) programmaticSeekTargetRef.current = null;
                programmaticSeekResetTimerRef.current = null;
              }, 1000);
              audio.currentTime = blockedTarget;
              setProgress(blockedTarget);
            }
          }}
          onSeeked={() => {
            programmaticSeekTargetRef.current = null;
          }}
          onLoadedMetadata={() => {
            const audio = audioRef.current;
            if (!audio) return;
            if (session?.modePolicy.onePlayOnly) {
              audio.defaultPlaybackRate = 1;
              audio.playbackRate = 1;
            }
            setDuration(audio.duration);
            const startMs = activeAudioStartMs;
            const endMs = activeAudioEndMs;
            if ((phase === 'audio' || phase === 'preview') && startMs != null && endMs != null && endMs > startMs) {
              seekAudioTo(startMs / 1000);
            }
            setAudioState('ready');
          }}
          onRateChange={() => {
            const audio = audioRef.current;
            if (!audio || !session?.modePolicy.onePlayOnly || audio.playbackRate === 1) return;
            const requestedRate = Number(audio.playbackRate);
            logAttemptEvent(
              'audio_speed_change_blocked',
              { requestedRate: Number.isFinite(requestedRate) ? requestedRate : null },
            );
            audio.defaultPlaybackRate = 1;
            audio.playbackRate = 1;
          }}
          onWaiting={() => {
            if (!audioBufferingActiveRef.current) {
              audioBufferingActiveRef.current = true;
              logAttemptEvent('audio_buffering_start', currentSection ? { section: currentSection } : undefined);
            }
            // The browser has already halted playback while waiting for data.
            // Keep the UI explicit so a candidate does not try to replay or
            // seek around a network interruption.
            setAudioState('buffering');
          }}
          onStalled={() => {
            if (!audioBufferingActiveRef.current) {
              audioBufferingActiveRef.current = true;
              logAttemptEvent('audio_buffering_start', currentSection ? { section: currentSection } : undefined);
            }
            logAttemptEvent('audio_stalled', currentSection ? { section: currentSection } : undefined);
            setAudioState('buffering');
          }}
          onCanPlay={() => {
            if (audioBufferingActiveRef.current) {
              audioBufferingActiveRef.current = false;
              logAttemptEvent('audio_buffering_end', currentSection ? { section: currentSection } : undefined);
            }
            setAudioState('ready');
            tryAutoPlay();
          }}
          onPlay={() => {
            // A real playback start clears any transient audio-validity hold so
            // the attempt can still be submitted even after a one-off glitch.
            if (audioValidityHeld) {
              setAudioValidityHeld(false);
              setIntegrityWarning(null);
              setAudioError(null);
              allowedPauseRef.current = true;
              audioRef.current?.pause();
              setIsPlaying(false);
              return;
            }
            // C8e — once the active extract's end has been reached in
            // exam mode, any subsequent play() is immediately re-paused.
            const audio = audioRef.current;
            if (audio && hasReachedEndRef.current && session?.modePolicy.onePlayOnly) {
              logIntegrityEvent('audio_replay_blocked');
              allowedPauseRef.current = true;
              audio.pause();
              return;
            }
            // C8f — block playback during the pre-audio reading window.
            if (phase === 'preview' && audio) {
              audio.pause();
              return;
            }
            if (audio && audioResumeInFlightRef.current) {
              allowedPauseRef.current = true;
              setIsPlaying(false);
              audio.pause();
              return;
            }
            setIsPlaying(true);
            // §17.11 — one audio_started per audio run (a pause→resume cycle
            // re-enters onPlay but must not re-emit). Reset on onEnded / new
            // section so each section logs a single start/end pair.
            if (!audioStartedLoggedRef.current) {
              audioStartedLoggedRef.current = true;
              logAttemptEvent('audio_started', currentSection ? { section: currentSection } : undefined);
            }
            // C8g — validate the resume against the server grace window.
            handleAudioResume();
          }}
          onPause={() => {
            const audio = audioRef.current;
            const allowedProgrammaticPause = allowedPauseRef.current;
            allowedPauseRef.current = false;
            if (audioStartedLoggedRef.current && !hasReachedEndRef.current) {
              logAttemptEvent('audio_stopped', {
                section: currentSection ?? undefined,
                reason: allowedProgrammaticPause ? 'programmatic' : 'pause',
              });
            }
            if (shouldResumeAfterBlockedPause({
              canPause: session?.modePolicy.canPause !== false,
              phase,
              hasStarted,
              hasReachedEnd: hasReachedEndRef.current,
              allowedProgrammaticPause,
            })) {
              logIntegrityEvent('audio_pause_blocked');
              // C8g — flag the upcoming play() as a resume so the onPlay
              // handler issues a server-side audio-resume validation.
              wasPausedRef.current = true;
              audio?.play().catch(handlePlaybackFailure);
              return;
            }
            // C8g — flag the next play() as a resume.
            if (!audioResumeInFlightRef.current) wasPausedRef.current = true;
            setIsPlaying(false);
          }}
          onEnded={() => {
            if (session?.modePolicy.onePlayOnly) hasReachedEndRef.current = true;
            setIsPlaying(false);
            // Per-section audio: the section's own file finishing IS the
            // listen-complete signal that opens the review window in exam mode.
            if (usingPerSectionAudio && currentSection) {
              setEndedSections((prev) => {
                if (prev.has(currentSection)) return prev;
                const next = new Set(prev);
                next.add(currentSection);
                return next;
              });
            }
            // §17.11 — close the audio run and arm the next section's start.
            logAttemptEvent('audio_ended', currentSection ? { section: currentSection } : undefined);
            audioStartedLoggedRef.current = false;
            // Audio completion opens the explicit finish confirmation/review
            // state; it must not silently lock and cross the boundary.
            void autoAdvanceAfterAudio();
          }}
          onError={() => {
            flagAudioFailure('Audio failed to load. This attempt has been halted and flagged for administrator review; do not replay the scored audio.');
          }}
          preload="metadata"
        />
      ) : null}

      <div ref={rootRef} className="mx-auto max-w-3xl px-4 py-8 pb-24 sm:px-6 lg:px-8">
        {isPartialSourceGapListeningSession(session) ? (
          <InlineAlert variant="warning" className="mb-5">
            Questions 37–42 are unavailable. The supplied Atlas Sample Test 9 source ends at Question 36, so Part C extract 2 is not included. Score this paper as 36 items, not a full 42-item OET Listening paper.
          </InlineAlert>
        ) : null}
        {!hasStarted ? (
          <ListeningIntroCard
            session={session}
            isExam={isExam}
            drillId={drillId ?? null}
            strictReadinessRequired={strictReadinessRequired}
            techReadiness={techReadiness}
            audioUrls={scoredAudioUrls}
            isStarting={isStarting}
            audioError={audioError}
            startError={startError}
            onTechReadinessReady={(result) => {
              setTechReadiness(result);
              setStartError(null);
            }}
            onStart={startTask}
          />
        ) : (
          <motion.div {...listMotion} className="space-y-5 sm:space-y-8">
            <ListeningAudioTransport
              isPlaying={isPlaying}
              progressSeconds={progress}
              durationSeconds={duration}
              canScrub={session.modePolicy.canScrub !== false}
              canPause={session.modePolicy.canPause !== false}
              isPreviewPhase={phase === 'preview'}
              isHalted={audioValidityHeld}
              audioState={audioState}
              saveState={saveState}
              answeredCount={answeredCount}
              totalQuestions={session.questions.length}
              attemptSecondsRemaining={attemptSecondsRemaining}
              warningThresholdsSeconds={session.modePolicy.countdownWarningsSeconds}
              onTogglePlayPause={togglePlayPause}
              onScrub={handleScrub}
              onSubmit={() => setShowSubmitConfirm(true)}
              submitDisabled={audioValidityHeld}
            />

            {session.modePolicy.screenReaderOptimised ? (
              <p
                data-testid="listening-screen-reader-status"
                className="sr-only"
                aria-live="polite"
                aria-atomic="true"
              >
                {phase === 'preview' && currentSection
                  ? `${LISTENING_SECTION_LABEL[currentSection]} reading time has started.`
                  : phase === 'review' && currentSection
                    ? `${LISTENING_SECTION_LABEL[currentSection]} review window is open.`
                    : currentSection
                      ? `${LISTENING_SECTION_LABEL[currentSection]} audio and questions are active.`
                      : 'Listening attempt active.'}
              </p>
            ) : null}

            {audioState === 'buffering' ? (
              <InlineAlert variant="warning">
                Audio buffering has halted playback. Do not replay or seek; playback will continue when the stream is ready.
              </InlineAlert>
            ) : null}

            {saveState === 'offline-saved' ? (
              <InlineAlert variant="info">
                Your latest Listening answer is encrypted and saved on this device. It will reconcile with the server when the connection returns.
              </InlineAlert>
            ) : null}
            {saveState === 'conflict' ? (
              <InlineAlert variant="warning">
                A newer Listening answer is already saved on the server. Your offline answer was not applied.
              </InlineAlert>
            ) : null}

            {mockAttemptId ? (
              <InlineAlert variant="info">
                You&rsquo;re taking this section as part of a mock. Submitting will mark this section complete and return you to the mock dashboard.
              </InlineAlert>
            ) : null}
            {audioError ? (
              <InlineAlert variant="error">
                {audioError}
                {!audioValidityHeld ? (
                  <button
                    type="button"
                    className="ml-3 underline font-medium"
                    onClick={() => {
                      setAudioError(null);
                      setAudioState('idle');
                      setAudioRetryKey((k) => k + 1);
                    }}
                  >
                    Retry
                  </button>
                ) : null}
              </InlineAlert>
            ) : null}
            {saveState === 'error' ? <InlineAlert variant="warning">One answer did not autosave. Keep working; submit will retry saving all answers.</InlineAlert> : null}
            {integrityWarning ? <InlineAlert variant="warning">{integrityWarning}</InlineAlert> : null}
            {audioResumeWarning ? <InlineAlert variant="warning">{audioResumeWarning}</InlineAlert> : null}

            {visibleExtracts.length > 0 ? (
              <div className="rounded-2xl border border-border bg-surface p-4">
                <div className="flex flex-wrap items-center gap-3 text-xs text-muted">
                  {visibleExtracts.map((extract) => {
                    const extractKey = `${extract.partCode}-${extract.displayOrder}`;
                    const completed = completedExtractIds.has(extractKey);
                    return (
                      <span key={extractKey} className="inline-flex items-center gap-2 rounded-lg bg-background-light px-3 py-2">
                        {completed ? (
                          <CheckCircle2 className="h-4 w-4 text-success" aria-label="Listened to completion" />
                        ) : (
                          <Volume2 className="h-4 w-4" />
                        )}
                        <span className="font-semibold text-navy">{extract.title}</span>
                        <span>{extract.accentCode ?? 'accent not set'}</span>
                        {extract.audioStartMs != null || extract.audioEndMs != null ? (
                          <span>{formatMilliseconds(extract.audioStartMs) ?? '00:00'} - {formatMilliseconds(extract.audioEndMs) ?? 'end'}</span>
                        ) : null}
                      </span>
                    );
                  })}
                </div>
              </div>
            ) : null}

            {/* Section stepper — forward only. Completed sections are permanently locked. */}
            <ListeningSectionStepper
              sections={sectionsInPaper}
              currentIndex={currentSectionIndex}
              isReviewing={phase === 'review'}
              freeNavigation={false}
              onSelectSection={() => undefined}
            />

            {/* C8f — pre-audio reading window. Audio stays paused; answer
                inputs remain editable so candidates can mark in advance. */}
            {phase === 'preview' && currentSection ? (
              <ListeningPreviewBanner
                section={currentSection}
                secondsRemaining={previewSecondsRemaining}
                canSkip={canSkipPreview}
                onSkip={() => setPreviewSecondsRemaining(0)}
              />
            ) : null}

            {/* Review-window countdown banner — answer boxes stay editable. */}
            {phase === 'review' && currentSection ? (
              <ListeningReviewBanner
                section={currentSection}
                secondsRemaining={reviewSecondsRemaining}
                isLastSection={isLastSection}
                onNext={() => (isLastSection ? setShowSubmitConfirm(true) : setShowNextConfirm(true))}
              />
            ) : null}

            <div className="space-y-6">
              {visibleQuestionSections.length === 0 ? (
                <InlineAlert variant="warning">
                  This Listening paper does not contain any authored sections yet.
                </InlineAlert>
              ) : (
                <>
                  {/* Question jumper — intra-section in the computer-based flow.
                      In-flow (NOT sticky) so it never floats over the questions as a
                      translucent overlay on scroll (owner directive 2026-07-05). */}
                  {navigationQuestions.length > 1 ? (
                    <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-surface p-3">
                      <span className="mr-1 text-[10px] font-black uppercase tracking-widest text-muted">
                        Jump to
                      </span>
                      {navigationQuestions.map((question) => {
                        const isAnswered = (answers[question.id] ?? '').trim().length > 0;
                        return (
                          <button
                            key={`jump-${question.id}`}
                            type="button"
                            onClick={() => {
                              const index = navigationQuestions.findIndex((item) => item.id === question.id);
                              if (index >= 0 && currentSection) {
                                setActiveQuestionIndexBySection((previous) => ({
                                  ...previous,
                                  [currentSection]: index,
                                }));
                              }
                              // In strict Part B only one card is mounted at a
                              // time. Wait for the selected card to commit
                              // before scrolling/focusing it; otherwise a jump
                              // button would change no visible content.
                              window.setTimeout(() => {
                                const element = document.getElementById(`listening-question-${question.id}`);
                                element?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                                const input = document.getElementById(`listening-answer-${question.id}`);
                                (input as HTMLInputElement | null)?.focus();
                              }, 0);
                            }}
                            aria-label={`Go to question ${question.number}`}
                            className={`inline-flex h-8 w-8 items-center justify-center rounded-full text-xs font-black transition-colors ${
                              isAnswered
                                ? 'bg-success/10 text-success hover:bg-success/20'
                                : 'bg-background-light text-muted hover:bg-border'
                            }`}
                          >
                            {question.number}
                          </button>
                        );
                      })}
                    </div>
                  ) : null}

                  <>
                      <ZoomControls value={questionZoomPercent} onChange={setQuestionZoomPercent} />

                      <div data-testid="listening-question-surface" className="space-y-6" style={{ fontSize: `${questionZoomPercent}%` }}>
                        {visibleQuestionSections.map(({ section, questions }) => (
                          <section key={section} className="space-y-4" aria-label={LISTENING_SECTION_LABEL[section]}>
                            {(() => {
                              if (section === 'A1' || section === 'A2') {
                                const extract = extracts.find((e) => e.partCode === section);
                                if (extract?.notesBody?.trim()) {
                                  return (
                                    <PartANotesDocument
                                      partLabel={LISTENING_SECTION_LABEL[section]}
                                      notesBody={extract.notesBody}
                                      questions={questions.map((q) => ({ id: q.id, number: q.number }))}
                                      answers={answers}
                                      onAnswerChange={handleAnswerChange}
                                      locked={false}
                                    />
                                  );
                                }
                              }

                              // Legacy Part A (no notesBody) or Part B/C → per-question renderer.
                              // Track the last rendered extract context so a Part C
                              // presentation shows its scenario line once (above its
                              // first card) while Part B shows each clip's own line.
                              if (questions.length > 0 && questions[0].options.length === 0) {
                                return questions.map((question) => (
                                  <div id={`listening-question-${question.id}`} key={question.id} className="scroll-mt-48">
                                    <PartARenderer
                                      questionNumber={question.number}
                                      partLabel={LISTENING_SECTION_LABEL[section]}
                                      prompt={question.text}
                                      inputId={`listening-answer-${question.id}`}
                                      value={answers[question.id] ?? ''}
                                      onChange={(value) => handleAnswerChange(question.id, value)}
                                      locked={false}
                                    />
                                  </div>
                                ));
                              }

                              const activeIdx = Math.max(0, Math.min(questions.length - 1, activeQuestionIndexBySection[section] ?? 0));
                              const currentQuestion = questions[activeIdx] ?? questions[0];
                              if (!currentQuestion) return null;

                              const questionExtract = extracts.find((e) => e.partCode === currentQuestion.partCode);
                              const contextIntro = questionExtract?.contextIntro?.trim() || null;

                              return (
                                <div className="space-y-6">
                                  {/* Jump-to question selector pills */}
                                  {questions.length > 1 && (
                                    <div
                                      className="flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-background-light p-3"
                                      role="tablist"
                                      aria-label="Question selector"
                                    >
                                      <span className="text-xs font-black uppercase tracking-widest text-muted mr-1">Questions:</span>
                                      {questions.map((q, idx) => {
                                        const isActive = idx === activeIdx;
                                        const isAnswered = Boolean((answers[q.id] ?? '').trim());
                                        const isFlagged = Boolean(annotations.state.byQuestion[q.id]?.flagged);
                                        return (
                                          <button
                                            key={q.id}
                                            type="button"
                                            role="tab"
                                            aria-selected={isActive}
                                            aria-label={`Question ${q.number}${isAnswered ? ' (answered)' : ''}${isFlagged ? ' (flagged)' : ''}`}
                                            onClick={() => {
                                              setActiveQuestionIndexBySection((prev) => ({ ...prev, [section]: idx }));
                                            }}
                                            className={cn(
                                              "relative flex h-10 w-10 items-center justify-center rounded-xl text-xs font-bold transition-all",
                                              isActive
                                                ? "border-2 border-primary bg-primary text-white shadow-sm"
                                                : isAnswered
                                                ? "border border-primary/40 bg-primary/10 text-primary hover:bg-primary/20"
                                                : "border border-border bg-surface text-navy hover:border-border-hover",
                                              isFlagged && !isActive && "ring-2 ring-warning",
                                            )}
                                          >
                                            {q.number}
                                            {isFlagged && (
                                              <span className="absolute -top-1 -right-1 flex h-3 w-3 items-center justify-center rounded-full bg-warning">
                                                <span className="h-1.5 w-1.5 rounded-full bg-navy" />
                                              </span>
                                            )}
                                          </button>
                                        );
                                      })}
                                    </div>
                                  )}

                                  {/* Active question card */}
                                  <div id={`listening-question-${currentQuestion.id}`} key={currentQuestion.id} className="scroll-mt-48 space-y-4">
                                    {contextIntro ? (
                                      <p
                                        data-testid="listening-extract-context"
                                        className="mb-3 rounded-2xl border border-border bg-background-light px-4 py-3 text-sm font-medium leading-relaxed text-navy"
                                      >
                                        {contextIntro}
                                      </p>
                                    ) : null}
                                    <BCQuestionRenderer
                                      questionNumber={currentQuestion.number}
                                      partLabel={listeningQuestionPartLabel(currentQuestion, LISTENING_SECTION_LABEL[section])}
                                      prompt={currentQuestion.text}
                                      options={currentQuestion.options}
                                      optionKeys={currentQuestion.optionKeys}
                                      value={answers[currentQuestion.id] ?? ''}
                                      onChange={(value) => handleAnswerChange(currentQuestion.id, value)}
                                      annotation={annotations.state.byQuestion[currentQuestion.id] ?? {}}
                                      onAnnotationChange={(mutator) => handleAnnotationChange(currentQuestion.id, mutator)}
                                      locked={false}
                                    />

                                    {/* Stepper Controls */}
                                    <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border/60 pt-4">
                                      <div>
                                        {questions.length > 1 && activeIdx > 0 ? (
                                          <Button
                                            variant="outline"
                                            onClick={() => {
                                              setActiveQuestionIndexBySection((prev) => ({
                                                ...prev,
                                                [section]: Math.max(0, activeIdx - 1),
                                              }));
                                            }}
                                            aria-label="Previous question"
                                          >
                                            <ArrowLeft className="mr-1.5 h-4 w-4" aria-hidden="true" />
                                            Previous Question
                                          </Button>
                                        ) : null}
                                      </div>
                                      <div>
                                        {questions.length > 1 && activeIdx < questions.length - 1 ? (
                                          <Button
                                            variant="primary"
                                            onClick={() => {
                                              setActiveQuestionIndexBySection((prev) => ({
                                                ...prev,
                                                [section]: Math.min(questions.length - 1, activeIdx + 1),
                                              }));
                                            }}
                                            aria-label="Next question"
                                          >
                                            Next Question
                                            <ArrowRight className="ml-1.5 h-4 w-4" aria-hidden="true" />
                                          </Button>
                                        ) : (
                                          <Button
                                            variant="primary"
                                            onClick={() => {
                                              if (isLastSection) {
                                                setShowSubmitConfirm(true);
                                              } else {
                                                setShowNextConfirm(true);
                                              }
                                            }}
                                            disabled={isAdvancingPhase}
                                          >
                                            {isLastSection ? 'Finish & Submit' : 'Next Section'}
                                            <ArrowRight className="ml-1.5 h-4 w-4" aria-hidden="true" />
                                          </Button>
                                        )}
                                      </div>
                                    </div>
                                  </div>
                                </div>
                              );
                            })()}
                          </section>
                        ))}
                      </div>
                  </>
                </>
              )}
            </div>

            <div className="flex items-center justify-between gap-3 pt-4">
              <p className="text-xs text-muted">
              {shouldSlicePartB
                ? 'Part B shows one question at a time. Each short extract plays once; after it ends, Next is irreversible.'
                : currentSection === 'B'
                  ? 'Part B — 6 short workplace extracts. Audio shares one file; all questions are visible and remain editable until you submit or advance.'
                  : currentSection === 'C1' || currentSection === 'C2'
                    ? 'Part C — 12 questions across Extracts 1 and 2. The extracts play in order; all Part C question cards remain available without restarting audio.'
                  : 'Audio plays once per section and cannot be paused, scrubbed, or replayed. When it ends, confirm the irreversible boundary before the next section opens.'}
              </p>
              {shouldSlicePartB && phase === 'audio' && partBQuestionAudioEnded && !audioValidityHeld ? (
                <Button
                  variant="primary"
                  onClick={() => setShowNextConfirm(true)}
                  disabled={isAdvancingPhase}
                >
                  {currentPartBQuestionIndex + 1 < (sectionGroups?.B?.length ?? 0) ? 'Next question' : 'Finish Part B'}
                </Button>
              ) : null}
            </div>

            {/* Forward-only lock confirmation */}
            <Modal
              open={showNextConfirm}
              onClose={() => setShowNextConfirm(false)}
              title={shouldSlicePartB
                ? currentPartBQuestionIndex + 1 < (sectionGroups?.B?.length ?? 0) ? 'Continue to the next Part B question?' : 'Lock Part B and continue?'
                : phase === 'review' ? 'Lock this section and continue?' : currentSectionReviewSeconds > 0 ? 'Open review window?' : 'Lock Part B and continue?'}
              size="sm"
            >
              <div className="space-y-4">
                <p className="text-sm text-muted">
                  {shouldSlicePartB
                    ? `This will permanently lock question ${activePartBQuestion?.number ?? ''}. You will not be able to return to it. ${currentPartBQuestionIndex + 1 < (sectionGroups?.B?.length ?? 0) ? 'The next short extract will start automatically.' : 'Part C will open next.'}`
                    : phase === 'review'
                    ? `This will permanently lock ${currentSection ? LISTENING_SECTION_LABEL[currentSection] : 'this section'}. You will not be able to return to it at any point.`
                    : currentSectionReviewSeconds > 0
                      ? `Starts the ${currentSectionReviewSeconds}-second review window for ${currentSection ? LISTENING_SECTION_LABEL[currentSection] : 'this section'}. Answer boxes stay editable for this section only during the window.`
                      : `${currentSection ? LISTENING_SECTION_LABEL[currentSection] : 'This section'} has no review window. Continuing will lock it permanently and open the next section.`}
                </p>
                {currentSectionUnansweredNumbers.length > 0 ? (
                  <InlineAlert variant="warning">
                    This section still has {currentSectionUnansweredNumbers.length} unanswered question{currentSectionUnansweredNumbers.length === 1 ? '' : 's'}: {currentSectionUnansweredList}. Locked unanswered items will score zero.
                  </InlineAlert>
                ) : null}
                <div className="flex justify-end gap-3">
                  <Button variant="outline" onClick={() => setShowNextConfirm(false)}>Keep listening</Button>
                  <Button
                    disabled={isAdvancingPhase}
                    onClick={async () => {
                      setShowNextConfirm(false);
                      if (shouldSlicePartB) {
                        await advancePartBQuestion();
                      } else if (phase === 'review') {
                        await advanceFromReview();
                      } else {
                        await confirmNextFromAudio();
                      }
                    }}
                  >
                    {shouldSlicePartB
                      ? currentPartBQuestionIndex + 1 < (sectionGroups?.B?.length ?? 0) ? 'Lock & start next' : 'Lock & continue'
                      : phase === 'review' ? 'Lock & continue' : currentSectionReviewSeconds > 0 ? 'Open review window' : 'Lock & continue'}
                  </Button>
                </div>
              </div>
            </Modal>

            <Modal open={showSubmitConfirm} onClose={() => setShowSubmitConfirm(false)} title="Submit listening task?" size="sm">
              <div className="space-y-4">
                <p className="text-sm text-muted">
                  Are you sure you want to submit this exam? You will not be able to continue this attempt after submission.
                </p>
                {unansweredQuestionNumbers.length > 0 ? (
                  <InlineAlert variant="warning">
                    {unansweredQuestionNumbers.length} unanswered question{unansweredQuestionNumbers.length === 1 ? '' : 's'} will score zero if you submit now: {unansweredQuestionList}.
                  </InlineAlert>
                ) : null}
                <div className="flex justify-end gap-3">
                  <Button variant="outline" onClick={() => setShowSubmitConfirm(false)}>
                    Keep reviewing
                  </Button>
                  <Button
                    onClick={async () => {
                      setShowSubmitConfirm(false);
                      await handleSubmit();
                    }}
                  >
                    Submit now
                  </Button>
                </div>
              </div>
            </Modal>

            {/* Autoplay fallback modal */}
            <Modal
              open={showAutoplayModal}
              onClose={() => setShowAutoplayModal(false)}
              title="Click to Start Audio"
            >
              <div className="space-y-4">
                <p className="text-sm leading-6 text-muted">
                  Your browser requires a user interaction to begin audio playback. Click the button below to start your Listening audio.
                </p>
                <div className="flex justify-end">
                  <Button
                    variant="primary"
                    onClick={() => {
                      setShowAutoplayModal(false);
                      // A button click is an explicit browser gesture. Clear
                      // the failed automatic key so this click can retry even
                      // when the source/section has not changed.
                      autoPlayAttemptedKeyRef.current = null;
                      tryAutoPlay();
                    }}
                  >
                    <Volume2 className="mr-2 h-4 w-4" aria-hidden="true" />
                    Start Audio
                  </Button>
                </div>
              </div>
            </Modal>
          </motion.div>
        )}

      </div>
    </AppShell>
    </ListeningPlayerSkinShell>
  );
}

export default function ListeningPlayer() {
  return (
    <Suspense fallback={
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="flex flex-1 items-center justify-center">
          <Loader2 className="h-8 w-8 animate-spin text-primary" />
        </div>
      </AppShell>
    }>
      <PlayerContent />
    </Suspense>
  );
}
