'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import {
  startDiagnostic,
  submitDiagnostic,
  type DiagnosticResultDto,
  type PracticeSessionDto,
} from '@/lib/reading-pathway-api';

type FlowState = 'brief' | 'loading' | 'testing' | 'submitting';

// Minimal shape we need from question data.
interface DiagnosticQuestion {
  id: string;
  stem: string;
  options: Record<string, string>;
}

// We generate placeholder questions from the question IDs returned by the server,
// since the actual question content lives in the reading-papers endpoint.
// A real implementation would fetch each question via the reading-papers API.
function makePlaceholderQuestions(ids: string[]): DiagnosticQuestion[] {
  return ids.map((id) => ({
    id,
    stem: `Question (ID: ${id}) — question text will load from the reading-papers service.`,
    options: { A: 'Option A', B: 'Option B', C: 'Option C', D: 'Option D' },
  }));
}

function LoadingSpinner({ label }: { label: string }) {
  return (
    <div className="flex flex-col items-center gap-4">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      <p className="text-sm text-gray-500">{label}</p>
    </div>
  );
}

export default function DiagnosticPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();

  const [flowState, setFlowState] = useState<FlowState>('brief');
  const [session, setSession] = useState<PracticeSessionDto | null>(null);
  const [questions, setQuestions] = useState<DiagnosticQuestion[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [startError, setStartError] = useState<string | null>(null);

  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);

  // Auth guard
  useEffect(() => {
    if (!authLoading && !isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  // Timer while testing
  useEffect(() => {
    if (flowState === 'testing') {
      timerRef.current = setInterval(() => setElapsedSeconds((s) => s + 1), 1000);
    } else {
      if (timerRef.current) clearInterval(timerRef.current);
    }
    return () => {
      if (timerRef.current) clearInterval(timerRef.current);
    };
  }, [flowState]);

  const handleStart = async () => {
    setStartError(null);
    setFlowState('loading');
    try {
      const sess = await startDiagnostic();
      setSession(sess);
      const qs = makePlaceholderQuestions(sess.questionIds);
      setQuestions(qs);
      setFlowState('testing');
    } catch {
      setStartError('Could not start the diagnostic. Please try again.');
      setFlowState('brief');
    }
  };

  const currentQuestion = questions[currentIndex] ?? null;
  const totalQuestions = questions.length;

  const selectOption = (option: string) => {
    if (!currentQuestion) return;
    setAnswers((prev) => ({ ...prev, [currentQuestion.id]: option }));
  };

  const markUnknown = () => {
    if (!currentQuestion) return;
    setAnswers((prev) => ({ ...prev, [currentQuestion.id]: '__unknown__' }));
  };

  const handleNext = () => {
    if (currentIndex < totalQuestions - 1) {
      setCurrentIndex((i) => i + 1);
    }
  };

  const handleFinish = async () => {
    if (!session) return;
    setFlowState('submitting');
    try {
      const result: DiagnosticResultDto = await submitDiagnostic(session.sessionId, answers);
      // Store result for the results page to read
      if (typeof window !== 'undefined') {
        sessionStorage.setItem('diagnostic_result', JSON.stringify(result));
      }
      router.push(`/reading/diagnostic-results?sessionId=${encodeURIComponent(session.sessionId)}`);
    } catch {
      setFlowState('testing');
    }
  };

  const elapsedMinutes = Math.floor(elapsedSeconds / 60);
  const elapsedSecondsRemainder = elapsedSeconds % 60;
  const timerLabel = `${elapsedMinutes}:${String(elapsedSecondsRemainder).padStart(2, '0')}`;

  const selectedOption = currentQuestion ? (answers[currentQuestion.id] ?? null) : null;
  const isLastQuestion = currentIndex === totalQuestions - 1;

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <LoadingSpinner label="Loading…" />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-gradient-to-b from-violet-50 to-white px-4 py-12">
      <div className="w-full max-w-2xl">
        {/* Brief state */}
        {flowState === 'brief' && (
          <div className="rounded-2xl border border-gray-100 bg-white p-8 shadow-sm">
            <div className="mb-6 text-center">
              <div className="mb-3 inline-flex h-14 w-14 items-center justify-center rounded-full bg-violet-100 text-violet-600">
                <span className="text-2xl">📋</span>
              </div>
              <h1 className="text-2xl font-extrabold text-gray-900">Diagnostic Assessment</h1>
            </div>
            <p className="mb-4 text-center text-gray-600">
              22 questions across all 3 parts (Part A, B, C). This helps us build your
              personalised plan.
            </p>
            <p className="mb-8 text-center text-sm text-gray-400">
              Takes approximately 30–40 minutes.
            </p>

            {startError && (
              <p className="mb-4 rounded-xl border border-red-200 bg-red-50 px-4 py-3 text-center text-sm text-red-700">
                {startError}
              </p>
            )}

            <button
              type="button"
              onClick={handleStart}
              className="w-full rounded-xl bg-violet-600 py-4 text-base font-bold text-white transition hover:bg-violet-700 active:scale-95"
            >
              I&apos;m ready — start diagnostic
            </button>
          </div>
        )}

        {/* Loading state */}
        {(flowState === 'loading' || flowState === 'submitting') && (
          <div className="flex flex-col items-center gap-6 py-16">
            <LoadingSpinner
              label={flowState === 'loading' ? 'Loading your diagnostic…' : 'Submitting answers…'}
            />
          </div>
        )}

        {/* Testing state */}
        {flowState === 'testing' && currentQuestion && (
          <div className="space-y-6">
            {/* Header bar */}
            <div className="flex items-center justify-between rounded-xl border border-violet-100 bg-white px-5 py-3 shadow-sm">
              <span className="text-sm font-semibold text-gray-700">
                Question {currentIndex + 1} of {totalQuestions}
              </span>
              <span className="rounded-full bg-violet-100 px-3 py-1 text-xs font-bold text-violet-700">
                {timerLabel}
              </span>
            </div>

            {/* Progress bar */}
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-gray-100">
              <div
                className="h-full rounded-full bg-violet-600 transition-all duration-300"
                style={{ width: `${((currentIndex + 1) / totalQuestions) * 100}%` }}
              />
            </div>

            {/* Question card */}
            <div className="rounded-2xl border border-gray-100 bg-white p-6 shadow-sm">
              <p className="mb-6 text-base font-medium text-gray-900 leading-relaxed">
                {currentQuestion.stem}
              </p>

              <div className="space-y-3">
                {Object.entries(currentQuestion.options).map(([key, label]) => (
                  <button
                    key={key}
                    type="button"
                    onClick={() => selectOption(key)}
                    className={`flex w-full items-center gap-3 rounded-xl border px-4 py-3 text-sm text-left transition-colors ${
                      selectedOption === key
                        ? 'border-violet-600 bg-violet-50 text-violet-900'
                        : 'border-gray-200 bg-white text-gray-700 hover:border-violet-200 hover:bg-violet-50/50'
                    }`}
                  >
                    <span
                      className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full border text-xs font-bold ${
                        selectedOption === key
                          ? 'border-violet-600 bg-violet-600 text-white'
                          : 'border-gray-300 text-gray-500'
                      }`}
                    >
                      {key}
                    </span>
                    {label}
                  </button>
                ))}
              </div>
            </div>

            {/* Action buttons */}
            <div className="flex gap-3">
              <button
                type="button"
                onClick={markUnknown}
                className="rounded-xl border border-gray-200 px-5 py-3 text-sm font-semibold text-gray-500 transition hover:bg-gray-50"
              >
                I don&apos;t know
              </button>

              {!isLastQuestion ? (
                <button
                  type="button"
                  onClick={handleNext}
                  className="flex-1 rounded-xl bg-violet-600 py-3 text-sm font-bold text-white transition hover:bg-violet-700"
                >
                  Next
                </button>
              ) : (
                <button
                  type="button"
                  onClick={handleFinish}
                  className="flex-1 rounded-xl bg-violet-600 py-3 text-sm font-bold text-white transition hover:bg-violet-700"
                >
                  Finish Diagnostic
                </button>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
