'use client';

import { useEffect, useState, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Timer } from '@/components/ui/timer';
import { Modal } from '@/components/ui/modal';
import { InlineAlert } from '@/components/ui/alert';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchReadingTask, submitReadingAnswers } from '@/lib/api';
import type { ReadingTask, Question } from '@/lib/mock-data';
import { cn } from '@/lib/utils';
import {
  LogOut,
  ChevronLeft,
  ChevronRight,
  Send,
  CheckCircle2,
  BookOpen,
} from 'lucide-react';

const DIAGNOSTIC_READING_TASK_ID = 'rt-001';
const LOCAL_STORAGE_KEY = `diag-reading-answers-${DIAGNOSTIC_READING_TASK_ID}`;

export default function DiagnosticReadingPage() {
  const router = useRouter();
  const { track } = useAnalytics();

  const [task, setTask] = useState<ReadingTask | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [currentQ, setCurrentQ] = useState(0);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [showSubmitModal, setShowSubmitModal] = useState(false);
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [timerDone, setTimerDone] = useState(false);
  const [showPassage, setShowPassage] = useState(true);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const t = await fetchReadingTask(DIAGNOSTIC_READING_TASK_ID);
      setTask(t);
      // Restore saved answers
      const saved = localStorage.getItem(LOCAL_STORAGE_KEY);
      if (saved) {
        try { setAnswers(JSON.parse(saved)); } catch { /* ignore */ }
      }
      track('task_started', { subTest: 'Reading', mode: 'diagnostic', taskId: DIAGNOSTIC_READING_TASK_ID });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load reading task');
    } finally {
      setLoading(false);
    }
  }, [track]);

  useEffect(() => { load(); }, [load]);

  // Persist answers on every change
  useEffect(() => {
    if (Object.keys(answers).length > 0) {
      localStorage.setItem(LOCAL_STORAGE_KEY, JSON.stringify(answers));
    }
  }, [answers]);

  const questions = task?.questions ?? [];
  const question = questions[currentQ] as Question | undefined;
  const answeredCount = Object.keys(answers).filter((k) => answers[k]?.trim()).length;

  const setAnswer = (questionId: string, value: string) => {
    setAnswers((prev) => ({ ...prev, [questionId]: value }));
  };

  const handleSubmit = async () => {
    if (!task) return;
    try {
      setSubmitting(true);
      await submitReadingAnswers(DIAGNOSTIC_READING_TASK_ID, answers);
      localStorage.removeItem(LOCAL_STORAGE_KEY);
      track('task_submitted', { subTest: 'Reading', mode: 'diagnostic', taskId: DIAGNOSTIC_READING_TASK_ID });
      router.push('/diagnostic/hub');
    } catch {
      setError('Submission failed. Please try again.');
    } finally {
      setSubmitting(false);
      setShowSubmitModal(false);
    }
  };

  const handleLeave = () => {
    if (answeredCount > 0) {
      setShowLeaveModal(true);
    } else {
      router.push('/diagnostic/hub');
    }
  };

  const status: 'loading' | 'error' | 'success' =
    loading ? 'loading' : error ? 'error' : 'success';

  return (
    <AppShell
      pageTitle="Diagnostic — Reading"
      distractionFree
      navActions={
        <div className="flex flex-wrap items-center gap-2 sm:gap-3">
          {task && (
            <Timer
              mode="countdown"
              initialSeconds={task.timeLimit * 60}
              size="sm"
              onComplete={() => setTimerDone(true)}
            />
          )}
          <Button variant="ghost" size="sm" onClick={handleLeave} className="gap-1.5">
            <LogOut className="w-3.5 h-3.5" /> Exit
          </Button>
        </div>
      }
    >
      <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
        {task && (
          <div className="flex flex-1 min-h-0 h-[calc(100dvh-4rem)] flex-col overflow-hidden lg:flex-row">
            {/* Passage panel — left */}
            <div
              className={cn(
                    'border-b border-border overflow-y-auto transition-all lg:border-b-0 lg:border-r',
                    showPassage
                      ? 'block w-full max-h-[45dvh] lg:max-h-none lg:w-[55%]'
                      : 'hidden lg:block lg:w-0 overflow-hidden',
              )}
            >
              <div className="p-6">
                <div className="flex items-center gap-2 mb-4">
                  <BookOpen className="w-4 h-4 text-primary" />
                  <h2 className="text-sm font-bold text-navy">{task.title} — Part {task.part}</h2>
                </div>
                {task.texts.map((text) => (
                  <div key={text.id} className="mb-6">
                    <h3 className="text-sm font-bold text-navy mb-2">{text.title}</h3>
                    <div className="prose prose-sm max-w-none text-navy whitespace-pre-wrap leading-relaxed">
                      {text.content}
                    </div>
                  </div>
                ))}
              </div>
            </div>

            {/* Questions panel — right */}
            <div className="flex-1 flex min-h-0 flex-col min-w-0 overflow-hidden">
              {/* Toggle passage on mobile */}
              <div className="lg:hidden px-4 py-2 border-b border-border bg-background-light">
                <Button
                  variant="outline"
                  size="sm"
                  onClick={() => setShowPassage((p) => !p)}
                >
                  {showPassage ? 'Hide Passage' : 'Show Passage'}
                </Button>
              </div>

              {timerDone && (
                <InlineAlert variant="warning" className="mx-4 mt-2">
                  Time is up! You can still submit your answers.
                </InlineAlert>
              )}

              {/* Question navigator */}
              <div className="px-4 py-3 border-b border-border bg-surface">
                <div className="flex gap-1.5 flex-wrap">
                  {questions.map((q, i) => (
                    <button
                      key={q.id}
                      onClick={() => setCurrentQ(i)}
                      className={cn(
                        'w-8 h-8 rounded text-xs font-bold transition-colors',
                        currentQ === i && 'bg-primary text-white',
                        currentQ !== i && answers[q.id]?.trim() && 'bg-success/10 text-success',
                        currentQ !== i && !answers[q.id]?.trim() && 'bg-background-light text-muted hover:bg-border',
                      )}
                    >
                      {q.number}
                    </button>
                  ))}
                </div>
                <p className="text-xs text-muted mt-2">
                  {answeredCount} of {questions.length} answered
                </p>
              </div>

              {/* Current question */}
              {question && (
                <div className="flex-1 overflow-y-auto p-6">
                  <Card>
                    <p className="text-xs font-semibold text-primary mb-2">
                      Question {question.number} of {questions.length}
                    </p>
                    <p className="text-sm font-bold text-navy mb-4">{question.text}</p>

                    {/* MCQ */}
                    {question.type === 'mcq' && question.options && (
                      <div className="space-y-2">
                        {question.options.map((opt, i) => (
                          <label
                            key={i}
                            className={cn(
                              'flex items-center gap-3 p-3 rounded border cursor-pointer transition-colors',
                              answers[question.id] === opt
                                ? 'border-primary bg-primary/5'
                                : 'border-border hover:border-border-hover',
                            )}
                          >
                            <input
                              type="radio"
                              name={question.id}
                              value={opt}
                              checked={answers[question.id] === opt}
                              onChange={() => setAnswer(question.id, opt)}
                              className="accent-primary"
                            />
                            <span className="text-sm text-navy">{opt}</span>
                          </label>
                        ))}
                      </div>
                    )}

                    {/* Short answer / gap fill */}
                    {(question.type === 'short_answer' || question.type === 'gap_fill') && (
                      <input
                        type="text"
                        value={answers[question.id] ?? ''}
                        onChange={(e) => setAnswer(question.id, e.target.value)}
                        placeholder="Type your answer…"
                        className="w-full px-3 py-2 border border-border rounded text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/20"
                      />
                    )}

                    {/* Matching */}
                    {question.type === 'matching' && question.options && (
                      <select
                        value={answers[question.id] ?? ''}
                        onChange={(e) => setAnswer(question.id, e.target.value)}
                        className="w-full px-3 py-2 border border-border rounded text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/20"
                      >
                        <option value="">Select a match…</option>
                        {question.options.map((opt, i) => (
                          <option key={i} value={opt}>
                            {opt}
                          </option>
                        ))}
                      </select>
                    )}
                  </Card>
                </div>
              )}

              {/* Navigation footer */}
              <div className="px-4 py-3 border-t border-border bg-surface flex items-center justify-between shrink-0">
                <Button
                  variant="outline"
                  size="sm"
                  disabled={currentQ === 0}
                  onClick={() => setCurrentQ((p) => p - 1)}
                  className="gap-1.5"
                >
                  <ChevronLeft className="w-3.5 h-3.5" /> Previous
                </Button>

                {currentQ < questions.length - 1 ? (
                  <Button
                    size="sm"
                    onClick={() => setCurrentQ((p) => p + 1)}
                    className="gap-1.5"
                  >
                    Next <ChevronRight className="w-3.5 h-3.5" />
                  </Button>
                ) : (
                  <Button
                    size="sm"
                    onClick={() => setShowSubmitModal(true)}
                    className="gap-1.5"
                  >
                    <Send className="w-3.5 h-3.5" /> Submit
                  </Button>
                )}
              </div>
            </div>
          </div>
        )}
      </AsyncStateWrapper>

      {/* Submit Confirmation */}
      <Modal
        open={showSubmitModal}
        onClose={() => setShowSubmitModal(false)}
        title="Submit Answers?"
      >
        <div className="space-y-4">
          <p className="text-sm text-muted">
            You have answered <strong>{answeredCount}</strong> of <strong>{questions.length}</strong> questions.
            {answeredCount < questions.length && (
              <span className="text-warning font-semibold"> Some questions are unanswered.</span>
            )}
          </p>
          {answeredCount === questions.length && (
            <div className="flex items-center gap-2 text-success text-sm font-semibold">
              <CheckCircle2 className="w-4 h-4" /> All questions answered
            </div>
          )}
          <div className="flex gap-3 justify-end">
            <Button variant="outline" onClick={() => setShowSubmitModal(false)}>
              Review Answers
            </Button>
            <Button onClick={handleSubmit} loading={submitting} className="gap-1.5">
              <Send className="w-3.5 h-3.5" /> Submit
            </Button>
          </div>
        </div>
      </Modal>

      {/* Leave Confirmation */}
      <Modal
        open={showLeaveModal}
        onClose={() => setShowLeaveModal(false)}
        title="Leave Reading Task?"
      >
        <div className="space-y-4">
          <p className="text-sm text-muted">
            Your answers are saved locally and you can resume later.
          </p>
          <div className="flex gap-3 justify-end">
            <Button variant="outline" onClick={() => setShowLeaveModal(false)}>Stay</Button>
            <Button variant="destructive" onClick={() => router.push('/diagnostic/hub')}>Leave</Button>
          </div>
        </div>
      </Modal>
    </AppShell>
  );
}
