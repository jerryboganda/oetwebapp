'use client';

import { useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { useReadingProfile } from '@/hooks/useReadingProfile';
import {
  getDiagnosticQuestions,
  getDiagnosticResult,
  startDiagnostic,
  submitDiagnostic,
  type DiagnosticQuestionDto,
  type DiagnosticResultDto,
  type DiagnosticStartDto,
} from '@/lib/reading-pathway-api';
import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';

type FlowState = 'brief' | 'loading' | 'testing' | 'submitting';

const DIAGNOSTIC_SUBMIT_TIMEOUT_MS = 120_000;
const DIAGNOSTIC_RESULT_RECOVERY_TIMEOUT_MS = 20_000;
const DIAGNOSTIC_RESULT_RECOVERY_DELAYS_MS = [1000, 2000, 4000, 8000];
const DIAGNOSTIC_RESULT_STORAGE_KEY = 'diagnostic_result';

// Minimal shape we need from question data.
interface DiagnosticOption {
  key: string;
  label: string;
}

function normalizeOptions(options: unknown): DiagnosticOption[] {
  if (Array.isArray(options)) {
    return options.map((option, index) => ({
      key: optionKey(option, String.fromCharCode(65 + index)),
      label: optionLabel(option),
    }));
  }

  if (options && typeof options === 'object') {
    return Object.entries(options as Record<string, unknown>).map(([key, value]) => ({
      key,
      label: optionLabel(value),
    }));
  }

  return [];
}

function optionKey(option: unknown, fallback: string): string {
  if (option && typeof option === 'object') {
    const record = option as Record<string, unknown>;
    return String(record.value ?? record.key ?? record.id ?? fallback);
  }

  return fallback;
}

function optionLabel(option: unknown): string {
  if (option && typeof option === 'object') {
    const record = option as Record<string, unknown>;
    return String(record.label ?? record.text ?? record.title ?? record.value ?? record.key ?? '');
  }

  return String(option);
}

function LoadingSpinner({ label }: { label: string }) {
  return (
    <div className="flex flex-col items-center gap-4">
      <div className="h-10 w-10 motion-safe:animate-spin rounded-full border-4 border-violet-200 border-t-violet-600" />
      <p className="text-sm text-muted">{label}</p>
    </div>
  );
}

function cacheDiagnosticResult(result: DiagnosticResultDto): void {
  if (typeof window === 'undefined') return;

  try {
    window.sessionStorage.setItem(DIAGNOSTIC_RESULT_STORAGE_KEY, JSON.stringify(result));
  } catch {
    // Results are still reachable through the API if browser storage is blocked.
  }
}

function isRecoverableDiagnosticSubmitError(error: unknown): boolean {
  if (error instanceof DOMException) {
    return error.name === 'AbortError';
  }

  if (error instanceof TypeError) {
    return true;
  }

  if (!error || typeof error !== 'object') {
    return true;
  }

  const record = error as {
    status?: number;
    detail?: { code?: string } | unknown;
  };

  if (record.status === 401 || record.status === 403) {
    return false;
  }

  if (record.status === 400) {
    const detail = record.detail as { code?: string } | null | undefined;
    return detail?.code === 'diagnostic_already_submitted';
  }

  if (record.status === undefined) {
    return false;
  }

  return record.status === 0 || record.status === 408 || record.status === 429 || record.status >= 500;
}

async function waitForDiagnosticResult(sessionId: string): Promise<DiagnosticResultDto | null> {
  const deadline = Date.now() + DIAGNOSTIC_RESULT_RECOVERY_TIMEOUT_MS;

  for (let attempt = 0; Date.now() <= deadline; attempt += 1) {
    const remainingBeforeFetch = deadline - Date.now();
    if (remainingBeforeFetch <= 0) {
      break;
    }

    try {
      return await getDiagnosticResult(sessionId, { timeoutMs: Math.min(5_000, remainingBeforeFetch) });
    } catch (error) {
      if (!isDiagnosticResultPendingError(error)) {
        return null;
      }
    }

    const delay = DIAGNOSTIC_RESULT_RECOVERY_DELAYS_MS[Math.min(attempt, DIAGNOSTIC_RESULT_RECOVERY_DELAYS_MS.length - 1)];
    const remainingAfterFetch = deadline - Date.now();
    if (remainingAfterFetch <= 0) {
      break;
    }

    await new Promise((resolve) => setTimeout(resolve, Math.min(delay, remainingAfterFetch)));
  }

  return null;
}

function isDiagnosticResultPendingError(error: unknown): boolean {
  if (error instanceof DOMException) {
    return error.name === 'AbortError';
  }

  if (error instanceof TypeError) {
    return true;
  }

  if (!error || typeof error !== 'object') {
    return false;
  }

  const record = error as { status?: number };
  return (
    record.status === 404 ||
    record.status === 408 ||
    record.status === 429 ||
    (record.status != null && record.status >= 500)
  );
}

export default function DiagnosticPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const { isLoading: profileLoading } = useReadingProfile();
  const router = useRouter();

  const [flowState, setFlowState] = useState<FlowState>('brief');
  const [session, setSession] = useState<DiagnosticStartDto | null>(null);
  const [questions, setQuestions] = useState<DiagnosticQuestionDto[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [answers, setAnswers] = useState<Record<string, string>>({});
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [startError, setStartError] = useState<string | null>(null);
  const [submitError, setSubmitError] = useState<string | null>(null);

  const timerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const isMountedRef = useRef(true);

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

  useEffect(() => () => {
    isMountedRef.current = false;
  }, []);

  const handleStart = async () => {
    setStartError(null);
    setSubmitError(null);
    setFlowState('loading');
    try {
      const sess = await startDiagnostic();
      if (!isMountedRef.current) return;
      setSession(sess);
      const qs = await getDiagnosticQuestions(sess.sessionId);
      if (!isMountedRef.current) return;
      if (qs.length === 0) {
        throw new Error('No diagnostic questions are available yet.');
      }
      setQuestions(qs);
      setFlowState('testing');
    } catch {
      if (!isMountedRef.current) return;
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
    setSubmitError(null);
    setFlowState('submitting');
    try {
      const result: DiagnosticResultDto = await submitDiagnostic(session.sessionId, answers, {
        timeoutMs: DIAGNOSTIC_SUBMIT_TIMEOUT_MS,
      });
      if (!isMountedRef.current) return;
      cacheDiagnosticResult(result);
      router.push(`/reading/diagnostic-results?sessionId=${encodeURIComponent(session.sessionId)}`);
    } catch (error) {
      if (!isRecoverableDiagnosticSubmitError(error)) {
        if (!isMountedRef.current) return;
        setSubmitError('Could not finish the diagnostic right now. Your answers are still on the page, so please try again.');
        setFlowState('testing');
        return;
      }

      const recovered = await waitForDiagnosticResult(session.sessionId);
      if (recovered) {
        if (!isMountedRef.current) return;
        cacheDiagnosticResult(recovered);
        router.push(`/reading/diagnostic-results?sessionId=${encodeURIComponent(session.sessionId)}`);
        return;
      }

      if (!isMountedRef.current) return;
      setSubmitError('Could not finish the diagnostic right now. Your answers are still on the page, so please try again.');
      setFlowState('testing');
    }
  };

  const elapsedMinutes = Math.floor(elapsedSeconds / 60);
  const elapsedSecondsRemainder = elapsedSeconds % 60;
  const timerLabel = `${elapsedMinutes}:${String(elapsedSecondsRemainder).padStart(2, '0')}`;

  const selectedOption = currentQuestion ? (answers[currentQuestion.id] ?? null) : null;
  const currentOptions = currentQuestion ? normalizeOptions(currentQuestion.options) : [];
  const expectsTextAnswer = currentQuestion ? currentOptions.length === 0 : false;
  const isLastQuestion = currentIndex === totalQuestions - 1;

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <LoadingSpinner label="Loading…" />
      </div>
    );
  }

  if (profileLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <LoadingSpinner label="Loading your reading profile…" />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background-light px-4 py-12">
      <div className="w-full max-w-2xl">
        {/* Brief state */}
        {flowState === 'brief' && (
          <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
            <div className="mb-6 text-center">
              <span className="mb-3 block text-4xl" aria-hidden="true">📋</span>
              <h1 className="text-2xl font-extrabold text-navy">Diagnostic Assessment</h1>
            </div>
            <p className="mb-4 text-center text-muted">
              22 questions across all 3 parts (Part A, B, C). This helps us build your
              personalised plan.
            </p>
            <p className="mb-8 text-center text-sm text-muted">
              Takes approximately 25 minutes.
            </p>

            {startError && (
              <p className="mb-4 rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-center text-sm text-danger">
                {startError}
              </p>
            )}

            <button
              type="button"
              onClick={handleStart}
              className="w-full rounded-xl bg-primary py-4 text-base font-bold text-white transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
            >
              I&apos;m ready, start diagnostic
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
            <div className="flex items-center justify-between rounded-xl border border-violet-100 bg-surface px-5 py-3 shadow-sm">
              <span className="text-sm font-semibold text-navy">
                Question {currentIndex + 1} of {totalQuestions}
              </span>
              <div className="flex items-center gap-2">
                <span className="rounded-full bg-background-light px-3 py-1 text-xs font-bold text-muted">
                  Part {currentQuestion.partCode || 'Reading'}
                </span>
                <span className="rounded-full bg-lavender px-3 py-1 text-xs font-bold text-primary">
                  {timerLabel}
                </span>
              </div>
            </div>

            {/* Progress bar */}
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-border">
              <div
                className="h-full rounded-full bg-primary transition-[width] duration-300"
                style={{ width: `${((currentIndex + 1) / totalQuestions) * 100}%` }}
              />
            </div>

            {/* Passage card */}
            {currentQuestion.textHtml && (
              <div className="max-h-80 overflow-auto rounded-2xl border border-border bg-surface p-6 shadow-sm">
                {currentQuestion.textTitle && (
                  <h2 className="mb-3 text-sm font-bold uppercase tracking-wide text-muted">
                    {currentQuestion.textTitle}
                  </h2>
                )}
                <div
                  className="prose prose-sm max-w-[70ch] text-navy"
                  dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(currentQuestion.textHtml) }}
                />
              </div>
            )}

            {/* Question card */}
            <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <p className="mb-6 text-base font-medium text-navy leading-relaxed">
                {currentQuestion.stem}
              </p>

                {submitError && (
                  <p className="mb-6 rounded-xl border border-danger/30 bg-danger/10 px-4 py-3 text-sm text-danger" role="alert">
                    {submitError}
                  </p>
                )}

              {expectsTextAnswer ? (
                <label className="block">
                  <span className="mb-2 block text-sm font-semibold text-navy">Your answer</span>
                  <input
                    type="text"
                    value={selectedOption === '__unknown__' ? '' : selectedOption ?? ''}
                    onChange={(event) => selectOption(event.target.value)}
                    className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20"
                  />
                </label>
              ) : (
                <div className="space-y-3">
                  {currentOptions.map(({ key, label }) => (
                    <button
                      key={key}
                      type="button"
                      onClick={() => selectOption(key)}
                      className={`flex w-full items-center gap-3 rounded-xl border px-4 py-3 text-sm text-left transition-colors ${
                        selectedOption === key
                          ? 'border-primary bg-lavender text-primary'
                          : 'border-border bg-surface text-navy hover:border-primary/40 hover:bg-lavender/40'
                      }`}
                    >
                      <span
                        className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full border text-xs font-bold ${
                          selectedOption === key
                            ? 'border-primary bg-primary text-white dark:bg-violet-700'
                            : 'border-border text-muted'
                        }`}
                      >
                        {key}
                      </span>
                      {label}
                    </button>
                  ))}
                </div>
              )}
            </div>

            {/* Action buttons */}
            <div className="flex gap-3">
              <button
                type="button"
                onClick={markUnknown}
                className="rounded-xl border border-border px-5 py-3 text-sm font-semibold text-muted transition-colors hover:bg-background-light"
              >
                I don&apos;t know
              </button>

              {!isLastQuestion ? (
                <button
                  type="button"
                  onClick={handleNext}
                  className="flex-1 rounded-xl bg-primary py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
                >
                  Next
                </button>
              ) : (
                <button
                  type="button"
                  onClick={handleFinish}
                  className="flex-1 rounded-xl bg-primary py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
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
