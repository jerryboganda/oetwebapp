'use client';

import { Suspense, useEffect, useRef, useState } from 'react';
import Link from 'next/link';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, useReducedMotion } from 'motion/react';
import { AlertCircle, CheckCircle2, FileText, Loader2, Pause, Play, Save, Volume2, WifiOff } from 'lucide-react';
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

  if (loadingTask) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="mx-auto max-w-3xl space-y-6 px-4 py-8 sm:px-6 lg:px-8">
          <Skeleton className="h-16 rounded-2xl" />
          <Skeleton className="h-48 rounded-[24px]" />
          <Skeleton className="h-48 rounded-[24px]" />
        </div>
      </AppShell>
    );
  }

  if (!session || loadError) {
    return (
      <AppShell pageTitle="Listening Task" distractionFree>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <AlertCircle className="h-12 w-12 text-rose-500" />
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
          <p className="text-sm text-muted">Your score and transcript policy are being resolved on the server.</p>
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
            className="mt-8 rounded-[32px] border border-gray-200 bg-surface p-8 text-center shadow-sm sm:p-12"
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

            <div className="mx-auto mb-8 max-w-lg space-y-4 rounded-2xl bg-gray-50 p-6 text-left">
              <h3 className="text-sm font-black uppercase tracking-widest text-muted">Before you start</h3>
              <ul className="space-y-3 text-sm text-gray-600">
                <li className="flex items-start gap-2">
                  <CheckCircle2 className="h-5 w-5 shrink-0 text-green-500" />
                  <span>Answers autosave to your server attempt as you work.</span>
                </li>
                <li className="flex items-start gap-2">
                  <CheckCircle2 className="h-5 w-5 shrink-0 text-green-500" />
                  <span>Transcript evidence and answer keys stay locked until submit.</span>
                </li>
                {isExam ? (
                  <li className="flex items-start gap-2">
                    <AlertCircle className="h-5 w-5 shrink-0 text-rose-500" />
                    <span className="font-bold text-rose-700">Exam mode plays once and disables pause/scrub controls.</span>
                  </li>
                ) : (
                  <li className="flex items-start gap-2">
                    <CheckCircle2 className="h-5 w-5 shrink-0 text-green-500" />
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
                    : 'bg-white text-navy hover:bg-gray-100'
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
                    className="absolute left-0 top-0 h-full bg-blue-400 transition-all duration-100 ease-linear"
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

            <div className="space-y-6">
              {session.questions.map((question) => (
                <div key={question.id} className="rounded-[24px] border border-gray-200 bg-surface p-6 shadow-sm sm:p-8">
                  <h3 className="mb-6 text-lg font-medium leading-relaxed text-navy">
                    <span className="mb-2 block text-xs font-black uppercase tracking-widest text-muted">
                      Part {question.partCode} / Question {question.number}
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
                            onClick={() => handleAnswerChange(question.id, option)}
                            className={`w-full rounded-xl border-2 p-4 text-left transition-all sm:p-5 ${
                              isSelected
                                ? 'border-primary bg-primary/5 font-medium text-primary'
                                : 'border-gray-100 text-gray-700 hover:border-gray-200 hover:bg-gray-50'
                            }`}
                          >
                            <div className="flex items-center gap-4">
                              <div className={`flex h-5 w-5 shrink-0 items-center justify-center rounded-full border-2 transition-colors ${
                                isSelected ? 'border-primary' : 'border-gray-300'
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
                        placeholder="Type your answer here..."
                        className="w-full rounded-xl border border-gray-200 bg-white px-4 py-3 text-base text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/15"
                        aria-label={`Answer for question ${question.number}`}
                      />
                    </div>
                  )}
                </div>
              ))}
            </div>

            <div className="flex justify-end pt-4">
              <Button onClick={() => setShowSubmitConfirm(true)} size="lg">Submit Answers</Button>
            </div>
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
