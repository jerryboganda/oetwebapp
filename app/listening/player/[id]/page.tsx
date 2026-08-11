'use client';

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, CheckCircle2, Loader2, Volume2 } from 'lucide-react';
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
import { ContentLockedNotice, isContentLockedError, readContentLockedMessage } from '@/components/domain/ContentLockedNotice';
import { InsufficientCreditsModal, isInsufficientCreditsError, readInsufficientCreditsMessage } from '@/components/domain/InsufficientCreditsModal';
import { BCQuestionRenderer } from '@/components/domain/listening/BCQuestionRenderer';
import { PartARenderer } from '@/components/domain/listening/PartARenderer';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';
import { PartAPdfOverlayDocument } from '@/components/domain/listening/PartAPdfOverlayDocument';
import type { PartAOverlayBlank } from '@/components/domain/listening/admin/PartAPdfOverlayEditor';
import { ListeningQuestionPaperViewer } from '@/components/domain/listening/ListeningQuestionPaperViewer';
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
import { listeningV2Api, type AdvanceResult, type ListeningV2SessionState } from '@/lib/listening/v2-api';
import { buildTechReadinessProbe } from '@/lib/listening/tech-readiness-probe';
import { presentationModeFromSession } from '@/lib/listening/modes';
import { ListeningPlayerSkinShell } from '@/components/domain/listening/player/skins/ListeningPlayerSkinShell';
import { useListeningAnnotations, type ListeningQuestionAnnotation } from '@/hooks/use-listening-annotations';

const FIRST_STRICT_STATE: ListeningFsmState = 'a1_preview';

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

