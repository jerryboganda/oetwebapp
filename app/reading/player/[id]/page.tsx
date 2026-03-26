'use client';

import { useState, useEffect, Suspense } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
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
import { Timer } from '@/components/ui/timer';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchReadingTask, submitReadingAnswers } from '@/lib/api';
import { analytics } from '@/lib/analytics';
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

  useEffect(() => {
    fetchReadingTask(id)
      .then(t => {
        setTask(t);
        analytics.track('task_started', { subtest: 'reading', taskId: id });
      })
      .finally(() => setLoadingTask(false));
  }, [id]);

  const currentQuestion = task?.questions[currentIndex];

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

  const handleSubmit = async () => {
    if (window.confirm('Are you sure you want to submit? You cannot change answers after submitting.')) {
      await doSubmit();
    }
  };

  const handleTimerExpired = async () => {
    setTimerExpired(true);
    await doSubmit();
  };

  if (loadingTask) {
    return (
      <AppShell pageTitle="Reading Task" distractionFree>
        <div className="max-w-4xl mx-auto p-6 space-y-6">
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
        <div className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-4">
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
        <div className="flex-1 flex flex-col items-center justify-center p-8 text-center gap-4">
          <Loader2 className="w-12 h-12 text-primary animate-spin" />
          <h2 className="text-xl font-black text-navy">Submitting Answers...</h2>
          <p className="text-muted">Please wait while we process your reading task.</p>
        </div>
      </AppShell>
    );
  }

  const timerNavActions = (
    <div className="flex items-center gap-3">
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
      <div className="flex-1 flex flex-col lg:flex-row overflow-hidden relative h-full">

        {/* Pause Overlay */}
        <AnimatePresence>
          {isPaused && (
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              exit={{ opacity: 0 }}
              className="absolute inset-0 bg-white/80 backdrop-blur-sm z-30 flex flex-col items-center justify-center"
            >
              <Pause className="w-16 h-16 text-muted mb-4" />
              <h2 className="text-2xl font-black text-navy mb-2">Practice Paused</h2>
              <p className="text-muted mb-8">Take a breath. The timer is stopped.</p>
              <Button onClick={() => setIsPaused(false)} size="lg">Resume Practice</Button>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Left Pane: Reading Text */}
        <div className="flex-1 lg:w-1/2 lg:border-r border-gray-200 bg-surface overflow-y-auto p-6 sm:p-8 lg:p-12">
          <div className="max-w-2xl mx-auto space-y-12">
            {task.texts.map((text) => (
              <article key={text.id} className="prose prose-gray max-w-none">
                <h2 className="text-xl font-black text-navy mb-6 border-b border-gray-100 pb-4">{text.title}</h2>
                <div className="text-gray-700 leading-relaxed whitespace-pre-wrap font-serif text-lg">
                  {text.content}
                </div>
              </article>
            ))}
          </div>
        </div>

        {/* Right Pane: Questions */}
        <div className="flex-1 lg:w-1/2 flex flex-col bg-background-light">

          {/* Question Navigator Grid */}
          <div className="bg-surface border-b border-gray-200 p-4 shrink-0">
            <div className="flex flex-wrap gap-2 max-w-2xl mx-auto">
              {task.questions.map((q, idx) => {
                const isAnswered = !!answers[q.id];
                const isFlagged = flagged[q.id];
                const isCurrent = currentIndex === idx;
                return (
                  <button
                    key={q.id}
                    onClick={() => setCurrentIndex(idx)}
                    className={`relative w-10 h-10 rounded-lg flex items-center justify-center text-sm font-bold transition-all border-2
                      ${isCurrent ? 'border-primary text-primary bg-primary/5' :
                        isAnswered ? 'border-gray-300 bg-gray-50 text-gray-700' :
                        'border-gray-200 bg-surface text-muted hover:border-gray-300'}
                    `}
                  >
                    {q.number}
                    {isFlagged && (
                      <div className="absolute -top-1.5 -right-1.5 w-4 h-4 bg-amber-100 rounded-full flex items-center justify-center border border-amber-200">
                        <Flag className="w-2.5 h-2.5 text-amber-600 fill-amber-600" />
                      </div>
                    )}
                    {isAnswered && !isFlagged && !isCurrent && (
                      <div className="absolute -bottom-1 -right-1 w-3.5 h-3.5 bg-green-100 rounded-full flex items-center justify-center border border-green-200">
                        <CheckCircle2 className="w-2.5 h-2.5 text-green-600" />
                      </div>
                    )}
                  </button>
                );
              })}
            </div>
          </div>

          {/* Active Question Area */}
          {currentQuestion && (
            <div className="flex-1 overflow-y-auto p-6 sm:p-8">
              <div className="max-w-2xl mx-auto">
                <AnimatePresence mode="wait">
                  <motion.div
                    key={currentQuestion.id}
                    initial={{ opacity: 0, y: 10 }}
                    animate={{ opacity: 1, y: 0 }}
                    exit={{ opacity: 0, y: -10 }}
                    className="bg-surface rounded-[32px] border border-gray-200 p-8 shadow-sm"
                  >
                    <div className="flex items-center justify-between mb-6">
                      <span className="text-sm font-black text-muted uppercase tracking-widest">
                        Question {currentQuestion.number} of {task.questions.length}
                      </span>
                      <button
                        onClick={toggleFlag}
                        className={`flex items-center gap-2 text-xs font-bold uppercase tracking-widest px-3 py-1.5 rounded-lg transition-colors ${
                          flagged[currentQuestion.id] ? 'bg-amber-50 text-amber-700' : 'bg-gray-50 text-muted hover:bg-gray-100'
                        }`}
                      >
                        <Flag className={`w-4 h-4 ${flagged[currentQuestion.id] ? 'fill-amber-700' : ''}`} />
                        {flagged[currentQuestion.id] ? 'Flagged' : 'Flag for review'}
                      </button>
                    </div>

                    <h3 className="text-xl font-medium text-navy mb-8 leading-relaxed">
                      {currentQuestion.text}
                    </h3>

                    {currentQuestion.type === 'mcq' && currentQuestion.options && (
                      <div className="space-y-3">
                        {currentQuestion.options.map((option, optIdx) => {
                          const letter = String.fromCharCode(65 + optIdx);
                          const isSelected = answers[currentQuestion.id] === option;
                          return (
                            <button
                              key={optIdx}
                              onClick={() => handleAnswerChange(option)}
                              className={`w-full flex items-start gap-4 p-4 rounded-2xl border-2 transition-all text-left ${
                                isSelected ? 'border-primary bg-primary/5' : 'border-gray-100 hover:border-gray-200 bg-surface'
                              }`}
                            >
                              <div className={`w-8 h-8 rounded-lg flex items-center justify-center shrink-0 font-black text-sm transition-colors ${
                                isSelected ? 'bg-primary text-white' : 'bg-gray-100 text-muted'
                              }`}>
                                {letter}
                              </div>
                              <span className={`text-base pt-1 ${isSelected ? 'text-navy font-medium' : 'text-gray-600'}`}>
                                {option}
                              </span>
                            </button>
                          );
                        })}
                      </div>
                    )}

                    {currentQuestion.type === 'short_answer' && (
                      <div>
                        <label className="block text-xs font-bold text-muted uppercase tracking-widest mb-3">
                          Your Answer
                        </label>
                        <input
                          type="text"
                          value={answers[currentQuestion.id] || ''}
                          onChange={(e) => handleAnswerChange(e.target.value)}
                          placeholder="Type your answer here..."
                          className="w-full p-4 bg-gray-50 border border-gray-200 rounded-2xl text-base focus:outline-none focus:ring-2 focus:ring-primary/20 transition-all"
                        />
                      </div>
                    )}
                  </motion.div>
                </AnimatePresence>
              </div>
            </div>
          )}

          {/* Bottom Navigation */}
          <div className="bg-surface border-t border-gray-200 p-4 shrink-0">
            <div className="max-w-2xl mx-auto flex items-center justify-between">
              <Button variant="ghost" onClick={handlePrev} disabled={currentIndex === 0}>
                <ChevronLeft className="w-5 h-5" /> Previous
              </Button>
              <Button variant="ghost" onClick={handleNext} disabled={!task || currentIndex === task.questions.length - 1}>
                Next <ChevronRight className="w-5 h-5" />
              </Button>
            </div>
          </div>

        </div>
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

