'use client';

import { useMemo } from 'react';
import { CheckCircle2, XCircle } from 'lucide-react';
import { cn } from '@/lib/utils';
import { matches } from '@/lib/grammar/grading';
import type {
  GrammarExerciseLearner,
  GrammarExerciseResult,
  GrammarExerciseType,
  GrammarMatchingOption,
  GrammarMcqOption,
} from '@/lib/grammar/types';
import { SafeRichText } from './grammar-content-renderer';

export interface GrammarExerciseRunnerProps {
  exercise: GrammarExerciseLearner;
  answer: unknown;
  onAnswer: (answer: unknown) => void;
  disabled?: boolean;
  /** If a graded result exists for this exercise, surface its feedback inline. */
  result?: GrammarExerciseResult | null;
}

/**
 * Polymorphic exercise runner. Delegates to per-type sub-renderers;
 * none of them grade — grading happens on the server and is surfaced via
 * `result`.
 */
export function GrammarExerciseRunner(props: GrammarExerciseRunnerProps) {
  const { exercise } = props;
  const type = exercise.type as GrammarExerciseType;

  let body: React.ReactNode;
  switch (type) {
    case 'mcq':
      body = <GrammarMcqExercise {...props} />;
      break;
    case 'fill_blank':
    case 'error_correction':
    case 'sentence_transformation':
      body = <GrammarTextExercise {...props} />;
      break;
    case 'matching':
      body = <GrammarMatchingExercise {...props} />;
      break;
    default:
      body = <GrammarTextExercise {...props} />;
  }

  return (
    <article className="rounded-2xl border border-border bg-white p-5 shadow-sm dark:border-gray-700 dark:bg-gray-800">
      <header className="mb-3 flex items-start justify-between gap-3">
        <div className="min-w-0 flex-1">
          <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-muted">
            {labelFor(type)} · {exercise.difficulty}
          </p>
          <div className="mt-1 text-sm font-medium text-navy dark:text-white">
            <SafeRichText markdown={exercise.promptMarkdown} />
          </div>
        </div>
        {props.result ? (
          <GrammarResultBadge isCorrect={props.result.isCorrect} />
        ) : null}
      </header>
      {body}
      {props.result?.explanationMarkdown ? (
        <div className="mt-4 rounded-xl border border-primary/20 bg-primary/5 p-3 text-sm leading-6 text-primary-dark">
          <p className="mb-1 text-[10px] font-semibold uppercase tracking-[0.18em] opacity-80">Explanation</p>
          <SafeRichText markdown={props.result.explanationMarkdown} className="text-primary-dark" />
        </div>
      ) : null}
    </article>
  );
}

function labelFor(type: GrammarExerciseType | string) {
  switch (type) {
    case 'mcq': return 'Multiple choice';
    case 'fill_blank': return 'Fill the blank';
    case 'error_correction': return 'Error correction';
    case 'sentence_transformation': return 'Sentence transformation';
    case 'matching': return 'Matching';
    default: return 'Practice';
  }
}

function GrammarResultBadge({ isCorrect }: { isCorrect: boolean }) {
  return isCorrect ? (
    <span className="inline-flex items-center gap-1 rounded-full bg-emerald-100 px-2.5 py-1 text-[11px] font-semibold text-emerald-700">
      <CheckCircle2 className="h-3.5 w-3.5" /> Correct
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 rounded-full bg-rose-100 px-2.5 py-1 text-[11px] font-semibold text-rose-700">
      <XCircle className="h-3.5 w-3.5" /> Review
    </span>
  );
}

// ── MCQ ─────────────────────────────────────────────────────────────────

function GrammarMcqExercise({ exercise, answer, onAnswer, disabled, result }: GrammarExerciseRunnerProps) {
  const options = useMemo<GrammarMcqOption[]>(() => {
    if (!Array.isArray(exercise.options)) return [];
    return (exercise.options as GrammarMcqOption[]).filter((o) => o && typeof o === 'object' && 'id' in o);
  }, [exercise.options]);

  const selectedId = typeof answer === 'string' ? answer : null;
  const correctId = typeof result?.correctAnswer === 'string' ? result.correctAnswer
    : Array.isArray(result?.correctAnswer) && typeof result?.correctAnswer[0] === 'string' ? result.correctAnswer[0]
    : null;

  return (
    <div className="space-y-2" role="radiogroup" aria-label="Answer options">
      {options.map((opt) => {
        const isSelected = selectedId === opt.id;
        const isMarkedCorrect = correctId === opt.id;
        const state = result ? (isMarkedCorrect ? 'correct' : isSelected ? 'wrong' : 'idle') : isSelected ? 'selected' : 'idle';

        return (
          <button
            key={opt.id}
            type="button"
            role="radio"
            aria-checked={isSelected}
            disabled={disabled}
            onClick={() => onAnswer(opt.id)}
            className={cn(
              'pressable w-full rounded-xl border px-3 py-2.5 text-left text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
              state === 'idle' && 'border-border bg-surface text-gray-800 hover:border-primary/40 dark:text-gray-200',
              state === 'selected' && 'border-primary/40 bg-primary/10 text-primary-dark',
              state === 'correct' && 'border-emerald-400 bg-emerald-50 text-emerald-800 dark:bg-emerald-900/20 dark:text-emerald-200',
              state === 'wrong' && 'border-rose-400 bg-rose-50 text-rose-800 dark:bg-rose-900/20 dark:text-rose-200',
            )}
          >
            <span className="mr-2 inline-flex h-5 w-5 items-center justify-center rounded-full border border-current text-[10px] font-semibold uppercase">
              {opt.id}
            </span>
            {opt.label}
          </button>
        );
      })}
    </div>
  );
}

