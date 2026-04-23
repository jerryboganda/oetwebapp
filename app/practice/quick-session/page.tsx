'use client';

import { useEffect, useState, useCallback } from 'react';
import { Play, CheckCircle2, XCircle, ChevronRight, RotateCcw, Timer, Volume2, BookOpen, Headphones, Trophy } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { ProgressBar } from '@/components/ui/progress';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';

/* ── types ─────────────────────────────────────── */
interface QuickQuestion {
  id: string;
  type: 'vocab' | 'listening' | 'grammar';
  prompt: string;
  options: string[];
  correctIndex: number;
  explanation: string;
  audioUrl?: string;
}

interface SessionConfig {
  questions: QuickQuestion[];
  sessionId: string;
  timeLimit: number; // seconds
}

/* ── api helper ───────────────────────────────── */
async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

/* ── fallback question bank (used when /v1/learner/quick-session is unavailable) ── */
function generateFallbackSession(): SessionConfig {
  const vocabWords = [
    { prompt: 'The patient presented with acute ___ in the lower abdomen.', options: ['pain', 'pane', 'bane', 'gain'], correctIndex: 0, explanation: '"Pain" is the correct medical term for physical discomfort.' },
    { prompt: 'Select the word that means "difficulty breathing":', options: ['Dysphagia', 'Dyspnoea', 'Dysuria', 'Dystonia'], correctIndex: 1, explanation: 'Dyspnoea refers to difficulty or laboured breathing.' },
    { prompt: 'The nurse should ___ the wound twice daily.', options: ['dress', 'address', 'distress', 'compress'], correctIndex: 0, explanation: '"Dress" means to clean and apply a covering to a wound.' },
    { prompt: 'Which term describes a slow heart rate?', options: ['Tachycardia', 'Arrhythmia', 'Bradycardia', 'Fibrillation'], correctIndex: 2, explanation: 'Bradycardia = heart rate below 60 bpm.' },
    { prompt: 'A medication taken ___ means "by mouth".', options: ['parenterally', 'orally', 'topically', 'rectally'], correctIndex: 1, explanation: 'Orally (per os / PO) means taken by mouth.' },
    { prompt: 'The patient was ___ to the recovery ward.', options: ['transferred', 'transformed', 'transposed', 'transmitted'], correctIndex: 0, explanation: '"Transferred" means moved from one place to another.' },
    { prompt: '"PRN" in prescriptions means:', options: ['Every 4 hours', 'As needed', 'Before meals', 'At bedtime'], correctIndex: 1, explanation: 'PRN = pro re nata = "as the situation demands" / as needed.' },
    { prompt: 'Which describes inflammation of the liver?', options: ['Nephritis', 'Hepatitis', 'Arthritis', 'Dermatitis'], correctIndex: 1, explanation: 'Hepatitis = inflammation of the liver (hepat- = liver, -itis = inflammation).' },
  ];
  const questions: QuickQuestion[] = vocabWords.slice(0, 8).map((q, i) => ({
    id: `q-${i}`, type: 'vocab' as const, ...q,
  }));
  return { questions, sessionId: `qs-${Date.now()}`, timeLimit: 300 };
}

/* ── page component ──────────────────────────── */
type QuickStep = 'menu' | 'session' | 'results';

