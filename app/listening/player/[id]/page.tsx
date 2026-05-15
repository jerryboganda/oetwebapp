'use client';

import { Suspense, useCallback, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, CheckCircle2, ChevronRight, Loader2, Volume2 } from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
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
  type ListeningSessionDto,
} from '@/lib/listening-api';
import {
  LISTENING_PREVIEW_SECONDS,
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
import { BCQuestionRenderer } from '@/components/domain/listening/BCQuestionRenderer';
import { PartARenderer } from '@/components/domain/listening/PartARenderer';
import { ZoomControls } from '@/components/domain/listening/ZoomControls';
import { ListeningIntroCard } from '@/components/domain/listening/player/ListeningIntroCard';
import { ListeningAudioTransport } from '@/components/domain/listening/player/ListeningAudioTransport';
import { ListeningSectionStepper } from '@/components/domain/listening/player/ListeningSectionStepper';
import { ListeningPreviewBanner, ListeningReviewBanner } from '@/components/domain/listening/player/ListeningPhaseBanner';
import { completeMockSection } from '@/lib/api';
import { resolveBlockedSeekTarget, shouldResumeAfterBlockedPause } from '@/lib/listening/audio-integrity';
import { listeningV2Api, type AdvanceResult, type ListeningV2SessionState } from '@/lib/listening/v2-api';

const FIRST_STRICT_STATE: ListeningFsmState = 'a1_preview';

type ListeningPlayerMode = 'practice' | 'exam' | 'home' | 'paper' | 'diagnostic';

function formatTime(seconds: number) {
  if (!seconds || Number.isNaN(seconds)) return '00:00';
  const minutes = Math.floor(seconds / 60);
  const secs = Math.floor(seconds % 60);
  return `${minutes.toString().padStart(2, '0')}:${secs.toString().padStart(2, '0')}`;
}

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function handleAudioPlaybackError(error: unknown, onVisibleError: (message: string) => void) {
  const message = error instanceof Error ? error.message : String(error);
  if (
    (error instanceof DOMException && error.name === 'AbortError')
    || message.includes('play() request was interrupted')
  ) {
    return;
  }
  onVisibleError('Audio could not start. Check the device output, reload the audio, and try again.');
}

function formatMilliseconds(value: number | null | undefined) {
  if (value == null) return null;
  const seconds = Math.floor(value / 1000);
  return formatTime(seconds);
}

function formatQuestionNumberList(numbers: number[]) {
  return numbers.map((number) => `Q${number}`).join(', ');
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
  if (rawMode === 'exam' || rawMode === 'home' || rawMode === 'paper' || rawMode === 'diagnostic') return rawMode;
  const normalizedDelivery = deliveryMode.trim().toLowerCase();
  if (normalizedDelivery === 'paper') return 'paper';
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

async function advanceStrictTransition(attemptId: string, toState: ListeningFsmState): Promise<ListeningV2SessionState> {
  const first = await listeningV2Api.advance(attemptId, toState, null);
  if (first.outcome === 'applied') return first.state ?? listeningV2Api.getState(attemptId);
  if (first.outcome === 'confirm-required' && first.confirmToken) {
    const confirmed = await listeningV2Api.advance(attemptId, toState, first.confirmToken);
    if (confirmed.outcome === 'applied') return confirmed.state ?? listeningV2Api.getState(attemptId);
    throw new Error(advanceRejectionMessage(confirmed));
  }
  throw new Error(advanceRejectionMessage(first));
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
  const audioResumeInFlightRef = useRef<boolean>(false);
  const applyStrictServerStateRef = useRef<((state: ListeningV2SessionState) => void) | null>(null);
  const strictAdvanceTargetRef = useRef<ListeningFsmState | null>(null);
  const [session, setSession] = useState<ListeningSessionDto | null>(null);
  const [attempt, setAttempt] = useState<ListeningAttemptDto | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [loadingTask, setLoadingTask] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [contentLockedMessage, setContentLockedMessage] = useState<string | null>(null);
  const [audioError, setAudioError] = useState<string | null>(null);
  const [integrityWarning, setIntegrityWarning] = useState<string | null>(null);
  const [audioResumeWarning, setAudioResumeWarning] = useState<string | null>(null);
  const [audioState, setAudioState] = useState<'idle' | 'buffering' | 'ready' | 'error'>('idle');
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
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
        setAnswers(Object.fromEntries(Object.entries(data.attempt?.answers ?? {}).map(([key, value]) => [key, value ?? ''])));
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

  const logIntegrityEvent = useCallback((eventType: string, details?: string) => {
    if (!attempt?.attemptId || !session?.modePolicy.integrityLockRequired) return;
    void recordListeningIntegrityEvent(attempt.attemptId, eventType, details).catch(() => undefined);
  }, [attempt?.attemptId, session?.modePolicy.integrityLockRequired]);

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
            playResult.catch((err) => handleAudioPlaybackError(err, setAudioError));
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
      if (strictReadinessRequired) {
        if (!readinessSnapshot?.audioOk) {
          throw new Error('Complete the audio readiness check before starting this strict Listening attempt.');
        }
        await listeningV2Api.recordTechReadiness(started.attemptId, readinessSnapshot);
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
      setStartError(err instanceof Error ? err.message : 'Could not start this Listening attempt.');
    } finally {
      setIsStarting(false);
    }
  };

  const togglePlayPause = () => {
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
    else audioRef.current.play().catch((err) => handleAudioPlaybackError(err, setAudioError));
  };

  const handleScrub = (event: React.ChangeEvent<HTMLInputElement>) => {
    if (!session?.modePolicy.canScrub) return;
    const newTime = Number(event.target.value);
    if (!audioRef.current) return;
    audioRef.current.currentTime = newTime;
    setProgress(newTime);
  };

  const persistAnswer = (questionId: string, value: string, currentAttempt: ListeningAttemptDto | null) => {
    if (!currentAttempt) return;
    clearTimeout(saveTimers.current[questionId]);
    setSaveState('saving');
    saveTimers.current[questionId] = setTimeout(() => {
      listeningV2Api.saveAnswer(currentAttempt.attemptId, questionId, value)
        .then(() => setSaveState('saved'))
        .catch(() => setSaveState('error'));
    }, 500);
  };

  const handleAnswerChange = (questionId: string, answer: string) => {
    setAnswers((current) => ({ ...current, [questionId]: answer }));
    persistAnswer(questionId, answer, attempt);
  };

  const handleSubmit = async ({ skipFinalSave = false }: { skipFinalSave?: boolean } = {}) => {
    if (!session) return;
    // Set the synchronous gate before any await so the 15s heartbeat tick
    // cannot race a Submitted attempt and trip the 409 diagnostics check.
    isSubmittingRef.current = true;
    setIsSubmitting(true);
    pauseAudio();
    for (const timer of Object.values(saveTimers.current)) clearTimeout(timer);
    saveTimers.current = {};
    try {
      const activeAttempt = await ensureAttempt();
      if (!skipFinalSave) {
        await Promise.all(Object.entries(answers).map(([questionId, answer]) => listeningV2Api.saveAnswer(activeAttempt.attemptId, questionId, answer)));
      }
      const result = await listeningV2Api.submit(activeAttempt.attemptId, answers);
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
    return LISTENING_SECTION_SEQUENCE.filter((code) => sectionGroups[code].length > 0);
  }, [sectionGroups]);
  const currentSection: ListeningSectionCode | null = sectionsInPaper[currentSectionIndex] ?? null;
  const freeNavigationEnabled = session?.modePolicy.freeNavigation === true;
  const allPartsReviewEnabled = freeNavigationEnabled && session?.modePolicy.printableBooklet === true;
  const paperFinalReviewSeconds = session?.modePolicy.finalReviewAllPartsSeconds ?? null;
  const paperFinalReviewActive = allPartsReviewEnabled
    && paperFinalReviewSeconds !== null
    && attemptSecondsRemaining !== null
    && attemptSecondsRemaining <= paperFinalReviewSeconds;
  const currentExtracts = currentSection
    ? extracts.filter((extract) => extract.partCode === currentSection || (currentSection === 'B' && extract.partCode === 'B'))
    : [];
  const visibleExtracts = allPartsReviewEnabled ? extracts : currentExtracts;
  const activeExtract = visibleExtracts[0] ?? null;
  const currentExtractWindows = currentExtracts.filter((extract) => (
    extract.audioStartMs != null
    && extract.audioEndMs != null
    && extract.audioEndMs > extract.audioStartMs
  ));
  const allExtractWindows = extracts.filter((extract) => (
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
  const paperAudioStartMs = allExtractWindows.length > 0
    ? Math.min(...allExtractWindows.map((extract) => extract.audioStartMs!))
    : null;
  const paperAudioEndMs = allExtractWindows.length > 0
    ? Math.max(...allExtractWindows.map((extract) => extract.audioEndMs!))
    : null;
  const activeAudioStartMs = allPartsReviewEnabled ? paperAudioStartMs : currentSectionAudioStartMs;
  const activeAudioEndMs = allPartsReviewEnabled ? paperAudioEndMs : currentSectionAudioEndMs;
  const isLastSection = currentSection !== null && currentSectionIndex >= sectionsInPaper.length - 1;
  const currentSectionReviewSeconds = currentSection ? LISTENING_REVIEW_SECONDS[currentSection] : 0;
  const currentSectionPreviewSeconds = currentSection ? LISTENING_PREVIEW_SECONDS[currentSection] : 0;
  const canSkipPreview = session?.modePolicy.mode === 'practice';
  const allCurrentExtractsCompleted = currentExtracts.every((extract) => {
    if (extract.audioEndMs == null) return true;
    return completedExtractIds.has(`${extract.partCode}-${extract.displayOrder}`);
  });
  const canOpenReviewWindow = Boolean(
    currentSection
    && (allPartsReviewEnabled || session?.modePolicy.canScrub !== false || allCurrentExtractsCompleted || currentSectionAudioEndMs == null),
  );
  const strictServerNavigationActive = strictReadinessRequired && Boolean(attempt?.attemptId ?? attemptIdFromRoute);

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
      setStrictServerState(null);
      setHasStarted(false);
      return;
    }
    if (!strictReadinessRequired) {
      setStrictServerState(null);
      setHasStarted(true);
      return;
    }
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
        applyStrictServerState(state);
        setHasStarted(true);
      })
      .catch(() => {
        if (!alive) return;
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

  // C8f — when entering a new section after the player has started, drop into
  // the preview phase with the section's reading window. Skipped entirely
  // when the section has no preview seconds authored (e.g. legacy data).
  useEffect(() => {
    if (!hasStarted || !currentSection) return;
    if (allPartsReviewEnabled) return;
    if (strictReadinessRequired && strictServerState) return;
    if (phase === 'review') return;
    // Reset per-section forward-only end-of-extract latch.
    hasReachedEndRef.current = false;
    if (currentSectionPreviewSeconds > 0) {
      previewArmedRef.current = true;
      setPhase('preview');
      setPreviewSecondsRemaining(currentSectionPreviewSeconds);
    } else {
      previewArmedRef.current = false;
      setPhase('audio');
    }
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

  // C8f — when the preview countdown hits zero, transition to audio and
  // trigger playback. The cue-point seek effect below handles auto-seeking
  // to the active extract's audioStartMs once phase === 'audio'.
  useEffect(() => {
    if (phase !== 'preview' || previewSecondsRemaining > 0) return;
    if (!hasStarted || !currentSection) return;
    if (!previewArmedRef.current) return;
    previewArmedRef.current = false;
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
          result.catch((err) => handleAudioPlaybackError(err, setAudioError));
        }
      }
    })();
  }, [advanceStrictPhaseIfNeeded, phase, previewSecondsRemaining, hasStarted, currentSection]);

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

  const confirmNextFromAudio = async () => {
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
          <Link href="/listening"><Button variant="ghost">Back to Listening</Button></Link>
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
          <Link href="/listening"><Button variant="ghost">Back to Listening</Button></Link>
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
  const navigationQuestions = allPartsReviewEnabled
    ? session.questions
    : currentSection ? sectionGroups?.[currentSection] ?? [] : [];
  const visibleQuestionSections = allPartsReviewEnabled
    ? sectionsInPaper.map((section) => ({
      section,
      questions: sectionGroups?.[section] ?? [],
    })).filter((entry) => entry.questions.length > 0)
    : currentSection ? [{ section: currentSection, questions: sectionGroups?.[currentSection] ?? [] }] : [];
  const shouldMountAudio = session.paper.audioAvailable && (!strictReadinessRequired || hasStarted);

  return (
    <AppShell pageTitle={session.paper.title} distractionFree>
      {shouldMountAudio ? (
        <audio
          ref={audioRef}
          src={session.paper.audioUrl ?? undefined}
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
          onWaiting={() => setAudioState('buffering')}
          onCanPlay={() => setAudioState('ready')}
          onPlay={() => {
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
              audio?.play().catch((err) => handleAudioPlaybackError(err, setAudioError));
              return;
            }
            // C8g — flag the next play() as a resume.
            if (!audioResumeInFlightRef.current) wasPausedRef.current = true;
            setIsPlaying(false);
          }}
          onEnded={() => {
            if (session?.modePolicy.onePlayOnly) hasReachedEndRef.current = true;
            setIsPlaying(false);
          }}
          onError={() => {
            setAudioState('error');
            setAudioError('Audio failed to load. Reload the audio or return to Listening if the media asset is still processing.');
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
          <motion.div {...listMotion} className="space-y-8">
            <ListeningAudioTransport
              isPlaying={isPlaying}
              progressSeconds={progress}
              durationSeconds={duration}
              canScrub={session.modePolicy.canScrub !== false}
              isPreviewPhase={phase === 'preview'}
              audioState={audioState}
              saveState={saveState}
              answeredCount={answeredCount}
              totalQuestions={session.questions.length}
              attemptSecondsRemaining={attemptSecondsRemaining}
              onTogglePlayPause={togglePlayPause}
              onScrub={handleScrub}
            />

            {/* C8e — practice mode disclosure: replay is allowed in this
                mode but the real CBLA exam plays once. */}
            {session.modePolicy.mode === 'practice' && session.modePolicy.onePlayOnly === false ? (
              <div>
                <Badge variant="info">Practice mode — replay allowed</Badge>
              </div>
            ) : null}

            {mockAttemptId ? (
              <InlineAlert variant="info">
                You&rsquo;re taking this section as part of a mock. Submitting will mark this section complete and return you to the mock dashboard.
              </InlineAlert>
            ) : null}
            {audioError ? <InlineAlert variant="error">{audioError}</InlineAlert> : null}
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
              freeNavigation={allPartsReviewEnabled}
              onSelectSection={(index) => {
                if (!allPartsReviewEnabled) return;
                setCurrentSectionIndex(index);
                setPhase('audio');
                setPreviewSecondsRemaining(0);
                setReviewSecondsRemaining(0);
              }}
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
                  {allPartsReviewEnabled ? (
                    <InlineAlert variant="info" data-testid="listening-paper-final-review-banner">
                      {paperFinalReviewActive
                        ? `Final ${formatTime(paperFinalReviewSeconds ?? 0)} all-parts review: every part remains editable. Use the section buttons to check any answer before the timer ends.`
                        : 'Paper simulation keeps all parts editable for all-parts review.'}
                      {' '}
                      {unansweredQuestionNumbers.length > 0
                        ? `Still unanswered: ${unansweredQuestionList}.`
                        : 'All questions currently have an answer.'}
                    </InlineAlert>
                  ) : null}

                  {/* Question jumper — intra-section in CBT, all-parts in paper mode. */}
                  {navigationQuestions.length > 1 ? (
                    <div className="sticky top-40 z-10 flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-surface/95 p-3 backdrop-blur">
                      <span className="mr-1 text-[10px] font-black uppercase tracking-widest text-muted">
                        {allPartsReviewEnabled ? 'All-parts jump' : 'Jump to'}
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

                  <ZoomControls value={questionZoomPercent} onChange={setQuestionZoomPercent} />

                  <div data-testid="listening-question-surface" className="space-y-6" style={{ fontSize: `${questionZoomPercent}%` }}>
                    {visibleQuestionSections.map(({ section, questions }) => (
                      <section key={section} className="space-y-4" aria-label={LISTENING_SECTION_LABEL[section]}>
                        {allPartsReviewEnabled ? (
                          <h2 className="rounded-2xl border border-border bg-surface px-4 py-3 text-sm font-black uppercase tracking-widest text-muted">
                            {LISTENING_SECTION_LABEL[section]}
                          </h2>
                        ) : null}
                        {questions.map((question) => {
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

                          return (
                            <div id={`listening-question-${question.id}`} key={question.id} className="scroll-mt-48">
                              <BCQuestionRenderer
                                questionNumber={question.number}
                                partLabel={LISTENING_SECTION_LABEL[section]}
                                prompt={question.text}
                                options={question.options}
                                value={answers[question.id] ?? ''}
                                onChange={(value) => handleAnswerChange(question.id, value)}
                                locked={!canEdit}
                              />
                            </div>
                          );
                        })}
                      </section>
                    ))}
                  </div>
                </>
              )}
            </div>

            <div className="flex items-center justify-between gap-3 pt-4">
              <p className="text-xs text-muted">
                {allPartsReviewEnabled
                  ? 'Paper simulation — all parts stay editable for final all-parts review.'
                  : phase === 'review'
                  ? 'Edit freely until the countdown ends. This section will lock permanently.'
                  : isLastSection
                    ? 'Final section — the 2-minute review window opens when you press Next.'
                    : `Pressing Next locks ${currentSection ? LISTENING_SECTION_LABEL[currentSection] : 'this section'} and opens the next one.`}
              </p>
              {phase === 'audio' ? (
                <Button
                  size="lg"
                  onClick={() => (allPartsReviewEnabled ? setShowSubmitConfirm(true) : setShowNextConfirm(true))}
                  disabled={!allPartsReviewEnabled && (!canOpenReviewWindow || isAdvancingPhase)}
                  className="gap-2"
                >
                  {allPartsReviewEnabled ? 'Finish & Submit' : 'Next'} <ChevronRight className="h-5 w-5" />
                </Button>
              ) : null}
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

        {session.modePolicy.printableBooklet ? (
          <div className="hidden print:block print:p-6">
            <h1 className="text-2xl font-bold text-navy">{session.paper.title}</h1>
            <p className="mt-2 text-sm text-muted">Listening paper-mode answer sheet</p>
            <div className="mt-6 grid grid-cols-2 gap-x-8 gap-y-3">
              {session.questions.map((question) => (
                <div key={`print-${question.id}`} className="flex items-end gap-3 border-b border-border pb-2 text-sm">
                  <span className="w-12 font-bold">Q{question.number}</span>
                  <span className="flex-1 text-muted">{question.partCode}</span>
                </div>
              ))}
            </div>
          </div>
        ) : null}
      </div>
    </AppShell>
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
