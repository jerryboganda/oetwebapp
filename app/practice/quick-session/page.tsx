'use client';

import { useEffect, useState, useCallback } from 'react';
import { Play, CheckCircle2, XCircle, ChevronRight, RotateCcw, Timer, Volume2, BookOpen, Headphones, Trophy } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Progress } from '@/components/ui/progress';
import LearnerDashboardShell from '@/components/learner/LearnerDashboardShell';
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

/* ── mock data generator (until backend endpoint exists) ── */
function generateMockSession(): SessionConfig {
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
      // Try backend first, fall back to mock
      const data = await apiRequest<SessionConfig>(`/v1/learner/quick-session?mode=${mode}`).catch(() => generateMockSession());
      setConfig(data);
      setAnswers(new Array(data.questions.length).fill(null));
      setCurrentQ(0);
      setElapsed(0);
      setRevealed(false);
      setStep('session');
    } catch { setConfig(generateMockSession()); setStep('session'); }
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

  /* ── render ────────────────────────────────── */
  return (
    <LearnerDashboardShell>
      <div className="max-w-lg mx-auto px-4 py-6">

        {/* ── MENU ──────────────────────────── */}
        {step === 'menu' && (
          <>
            <div className="mb-6">
              <h1 className="text-xl font-bold mb-1">Quick Practice</h1>
              <p className="text-sm text-muted-foreground">5-minute drills designed for mobile sessions</p>
            </div>

            <MotionSection className="space-y-3">
              {[
                { mode: 'vocab', icon: BookOpen, label: 'Medical Vocabulary', desc: '8 questions · Fill-in & multiple choice', color: 'text-blue-500' },
                { mode: 'listening', icon: Headphones, label: 'Listening Snap Quiz', desc: '5 audio clips with comprehension Qs', color: 'text-purple-500' },
                { mode: 'grammar', icon: BookOpen, label: 'Grammar Quick-Fix', desc: '8 sentence correction exercises', color: 'text-emerald-500' },
              ].map(m => (
                <MotionItem key={m.mode}>
                  <Card className="p-4 active:scale-[0.98] transition-transform cursor-pointer" onClick={() => startSession(m.mode)}>
                    <div className="flex items-center gap-3">
                      <div className={`h-10 w-10 rounded-lg bg-muted/50 flex items-center justify-center ${m.color}`}>
                        <m.icon className="h-5 w-5" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <p className="font-medium text-sm">{m.label}</p>
                        <p className="text-xs text-muted-foreground">{m.desc}</p>
                      </div>
                      <ChevronRight className="h-4 w-4 text-muted-foreground" />
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
            <>
              {/* progress bar */}
              <div className="mb-4 space-y-2">
                <div className="flex items-center justify-between text-xs text-muted-foreground">
                  <span>Question {currentQ + 1} of {config.questions.length}</span>
                  <span className="flex items-center gap-1"><Timer className="h-3 w-3" />{formatTime(elapsed)}</span>
                </div>
                <Progress value={progress} className="h-1.5" />
              </div>

              {/* question */}
              <Card className="p-5 mb-4">
                <Badge variant="outline" className="text-[10px] mb-3 capitalize">{q.type}</Badge>
                <p className="text-sm font-medium leading-relaxed">{q.prompt}</p>
                {q.audioUrl && (
                  <button className="mt-3 flex items-center gap-2 text-sm text-primary">
                    <Volume2 className="h-4 w-4" /> Play audio
                  </button>
                )}
              </Card>

              {/* options */}
              <MotionSection className="space-y-2 mb-4">
                {q.options.map((opt, i) => {
                  let cls = 'border-border bg-muted/20';
                  if (revealed) {
                    if (i === q.correctIndex) cls = 'border-green-500 bg-green-50 dark:bg-green-950';
                    else if (i === userAnswer && i !== q.correctIndex) cls = 'border-red-500 bg-red-50 dark:bg-red-950';
                  } else if (userAnswer === i) {
                    cls = 'border-primary bg-primary/5';
                  }
                  return (
                    <MotionItem key={i}>
                      <button
                        onClick={() => selectAnswer(i)}
                        disabled={revealed}
                        className={`w-full text-left px-4 py-3 rounded-lg border text-sm transition-all ${cls}`}
                      >
                        <div className="flex items-center gap-3">
                          <span className="h-6 w-6 rounded-full border flex items-center justify-center text-xs font-medium shrink-0">
                            {String.fromCharCode(65 + i)}
                          </span>
                          <span>{opt}</span>
                          {revealed && i === q.correctIndex && <CheckCircle2 className="h-4 w-4 text-green-600 ml-auto shrink-0" />}
                          {revealed && i === userAnswer && i !== q.correctIndex && <XCircle className="h-4 w-4 text-red-500 ml-auto shrink-0" />}
                        </div>
                      </button>
                    </MotionItem>
                  );
                })}
              </MotionSection>

              {/* explanation */}
              {revealed && (
                <Card className="p-4 mb-4 border-blue-200 dark:border-blue-800 bg-blue-50/50 dark:bg-blue-950/30">
                  <p className="text-xs font-medium text-blue-700 dark:text-blue-300 mb-1">Explanation</p>
                  <p className="text-sm text-muted-foreground">{q.explanation}</p>
                </Card>
              )}

              {/* next button */}
              {revealed && (
                <Button className="w-full" size="lg" onClick={nextQuestion}>
                  {currentQ + 1 >= config.questions.length ? 'See Results' : 'Next Question'} <ChevronRight className="h-4 w-4 ml-1" />
                </Button>
              )}
            </>
          );
        })()}

        {/* ── RESULTS ───────────────────────── */}
        {step === 'results' && config && (
          <div className="py-8 text-center">
            <div className="inline-flex items-center justify-center h-20 w-20 rounded-full bg-primary/10 mb-4">
              <Trophy className="h-10 w-10 text-primary" />
            </div>
            <h2 className="text-xl font-bold mb-1">Session Complete</h2>
            <p className="text-sm text-muted-foreground mb-6">in {formatTime(elapsed)}</p>

            <Card className="p-5 mb-6 text-left">
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
                    {correct ? <CheckCircle2 className="h-4 w-4 text-green-600 shrink-0" /> : <XCircle className="h-4 w-4 text-red-500 shrink-0" />}
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
        )}
      </div>
    </LearnerDashboardShell>
  );
}