// A Part B/C question card is "authored inline" when its stem is real prose (not
// the "See PDF" sentinel) AND every option is real prose (not the generic
// "Option A/B/C" placeholder). Once every MCQ question in a section is inline, the
// question-paper PDF is redundant and is hidden for that section (Part A unaffected).
function listeningQuestionIsInlineAuthored(question: { text: string; options: string[] }): boolean {
  const stem = question.text.trim().toLowerCase();
  const stemOk = stem.length > 0 && stem !== 'see pdf';
  const optionsOk = question.options.length > 0
    && question.options.every((option, index) => {
      const normalized = option.trim().toLowerCase();
      return normalized.length > 0 && normalized !== `option ${String.fromCharCode(97 + index)}`;
    });
  return stemOk && optionsOk;
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
  const [showNextConfirm, setShowNextConfirm] = useState(false);
  // C8d — whole-attempt 40-minute countdown driven by attempt.expiresAt.
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
  }, [attemptIdFromRoute, id, mode, pathwayStage]);

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
    if (!attempt?.attemptId || !session?.modePolicy.integrityLockRequired) return;
    void recordListeningIntegrityEvent(attempt.attemptId, eventType, details).catch(() => undefined);
  }, [attempt?.attemptId, session?.modePolicy.integrityLockRequired]);

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
      (error instanceof DOMException && error.name === 'AbortError')
      || message.includes('play() request was interrupted')
    ) {
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

    const latest = await getListeningSession(id, { mode, attemptId: attempt.attemptId });
    if (latest.attempt?.requiresAdminReview) {
      await markAttemptConflict(queued.id);
      setAudioValidityHeld(true);
      setIntegrityWarning('This Listening attempt is on hold because audio playback failed and has been flagged for administrator review. Do not replay the scored audio.');
      return true;
    }

    const serverValue = latest.attempt?.answers?.[payload.questionId] ?? null;
    const decision = reconcileOfflineAnswer(serverValue, {
      questionId: payload.questionId,
      value: payload.value,
      baseValue: typeof payload.baseValue === 'string' ? payload.baseValue : null,
    });

    if (decision === 'already-synced') {
      await markAttemptSynced(queued.id);
      serverAnswersRef.current[payload.questionId] = payload.value;
      delete answerBaseValuesRef.current[payload.questionId];
      setSaveState('saved');
      return true;
    }

    if (decision === 'conflict') {
      await markAttemptConflict(queued.id);
      serverAnswersRef.current[payload.questionId] = serverValue;
      delete answerBaseValuesRef.current[payload.questionId];
      setAnswers((current) => current[payload.questionId] === payload.value
        ? { ...current, [payload.questionId]: serverValue ?? '' }
        : current);
      setSaveState('conflict');
      return true;
    }

    try {
      await listeningV2Api.saveAnswer(attempt.attemptId, payload.questionId, payload.value);
      await markAttemptSynced(queued.id);
      serverAnswersRef.current[payload.questionId] = payload.value;
      delete answerBaseValuesRef.current[payload.questionId];
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
    if (!attempt?.attemptId || !hasStarted || !session?.modePolicy.integrityLockRequired) return;

    const onFullscreenChange = () => {
      if (!document.fullscreenElement) {
        setIntegrityWarning('Full-screen was exited. This has been recorded for the OET@Home attempt.');
        logIntegrityEvent('fullscreen_exit');
      }
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
  }, [attempt?.attemptId, hasStarted, logIntegrityEvent, session?.modePolicy.integrityLockRequired]);

  const ensureAttempt = async () => {
    if (!session) throw new Error('Listening session is not ready.');
    if (attempt) return attempt;
    const started = await startListeningAttempt(session.paper.id, mode, { pathwayStage, mockAttemptId, mockSectionId });
    setAttempt(started);
    if (id && mockAttemptId && mockSectionId && !attemptIdFromRoute) {
      const nextParams = new URLSearchParams(searchParams?.toString());
      nextParams.set('attemptId', started.attemptId);
      router.replace(`/listening/player/${encodeURIComponent(id)}?${nextParams.toString()}`);
    }
    return started;
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
        await advanceStrictStart(started.attemptId);
      }
      if (session.modePolicy.integrityLockRequired && rootRef.current && !document.fullscreenElement) {
        try {
          await rootRef.current.requestFullscreen();
          void recordListeningIntegrityEvent(started.attemptId, 'fullscreen_enter', 'entered_before_audio').catch(() => undefined);
        } catch {
          setIntegrityWarning('Full-screen could not be started by this browser. This has been recorded for the OET@Home attempt.');
          void recordListeningIntegrityEvent(started.attemptId, 'fullscreen_request_failed').catch(() => undefined);
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
      // C8f — do NOT auto-play here. Entering hasStarted triggers the
      // currentSection effect, which drops into the pre-audio reading
      // window. Audio play() fires when the preview countdown hits zero.
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
  const currentExtracts = currentSection
    ? extracts.filter((extract) => extract.partCode === currentSection || (currentSection === 'B' && extract.partCode === 'B'))
    : [];
  const visibleExtracts = currentExtracts;
  // Learner-facing question-paper PDF for the current section. Per-part map is
  // keyed by uppercased part/section code; resolve exact section code first,
  // then fall back to the parent part letter (mirrors the Reading PDF viewer).
  const currentQuestionPaperUrl = (() => {
    const map = session?.paper.questionPaperUrlByPart;
    if (!map || !currentSection) return null;
    const code = String(currentSection).toUpperCase();
    return map[code] ?? map[code.charAt(0)] ?? null;
  })();
  // A Part A consultation authored as a note-completion document is its own
  // answer surface, so a missing question-paper PDF there is expected — we don't
  // show an empty-state for those. Every other section shows a friendly
  // "no question paper yet" message when its PDF slot is empty (content is
  // never compulsory, so a published paper may legitimately omit it).
  const currentSectionHasNotes =
    (currentSection === 'A1' || currentSection === 'A2')
    && extracts.some((e) => e.partCode === currentSection && (e.notesBody?.trim().length ?? 0) > 0);
  // Drop the question-paper PDF for a Part B/C section once every MCQ question in
  // it is authored inline (stem + options). A section with any placeholder question
  // keeps the PDF as a fallback. Part A (notes / PDF-overlay) is never gated here.
  const currentSectionMcqs = (currentSection ? sectionGroups?.[currentSection] ?? [] : [])
    .filter((question) => question.options.length > 0);
  const currentSectionInlineBcReady =
    currentSectionMcqs.length > 0 && currentSectionMcqs.every(listeningQuestionIsInlineAuthored);
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
  const activeExtract = visibleExtracts[0] ?? null;
  // Per-section files have their own timeline, so the authored cue windows
  // (offsets into the old combined file) don't apply — the whole file is the
  // section. Drop them so we never seek to a stale offset or force-pause mid-file.
  const currentExtractWindows = usingPerSectionAudio ? [] : currentExtracts.filter((extract) => (
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
  const activeAudioStartMs = usingPerSectionAudio
    ? null
    : currentSectionAudioStartMs;
  const activeAudioEndMs = usingPerSectionAudio
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
  const audioGateSatisfied = usingPerSectionAudio
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
      setHasStarted(false);
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
      applyStrictServerState(state);
      return true;
    } catch (err) {
      setAudioError(err instanceof Error ? err.message : 'The server rejected this Listening transition.');
      return false;
    } finally {
      strictAdvanceTargetRef.current = null;
      setIsAdvancingPhase(false);
    }
  }, [applyStrictServerState, attempt?.attemptId, attemptIdFromRoute, strictReadinessRequired]);

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
  // countdown (owner directive 2026-07-05) — the learner starts each section's
  // audio with the transport Play button. Strict @home exams keep the
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
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [currentSection, hasStarted]);

  const advanceToNextSection = () => {
    // Reset phase off 'review' BEFORE the section index changes. Otherwise
    // the auto-advance effect (which fires whenever phase==='review' &&
    // reviewSecondsRemaining===0) sees the new section's currentSection on
    // re-render and immediately calls advanceFromReview again — which on
    // the last section triggers handleSubmit and skips the section entirely.
    setPhase('audio');
    setReviewSecondsRemaining(0);
    setCurrentSectionIndex((value) => value + 1);
    // The currentSection effect above re-enters preview for the new section.
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

  // Auto-advance when countdown hits zero.
  useEffect(() => {
    if (phase === 'review' && reviewSecondsRemaining === 0 && hasStarted && currentSection !== null) {
      void advanceFromReview();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase, reviewSecondsRemaining, hasStarted, currentSection]);

  // Audio is non-pausable, so a section completing is the advance trigger.
  // Legacy single-file (cue-point) papers never fire the <audio> `ended` event
  // mid-paper — a section is "done" once all its extracts cross their
  // audioEndMs. When that happens, jump straight to the next section.
  // Per-section-audio papers advance via the <audio> onEnded handler instead.
  useEffect(() => {
    if (phase !== 'audio' || !hasStarted) return;
    if (usingPerSectionAudio) return;
    if (currentSectionAudioEndMs == null) return;
    if (!allCurrentExtractsCompleted) return;
    void autoAdvanceAfterAudio();
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase, hasStarted, usingPerSectionAudio, currentSectionAudioEndMs, allCurrentExtractsCompleted]);

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
      const audio = audioRef.current;
      if (audio) {
        const result = audio.play();
        if (result && typeof result.catch === 'function') {
          result.catch(handlePlaybackFailure);
        }
      }
    })();
  }, [advanceStrictPhaseIfNeeded, logAttemptEvent, phase, previewSecondsRemaining, hasStarted, currentSection]);

  // C8d — whole-attempt 40-minute countdown. Driven by attempt.expiresAt.
  // Auto-submits in exam/home modes (canScrub === false) when the timer
  // hits zero. Practice mode just shows "Time up" without submitting.
  useEffect(() => {
    const expiresAt = attempt?.expiresAt;
    if (!expiresAt || !hasStarted) {
      setAttemptSecondsRemaining(null);
      return;
    }
    const compute = () => {
      const remaining = Math.max(0, Math.floor((Date.parse(expiresAt) - Date.now()) / 1000));
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
      return;
    }
    if (/^https?:\/\//i.test(url)) {
      setResolvedAudioSrc(url);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    setResolvedAudioSrc(null);
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

  // Audio is non-pausable in every mode, so a section's audio reaching its end
  // is the sole advance trigger — there is no manual "Next" / review window.
  // Fired from the <audio> `onEnded` handler. Idempotent via
  // `autoAdvanceInFlightRef` (reset when the section changes).
  const autoAdvanceAfterAudio = async () => {
    if (audioValidityHeld) return;
    if (!currentSection) return;
    if (autoAdvanceInFlightRef.current) return;
    autoAdvanceInFlightRef.current = true;
    if (isLastSection) {
      // Terminal: submit and leave the latch set (no section change will reset
      // it) so a near-simultaneous cue-end + file-`ended` can't double-submit.
      void handleSubmit();
      return;
    }
    try {
      const nextSection = sectionsInPaper[currentSectionIndex + 1];
      if (!nextSection) return;
      if (strictServerNavigationActive) {
        // Strict server FSM is linear (a1_audio → a1_review → a2_preview). Hop
        // through the section's review state (skipped for Part B, which has
        // none) before the next section's preview so the confirm-token FSM
        // stays in sync. `applyStrictServerState` (inside the helper) drives
        // local phase/section, so we must NOT also call advanceToNextSection.
        const reviewState = listeningStateForPosition(currentSection, 'review');
        if (reviewState) {
          const advancedReview = await advanceStrictPhaseIfNeeded(reviewState);
          if (!advancedReview) return;
        }
        const nextState = listeningStateForPosition(nextSection, 'preview');
        if (nextState) {
          const advancedPreview = await advanceStrictPhaseIfNeeded(nextState);
          if (!advancedPreview) return;
        }
        return;
      }
      // Non-strict (practice / learning): no server FSM — advance locally. The
      // currentSection effect re-enters the next section's reading window.
      advanceToNextSection();
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
    ? (sectionGroups?.[currentSection] ?? [])
      .filter((question) => (answers[question.id] ?? '').trim().length === 0)
      .map((question) => question.number)
      .sort((a, b) => a - b)
    : [];
  const currentSectionUnansweredList = formatQuestionNumberList(currentSectionUnansweredNumbers);
  const navigationQuestions = currentSection ? sectionGroups?.[currentSection] ?? [] : [];
  const visibleQuestionSections = currentSection
    ? [{ section: currentSection, questions: sectionGroups?.[currentSection] ?? [] }]
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
            const startMs = activeAudioStartMs;
            const endMs = activeAudioEndMs;
            if (
              session?.modePolicy.canScrub === false
              && startMs != null
              && endMs != null
              && endMs > startMs
              && now * 1000 >= endMs
              && !audio.paused
            ) {
              hasReachedEndRef.current = true;
              allowedPauseRef.current = true;
              audio.pause();
            }
            // C8c — mark this extract as completed once its audioEndMs is
            // crossed (regardless of mode), so the section panel renders a
            // checkmark next to the row.
            const completedNow = visibleExtracts.filter((extract) => (
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
            if (!audioRef.current) return;
            setDuration(audioRef.current.duration);
            const startMs = activeAudioStartMs;
            const endMs = activeAudioEndMs;
            if ((phase === 'audio' || phase === 'preview') && startMs != null && endMs != null && endMs > startMs) {
              seekAudioTo(startMs / 1000);
            }
            setAudioState('ready');
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
          }}
          onPlay={() => {
            if (audioValidityHeld) {
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
            // Audio is non-pausable, so reaching the end is the advance signal:
            // jump straight to the next section (or submit on the last).
            void autoAdvanceAfterAudio();
          }}
          onError={() => {
            flagAudioFailure('Audio failed to load. This attempt has been halted and flagged for administrator review; do not replay the scored audio.');
          }}
          preload="metadata"
        />
      ) : null}

      <div ref={rootRef} className="mx-auto max-w-3xl px-4 py-8 pb-24 sm:px-6 lg:px-8">
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
              onTogglePlayPause={togglePlayPause}
              onScrub={handleScrub}
            />

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
                              const element = document.getElementById(`listening-question-${question.id}`);
                              element?.scrollIntoView({ behavior: 'smooth', block: 'start' });
                              const input = document.getElementById(`listening-answer-${question.id}`);
                              (input as HTMLInputElement | null)?.focus();
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

                      {currentQuestionPaperUrl && !currentSectionInlineBcReady ? (
                        <ListeningQuestionPaperViewer
                          url={currentQuestionPaperUrl}
                          partLabel={currentSection}
                        />
                      ) : (currentSectionHasNotes || currentSectionInlineBcReady) ? null : (
                        <div
                          data-testid="listening-question-paper-empty"
                          className="rounded-2xl border border-dashed border-border bg-surface px-4 py-5 text-center text-sm text-muted"
                        >
                          No question paper has been added for this part yet.
                        </div>
                      )}

                      <div data-testid="listening-question-surface" className="space-y-6" style={{ fontSize: `${questionZoomPercent}%` }}>
                        {visibleQuestionSections.map(({ section, questions }) => (
                          <section key={section} className="space-y-4" aria-label={LISTENING_SECTION_LABEL[section]}>
                            {(() => {
                              // Part A1 / A2 with an authored notes body → ONE continuous note-completion document.
                              if (section === 'A1' || section === 'A2') {
                                const extract = extracts.find((e) => e.partCode === section);
                                // PDF-overlay method (Method C): render the question-paper PDF
                                // with fill-in inputs positioned at the authored blanks.
                                if (extract?.authoringMethod === 'pdf_overlay' && extract.partAOverlayBlanksJson) {
                                  let blanks: PartAOverlayBlank[] = [];
                                  try {
                                    blanks = (JSON.parse(extract.partAOverlayBlanksJson) as PartAOverlayBlank[]) ?? [];
                                  } catch {
                                    blanks = [];
                                  }
                                  const pdfPath =
                                    session?.paper.questionPaperUrlByPart?.[section]
                                    ?? session?.paper.questionPaperUrlByPart?.['A']
                                    ?? session?.paper.questionPaperUrl
                                    ?? null;
                                  return (
                                    <PartAPdfOverlayDocument
                                      pdfDownloadPath={pdfPath}
                                      blanks={blanks}
                                      questions={questions.map((q) => ({ id: q.id, number: q.number }))}
                                      answers={answers}
                                      onAnswerChange={handleAnswerChange}
                                      locked={false}
                                    />
                                  );
                                }
                                if (extract?.notesBody?.trim()) {
                                  const canEdit = true;
                                  return (
                                    <PartANotesDocument
                                      partLabel={LISTENING_SECTION_LABEL[section]}
                                      notesBody={extract.notesBody}
                                      questions={questions.map((q) => ({ id: q.id, number: q.number }))}
                                      answers={answers}
                                      onAnswerChange={handleAnswerChange}
                                      locked={!canEdit}
                                    />
                                  );
                                }
                              }

                              // Legacy Part A (no notesBody) or Part B/C → per-question renderer.
                              // Track the last rendered extract context so a Part C
                              // presentation shows its scenario line once (above its
                              // first card) while Part B shows each clip's own line.
                              let lastContext: string | null = null;
                              return questions.map((question) => {
                                const canEdit = true;
                                if (question.options.length === 0) {
                                  return (
                                    <div id={`listening-question-${question.id}`} key={question.id} className="scroll-mt-48">
                                      <PartARenderer
                                        questionNumber={question.number}
                                        partLabel={LISTENING_SECTION_LABEL[section]}
                                        prompt={question.text}
                                        inputId={`listening-answer-${question.id}`}
                                        value={answers[question.id] ?? ''}
                                        onChange={(value) => handleAnswerChange(question.id, value)}
                                        locked={!canEdit}
                                      />
                                    </div>
                                  );
                                }

                                const questionExtract = extracts.find((e) => e.partCode === question.partCode);
                                const contextIntro = questionExtract?.contextIntro?.trim() || null;
                                const showContext = Boolean(contextIntro) && contextIntro !== lastContext;
                                if (contextIntro) lastContext = contextIntro;

                                return (
                                  <div id={`listening-question-${question.id}`} key={question.id} className="scroll-mt-48">
                                    {showContext ? (
                                      <p
                                        data-testid="listening-extract-context"
                                        className="mb-3 rounded-2xl border border-border bg-background-light px-4 py-3 text-sm font-medium leading-relaxed text-navy"
                                      >
                                        {contextIntro}
                                      </p>
                                    ) : null}
                                    <BCQuestionRenderer
                                      questionNumber={question.number}
                                      partLabel={LISTENING_SECTION_LABEL[section]}
                                      prompt={question.text}
                                      options={question.options}
                                      optionKeys={question.optionKeys}
                                      value={answers[question.id] ?? ''}
                                      onChange={(value) => handleAnswerChange(question.id, value)}
                                      annotation={annotations.state.byQuestion[question.id] ?? {}}
                                      onAnnotationChange={(mutator) => handleAnnotationChange(question.id, mutator)}
                                      locked={!canEdit}
                                    />
                                  </div>
                                );
                              });
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
                Audio plays once per section and cannot be paused, scrubbed, or replayed. The next section starts automatically when the audio ends.
              </p>
            </div>

            {/* Forward-only lock confirmation */}
            <Modal
              open={showNextConfirm}
              onClose={() => setShowNextConfirm(false)}
              title={phase === 'review' ? 'Lock this section and continue?' : currentSectionReviewSeconds > 0 ? 'Open review window?' : 'Lock Part B and continue?'}
              size="sm"
            >
              <div className="space-y-4">
                <p className="text-sm text-muted">
                  {phase === 'review'
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
                      if (phase === 'review') {
                        await advanceFromReview();
                      } else {
                        await confirmNextFromAudio();
                      }
                    }}
                  >
                    {phase === 'review' ? 'Lock & continue' : currentSectionReviewSeconds > 0 ? 'Open review window' : 'Lock & continue'}
                  </Button>
                </div>
              </div>
            </Modal>

            <Modal open={showSubmitConfirm} onClose={() => setShowSubmitConfirm(false)} title="Submit listening task?" size="sm">
              <div className="space-y-4">
                <p className="text-sm text-muted">
                  Submit your answers now? This locks the attempt and opens server-graded OET score plus transcript-backed review.
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
