'use client';

import { useState, useEffect, Suspense } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, AnimatePresence, useReducedMotion } from 'motion/react';
import {
  Clock,
  ChevronLeft,
  ChevronRight,
  CheckCircle2,
  Pause,
  Play,
  Flag,
  Loader2,
  AlertCircle
} from 'lucide-react';
import Link from 'next/link';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Timer } from '@/components/ui/timer';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { fetchReadingTask, submitReadingAnswers } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { getMotionPresenceMode, getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import type { ReadingTask } from '@/lib/mock-data';

function ReadingPlayerContent() {
  const params = useParams();
  const searchParams = useSearchParams();
  const router = useRouter();

  const id = params?.id as string;
  const mode = (searchParams?.get('mode') as 'practice' | 'exam') || 'practice';
  const isExam = mode === 'exam';

  const [task, setTask] = useState<ReadingTask | null>(null);
  const [loadingTask, setLoadingTask] = useState(true);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [flagged, setFlagged] = useState<Record<string, boolean>>({});
  const [currentIndex, setCurrentIndex] = useState(0);
  const [isPaused, setIsPaused] = useState(false);
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [timerExpired, setTimerExpired] = useState(false);
  const [showSubmitConfirm, setShowSubmitConfirm] = useState(false);
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const overlayMotion = getSurfaceMotion('overlay', reducedMotion);
  const questionMotion = getSurfaceMotion('section', reducedMotion);

  useEffect(() => {
    fetchReadingTask(id)
      .then(t => {
        setTask(t);
        analytics.track('task_started', { subtest: 'reading', taskId: id });
      })
      .finally(() => setLoadingTask(false));
  }, [id]);

  const currentQuestion = task?.questions[currentIndex];
  const heroHighlights = task ? [
    { icon: Clock, label: 'Time limit', value: `${task.timeLimit} mins` },
    { icon: CheckCircle2, label: 'Questions', value: `${task.questions.length}` },
    { icon: Flag, label: 'Mode', value: isExam ? 'Exam' : 'Practice' },
  ] : [];

  const handleAnswerChange = (val: string) => {
    if (!currentQuestion) return;
    setAnswers(prev => ({ ...prev, [currentQuestion.id]: val }));
  };

  const toggleFlag = () => {
    if (!currentQuestion) return;
    setFlagged(prev => ({ ...prev, [currentQuestion.id]: !prev[currentQuestion.id] }));
  };

  const handleNext = () => {
    if (task && currentIndex < task.questions.length - 1) setCurrentIndex(prev => prev + 1);
  };

  const handlePrev = () => {
    if (currentIndex > 0) setCurrentIndex(prev => prev - 1);
  };

  const doSubmit = async () => {
    if (!task) return;
    setIsSubmitting(true);
    try {
      await submitReadingAnswers(task.id, answers);
      analytics.track('task_submitted', { subtest: 'reading', taskId: task.id });
      router.push(`/reading/results/${task.id}`);
    } catch {
      setIsSubmitting(false);
    }
  };

  const handleSubmit = () => {
    setShowSubmitConfirm(true);
  };

  const handleTimerExpired = async () => {
    setTimerExpired(true);
    await doSubmit();
  };

  if (loadingTask) {
    return (
      <AppShell pageTitle="Reading Task" distractionFree>
        <div className="mx-auto max-w-5xl space-y-6 p-6">
          <Skeleton className="h-48 rounded-3xl" />
          <Skeleton className="h-8 w-48 rounded-xl" />
          <Skeleton className="h-64 rounded-2xl" />
          <Skeleton className="h-40 rounded-2xl" />
        </div>
      </AppShell>
    );
  }

  if (!task) {
    return (
      <AppShell pageTitle="Reading Task" distractionFree>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <AlertCircle className="w-12 h-12 text-rose-500" />
          <h2 className="text-xl font-black text-navy">Task not found</h2>
          <Link href="/reading"><Button variant="ghost">Back to Reading</Button></Link>
        </div>
      </AppShell>
    );
  }

  if (isSubmitting) {
    return (
      <AppShell pageTitle="Submitting" distractionFree>
        <div className="flex flex-1 flex-col items-center justify-center gap-4 p-8 text-center">
          <Loader2 className="w-12 h-12 text-primary animate-spin" />
          <h2 className="text-xl font-black text-navy">Submitting Answers...</h2>
          <p className="text-muted">Please wait while we process your reading task.</p>
        </div>
      </AppShell>
    );
  }

  const timerNavActions = (
    <div className="flex flex-wrap items-center gap-2 sm:gap-3">
      <Timer
        mode="countdown"
        initialSeconds={task.timeLimit}
        running={!isPaused}
        onComplete={handleTimerExpired}
        className="font-mono text-base font-bold"
      />
      {!isExam && (
        <button
          onClick={() => setIsPaused(!isPaused)}
          className="p-2 hover:bg-gray-100 rounded-full transition-colors text-muted"
          title={isPaused ? 'Resume' : 'Pause'}
          aria-label={isPaused ? 'Resume timer' : 'Pause timer'}
        >
          {isPaused ? <Play className="w-5 h-5" /> : <Pause className="w-5 h-5" />}
        </button>
      )}
      <Button size="sm" onClick={handleSubmit}>Submit</Button>
    </div>
  );

  return (
    <AppShell
      pageTitle={task.title}
      distractionFree
      navActions={timerNavActions}
    >
      <div className="mx-auto flex min-h-0 w-full max-w-[1440px] flex-1 flex-col gap-6 overflow-y-auto px-4 py-6 md:overflow-hidden md:px-6 lg:px-8">
        <LearnerPageHero
          eyebrow="Practice"
          icon={Flag}
          title={task.title}
          description={isExam ? 'Exam mode keeps the focus strict and timed.' : 'Practice mode lets you review and move at your own pace.'}
          highlights={heroHighlights}
        />

        <AnimatePresence mode={getMotionPresenceMode(reducedMotion)}>
          {isPaused && (
            <motion.div
              {...overlayMotion}
              className="absolute inset-0 z-30 flex flex-col items-center justify-center bg-white/80 backdrop-blur-sm"
            >
              <Pause className="mb-4 h-16 w-16 text-muted" />
              <h2 className="mb-2 text-2xl font-black text-navy">Practice Paused</h2>
              <p className="mb-8 text-muted">Take a breath. The timer is stopped.</p>
              <Button onClick={() => setIsPaused(false)} size="lg">Resume Practice</Button>
            </motion.div>
          )}
        </AnimatePresence>

        <div className="grid min-h-0 flex-1 gap-6 md:grid-cols-2">
          <div className="flex min-h-0 flex-col rounded-3xl border border-border bg-surface p-6 shadow-sm sm:p-8 md:p-10">
            <LearnerSurfaceSectionHeader
              eyebrow="Reading passage"
              title="Source text"
              description="Keep the passage visually calm so the learner can focus on extraction and navigation."
              className="mb-5"
            />
            <div className="min-h-0 space-y-12 overflow-y-auto pr-1">
              {task.texts.map((text) => (
                <article key={text.id} className="prose prose-gray max-w-none">
                  <h2 className="mb-6 border-b border-border pb-4 text-xl font-black text-navy">{text.title}</h2>
                  <div className="whitespace-pre-wrap font-serif text-lg leading-relaxed text-navy/80">
                    {text.content}
                  </div>
                </article>
              ))}
            </div>
          </div>

          <div className="flex min-h-0 flex-col rounded-3xl border border-border bg-background-light shadow-sm">
            <div className="shrink-0 border-b border-border bg-surface p-4">
              <div className="mx-auto flex max-w-2xl flex-wrap gap-2">
                {task.questions.map((q, idx) => {
                  const isAnswered = !!answers[q.id];
                  const isFlagged = flagged[q.id];
                  const isCurrent = currentIndex === idx;

                  return (
                    <button
                      key={q.id}
                      onClick={() => setCurrentIndex(idx)}
                      className={`relative flex h-10 w-10 items-center justify-center rounded-2xl border-2 text-sm font-bold transition-all ${
                        isCurrent
                          ? 'border-primary bg-primary/5 text-primary'
                          : isAnswered
                            ? 'border-gray-300 bg-gray-50 text-gray-700'
                            : 'border-gray-200 bg-surface text-muted hover:border-gray-300'
                      }`}
                    >
                      {q.number}
                      {isFlagged ? (
                        <div className="absolute -right-1.5 -top-1.5 flex h-4 w-4 items-center justify-center rounded-full border border-amber-200 bg-amber-100">
                          <Flag className="h-2.5 w-2.5 fill-amber-600 text-amber-600" />
                        </div>
                      ) : null}
                      {isAnswered && !isFlagged && !isCurrent ? (
                        <div className="absolute -bottom-1 -right-1 flex h-3.5 w-3.5 items-center justify-center rounded-full border border-green-200 bg-green-100">
                          <CheckCircle2 className="h-2.5 w-2.5 text-green-600" />
                        </div>
                      ) : null}
                    </button>
                  );
                })}
              </div>
            </div>

            {currentQuestion ? (
              <div className="min-h-0 flex-1 overflow-y-auto p-6 sm:p-8">
                <div className="mx-auto max-w-2xl">
                  <AnimatePresence mode={getMotionPresenceMode(reducedMotion)}>
                    <motion.div
                      key={currentQuestion.id}
                      {...questionMotion}
                      className="rounded-[32px] border border-border bg-surface p-8 shadow-sm"
                    >
                      <div className="mb-6 flex items-center justify-between">
                        <span className="text-sm font-black uppercase tracking-widest text-muted">
                          Question {currentQuestion.number} of {task.questions.length}
                        </span>
                        <button
                          onClick={toggleFlag}
                          aria-pressed={!!flagged[currentQuestion.id]}
                          aria-label={flagged[currentQuestion.id] ? 'Remove flag from this question' : 'Flag this question for review'}
                          className={`flex items-center gap-2 rounded-lg px-3 py-1.5 text-xs font-bold uppercase tracking-widest transition-colors ${
                            flagged[currentQuestion.id] ? 'bg-amber-50 text-amber-700' : 'bg-gray-50 text-muted hover:bg-gray-100'
                          }`}
                        >
                          <Flag className={`h-4 w-4 ${flagged[currentQuestion.id] ? 'fill-amber-700' : ''}`} />
                          {flagged[currentQuestion.id] ? 'Flagged' : 'Flag for review'}
                        </button>
                      </div>

                      <h3 className="mb-8 text-xl font-medium leading-relaxed text-navy">
                        {currentQuestion.text}
                      </h3>

                      {currentQuestion.type === 'mcq' && currentQuestion.options ? (
                        <div className="space-y-3">
                          {currentQuestion.options.map((option, optIdx) => {
                            const letter = String.fromCharCode(65 + optIdx);
                            const isSelected = answers[currentQuestion.id] === option;

                            return (
                              <button
                                key={optIdx}
                                onClick={() => handleAnswerChange(option)}
                                className={`flex w-full items-start gap-4 rounded-2xl border-2 p-4 text-left transition-all ${
                                  isSelected ? 'border-primary bg-primary/5' : 'border-gray-100 bg-surface hover:border-gray-200'
                                }`}
                              >
                                <div className={`flex h-8 w-8 shrink-0 items-center justify-center rounded-lg text-sm font-black transition-colors ${
                                  isSelected ? 'bg-primary text-white' : 'bg-gray-100 text-muted'
                                }`}>
                                  {letter}
                                </div>
                                <span className={`pt-1 text-base ${isSelected ? 'font-medium text-navy' : 'text-gray-600'}`}>
                                  {option}
                                </span>
                              </button>
                            );
                          })}
                        </div>
                      ) : null}

                      {currentQuestion.type === 'short_answer' ? (
                        <div>
                          <label className="mb-3 block text-xs font-bold uppercase tracking-widest text-muted">
                            Your Answer
                          </label>
                          <input
                            type="text"
                            value={answers[currentQuestion.id] || ''}
                            onChange={(e) => handleAnswerChange(e.target.value)}
                            placeholder="Type your answer here..."
                            className="w-full rounded-2xl border border-border bg-background-light p-4 text-base transition-all focus:outline-none focus:ring-2 focus:ring-primary/20"
                          />
                        </div>
                      ) : null}
                    </motion.div>
                  </AnimatePresence>
                </div>
              </div>
            ) : null}

            <div className="shrink-0 border-t border-border bg-surface p-4">
              <div className="mx-auto flex max-w-2xl items-center justify-between">
                <Button variant="ghost" onClick={handlePrev} disabled={currentIndex === 0}>
                  <ChevronLeft className="h-5 w-5" /> Previous
                </Button>
                <Button variant="ghost" onClick={handleNext} disabled={!task || currentIndex === task.questions.length - 1}>
                  Next <ChevronRight className="h-5 w-5" />
                </Button>
              </div>
            </div>
          </div>
        </div>
      <Modal open={showSubmitConfirm} onClose={() => setShowSubmitConfirm(false)} title="Submit reading task?" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Submit your current answers now? You will not be able to change them after submission.
          </p>
          <div className="flex justify-end gap-3">
            <Button variant="outline" onClick={() => setShowSubmitConfirm(false)}>
              Keep reviewing
            </Button>
            <Button
              onClick={async () => {
                setShowSubmitConfirm(false);
                await doSubmit();
              }}
            >
              Submit now
            </Button>
            </div>
          </div>
        </Modal>
      </div>
    </AppShell>
  );
}

export default function ReadingPlayer() {
  return (
    <Suspense fallback={
      <AppShell pageTitle="Reading Task" distractionFree>
        <div className="flex-1 flex items-center justify-center">
          <Loader2 className="w-8 h-8 text-primary animate-spin" />
        </div>
      </AppShell>
    }>
      <ReadingPlayerContent />
    </Suspense>
  );
}