// ── Text-entry (fill_blank, error_correction, sentence_transformation) ─

function GrammarTextExercise({ exercise, answer, onAnswer, disabled, result }: GrammarExerciseRunnerProps) {
  const value = typeof answer === 'string' ? answer : '';
  const canonicalHit = useMemo(() => {
    if (!result) return false;
    const ca = result.correctAnswer;
    if (typeof ca === 'string') return matches(value, ca);
    if (Array.isArray(ca)) return ca.some((s) => typeof s === 'string' && matches(value, s));
    return false;
  }, [result, value]);

  return (
    <div className="space-y-2">
      <input
        type="text"
        value={value}
        disabled={disabled}
        placeholder="Type your answer…"
        onChange={(e) => onAnswer(e.target.value)}
        className={cn(
          'w-full rounded-xl border px-3 py-2.5 text-sm shadow-sm transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 dark:bg-gray-800 dark:text-gray-100',
          result
            ? result.isCorrect
              ? 'border-emerald-400 bg-emerald-50 dark:bg-emerald-900/20'
              : 'border-rose-400 bg-rose-50 dark:bg-rose-900/20'
            : 'border-border bg-white',
        )}
        aria-label={`Exercise ${exercise.sortOrder} answer`}
      />
      {result && !result.isCorrect ? (
        <p className="rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-xs font-medium text-emerald-800 dark:border-emerald-900/40 dark:bg-emerald-900/20 dark:text-emerald-200">
          Expected: {formatExpected(result.correctAnswer)}
        </p>
      ) : null}
      {result && canonicalHit && !result.isCorrect ? (
        <p className="text-xs text-amber-700">
          Your answer was close — check spelling or punctuation.
        </p>
      ) : null}
    </div>
  );
}

function formatExpected(raw: unknown): string {
  if (typeof raw === 'string') return raw;
  if (Array.isArray(raw)) return raw.filter((x) => typeof x === 'string').join(' / ');
  return JSON.stringify(raw);
}

// ── Matching ───────────────────────────────────────────────────────────

function GrammarMatchingExercise({ exercise, answer, onAnswer, disabled, result }: GrammarExerciseRunnerProps) {
  const pairs = useMemo<GrammarMatchingOption[]>(() => {
    if (!Array.isArray(exercise.options)) return [];
    return (exercise.options as GrammarMatchingOption[]).filter((p) => p && 'left' in p && 'right' in p);
  }, [exercise.options]);

  const current = (answer && typeof answer === 'object') ? (answer as Record<string, string>) : {};
  const rights = Array.from(new Set(pairs.map((p) => p.right)));

  return (
    <div className="space-y-2" aria-label="Matching exercise">
      {pairs.map((p) => {
        const selected = current[p.left] ?? '';
        const correctRight = result?.correctAnswer && Array.isArray(result.correctAnswer)
          ? (result.correctAnswer as GrammarMatchingOption[]).find((x) => x.left === p.left)?.right ?? null
          : null;
        const isRowCorrect = result && correctRight !== null ? matches(selected, correctRight) : null;

        return (
          <div key={p.left} className="flex items-center gap-2">
            <span className="min-w-[9rem] flex-shrink-0 rounded-xl border border-border bg-surface px-3 py-2 text-sm font-medium text-navy dark:text-gray-100">
              {p.left}
            </span>
            <span className="text-muted">→</span>
            <select
              value={selected}
              disabled={disabled}
              onChange={(e) => onAnswer({ ...current, [p.left]: e.target.value })}
              className={cn(
                'flex-1 rounded-xl border px-3 py-2 text-sm shadow-sm transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 dark:bg-gray-800 dark:text-gray-100',
                result
                  ? isRowCorrect
                    ? 'border-emerald-400 bg-emerald-50 dark:bg-emerald-900/20'
                    : 'border-rose-400 bg-rose-50 dark:bg-rose-900/20'
                  : 'border-border bg-white',
              )}
            >
              <option value="" disabled>Select a match…</option>
              {rights.map((r) => (
                <option key={r} value={r}>{r}</option>
              ))}
            </select>
            {result && !isRowCorrect && correctRight ? (
              <span className="text-xs font-medium text-emerald-700">→ {correctRight}</span>
            ) : null}
          </div>
        );
      })}
    </div>
  );
}
