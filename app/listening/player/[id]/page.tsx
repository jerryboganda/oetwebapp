'use client';

import { Suspense, useEffect, useMemo, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, CheckCircle2, ChevronRight, FileText, Loader2, Lock, Pause, Play, Save, Timer, Volume2, WifiOff } from 'lucide-react';
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
  saveListeningAnswer,
  startListeningAttempt,
  submitListeningAttempt,
  type ListeningAttemptDto,
  type ListeningSessionDto,
} from '@/lib/listening-api';
import {
  LISTENING_REVIEW_SECONDS,
  LISTENING_SECTION_LABEL,
  LISTENING_SECTION_SEQUENCE,
  LISTENING_SECTION_SHORT_LABEL,
  formatReviewSeconds,
  groupQuestionsBySection,
  type ListeningSectionCode,
} from '@/lib/listening-sections';

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

function PlayerContent() {
  const params = useParams<{ id?: string | string[] }>();
  const router = useRouter();
  const searchParams = useSearchParams();
  const id = firstParam(params?.id);
  const mode = searchParams?.get('mode') === 'exam' ? 'exam' : 'practice';
  const attemptIdFromRoute = searchParams?.get('attemptId');
  const drillId = searchParams?.get('drill');

  const audioRef = useRef<HTMLAudioElement>(null);
  const saveTimers = useRef<Record<string, ReturnType<typeof setTimeout>>>({});
  const [session, setSession] = useState<ListeningSessionDto | null>(null);
  const [attempt, setAttempt] = useState<ListeningAttemptDto | null>(null);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [loadingTask, setLoadingTask] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [audioError, setAudioError] = useState<string | null>(null);
  const [audioState, setAudioState] = useState<'idle' | 'buffering' | 'ready' | 'error'>('idle');
  const [saveState, setSaveState] = useState<'idle' | 'saving' | 'saved' | 'error'>('idle');
  const [isPlaying, setIsPlaying] = useState(false);
  const [progress, setProgress] = useState(0);
  const [duration, setDuration] = useState(0);
  const [hasStarted, setHasStarted] = useState(Boolean(attemptIdFromRoute));
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [showSubmitConfirm, setShowSubmitConfirm] = useState(false);
  const [currentSectionIndex, setCurrentSectionIndex] = useState(0);
  const [phase, setPhase] = useState<'audio' | 'review'>('audio');
  const [reviewSecondsRemaining, setReviewSecondsRemaining] = useState(0);
  const [showNextConfirm, setShowNextConfirm] = useState(false);
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const sectionMotion = getSurfaceMotion('section', reducedMotion);
  const listMotion = getSurfaceMotion('list', reducedMotion);

  useEffect(() => {
    if (!id) return;
    let cancelled = false;
    const timers = saveTimers.current;

    Promise.resolve()
      .then(() => {
        if (cancelled) return null;
        setLoadingTask(true);
        setLoadError(null);
        return getListeningSession(id, { mode, attemptId: attemptIdFromRoute });
      })
      .then((data) => {
        if (cancelled || !data) return;
        setSession(data);
        setAttempt(data.attempt);
        setAnswers(Object.fromEntries(Object.entries(data.attempt?.answers ?? {}).map(([key, value]) => [key, value ?? ''])));
        analytics.track('task_started', { subtest: 'listening', taskId: id, attemptId: data.attempt?.attemptId, mode });
      })
      .catch((err) => {
        if (!cancelled) setLoadError(err instanceof Error ? err.message : 'Could not load this Listening task.');
      })
      .finally(() => {
        if (!cancelled) setLoadingTask(false);
      });

    return () => {
      cancelled = true;
      Object.values(timers).forEach(clearTimeout);
    };
  }, [attemptIdFromRoute, id, mode]);

  useEffect(() => {
    if (!attempt?.attemptId || !hasStarted) return;
    const interval = window.setInterval(() => {
      void heartbeatListeningAttempt(attempt.attemptId, Math.round(progress), 'web').catch(() => undefined);
    }, 15000);
    return () => window.clearInterval(interval);
  }, [attempt?.attemptId, hasStarted, progress]);

  const ensureAttempt = async () => {
    if (!session) throw new Error('Listening session is not ready.');
    if (attempt) return attempt;
    const started = await startListeningAttempt(session.paper.id, mode);
    setAttempt(started);
    return started;
  };

  const startTask = async () => {
    if (!session?.paper.audioAvailable || !session.readiness.objectiveReady) return;
    try {
      const started = await ensureAttempt();
      setHasStarted(true);
      router.replace(`/listening/player/${session.paper.id}?attemptId=${started.attemptId}&mode=${started.mode}${drillId ? `&drill=${encodeURIComponent(drillId)}` : ''}`);
      await audioRef.current?.play().catch((err) => handleAudioPlaybackError(err, setAudioError));
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Could not start this Listening attempt.');
    }
  };

  const togglePlayPause = () => {
    if (session?.modePolicy.onePlayOnly && hasStarted) return;
    if (!audioRef.current) return;
    if (isPlaying) audioRef.current.pause();
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
      saveListeningAnswer(currentAttempt.attemptId, questionId, value)
        .then(() => setSaveState('saved'))
        .catch(() => setSaveState('error'));
    }, 500);
  };

  const handleAnswerChange = (questionId: string, answer: string) => {
    setAnswers((current) => ({ ...current, [questionId]: answer }));
    persistAnswer(questionId, answer, attempt);
  };

  const handleSubmit = async () => {
    if (!session) return;
    setIsSubmitting(true);
    audioRef.current?.pause();
    try {
      const activeAttempt = await ensureAttempt();
      await Promise.all(Object.entries(answers).map(([questionId, answer]) => saveListeningAnswer(activeAttempt.attemptId, questionId, answer)));
      const result = await submitListeningAttempt(activeAttempt.attemptId);
      analytics.track('task_submitted', { subtest: 'listening', taskId: session.paper.id, attemptId: activeAttempt.attemptId });
      router.push(`/listening/results/${result.attemptId}`);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Could not submit this Listening attempt.');
      setIsSubmitting(false);
    }
  };

  // --- OET Listening forward-only section machine -----------------------------
  const sectionGroups = useMemo(
    () => (session ? groupQuestionsBySection(session.questions) : null),
    [session],
  );
  const sectionsInPaper = useMemo<ListeningSectionCode[]>(() => {
    if (!sectionGroups) return [];
    return LISTENING_SECTION_SEQUENCE.filter((code) => sectionGroups[code].length > 0);
  }, [sectionGroups]);
  const currentSection: ListeningSectionCode | null = sectionsInPaper[currentSectionIndex] ?? null;
  const isLastSection = currentSection !== null && currentSectionIndex >= sectionsInPaper.length - 1;
  const currentSectionReviewSeconds = currentSection ? LISTENING_REVIEW_SECONDS[currentSection] : 0;

  // 1-second countdown during review windows.
  useEffect(() => {
    if (phase !== 'review' || reviewSecondsRemaining <= 0) return;
    const timer = window.setTimeout(() => setReviewSecondsRemaining((value) => value - 1), 1000);
    return () => window.clearTimeout(timer);
  }, [phase, reviewSecondsRemaining]);

  const advanceToNextSection = () => {
    setPhase('audio');
    setReviewSecondsRemaining(0);
    setCurrentSectionIndex((value) => value + 1);
  };

  const advanceFromReview = () => {
    if (isLastSection) {
      void handleSubmit();
      return;
    }
    advanceToNextSection();
  };

  // Auto-advance when countdown hits zero.
  useEffect(() => {
    if (phase === 'review' && reviewSecondsRemaining === 0 && hasStarted && currentSection !== null) {
      advanceFromReview();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [phase, reviewSecondsRemaining, hasStarted, currentSection]);

  const confirmNextFromAudio = () => {
    if (!currentSection) return;
    if (currentSectionReviewSeconds > 0) {
      setPhase('review');
      setReviewSecondsRemaining(currentSectionReviewSeconds);
    } else {
      if (isLastSection) {
        void handleSubmit();
        return;
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

  return (
    <AppShell pageTitle={session.paper.title} distractionFree>
      {session.paper.audioAvailable ? (
        <audio
          ref={audioRef}
          src={session.paper.audioUrl ?? undefined}
          onTimeUpdate={() => audioRef.current && setProgress(audioRef.current.currentTime)}
          onLoadedMetadata={() => {
            if (!audioRef.current) return;
            setDuration(audioRef.current.duration);
            setAudioState('ready');
          }}
          onWaiting={() => setAudioState('buffering')}
          onCanPlay={() => setAudioState('ready')}
          onPlay={() => setIsPlaying(true)}
          onPause={() => setIsPlaying(false)}
          onEnded={() => setIsPlaying(false)}
          onError={() => {
            setAudioState('error');
            setAudioError('Audio failed to load. Reload the audio or return to Listening if the media asset is still processing.');
          }}
          preload="metadata"
        />
      ) : null}

      <div className="mx-auto max-w-3xl px-4 py-8 pb-24 sm:px-6 lg:px-8">
        {!hasStarted ? (
          <motion.div
            {...sectionMotion}
            className="mt-8 rounded-2xl border border-border bg-surface p-8 text-center shadow-sm sm:p-12"
          >
            <div className="mx-auto mb-6 flex h-20 w-20 items-center justify-center rounded-full bg-primary/10">
              <Volume2 className="h-10 w-10 text-primary" />
            </div>
            <p className="mb-2 text-xs font-black uppercase tracking-widest text-muted">{isExam ? 'Exam Mode' : 'Practice Mode'}</p>
            <h2 className="mb-4 text-2xl font-black text-navy">{session.paper.title}</h2>
            {drillId ? (
              <p className="mx-auto mb-4 max-w-lg text-sm text-muted">
                This launch came from a focused drill route, so listen for the error pattern before returning to review.
              </p>
            ) : null}

            <div className="mx-auto mb-8 max-w-lg space-y-4 rounded-2xl bg-background-light p-6 text-left">
              <h3 className="text-sm font-black uppercase tracking-widest text-muted">Before you start</h3>
              <ul className="space-y-3 text-sm text-muted">
                <li className="flex items-start gap-2">
                  <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
                  <span>Answers autosave to your server attempt as you work.</span>
                </li>
                <li className="flex items-start gap-2">
                  <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
                  <span>Transcript evidence and answer keys stay locked until submit.</span>
                </li>
                <li className="flex items-start gap-2">
                  <Lock className="h-5 w-5 shrink-0 text-warning" />
                  <span><strong className="text-navy">Forward-only exam:</strong> once you press Next on a section, it locks permanently and you cannot return to it.</span>
                </li>
                <li className="flex items-start gap-2">
                  <Timer className="h-5 w-5 shrink-0 text-warning" />
                  <span><strong className="text-navy">Review windows:</strong> A1 = 60s, A2 = 60s, C1 = 30s, C2 = 120s. Part B has no review window. Answer boxes remain editable during each window for its own section only.</span>
                </li>
                <li className="flex items-start gap-2">
                  <Volume2 className="h-5 w-5 shrink-0 text-muted" />
                  <span>Part B consists of six short workplace extracts (~40 seconds each), one multiple-choice item per extract.</span>
                </li>
                {isExam ? (
                  <li className="flex items-start gap-2">
                    <AlertCircle className="h-5 w-5 shrink-0 text-danger" />
                    <span className="font-bold text-danger">Exam mode plays once and disables pause/scrub controls.</span>
                  </li>
                ) : (
                  <li className="flex items-start gap-2">
                    <CheckCircle2 className="h-5 w-5 shrink-0 text-success" />
                    <span>Practice mode allows pause and scrubbing while you build accuracy.</span>
                  </li>
                )}
              </ul>
            </div>

            {!session.paper.audioAvailable ? (
              <InlineAlert variant="warning" className="mx-auto mb-6 max-w-lg text-left">
                {session.paper.audioUnavailableReason ?? 'Audio is not available for this task yet.'}
              </InlineAlert>
            ) : null}
            {!session.readiness.objectiveReady ? (
              <InlineAlert variant="warning" className="mx-auto mb-6 max-w-lg text-left">
                {session.readiness.missingReason ?? 'Structured Listening questions are not ready yet.'}
              </InlineAlert>
            ) : null}
            {audioError ? <InlineAlert variant="error" className="mx-auto mb-6 max-w-lg text-left">{audioError}</InlineAlert> : null}

            <div className="flex flex-col justify-center gap-3 sm:flex-row">
              <Button size="lg" onClick={startTask} disabled={!session.paper.audioAvailable || !session.readiness.objectiveReady} className="gap-2">
                <Play className="h-5 w-5" /> Start Audio &amp; Task
              </Button>
              {session.paper.questionPaperUrl ? (
                <Link href={session.paper.questionPaperUrl} target="_blank">
                  <Button size="lg" variant="outline" className="gap-2">
                    <FileText className="h-5 w-5" /> Question Paper
                  </Button>
                </Link>
              ) : null}
            </div>
          </motion.div>
        ) : (
          <motion.div {...listMotion} className="space-y-8">
            <div className="sticky top-20 z-20 flex items-center gap-4 rounded-2xl bg-navy p-4 text-white shadow-xl shadow-navy/10 sm:p-5">
              <button
                onClick={togglePlayPause}
                disabled={isExam}
                aria-label={isPlaying ? 'Pause audio' : 'Play audio'}
                className={`flex h-12 w-12 shrink-0 items-center justify-center rounded-full transition-colors ${
                  isExam
                    ? 'cursor-not-allowed bg-white/10 text-white/30'
                    : 'bg-surface text-navy hover:bg-background-light'
                }`}
              >
                {isPlaying ? <Pause className="h-6 w-6" /> : <Play className="ml-1 h-6 w-6" />}
              </button>
              <div className="flex flex-1 flex-col gap-1.5">
                <div className="flex justify-between font-mono text-xs font-bold text-white/70">
                  <span>{formatTime(progress)}</span>
                  <span>{formatTime(duration)}</span>
                </div>
                <div className="relative h-2.5 w-full overflow-hidden rounded-full bg-white/20">
                  <div
                    className="absolute left-0 top-0 h-full bg-info transition-all duration-100 ease-linear"
                    style={{ width: `${duration > 0 ? (progress / duration) * 100 : 0}%` }}
                  />
                  {session.modePolicy.canScrub ? (
                    <input
                      type="range"
                      min="0"
                      max={duration || 100}
                      value={progress}
                      onChange={handleScrub}
                      className="absolute left-0 top-0 h-full w-full cursor-pointer opacity-0"
                    />
                  ) : null}
                </div>
              </div>
              <div className="hidden items-center gap-2 text-xs font-bold text-white/60 sm:flex">
                {audioState === 'buffering' ? <Loader2 className="h-4 w-4 animate-spin" /> : audioState === 'error' ? <WifiOff className="h-4 w-4" /> : <Save className="h-4 w-4" />}
                {saveState === 'saving' ? 'Saving' : saveState === 'error' ? 'Save issue' : `${answeredCount}/${session.questions.length} saved`}
              </div>
            </div>

            {audioError ? <InlineAlert variant="error">{audioError}</InlineAlert> : null}
            {saveState === 'error' ? <InlineAlert variant="warning">One answer did not autosave. Keep working; submit will retry saving all answers.</InlineAlert> : null}

            {/* Section stepper — forward only. Completed sections are permanently locked. */}
            <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-surface p-3 text-xs font-black uppercase tracking-widest">
              {sectionsInPaper.map((code, idx) => {
                const state = idx < currentSectionIndex
                  ? 'locked'
                  : idx === currentSectionIndex
                    ? (phase === 'review' ? 'reviewing' : 'active')
                    : 'pending';
                return (
                  <span
                    key={code}
                    className={`inline-flex items-center gap-1 rounded-full px-3 py-1.5 ${
                      state === 'locked'
                        ? 'bg-background-light text-muted/60'
                        : state === 'active'
                          ? 'bg-primary text-white'
                          : state === 'reviewing'
                            ? 'bg-warning/10 text-warning'
                            : 'bg-background-light text-muted'
                    }`}
                  >
                    {state === 'locked' ? <Lock className="h-3 w-3" /> : state === 'reviewing' ? <Timer className="h-3 w-3" /> : null}
                    {LISTENING_SECTION_SHORT_LABEL[code]}
                  </span>
                );
              })}
              <span className="ml-auto hidden text-[10px] normal-case tracking-normal text-muted sm:inline">
                Forward-only — completed sections cannot be revisited.
              </span>
            </div>

            {/* Review-window countdown banner — answer boxes stay editable. */}
            {phase === 'review' && currentSection ? (
              <div className="flex flex-col gap-3 rounded-2xl border-2 border-warning/30 bg-warning/10 p-5 sm:flex-row sm:items-center sm:justify-between">
                <div className="flex items-start gap-3">
                  <Timer className="mt-0.5 h-5 w-5 shrink-0 text-warning" />
                  <div>
                    <p className="text-sm font-black text-warning">
                      {LISTENING_SECTION_LABEL[currentSection]} — review window
                    </p>
                    <p className="mt-0.5 text-xs text-warning">
                      You can finish completing any words you abbreviated. Answers for this section remain fully editable until the timer hits zero or you press Next.
                    </p>
                  </div>
                </div>
                <div className="flex items-center gap-3">
                  <span className="rounded-xl bg-warning/20 px-3 py-2 font-mono text-lg font-black text-warning">
                    {formatReviewSeconds(reviewSecondsRemaining)}
                  </span>
                  <Button
                    size="sm"
                    onClick={() => (isLastSection ? setShowSubmitConfirm(true) : setShowNextConfirm(true))}
                    className="gap-1"
                  >
                    {isLastSection ? 'Finish & Submit' : 'Next'} <ChevronRight className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            ) : null}

            <div className="space-y-6">
              {currentSection === null ? (
                <InlineAlert variant="warning">
                  This Listening paper does not contain any authored sections yet.
                </InlineAlert>
              ) : (
                <>
                  {/* Intra-section question jumper — free navigation between this section's questions only. */}
                  {(sectionGroups?.[currentSection]?.length ?? 0) > 1 ? (
                    <div className="sticky top-40 z-10 flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-surface/95 p-3 backdrop-blur">
                      <span className="mr-1 text-[10px] font-black uppercase tracking-widest text-muted">
                        Jump to
                      </span>
                      {(sectionGroups?.[currentSection] ?? []).map((question) => {
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

                  {(sectionGroups?.[currentSection] ?? []).map((question) => {
                  // Answer editing is allowed during both audio phase and the
                  // current section's review window. Locked sections (earlier
                  // indices) are never shown here, so their inputs are
                  // physically unreachable — that's the forward-only lock.
                  const canEdit = true;
                  return (
                    <div id={`listening-question-${question.id}`} key={question.id} className="rounded-2xl border border-border bg-surface p-6 shadow-sm scroll-mt-48 sm:p-8">
                      <h3 className="mb-6 text-lg font-medium leading-relaxed text-navy">
                        <span className="mb-2 block text-xs font-black uppercase tracking-widest text-muted">
                          {LISTENING_SECTION_LABEL[currentSection]} / Question {question.number}
                        </span>
                        {question.text}
                      </h3>
                      {question.options.length > 0 ? (
                        <div className="space-y-3">
                          {question.options.map((option) => {
                            const isSelected = answers[question.id] === option;
                            return (
                              <button
                                key={option}
                                onClick={() => canEdit && handleAnswerChange(question.id, option)}
                                disabled={!canEdit}
                                className={`w-full rounded-xl border-2 p-4 text-left transition-all sm:p-5 ${
                                  isSelected
                                    ? 'border-primary bg-primary/5 font-medium text-primary'
                                    : 'border-border text-navy hover:border-border-hover hover:bg-background-light'
                                } ${!canEdit ? 'cursor-not-allowed opacity-60' : ''}`}
                              >
                                <div className="flex items-center gap-4">
                                  <div className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 transition-colors ${
                                    isSelected ? 'border-primary' : 'border-border-hover'
                                  }`}>
                                    {isSelected ? <div className="h-2.5 w-2.5 rounded-full bg-primary" /> : null}
                                  </div>
                                  <span className="leading-relaxed">{option}</span>
                                </div>
                              </button>
                            );
                          })}
                        </div>
                      ) : (
                        <div className="space-y-3">
                          <label htmlFor={`listening-answer-${question.id}`} className="text-xs font-black uppercase tracking-widest text-muted">
                            Your answer
                          </label>
                          <input
                            id={`listening-answer-${question.id}`}
                            type="text"
                            value={answers[question.id] ?? ''}
                            onChange={(event) => handleAnswerChange(question.id, event.target.value)}
                            readOnly={!canEdit}
                            placeholder="Type your answer here..."
                            className="w-full rounded-xl border border-border bg-surface px-4 py-3 text-base text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/15"
                            aria-label={`Answer for question ${question.number}`}
                          />
                        </div>
                      )}
                    </div>
                  );
                })}
                </>
              )}
            </div>

            <div className="flex items-center justify-between gap-3 pt-4">
              <p className="text-xs text-muted">
                {phase === 'review'
                  ? 'Edit freely until the countdown ends. This section will lock permanently.'
                  : isLastSection
                    ? 'Final section — the 2-minute review window opens when you press Next.'
                    : `Pressing Next locks ${currentSection ? LISTENING_SECTION_LABEL[currentSection] : 'this section'} and opens the next one.`}
              </p>
              {phase === 'audio' ? (
                <Button
                  size="lg"
                  onClick={() => setShowNextConfirm(true)}
                  disabled={!currentSection}
                  className="gap-2"
                >
                  Next <ChevronRight className="h-5 w-5" />
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
                <div className="flex justify-end gap-3">
                  <Button variant="outline" onClick={() => setShowNextConfirm(false)}>Keep listening</Button>
                  <Button
                    onClick={() => {
                      setShowNextConfirm(false);
                      if (phase === 'review') {
                        advanceFromReview();
                      } else {
                        confirmNextFromAudio();
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
