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
import { fetchListeningTask, submitListeningAnswers } from '@/lib/api';
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
        <div className="flex items-center gap-3">
          <Button variant="ghost" size="sm" onClick={handleLeave} className="gap-1.5 rounded-full hover:bg-danger/10 hover:text-danger">
            <LogOut className="w-4 h-4" /> Exit
          </Button>
        </div>
      }
    >
      <div className="relative min-h-[calc(100dvh-4rem)] bg-background/30 overflow-hidden">
        <div className="absolute inset-0 bg-[radial-gradient(ellipse_at_top,_var(--tw-gradient-stops))] from-primary/10 via-transparent to-transparent pointer-events-none -z-10 blur-3xl opacity-70" />
        
        <div className="max-w-4xl mx-auto px-4 sm:px-6 py-8 flex flex-col h-[calc(100dvh-4rem)] relative z-10">
          <AsyncStateWrapper status={status} onRetry={load} errorMessage={error}>
            {task && (
              <div className="flex flex-col flex-1 min-h-0">
                {/* Audio player section */}
                <div className="bg-navy/95 backdrop-blur-3xl text-white mb-8 shrink-0 rounded-[2rem] p-6 sm:p-8 shadow-[0_20px_40px_-15px_rgba(0,0,0,0.3)] ring-1 ring-white/10 overflow-hidden relative group">
                  <div className="absolute inset-0 bg-[linear-gradient(45deg,transparent,rgba(255,255,255,0.03),transparent)] -translate-x-[150%] group-hover:translate-x-[150%] transition-transform duration-1000 ease-in-out pointer-events-none" />
                  <div className="absolute top-0 right-0 w-64 h-64 bg-primary/20 rounded-full blur-[80px] pointer-events-none -z-10" />
                  
                  <div className="flex flex-col sm:flex-row items-center gap-6 sm:gap-8 relative z-10">
                    <button
                      onClick={togglePlay}
                      className="w-20 h-20 sm:w-24 sm:h-24 rounded-full bg-gradient-to-tr from-primary to-primary-light flex items-center justify-center shrink-0 hover:scale-105 active:scale-95 transition-all shadow-[0_0_30px_rgba(var(--color-primary),0.3)] ring-4 ring-primary/20 group/play"
                    >
                      {isPlaying ? (
                        <Pause className="w-8 h-8 sm:w-10 sm:h-10 text-white fill-white drop-shadow-md" />
                      ) : (
                        <Play className="w-8 h-8 sm:w-10 sm:h-10 text-white fill-white ml-2 drop-shadow-md transition-transform group-hover/play:scale-110" />
                      )}
                    </button>
                    
                    <div className="flex-1 w-full min-w-0">
                      <div className="flex items-center gap-3 mb-4">
                        <div className="p-2 rounded-xl bg-white/10 backdrop-blur-md ring-1 ring-white/20 shadow-sm">
                          <Headphones className="w-5 h-5 text-lavender" />
                        </div>
                        <p className="text-lg sm:text-xl font-black tracking-tight drop-shadow-sm">{task.title}</p>
                      </div>
                      
                      {/* Progress bar */}
                      <div className="w-full h-2.5 sm:h-3 bg-white/10 rounded-full overflow-hidden shadow-inner ring-1 ring-inset ring-black/20">
                        <div
                          className="h-full bg-gradient-to-r from-primary to-lavender rounded-full shadow-[0_0_10px_rgba(var(--color-primary),0.8)] transition-all duration-300 ease-linear relative"
                          style={{ width: `${audioDuration ? (audioProgress / audioDuration) * 100 : 0}%` }}
                        >
                          <div className="absolute right-0 top-0 bottom-0 w-12 bg-gradient-to-l from-white/30 to-transparent" />
                        </div>
                      </div>
                      <div className="flex justify-between mt-2.5 text-sm font-bold text-white/50 tracking-wider font-mono">
                        <span>{formatTime(audioProgress)}</span>
                        <span>{formatTime(audioDuration)}</span>
                      </div>
                    </div>
                  </div>
                </div>

                <div className="max-w-2xl mx-auto w-full flex flex-col flex-1 min-h-0">
                  <div className="bg-info/10 border-l-4 border-info text-info-dark px-4 py-3 rounded-r-xl mb-6 shrink-0 flex items-center gap-3 font-semibold text-sm shadow-sm">
                    <div className="w-2 h-2 rounded-full bg-info animate-pulse" />
                    Listen to the audio and answer the questions below. You can pause and replay as needed.
                  </div>

                  {/* Question navigator */}
                  <div className="flex items-center justify-between mb-6 shrink-0 bg-white/60 backdrop-blur-xl p-2 rounded-2xl ring-1 ring-black/5 shadow-sm">
                    <div className="flex gap-2 flex-wrap">
                      {questions.map((q, i) => (
                        <button
                          key={q.id}
                          onClick={() => setCurrentQ(i)}
                          className={cn(
                            'w-10 h-10 rounded-xl text-sm font-black transition-all duration-200 transform hover:-translate-y-0.5 shadow-sm',
                            currentQ === i 
                              ? 'bg-primary text-white shadow-primary/20 ring-2 ring-primary ring-offset-2' 
                              : answers[q.id]?.trim() 
                                ? 'bg-success/10 text-success hover:bg-success/20 ring-1 ring-success/20' 
                                : 'bg-white text-navy/60 hover:bg-gray-50 ring-1 ring-black/5',
                          )}
                        >
                          {q.number}
                        </button>
                      ))}
                    </div>
                    <span className="text-xs px-3 py-1.5 font-bold uppercase tracking-widest text-navy/40 bg-white/50 rounded-lg shrink-0">
                      {answeredCount}/{questions.length} done
                    </span>
                  </div>

                  {/* Current question */}
                  {question && (
                    <div className="flex-1 overflow-y-auto pb-6 scrollbar-hide">
                      <div className="bg-white/80 backdrop-blur-2xl rounded-3xl p-6 sm:p-8 shadow-[0_8px_30px_rgb(0,0,0,0.04)] ring-1 ring-black/5 hover:shadow-[0_8px_40px_rgb(0,0,0,0.08)] transition-all">
                        <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-primary/5 text-primary text-xs font-black uppercase tracking-widest mb-4 ring-1 ring-primary/10">
                          <span className="w-1.5 h-1.5 rounded-full bg-primary" />
                          Question {question.number} of {questions.length}
                        </div>
                        <h3 className="text-xl sm:text-2xl font-black text-navy mb-8 leading-snug drop-shadow-sm">{question.text}</h3>

                        {/* MCQ */}
                        {question.type === 'mcq' && question.options && (
                          <div className="space-y-3">
                            {question.options.map((opt, i) => (
                              <label
                                key={i}
                                className={cn(
                                  'flex items-center gap-4 p-4 sm:p-5 rounded-2xl cursor-pointer transition-all duration-200 group relative overflow-hidden',
                                  answers[question.id] === opt
                                    ? 'bg-primary/[0.03] ring-2 ring-primary shadow-sm shadow-primary/5'
                                    : 'bg-white hover:bg-gray-50/80 ring-1 ring-black/5 hover:ring-black/10 hover:shadow-md',
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
                                    "w-6 h-6 rounded-full border-2 transition-all duration-200 flex items-center justify-center shrink-0",
                                    answers[question.id] === opt ? "border-primary" : "border-navy/20 group-hover:border-primary/50"
                                  )}>
                                    <div className={cn(
                                      "w-3 h-3 rounded-full bg-primary transition-all duration-200",
                                      answers[question.id] === opt ? "scale-100" : "scale-0"
                                    )} />
                                  </div>
                                </div>
                                <span className={cn(
                                  "text-base font-medium transition-colors duration-200",
                                  answers[question.id] === opt ? "text-navy font-bold" : "text-navy/70 group-hover:text-navy"
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
                            className="w-full px-5 py-4 border-0 bg-white/50 ring-1 ring-black/5 rounded-2xl text-lg font-medium text-navy placeholder:text-navy/30 focus:outline-none focus:ring-2 focus:ring-primary shadow-inner transition-all"
                          />
                        )}

                        {/* Matching */}
                        {question.type === 'matching' && question.options && (
                          <select
                            value={answers[question.id] ?? ''}
                            onChange={(e) => setAnswer(question.id, e.target.value)}
                            className="w-full px-5 py-4 border-0 bg-white/50 ring-1 ring-black/5 rounded-2xl text-lg font-medium text-navy focus:outline-none focus:ring-2 focus:ring-primary shadow-inner transition-all appearance-none cursor-pointer"
                          >
                            <option value="" disabled className="text-navy/30">Select a match…</option>
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
                        className="gap-2 rounded-full font-black px-8 shadow-primary/20 shadow-lg group hover:scale-105 transition-all"
                      >
                        <Send className="w-4 h-4 group-hover:-translate-y-0.5 group-hover:translate-x-0.5 transition-transform" /> 
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
