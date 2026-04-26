'use client';

import { AppShell } from "@/components/layout/app-shell";
import { AsyncStateWrapper } from "@/components/state/async-state-wrapper";
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Modal } from '@/components/ui/modal';
import { useAnalytics } from '@/hooks/use-analytics';
import { fetchListeningTask, submitListeningAnswers } from '@/lib/api';
import type { ListeningTask, Question } from '@/lib/mock-data';
import { cn } from '@/lib/utils';
import {
    CheckCircle2, ChevronLeft,
    ChevronRight, Headphones, LogOut, Pause, Play, Send, Volume2
} from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useRef, useState } from 'react';

const DIAGNOSTIC_LISTENING_TASK_ID = 'lt-001';
const LOCAL_STORAGE_KEY = `diag-listening-answers-${DIAGNOSTIC_LISTENING_TASK_ID}`;

export default function DiagnosticListeningPage() {
  const router = useRouter();
  const { track } = useAnalytics();

  const [task, setTask] = useState<ListeningTask | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string>();
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
      const t = await fetchListeningTask(DIAGNOSTIC_LISTENING_TASK_ID);
      setTask(t);
      setAudioDuration(t.duration);
      // Restore saved answers
      const saved = localStorage.getItem(LOCAL_STORAGE_KEY);
      if (saved) {
        try { setAnswers(JSON.parse(saved)); } catch { /* ignore */ }
      }
      track('task_started', { subTest: 'Listening', mode: 'diagnostic', taskId: DIAGNOSTIC_LISTENING_TASK_ID });
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load listening task');
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
    if (!task) return;
    try {
      setSubmitting(true);
      await submitListeningAnswers(DIAGNOSTIC_LISTENING_TASK_ID, answers);
      localStorage.removeItem(LOCAL_STORAGE_KEY);
      track('task_submitted', { subTest: 'Listening', mode: 'diagnostic', taskId: DIAGNOSTIC_LISTENING_TASK_ID });
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
      pageTitle="Diagnostic — Listening"
      distractionFree
      navActions={
        <Button variant="ghost" size="sm" onClick={handleLeave} className="gap-1.5">
          <LogOut className="w-3.5 h-3.5" /> Exit
        </Button>
      }
    >
      <div className="max-w-3xl mx-auto px-4 sm:px-6 py-6 flex flex-col h-[calc(100dvh-4rem)]">
        <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
          {task && (
            <div className="flex flex-col flex-1 min-h-0">
              {/* Audio player section */}
              <Card className="bg-navy text-white mb-6 shrink-0">
                <div className="flex items-center gap-4">
                  <button
                    onClick={togglePlay}
                    className="w-12 h-12 rounded-full bg-primary flex items-center justify-center shrink-0 hover:bg-primary/90 transition-colors"
                  >
                    {isPlaying ? (
                      <Pause className="w-5 h-5 text-white" />
                    ) : (
                      <Play className="w-5 h-5 text-white ml-0.5" />
                    )}
                  </button>
                  <div className="flex-1 min-w-0">
                    <div className="flex items-center gap-2 mb-2">
                      <Headphones className="w-4 h-4 text-lavender" />
                      <p className="text-sm font-semibold">{task.title}</p>
                    </div>
                    {/* Progress bar */}
                    <div className="w-full h-1.5 bg-white/20 rounded-full overflow-hidden">
                      <div
                        className="h-full bg-primary rounded-full transition-all duration-500"
                        style={{ width: `${audioDuration ? (audioProgress / audioDuration) * 100 : 0}%` }}
                      />
                    </div>
                    <div className="flex justify-between mt-1 text-xs text-white/60">
                      <span>{formatTime(audioProgress)}</span>
                      <span>{formatTime(audioDuration)}</span>
                    </div>
                  </div>
                  <Volume2 className="w-4 h-4 text-white/40 shrink-0" />
                </div>
              </Card>

              <InlineAlert variant="info" className="mb-4 shrink-0" dismissible>
                Listen to the audio and answer the questions below. You can pause and replay as needed.
              </InlineAlert>

              {/* Question navigator */}
              <div className="flex gap-1.5 flex-wrap mb-4 shrink-0">
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
                <span className="text-xs text-muted self-center ml-2">
                  {answeredCount}/{questions.length} answered
                </span>
              </div>

              {/* Current question */}
              {question && (
                <div className="flex-1 overflow-y-auto">
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

                    {/* Short answer */}
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
                          <option key={i} value={opt}>{opt}</option>
                        ))}
                      </select>
                    )}
                  </Card>
                </div>
              )}

              {/* Navigation footer */}
              <div className="pt-4 border-t border-border flex items-center justify-between shrink-0 mt-4">
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
          )}
        </AsyncStateWrapper>
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
