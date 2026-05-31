'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import {
  getDiagnosticQuestions,
  startDiagnostic,
  submitDiagnostic,
  submitDiagnosticAnswer,
  type DiagnosticAnswerInput,
  type DiagnosticQuestion,
  type SubmitDiagnosticPayload,
} from '@/lib/listening-pathway-api';
import { DiagnosticPlayer } from '@/components/listening/DiagnosticPlayer';
import { AccentBadge } from '@/components/listening/AccentBadge';
import { NotesPanel } from '@/components/listening/NotesPanel';

// ─────────────────────────────────────────────────────────────────────────────
// Types & persistence
// ─────────────────────────────────────────────────────────────────────────────

type FlowState = 'brief' | 'loading' | 'testing' | 'submitting' | 'error';

interface PersistedAnswer {
  questionId: string;
  selectedOption?: string;
  learnerAnswer?: string;
  isUnknown: boolean;
  timeSpentSeconds: number;
  replaysUsed: number;
  markedForReview: boolean;
}

interface PersistedState {
  sessionId: string;
  currentIndex: number;
  answers: Record<string, PersistedAnswer>;
  startedAt: string;
}

const STORAGE_KEY = 'listening_diagnostic_state';

function readPersistedState(): PersistedState | null {
  if (typeof window === 'undefined') return null;
  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (!raw) return null;
    const parsed = JSON.parse(raw) as PersistedState;
    if (!parsed.sessionId || typeof parsed.currentIndex !== 'number') return null;
    return parsed;
  } catch {
    return null;
  }
}

function writePersistedState(state: PersistedState): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(state));
  } catch {
    /* ignore */
  }
}

function clearPersistedState(): void {
  if (typeof window === 'undefined') return;
  try {
    window.sessionStorage.removeItem(STORAGE_KEY);
  } catch {
    /* ignore */
  }
}

