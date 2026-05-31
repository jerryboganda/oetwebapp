'use client';

import { useEffect, useState, useCallback, useRef } from 'react';
import { useRouter } from 'next/navigation';
import { AppShell } from '@/components/layout';
import { AsyncStateWrapper } from '@/components/state';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Modal } from '@/components/ui/modal';
import { InlineAlert } from '@/components/ui/alert';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchDiagnosticTaskId, fetchListeningTask, submitListeningAnswers } from '@/lib/api';
import type { ListeningTask, Question } from '@/lib/mock-data';
import { cn } from '@/lib/utils';
import {
  LogOut,
  Play,
  Pause,
  ChevronLeft,
  ChevronRight,
  Send,
  CheckCircle2,
  Headphones,
  Volume2,
} from 'lucide-react';

export default function DiagnosticListeningPage() {
  const router = useRouter();
  const { track } = useAnalytics();

  const [task, setTask] = useState<ListeningTask | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
  const [taskId, setTaskId] = useState<string | null>(null);
  const [currentQ, setCurrentQ] = useState(0);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [showSubmitModal, setShowSubmitModal] = useState(false);
  const [showLeaveModal, setShowLeaveModal] = useState(false);
  const [submitting, setSubmitting] = useState(false);

  // Audio playback state
  const [isPlaying, setIsPlaying] = useState(false);
  const [audioProgress, setAudioProgress] = useState(0);
  const [audioDuration, setAudioDuration] = useState(0);
  const audioIntervalRef = useRef<ReturnType<typeof setInterval> | undefined>(undefined);

  const load = useCallback(async () => {
    try {
      setLoading(true);
      setError(undefined);
      const resolvedId = await fetchDiagnosticTaskId('Listening');
      setTaskId(resolvedId);
      const t = await fetchListeningTask(resolvedId);
      setTask(t);
      setAudioDuration(t.duration);
      // Restore saved answers
      const saved = localStorage.getItem(`diag-listening-answers-${resolvedId}`);
      if (saved) {
        try { setAnswers(JSON.parse(saved)); } catch { /* ignore */ }
      }
      track('task_started', { subTest: 'Listening', mode: 'diagnostic', taskId: resolvedId });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load listening task');
    } finally {
      setLoading(false);
    }
  }, [track]);

  useEffect(() => { load(); }, [load]);

  // Persist answers on every change
  useEffect(() => {
    if (Object.keys(answers).length > 0 && taskId) {
      localStorage.setItem(`diag-listening-answers-${taskId}`, JSON.stringify(answers));
    }
  }, [answers, taskId]);

  // Simulated audio playback
  const togglePlay = () => {
    if (isPlaying) {
      setIsPlaying(false);
      if (audioIntervalRef.current) clearInterval(audioIntervalRef.current);
    } else {
      setIsPlaying(true);
      audioIntervalRef.current = setInterval(() => {
        setAudioProgress((prev) => {
          if (prev >= audioDuration) {
            setIsPlaying(false);
            if (audioIntervalRef.current) clearInterval(audioIntervalRef.current);
            return audioDuration;
          }
          return prev + 1;
        });
      }, 1000);
    }
  };

  useEffect(() => {
    return () => {
      if (audioIntervalRef.current) clearInterval(audioIntervalRef.current);
    };
  }, []);

  const formatTime = (s: number) => {
    const m = Math.floor(s / 60);
    const sec = s % 60;
    return `${m}:${String(sec).padStart(2, '0')}`;
  };

  const questions = task?.questions ?? [];
  const question = questions[currentQ] as Question | undefined;
  const answeredCount = Object.keys(answers).filter((k) => answers[k]?.trim()).length;

  const setAnswer = (questionId: string, value: string) => {
    setAnswers((prev) => ({ ...prev, [questionId]: value }));
  };

  const handleSubmit = async () => {
    if (!task || !taskId) return;
    try {
      setSubmitting(true);
      await submitListeningAnswers(taskId, answers);
      localStorage.removeItem(`diag-listening-answers-${taskId}`);
      track('task_submitted', { subTest: 'Listening', mode: 'diagnostic', taskId });
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
      pageTitle="Diagnostic: Listening"
      distractionFree
      navActions={
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="sm" onClick={handleLeave} className="gap-1.5 rounded-full hover:bg-danger/10 hover:text-danger">
            <LogOut className="w-4 h-4" /> Exit
          </Button>
        </div>
      }
    >
      <div className="relative min-h-[calc(100dvh-4rem)] bg-background-light overflow-hidden">

        <div className="max-w-4xl mx-auto px-4 sm:px-6 py-8 flex flex-col h-[calc(100dvh-4rem)] relative z-10">
          <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
            {task && (
              <div className="flex flex-col flex-1 min-h-0">
                {/* Audio player section */}
                <div className="bg-background-dark text-white mb-8 shrink-0 rounded-[2rem] p-6 sm:p-8 shadow-lg ring-1 ring-white/10 overflow-hidden relative">
                  <div className="flex flex-col sm:flex-row items-center gap-6 sm:gap-8 relative z-10">
                    <button
                      onClick={togglePlay}
                      aria-label={isPlaying ? 'Pause audio' : 'Play audio'}
                      aria-pressed={isPlaying}
                      className="w-20 h-20 sm:w-24 sm:h-24 rounded-full bg-primary flex items-center justify-center shrink-0 hoverable:scale-105 active:scale-95 transition-[background-color,transform,box-shadow] duration-200 shadow-lg ring-4 ring-primary/20 group/play"
                    >
                      {isPlaying ? (
                        <Pause className="w-8 h-8 sm:w-10 sm:h-10 text-white fill-white" />
                      ) : (
                        <Play className="w-8 h-8 sm:w-10 sm:h-10 text-white fill-white ml-2 transition-transform group-hover/play:scale-110" />
                      )}
                    </button>

                    <div className="flex-1 w-full min-w-0">
                      <div className="flex items-center gap-3 mb-4">
                        <Headphones className="w-5 h-5 text-lavender shrink-0" aria-hidden="true" />
                        <p className="text-lg sm:text-xl font-black tracking-tight">{task.title}</p>
                      </div>

                      {/* Progress bar */}
                      <div className="w-full h-2.5 sm:h-3 bg-white/10 rounded-full overflow-hidden ring-1 ring-inset ring-white/15">
                        <div
                          className="h-full bg-primary rounded-full transition-[width] duration-300 ease-linear"
                          style={{ width: `${audioDuration ? (audioProgress / audioDuration) * 100 : 0}%` }}
                        />
                      </div>
                      <div className="flex justify-between mt-2.5 text-sm font-bold text-white/50 tracking-wider font-mono">
                        <span>{formatTime(audioProgress)}</span>
                        <span>{formatTime(audioDuration)}</span>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="max-w-2xl mx-auto w-full flex flex-col flex-1 min-h-0">
                  <div className="bg-info/10 border border-info/30 text-info-dark px-4 py-3 rounded-xl mb-6 shrink-0 flex items-center gap-3 font-semibold text-sm shadow-sm">
                    <div className="w-2 h-2 rounded-full bg-info motion-safe:animate-pulse" />
                    Listen to the audio and answer the questions below. You can pause and replay as needed.
                  </div>

                  {/* Question navigator */}
                  <div className="flex items-center justify-between mb-6 shrink-0 bg-surface border border-border p-2 rounded-2xl shadow-sm">
                    <div className="flex gap-2 flex-wrap">
                      {questions.map((q, i) => (
                        <button
                          key={q.id}
                          onClick={() => setCurrentQ(i)}
                          className={cn(
                            'w-10 h-10 rounded-xl text-sm font-black transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200 transform hoverable:-translate-y-0.5 shadow-sm',
                            currentQ === i
                              ? 'bg-primary text-white dark:bg-violet-700 shadow-primary/20 ring-2 ring-primary ring-offset-2'
                              : answers[q.id]?.trim()
                                ? 'bg-success/10 text-success hover:bg-success/20 ring-1 ring-success/20'
                                : 'bg-surface text-muted hover:bg-primary hover:text-white ring-1 ring-border hover:ring-primary/50',
                          )}
                        >
                          {q.number}
                        </button>
                      ))}
                    </div>
                    <span className="text-xs px-3 py-1.5 font-bold uppercase tracking-widest text-muted bg-background-light rounded-lg shrink-0">
                      {answeredCount}/{questions.length} done
                    </span>
                  </div>

                  {/* Current question */}
                  {question && (
                    <div className="flex-1 overflow-y-auto pb-6 scrollbar-hide">
                      <div className="bg-surface border border-border rounded-3xl p-6 sm:p-8 shadow-sm hover:shadow-md transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200">
                        <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-primary/5 text-primary text-xs font-black uppercase tracking-widest mb-4 ring-1 ring-primary/10">
                          <span className="w-1.5 h-1.5 rounded-full bg-primary" />
                          Question {question.number} of {questions.length}
                        </div>
                        <h3 className="text-xl sm:text-2xl font-black text-navy mb-8 leading-snug">{question.text}</h3>

                        {/* MCQ */}
                        {question.type === 'mcq' && question.options && (
                          <div className="space-y-3">
                            {question.options.map((opt, i) => (
                              <label
                                key={i}
                                className={cn(
                                  'flex items-center gap-4 p-4 sm:p-5 rounded-2xl cursor-pointer transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200 group relative overflow-hidden',
                                  answers[question.id] === opt
                                    ? 'bg-primary/[0.03] ring-2 ring-primary shadow-sm shadow-primary/5'
                                    : 'bg-surface hover:bg-primary hover:text-white ring-1 ring-border hover:ring-primary/50 hover:shadow-md',
                                )}
                              >
                                <div className="relative flex items-center justify-center shrink-0">
                                  <input
                                    type="radio"
                                    name={question.id}
                                    value={opt}
                                    checked={answers[question.id] === opt}
                                    onChange={() => setAnswer(question.id, opt)}
                                    className="peer sr-only"
                                  />
                                  <div className={cn(
                                    "w-6 h-6 rounded-full border-2 transition-colors duration-200 flex items-center justify-center shrink-0",
                                    answers[question.id] === opt ? "border-primary" : "border-border-hover group-hover:border-primary/50"
                                  )}>
                                    <div className={cn(
                                      "w-3 h-3 rounded-full bg-primary transition-[transform,opacity] duration-200",
                                      answers[question.id] === opt ? "scale-100 opacity-100" : "scale-50 opacity-0"
                                    )} />
                                  </div>
                                </div>
                                <span className={cn(
                                  "text-base font-medium transition-colors duration-200",
                                  answers[question.id] === opt ? "text-navy font-bold" : "text-navy/70 group-hover:text-white"
                                )}>{opt}</span>
                              </label>
                            ))}
                          </div>
                        )}

                        {/* Short answer */}
                        {(question.type === 'short_answer' || question.type === 'gap_fill') && (
                          <input
                            type="text"
                            value={answers[question.id] ?? ''}
                            onChange={(e) => setAnswer(question.id, e.target.value)}
                            placeholder="Type your answer…"
                            className="w-full px-5 py-4 bg-background-light border border-border rounded-2xl text-lg font-medium text-navy placeholder:text-muted focus:outline-none focus:ring-2 focus:ring-primary transition-[color,background-color,border-color,box-shadow,opacity] duration-200"
                          />
                        )}

                        {/* Matching */}
                        {question.type === 'matching' && question.options && (
                          <select
                            value={answers[question.id] ?? ''}
                            onChange={(e) => setAnswer(question.id, e.target.value)}
                            className="w-full px-5 py-4 bg-background-light border border-border rounded-2xl text-lg font-medium text-navy focus:outline-none focus:ring-2 focus:ring-primary transition-[color,background-color,border-color,box-shadow,opacity] duration-200 appearance-none cursor-pointer"
                          >
                            <option value="" disabled className="text-muted">Select a match…</option>
                            {question.options.map((opt, i) => (
                              <option key={i} value={opt} className="text-navy font-medium">{opt}</option>
                            ))}
                          </select>
                        )}
                      </div>
                    </div>
                  )}

                  {/* Navigation footer */}
                  <div className="pt-5 border-t border-border/50 flex items-center justify-between shrink-0 mb-4 bg-transparent pt-4">
                    <Button
                      variant="outline"
                      size="lg"
                      disabled={currentQ === 0}
                      onClick={() => setCurrentQ((p) => p - 1)}
                      className="gap-2 rounded-full font-bold shadow-sm"
                    >
                      <ChevronLeft className="w-4 h-4" /> Previous
                    </Button>

                    {currentQ < questions.length - 1 ? (
                      <Button
                        size="lg"
                        onClick={() => setCurrentQ((p) => p + 1)}
                        className="gap-2 rounded-full font-bold shadow-sm"
                      >
                        Next <ChevronRight className="w-4 h-4" />
                      </Button>
                    ) : (
                      <Button
                        size="lg"
                        onClick={() => setShowSubmitModal(true)}
                        className="gap-2 rounded-full font-black px-8 shadow-primary/20 shadow-lg group hoverable:scale-105 transition-[color,background-color,border-color,box-shadow,transform,opacity] duration-200"
                      >
                        <Send className="w-4 h-4 group-hoverable:-translate-y-0.5 transition-transform" />
                        Submit Task
                      </Button>
                    )}
                  </div>
                </div>
              </div>
            )}
          </AsyncStateWrapper>
        </div>
      </div>

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
        title="Leave Listening Task?"
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
