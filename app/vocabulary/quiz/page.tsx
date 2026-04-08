'use client';

import { useEffect, useState } from 'react';
import { MotionSection } from '@/components/ui/motion-primitives';
import { HelpCircle, CheckCircle2, XCircle, ArrowLeft, RotateCcw } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
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
      <div className="flex items-center gap-3 mb-6">
        <Link href="/vocabulary" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
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
        <MotionSection className="max-w-md mx-auto text-center py-12">
          <div className="text-6xl font-bold text-gray-900 dark:text-white mb-2">{result.score}/{result.total}</div>
          <div className="text-xl text-gray-500 mb-4">{Math.round((result.score / result.total) * 100)}% correct</div>
          {result.xpAwarded > 0 && (
            <div className="inline-flex items-center gap-1.5 px-4 py-2 bg-yellow-100 dark:bg-yellow-900/30 text-yellow-700 dark:text-yellow-300 rounded-full text-sm font-medium mb-6">
              +{result.xpAwarded} XP earned!
            </div>
          )}
          <div className="flex gap-3 justify-center">
            <Link href="/vocabulary" className="px-5 py-2.5 border border-gray-300 dark:border-gray-600 rounded-xl text-sm font-medium text-gray-700 dark:text-gray-300">
              Back to Vocabulary
            </Link>
            <button onClick={() => { setResult(null); setAnswers({}); setCurrent(0); setSelectedOption(null); setRevealed(false); setLoading(true); fetchVocabQuiz(10).then(d => { setQuestions((d as { questions: QuizQuestion[] }).questions ?? d as QuizQuestion[]); setLoading(false); }); }} className="px-5 py-2.5 bg-green-600 hover:bg-green-700 text-white rounded-xl text-sm font-medium flex items-center gap-1.5">
              <RotateCcw className="w-4 h-4" /> New Quiz
            </button>
          </div>
        </MotionSection>
      ) : q ? (
        <div className="max-w-xl mx-auto">
          <div className="flex items-center justify-between mb-3 text-sm text-gray-500">
            <span>Question {current + 1} of {questions.length}</span>
          </div>
          <div className="w-full bg-gray-200 dark:bg-gray-700 rounded-full h-1.5 mb-6">
            <div className="bg-green-500 h-1.5 rounded-full transition-all" style={{ width: `${Math.min(100, ((current + 1) / questions.length) * 100)}%` }} />
          </div>

          <div className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-6 mb-4">
            <div className="text-xs font-medium text-gray-400 uppercase mb-2">What does this word mean?</div>
            <div className="text-3xl font-bold text-gray-900 dark:text-white">{q.word}</div>
          </div>

          <div className="space-y-2">
            {q.options.map((option, idx) => {
              let cls = 'border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 hover:border-indigo-400 hover:bg-indigo-50 dark:hover:bg-indigo-900/20';
              if (revealed) {
                if (idx === q.correctIndex) cls = 'border border-green-400 bg-green-50 dark:bg-green-900/20 text-green-800 dark:text-green-200';
                else if (idx === selectedOption) cls = 'border border-red-400 bg-red-50 dark:bg-red-900/20 text-red-800 dark:text-red-200';
                else cls = 'border border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 text-gray-500 opacity-60';
              }
              return (
                <button
                  key={idx}
                  onClick={() => handleSelect(idx)}
                  disabled={revealed}
                  className={`w-full text-left px-4 py-3.5 rounded-xl transition-colors flex items-center gap-3 ${cls}`}
                >
                  <span className="flex-shrink-0 w-6 h-6 rounded-full border border-current flex items-center justify-center text-xs font-bold">
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
                className="px-6 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl font-medium text-sm disabled:opacity-50"
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