function LoadingSpinner({ label }: { label: string }) {
  return (
    <div className="flex flex-col items-center gap-4">
      <div className="h-10 w-10 animate-spin rounded-full border-4 border-lavender border-t-primary" />
      <p className="text-sm text-muted">{label}</p>
    </div>
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Page
// ─────────────────────────────────────────────────────────────────────────────

export default function ListeningDiagnosticPage() {
  const { isAuthenticated, loading: authLoading } = useAuth();
  const router = useRouter();

  const [flowState, setFlowState] = useState<FlowState>('brief');
  const [sessionId, setSessionId] = useState<string | null>(null);
  const [questions, setQuestions] = useState<DiagnosticQuestion[]>([]);
  const [currentIndex, setCurrentIndex] = useState(0);
  const [answers, setAnswers] = useState<Record<string, PersistedAnswer>>({});
  const [startedAt, setStartedAt] = useState<string>('');

  const [selectedOption, setSelectedOption] = useState<string>('');
  const [learnerAnswer, setLearnerAnswer] = useState<string>('');
  const [isUnknown, setIsUnknown] = useState(false);

  const [questionElapsed, setQuestionElapsed] = useState(0);
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [advancing, setAdvancing] = useState(false);

  const questionTimerRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const startedAtRef = useRef<string>('');

  // Auth guard
  useEffect(() => {
    if (!authLoading && !isAuthenticated) {
      router.replace('/sign-in');
    }
  }, [authLoading, isAuthenticated, router]);

  // Per-question timer (count up)
  useEffect(() => {
    if (flowState !== 'testing') {
      if (questionTimerRef.current) clearInterval(questionTimerRef.current);
      return;
    }
    questionTimerRef.current = setInterval(() => {
      setQuestionElapsed((s) => s + 1);
    }, 1000);
    return () => {
      if (questionTimerRef.current) clearInterval(questionTimerRef.current);
    };
  }, [flowState, currentIndex]);

  // Persist state on every change while testing
  useEffect(() => {
    if (flowState !== 'testing' || !sessionId) return;
    writePersistedState({
      sessionId,
      currentIndex,
      answers,
      startedAt: startedAtRef.current,
    });
  }, [sessionId, currentIndex, answers, flowState]);

  // Reset per-question form state when index changes
  useEffect(() => {
    if (flowState !== 'testing') return;
    const current = questions[currentIndex];
    if (!current) return;
    const existing = answers[current.id];
    setSelectedOption(existing?.selectedOption ?? '');
    setLearnerAnswer(existing?.learnerAnswer ?? '');
    setIsUnknown(existing?.isUnknown ?? false);
    setQuestionElapsed(0);
  }, [currentIndex, flowState, questions, answers]);

  const handleStart = useCallback(async () => {
    setSubmitError(null);
    setFlowState('loading');
    try {
      // Try to resume an existing session first
      const persisted = readPersistedState();
      if (persisted) {
        const qs = await getDiagnosticQuestions(persisted.sessionId);
        if (qs.length > 0) {
          setSessionId(persisted.sessionId);
          setQuestions(qs);
          setAnswers(persisted.answers);
          setCurrentIndex(Math.min(persisted.currentIndex, qs.length - 1));
          startedAtRef.current = persisted.startedAt;
          setStartedAt(persisted.startedAt);
          setFlowState('testing');
          return;
        }
        clearPersistedState();
      }

      // Otherwise start a new session
      const session = await startDiagnostic();
      const qs = await getDiagnosticQuestions(session.sessionId);
      if (qs.length === 0) {
        throw new Error('No diagnostic questions are available yet.');
      }
      const now = new Date().toISOString();
      setSessionId(session.sessionId);
      setQuestions(qs);
      setAnswers({});
      setCurrentIndex(0);
      setStartedAt(now);
      startedAtRef.current = now;
      setFlowState('testing');
    } catch {
      setSubmitError('Could not start the diagnostic. Please try again.');
      setFlowState('brief');
    }
  }, []);

  const currentQuestion = questions[currentIndex] ?? null;
  const totalQuestions = questions.length;
  const isLastQuestion = totalQuestions > 0 && currentIndex === totalQuestions - 1;
  const isPartA = currentQuestion?.part === 'A';
  const isGapFill = isPartA || (currentQuestion?.options ?? null) === null;

  const canSubmitAnswer = useMemo(() => {
    if (!currentQuestion) return false;
    if (isUnknown) return true;
    if (isGapFill) return learnerAnswer.trim().length > 0;
    return selectedOption.length > 0;
  }, [currentQuestion, isUnknown, isGapFill, learnerAnswer, selectedOption]);

  const submitCurrentAnswer = useCallback(async () => {
    if (!currentQuestion || !sessionId) return;
    const payload: DiagnosticAnswerInput = {
      questionId: currentQuestion.id,
      isUnknown,
      timeSpentSeconds: questionElapsed,
      replaysUsed: 0,
      markedForReview: false,
    };
    if (!isUnknown) {
      if (isGapFill && learnerAnswer.trim().length > 0) {
        payload.learnerAnswer = learnerAnswer.trim();
      }
      if (!isGapFill && selectedOption.length > 0) {
        payload.selectedOption = selectedOption;
      }
    }

    setAdvancing(true);
    setSubmitError(null);
    try {
      await submitDiagnosticAnswer(sessionId, payload);
      const nextAnswers = { ...answers, [currentQuestion.id]: { ...payload } };
      setAnswers(nextAnswers);

      if (!isLastQuestion) {
        setCurrentIndex((i) => i + 1);
        return;
      }

      // Final submission
      setFlowState('submitting');
      const totalDurationSeconds = Math.max(
        1,
        Math.round((Date.now() - new Date(startedAtRef.current).getTime()) / 1000),
      );
      const submission: SubmitDiagnosticPayload = {
        sessionId,
        answers: Object.values(nextAnswers).map((a) => ({ ...a })),
        totalDurationSeconds,
      };
      await submitDiagnostic(submission);
      clearPersistedState();
      router.push(`/listening/diagnostic-results?sessionId=${encodeURIComponent(sessionId)}`);
    } catch {
      setSubmitError(
        isLastQuestion
          ? 'Could not submit your diagnostic. Please try again.'
          : 'Could not save your answer. Please try again.',
      );
      if (flowState === 'submitting') setFlowState('testing');
    } finally {
      setAdvancing(false);
    }
  }, [
    currentQuestion,
    sessionId,
    isUnknown,
    questionElapsed,
    isGapFill,
    learnerAnswer,
    selectedOption,
    answers,
    isLastQuestion,
    router,
    flowState,
  ]);

  const elapsedLabel = useMemo(() => {
    const m = Math.floor(questionElapsed / 60);
    const s = questionElapsed % 60;
    return `${m}:${String(s).padStart(2, '0')}`;
  }, [questionElapsed]);

  if (authLoading) {
    return (
      <div className="flex min-h-screen items-center justify-center">
        <LoadingSpinner label="Loading…" />
      </div>
    );
  }

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background-light px-4 py-12">
      <div className="w-full max-w-2xl">
        {/* Brief / start screen */}
        {flowState === 'brief' && (
          <div className="rounded-2xl border border-border bg-surface p-8 shadow-sm">
            <div className="mb-6 text-center">
              <span className="mb-3 block text-3xl" aria-hidden>🎧</span>
              <h1 className="text-2xl font-extrabold text-navy">Listening diagnostic</h1>
            </div>
            <p className="mb-4 text-center text-muted">
              23 questions across Parts A, B and C. We&apos;ll use the result to map every sub-skill
              and accent and build your 12-week pathway.
            </p>
            <ul className="mb-8 space-y-2 text-sm text-muted">
              <li>• Each clip plays once. No replays during the diagnostic.</li>
              <li>• Part A is gap-fill (type the missing word/phrase).</li>
              <li>• Parts B and C are multiple choice (3 options + &ldquo;I don&apos;t know&rdquo;).</li>
              <li>• You can take notes on Part A items.</li>
            </ul>
            <p className="mb-8 text-center text-sm text-muted">Takes approximately 30 minutes.</p>

            {submitError && (
              <p
                className="mb-4 rounded-xl border border-danger/20 bg-danger/10 px-4 py-3 text-center text-sm text-danger"
                role="alert"
              >
                {submitError}
              </p>
            )}

            <button
              type="button"
              onClick={() => void handleStart()}
              className="w-full rounded-xl bg-primary py-4 text-base font-bold text-white transition-colors hover:bg-primary-dark dark:bg-violet-700 dark:hover:bg-violet-600 active:scale-95"
            >
              I&apos;m ready to start the diagnostic
            </button>
          </div>
        )}

        {/* Loading / submitting states */}
        {(flowState === 'loading' || flowState === 'submitting') && (
          <div className="flex flex-col items-center gap-6 py-16">
            <LoadingSpinner
              label={flowState === 'loading' ? 'Loading your diagnostic…' : 'Submitting your answers…'}
            />
          </div>
        )}

        {/* Testing */}
        {flowState === 'testing' && currentQuestion && sessionId && (
          <div className="space-y-6">
            {/* Header bar */}
            <div className="flex flex-wrap items-center justify-between gap-3 rounded-xl border border-border bg-surface px-5 py-3 shadow-sm">
              <span className="text-sm font-semibold text-navy">
                Question {currentIndex + 1} of {totalQuestions}
              </span>
              <div className="flex items-center gap-2">
                <span className="rounded-full bg-background-light px-3 py-1 text-xs font-bold text-navy">
                  Part {currentQuestion.part === 'accent_test' ? 'Accent' : currentQuestion.part}
                </span>
                <AccentBadge accent={currentQuestion.accent} />
                <span className="rounded-full bg-lavender px-3 py-1 text-xs font-bold text-primary">
                  {elapsedLabel}
                </span>
              </div>
            </div>

            {/* Progress bar */}
            <div className="h-1.5 w-full overflow-hidden rounded-full bg-border">
              <div
                className="h-full rounded-full bg-primary transition-[width,background-color] duration-300"
                style={{ width: `${((currentIndex + 1) / totalQuestions) * 100}%` }}
                role="progressbar"
                aria-valuenow={currentIndex + 1}
                aria-valuemax={totalQuestions}
              />
            </div>

            {/* Audio player — falls back to a graceful "audio coming soon"
                placeholder when the extract has no synthesised clip yet
                (Phase 1 diagnostic items ship with AudioContentSha = null). */}
            <div className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
              {currentQuestion.audioPlaybackUrl ? (
                <DiagnosticPlayer audioUrl={currentQuestion.audioPlaybackUrl} allowReplay={false} onEnded={() => {}} />
              ) : (
                <div className="flex items-center gap-3 text-sm text-muted">
                  <span
                    aria-hidden
                    className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-background-light text-primary"
                  >
                    🔊
                  </span>
                  <div>
                    <p className="font-semibold text-navy">Audio coming soon</p>
                    <p>
                      This diagnostic clip isn&apos;t available yet — read the question below and
                      answer based on what you can, then continue.
                    </p>
                  </div>
                </div>
              )}
            </div>

            {/* Question stem */}
            <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <p className="mb-6 text-base font-medium leading-relaxed text-navy">
                {currentQuestion.stem}
              </p>

              {/* Answer entry */}
              {isGapFill ? (
                <div className="space-y-3">
                  <label
                    htmlFor="gapFillAnswer"
                    className="mb-1 block text-sm font-semibold text-navy"
                  >
                    Your answer
                  </label>
                  <input
                    id="gapFillAnswer"
                    type="text"
                    value={learnerAnswer}
                    onChange={(event) => {
                      setLearnerAnswer(event.target.value);
                      if (event.target.value.length > 0 && isUnknown) setIsUnknown(false);
                    }}
                    disabled={isUnknown}
                    placeholder="Type the missing word or phrase"
                    className="w-full rounded-xl border border-border px-4 py-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20 disabled:cursor-not-allowed disabled:bg-background-light disabled:text-muted"
                  />
                  <label className="flex cursor-pointer items-center gap-2 text-sm text-muted">
                    <input
                      type="checkbox"
                      checked={isUnknown}
                      onChange={(event) => {
                        setIsUnknown(event.target.checked);
                        if (event.target.checked) setLearnerAnswer('');
                      }}
                      className="rounded border-border text-primary focus:ring-primary"
                    />
                    I don&apos;t know
                  </label>
                </div>
              ) : (
                <div className="space-y-3" role="radiogroup" aria-label="Answer options">
                  {(currentQuestion.options ?? []).map(({ key, text }) => {
                    const active = !isUnknown && selectedOption === key;
                    return (
                      <button
                        key={key}
                        type="button"
                        role="radio"
                        aria-checked={active}
                        onClick={() => {
                          setSelectedOption(key);
                          setIsUnknown(false);
                        }}
                        className={`flex w-full items-center gap-3 rounded-xl border px-4 py-3 text-left text-sm transition-colors ${
                          active
                            ? 'border-primary bg-primary/5 text-primary'
                            : 'border-border bg-surface text-navy hover:border-border-hover hover:bg-background-light'
                        }`}
                      >
                        <span
                          className={`flex h-7 w-7 shrink-0 items-center justify-center rounded-full border text-xs font-bold ${
                            active
                              ? 'border-primary bg-primary text-white dark:bg-violet-700'
                              : 'border-border-hover text-muted'
                          }`}
                        >
                          {key}
                        </span>
                        {text}
                      </button>
                    );
                  })}
                  <button
                    type="button"
                    role="radio"
                    aria-checked={isUnknown}
                    onClick={() => {
                      setIsUnknown(true);
                      setSelectedOption('');
                    }}
                    className={`flex w-full items-center gap-3 rounded-xl border px-4 py-3 text-left text-sm transition-colors ${
                      isUnknown
                        ? 'border-border-hover bg-background-light text-navy'
                        : 'border-dashed border-border bg-surface text-muted hover:bg-background-light'
                    }`}
                  >
                    <span className="flex h-7 w-7 shrink-0 items-center justify-center rounded-full border border-border-hover text-xs font-bold text-muted">
                      ?
                    </span>
                    I don&apos;t know
                  </button>
                </div>
              )}
            </div>

            {/* Notes (Part A only) */}
            {isPartA && (
              <NotesPanel sessionId={sessionId} questionId={currentQuestion.id} />
            )}

            {submitError && (
              <p
                className="rounded-xl border border-danger/20 bg-danger/10 px-4 py-3 text-sm text-danger"
                role="alert"
              >
                {submitError}
              </p>
            )}

            {/* Action buttons */}
            <div className="flex gap-3">
              <button
                type="button"
                onClick={() => void submitCurrentAnswer()}
                disabled={!canSubmitAnswer || advancing}
                className="flex-1 rounded-xl bg-primary py-3 text-sm font-bold text-white transition-[color,background-color,transform] duration-200 hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50"
              >
                {advancing
                  ? 'Saving…'
                  : isLastQuestion
                    ? 'Submit diagnostic'
                    : 'Submit answer'}
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
