'use client';

import { useEffect, useState } from 'react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { HelpCircle, CheckCircle2, XCircle, ArrowLeft, RotateCcw } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchVocabQuiz, submitVocabQuiz } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type QuizQuestion = { termId: string; word: string; options: string[]; correctIndex: number };
type QuizResult = { score: number; total: number; xpAwarded: number };

export default function VocabQuizPage() {
  const [questions, setQuestions] = useState<QuizQuestion[]>([]);
  const [answers, setAnswers] = useState<Record<string, number>>({});
  const [current, setCurrent] = useState(0);
  const [selectedOption, setSelectedOption] = useState<number | null>(null);
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [result, setResult] = useState<QuizResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [revealed, setRevealed] = useState(false);

  useEffect(() => {
    analytics.track('vocab_quiz_viewed');
    fetchVocabQuiz(10).then(data => {
      const loadedQuestions = Array.isArray(data) ? data : (data as { questions?: QuizQuestion[] }).questions ?? [];
      setQuestions(loadedQuestions as QuizQuestion[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load quiz.');
      setLoading(false);
    });
  }, []);

  const q = questions[current];

  function handleSelect(idx: number) {
    if (revealed) return;
    setSelectedOption(idx);
    setRevealed(true);
    setAnswers(prev => ({ ...prev, [q.termId]: idx }));
  }

  function nextQuestion() {
    setRevealed(false);
    setSelectedOption(null);
    if (current + 1 >= questions.length) {
      handleSubmit();
    } else {
      setCurrent(c => c + 1);
    }
  }

  async function handleSubmit() {
    setSubmitting(true);
    try {
      const answerList = Object.entries(answers).map(([termId, selectedIndex]) => ({
        termId,
        correct: selectedIndex === (questions.find(q => q.termId === termId)?.correctIndex ?? -1),
        userAnswer: String(selectedIndex),
      }));
      const payload = { answers: answerList, durationSeconds: 60 };
      const res = await submitVocabQuiz(payload);
      setResult(res as QuizResult);
    } catch {
      setError('Failed to submit quiz.');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero title="Vocabulary Quiz" description="Test your medical vocabulary knowledge" icon={HelpCircle} />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-48 rounded-2xl" />
          {Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-12 rounded-xl" />)}
        </div>
      ) : result ? (
        <MotionSection className="mx-auto max-w-md py-12 text-center">
          <Card className="border-gray-200 bg-surface p-8">
            <div className="text-6xl font-bold text-navy mb-2">{result.score}/{result.total}</div>
            <div className="text-xl text-muted mb-4">{Math.round((result.score / result.total) * 100)}% correct</div>
          {result.xpAwarded > 0 && (
            <div className="inline-flex items-center gap-1.5 rounded-full bg-amber-100 px-4 py-2 text-sm font-medium text-amber-700 mb-6">
              +{result.xpAwarded} XP earned!
            </div>
          )}
          <div className="flex gap-3 justify-center">
            <Link href="/vocabulary" className="rounded-xl border border-gray-200 bg-background-light px-5 py-2.5 text-sm font-medium text-navy shadow-sm transition-colors hover:border-primary/30 hover:bg-surface">
              Back to Vocabulary
            </Link>
            <button onClick={() => { setResult(null); setAnswers({}); setCurrent(0); setSelectedOption(null); setRevealed(false); setLoading(true); fetchVocabQuiz(10).then(d => { setQuestions((d as { questions: QuizQuestion[] }).questions ?? d as QuizQuestion[]); setLoading(false); }); }} className="inline-flex items-center gap-1.5 rounded-xl bg-primary px-5 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90">
              <RotateCcw className="w-4 h-4" /> New Quiz
            </button>
          </div>
          </Card>
        </MotionSection>
      ) : q ? (
        <div className="mx-auto max-w-xl">
          <LearnerSurfaceSectionHeader
            eyebrow="Quiz Progress"
            title="Answer the question"
            description="Choose the best meaning for the medical term."
            className="mb-4"
          />
          <div className="mb-3 flex items-center justify-between text-sm text-muted">
            <span>Question {current + 1} of {questions.length}</span>
          </div>
          <div className="mb-6 h-1.5 w-full rounded-full bg-background-light">
            <div className="h-1.5 rounded-full bg-primary transition-all" style={{ width: `${Math.min(100, ((current + 1) / questions.length) * 100)}%` }} />
          </div>

          <Card className="mb-4 border-gray-200 bg-surface p-6">
            <div className="mb-2 text-xs font-medium uppercase text-muted">What does this word mean?</div>
            <div className="text-3xl font-bold text-navy">{q.word}</div>
          </Card>

          <div className="space-y-2">
            {q.options.map((option, idx) => {
              let cls = 'border border-gray-200 bg-surface text-navy hover:border-primary/30 hover:bg-background-light';
              if (revealed) {
                if (idx === q.correctIndex) cls = 'border border-green-300 bg-green-50 text-green-900';
                else if (idx === selectedOption) cls = 'border border-red-300 bg-red-50 text-red-900';
                else cls = 'border border-gray-200 bg-background-light text-muted opacity-70';
              }
              return (
                <button
                  key={idx}
                  onClick={() => handleSelect(idx)}
                  disabled={revealed}
                  className={`flex w-full items-center gap-3 rounded-2xl px-4 py-3.5 text-left transition-colors ${cls}`}
                >
                  <span className="flex h-6 w-6 flex-shrink-0 items-center justify-center rounded-full border border-current text-xs font-bold">
                    {String.fromCharCode(65 + idx)}
                  </span>
                  <span className="text-sm">{option}</span>
                  {revealed && idx === q.correctIndex && <CheckCircle2 className="w-4 h-4 text-green-500 ml-auto" />}
                  {revealed && idx === selectedOption && idx !== q.correctIndex && <XCircle className="w-4 h-4 text-red-500 ml-auto" />}
                </button>
              );
            })}
          </div>

          {revealed && (
            <MotionSection className="mt-4 flex justify-end">
              <button
                onClick={nextQuestion}
                disabled={submitting}
                className="rounded-xl bg-primary px-6 py-2.5 text-sm font-medium text-white shadow-sm transition-colors hover:bg-primary/90 disabled:opacity-50"
              >
                {current + 1 >= questions.length ? 'Finish Quiz' : 'Next Question →'}
              </button>
            </MotionSection>
          )}
        </div>
      ) : (
        <div className="text-center py-12 text-gray-400">No quiz questions available. Add more words to your vocabulary list first.</div>
      )}
    </LearnerDashboardShell>
  );
}