export default function MobileQuickSessionPage() {
  const [step, setStep] = useState<QuickStep>('menu');
  const [config, setConfig] = useState<SessionConfig | null>(null);
  const [currentQ, setCurrentQ] = useState(0);
  const [answers, setAnswers] = useState<(number | null)[]>([]);
  const [revealed, setRevealed] = useState(false);
  const [elapsed, setElapsed] = useState(0);
  const [loading, setLoading] = useState(false);
  const [usingFallback, setUsingFallback] = useState(false);

  /* timer */
  useEffect(() => {
    if (step !== 'session' || !config) return;
    const t = setInterval(() => setElapsed(p => p + 1), 1000);
    return () => clearInterval(t);
  }, [step, config]);

  /* start a session */
  const startSession = useCallback(async (mode: string) => {
    setLoading(true);
    analytics.track('quick_session_started', { mode });
    try {
      let data: SessionConfig;
      try {
        data = await apiRequest<SessionConfig>(`/v1/learner/quick-session?mode=${mode}`);
        setUsingFallback(false);
      } catch {
        data = generateFallbackSession();
        setUsingFallback(true);
      }
      setConfig(data);
      setAnswers(new Array(data.questions.length).fill(null));
      setCurrentQ(0);
      setElapsed(0);
      setRevealed(false);
      setStep('session');
    } catch {
      const fallback = generateFallbackSession();
      setConfig(fallback);
      setUsingFallback(true);
      setAnswers(new Array(fallback.questions.length).fill(null));
      setStep('session');
    }
    setLoading(false);
  }, []);

  /* answer a question */
  const selectAnswer = (idx: number) => {
    if (revealed) return;
    setAnswers(prev => { const n = [...prev]; n[currentQ] = idx; return n; });
    setRevealed(true);
  };

  /* next question */
  const nextQuestion = () => {
    if (!config) return;
    if (currentQ + 1 >= config.questions.length) {
      setStep('results');
      analytics.track('quick_session_completed', { correct: answers.filter((a, i) => a === config.questions[i].correctIndex).length, total: config.questions.length });
    } else {
      setCurrentQ(p => p + 1);
      setRevealed(false);
    }
  };

  /* stats */
  const correctCount = config ? answers.filter((a, i) => a === config.questions[i]?.correctIndex).length : 0;
  const progress = config ? ((currentQ + (revealed ? 1 : 0)) / config.questions.length) * 100 : 0;
  const formatTime = (s: number) => `${Math.floor(s / 60)}:${String(s % 60).padStart(2, '0')}`;
  const heroHighlights = [
    { icon: Timer, label: 'Session length', value: '5 mins' },
    { icon: CheckCircle2, label: 'Format', value: 'Adaptive drills' },
    { icon: Trophy, label: 'Outcome', value: 'Fast recall' },
  ];

  /* ── render ────────────────────────────────── */
  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Practice"
          icon={Play}
          title="Quick Practice"
          description="Short, focused drills that fit into a small study window without losing the dashboard feel."
          highlights={heroHighlights}
        />

        {/* ── MENU ──────────────────────────── */}
        {step === 'menu' && (
          <>
            <MotionSection className="space-y-4">
              <LearnerSurfaceSectionHeader
                eyebrow="Choose a drill"
                title="Start with a focused micro-session"
                description="Each card below uses the same surface language as the dashboard, just in a denser format."
              />
              {[
                { mode: 'vocab', icon: BookOpen, label: 'Medical Vocabulary', desc: '8 questions · Fill-in and multiple choice', color: 'bg-info/10 text-info border-info/30' },
                { mode: 'listening', icon: Headphones, label: 'Listening Snap Quiz', desc: '5 audio clips with comprehension questions', color: 'bg-primary/10 text-primary border-primary/30' },
                { mode: 'grammar', icon: BookOpen, label: 'Grammar Quick-Fix', desc: '8 sentence correction exercises', color: 'bg-success/10 text-success border-success/30' },
              ].map(m => (
                <MotionItem key={m.mode}>
                  <Card className="p-5 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99] cursor-pointer" onClick={() => startSession(m.mode)}>
                    <div className="flex items-start gap-4">
                      <div className={`flex h-11 w-11 items-center justify-center rounded-2xl border ${m.color}`}>
                        <m.icon className="h-5 w-5" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="text-sm font-semibold text-navy">{m.label}</p>
                        <p className="mt-1 text-sm leading-6 text-muted">{m.desc}</p>
                      </div>
                      <ChevronRight className="mt-1 h-4 w-4 text-muted" />
                    </div>
                  </Card>
                </MotionItem>
              ))}
            </MotionSection>

            {loading && <div className="flex justify-center mt-6"><Skeleton className="h-6 w-32" /></div>}
          </>
        )}

        {/* ── SESSION ───────────────────────── */}
        {step === 'session' && config && config.questions[currentQ] && (() => {
          const q = config.questions[currentQ];
          const userAnswer = answers[currentQ];
          return (
            <MotionSection className="space-y-6">
              {/* progress bar */}
              <LearnerSurfaceSectionHeader
                eyebrow="Live session"
                title="Answer, check, and move on"
                description="The session keeps the same calm surface language as the rest of the learner workspace."
              />

              <Card className="p-5 shadow-sm">
                <div className="flex items-center justify-between text-xs text-muted-foreground">
                  <span>Question {currentQ + 1} of {config.questions.length}</span>
                  <span className="flex items-center gap-1"><Timer className="h-3 w-3" />{formatTime(elapsed)}</span>
                </div>
                <ProgressBar value={progress} className="h-1.5" />
              </Card>

              {/* question */}
              <Card className="p-5 shadow-sm">
                <Badge variant="outline" className="mb-3 text-[10px] capitalize">{q.type}</Badge>
                <p className="text-sm font-medium leading-relaxed text-navy">{q.prompt}</p>
                {q.audioUrl && (
                  <button className="mt-3 flex items-center gap-2 text-sm text-primary">
                    <Volume2 className="h-4 w-4" /> Play audio
                  </button>
                )}
              </Card>

              {/* options */}
              <MotionSection className="space-y-2">
                {q.options.map((opt, i) => {
                  let cls = 'border-border bg-muted/20';
                  if (revealed) {
                    if (i === q.correctIndex) cls = 'border-success bg-success/10';
                    else if (i === userAnswer && i !== q.correctIndex) cls = 'border-danger bg-danger/10';
                  } else if (userAnswer === i) {
                    cls = 'border-primary bg-primary/5';
                  }
                  return (
                    <MotionItem key={i}>
                      <button
                        onClick={() => selectAnswer(i)}
                        disabled={revealed}
                        className={`w-full rounded-2xl border px-4 py-3 text-left text-sm transition-[background-color,border-color,transform,box-shadow] duration-200 hover:shadow-sm active:scale-[0.99] ${cls}`}
                      >
                        <div className="flex items-center gap-3">
                          <span className="h-6 w-6 rounded-full border flex items-center justify-center text-xs font-medium shrink-0">
                            {String.fromCharCode(65 + i)}
                          </span>
                          <span>{opt}</span>
                          {revealed && i === q.correctIndex && <CheckCircle2 className="h-4 w-4 text-success ml-auto shrink-0" />}
                          {revealed && i === userAnswer && i !== q.correctIndex && <XCircle className="h-4 w-4 text-danger ml-auto shrink-0" />}
                        </div>
                      </button>
                    </MotionItem>
                  );
                })}
              </MotionSection>

              {/* explanation */}
              {revealed && (
                <Card className="border-info/30 bg-info/10 p-4 shadow-sm">
                  <p className="text-xs font-medium text-info mb-1">Explanation</p>
                  <p className="text-sm text-muted-foreground">{q.explanation}</p>
                </Card>
              )}

              {/* next button */}
              {revealed && (
                <Button className="w-full" size="lg" onClick={nextQuestion}>
                  {currentQ + 1 >= config.questions.length ? 'See Results' : 'Next Question'} <ChevronRight className="h-4 w-4 ml-1" />
                </Button>
              )}
            </MotionSection>
          );
        })()}

        {/* ── RESULTS ───────────────────────── */}
        {step === 'results' && config && (
          <MotionSection className="space-y-6 py-2">
            <LearnerSurfaceSectionHeader
              eyebrow="Session complete"
              title="Quick practice results"
              description="The summary follows the same surface and hierarchy rules as the main dashboard."
            />

            <div className="rounded-3xl border border-border bg-surface p-6 text-center shadow-sm">
              <div className="inline-flex items-center justify-center h-20 w-20 rounded-full bg-primary/10 mb-4">
              <Trophy className="h-10 w-10 text-primary" />
              </div>
              <h2 className="text-xl font-bold mb-1 text-navy">Session Complete</h2>
              <p className="text-sm text-muted mb-6">in {formatTime(elapsed)}</p>

              <Card className="p-5 mb-6 text-left shadow-sm">
              <div className="grid grid-cols-3 gap-4 text-center">
                <div>
                  <p className="text-2xl font-bold text-primary">{correctCount}</p>
                  <p className="text-xs text-muted-foreground">Correct</p>
                </div>
                <div>
                  <p className="text-2xl font-bold">{config.questions.length - correctCount}</p>
                  <p className="text-xs text-muted-foreground">Incorrect</p>
                </div>
                <div>
                  <p className="text-2xl font-bold">{Math.round((correctCount / config.questions.length) * 100)}%</p>
                  <p className="text-xs text-muted-foreground">Accuracy</p>
                </div>
                </div>
              </Card>

            {/* question breakdown */}
            <div className="space-y-2 mb-6 text-left">
              {config.questions.map((q, i) => {
                const correct = answers[i] === q.correctIndex;
                return (
                  <div key={q.id} className="flex items-center gap-2 text-sm">
                    {correct ? <CheckCircle2 className="h-4 w-4 text-success shrink-0" /> : <XCircle className="h-4 w-4 text-danger shrink-0" />}
                    <span className="truncate flex-1">{q.prompt.slice(0, 50)}…</span>
                  </div>
                );
              })}
            </div>

            <div className="flex gap-3">
              <Button variant="outline" className="flex-1" onClick={() => { setStep('menu'); setConfig(null); }}>
                <RotateCcw className="h-4 w-4 mr-2" />Back
              </Button>
              <Button className="flex-1" onClick={() => startSession('vocab')}>
                <Play className="h-4 w-4 mr-2" />Play Again
              </Button>
            </div>
            </div>
          </MotionSection>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
