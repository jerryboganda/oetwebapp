'use client';

import { useMemo, useState } from 'react';
import {
  AlertTriangle,
  CheckCircle2,
  ChevronDown,
  ChevronRight,
  Clock,
  Flag,
  MinusCircle,
  Pencil,
  XCircle,
} from 'lucide-react';
import { cn } from '@/lib/utils';
import type {
  ReadingPrivilegedAttemptReview,
  ReadingPrivilegedQuestion,
  ReadingPrivilegedSection,
} from '@/lib/reading-tutor-api';

/**
 * PrivilegedAttemptReview — full, NON-redacted reading attempt review shared by
 * the admin and expert tutor surfaces.
 *
 * Unlike the learner-facing review, this renders correct answers, explanations,
 * miss reasons, distractor rationale, timing, and revision counts. It must only
 * ever be mounted behind a privileged route (admin / expert) — the redaction
 * happens at the API projection layer, so this component simply displays
 * whatever the privileged endpoint returns.
 *
 * Presentation uses neutral Tailwind tokens with explicit `dark:` variants so it
 * keeps light + dark parity inside both the admin and expert shells.
 */

function formatValue(value: unknown): string {
  if (value === null || value === undefined) return '-';
  if (typeof value === 'string') return value.length > 0 ? value : '-';
  if (typeof value === 'number' || typeof value === 'boolean') return String(value);
  if (Array.isArray(value)) {
    const parts = value.map((v) => formatValue(v)).filter((v) => v !== '-');
    return parts.length > 0 ? parts.join(', ') : '-';
  }
  try {
    return JSON.stringify(value);
  } catch {
    return '-';
  }
}

function formatMs(ms: number | null): string {
  if (ms === null || Number.isNaN(ms)) return '-';
  if (ms < 1000) return `${ms} ms`;
  const seconds = ms / 1000;
  if (seconds < 60) return `${seconds.toFixed(1)} s`;
  const minutes = Math.floor(seconds / 60);
  const rem = Math.round(seconds % 60);
  return `${minutes}m ${rem}s`;
}

function formatScore(raw: number | null, scaled: number | null, gradeLetter: string): string {
  const rawText = raw === null ? '-' : String(raw);
  const scaledText = scaled === null ? '-' : String(scaled);
  const grade = gradeLetter ? ` · Grade ${gradeLetter}` : '';
  return `${rawText} raw · ${scaledText} scaled${grade}`;
}

const PART_LABELS: Record<string, string> = {
  A: 'Part A — Expeditious reading',
  B: 'Part B — Workplace texts',
  C: 'Part C — Long-text comprehension',
};

function partLabel(partCode: string): string {
  return PART_LABELS[partCode] ?? `Part ${partCode}`;
}

// ── Override banner ─────────────────────────────────────────────────────────

function OverrideBanner({ review }: { review: ReadingPrivilegedAttemptReview }) {
  if (!review.hasOverride) return null;
  return (
    <div
      role="note"
      className="rounded-xl border border-amber-300 bg-amber-50 p-4 text-sm dark:border-amber-800 dark:bg-amber-950/40"
    >
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-amber-600 dark:text-amber-400" aria-hidden="true" />
        <div className="space-y-1">
          <p className="font-semibold text-amber-900 dark:text-amber-200">
            Manual score override is active
          </p>
          <p className="text-amber-800 dark:text-amber-300">
            Effective score:{' '}
            <span className="font-semibold">
              {formatScore(review.effectiveRawScore, review.effectiveScaledScore, review.effectiveGradeLetter)}
            </span>
            {' · '}System-graded:{' '}
            {formatScore(review.gradedRawScore, review.gradedScaledScore, review.gradedGradeLetter)}
          </p>
          {review.overrideReason ? (
            <p className="text-amber-800 dark:text-amber-300">
              <span className="font-medium">Reason:</span> {review.overrideReason}
            </p>
          ) : null}
          {review.overriddenAt ? (
            <p className="text-xs text-amber-700 dark:text-amber-400">
              Set {new Date(review.overriddenAt).toLocaleString()}
              {review.overriddenByUserId ? ` by ${review.overriddenByUserId}` : ''}
            </p>
          ) : null}
        </div>
      </div>
    </div>
  );
}

// ── Score summary ─────────────────────────────────────────────────────────

function ScoreSummary({ review }: { review: ReadingPrivilegedAttemptReview }) {
  return (
    <div className="grid gap-3 sm:grid-cols-2">
      <div className="rounded-xl border border-slate-200 bg-white p-4 dark:border-slate-700 dark:bg-slate-900">
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
          System-graded score
        </p>
        <p className="mt-1 text-lg font-bold text-slate-900 dark:text-slate-100">
          {formatScore(review.gradedRawScore, review.gradedScaledScore, review.gradedGradeLetter)}
        </p>
      </div>
      <div
        className={cn(
          'rounded-xl border p-4',
          review.hasOverride
            ? 'border-amber-300 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/40'
            : 'border-slate-200 bg-white dark:border-slate-700 dark:bg-slate-900',
        )}
      >
        <p className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
          Effective score
        </p>
        <p className="mt-1 text-lg font-bold text-slate-900 dark:text-slate-100">
          {formatScore(review.effectiveRawScore, review.effectiveScaledScore, review.effectiveGradeLetter)}
        </p>
      </div>
    </div>
  );
}

// ── Section table ─────────────────────────────────────────────────────────

function SectionTable({ sections }: { sections: ReadingPrivilegedSection[] }) {
  if (sections.length === 0) return null;
  return (
    <div className="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-700">
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">Per-section raw scores and accuracy</caption>
        <thead>
          <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs font-semibold uppercase tracking-wide text-slate-500 dark:border-slate-700 dark:bg-slate-800/60 dark:text-slate-400">
            <th scope="col" className="px-4 py-2.5">Section</th>
            <th scope="col" className="px-4 py-2.5 text-right">Raw</th>
            <th scope="col" className="px-4 py-2.5 text-right">Accuracy</th>
            <th scope="col" className="px-4 py-2.5 text-right">Correct</th>
            <th scope="col" className="px-4 py-2.5 text-right">Incorrect</th>
            <th scope="col" className="px-4 py-2.5 text-right">Unanswered</th>
          </tr>
        </thead>
        <tbody>
          {sections.map((section) => (
            <tr
              key={section.partCode}
              className="border-b border-slate-100 last:border-b-0 dark:border-slate-800"
            >
              <th scope="row" className="px-4 py-2.5 text-left font-medium text-slate-900 dark:text-slate-100">
                {partLabel(section.partCode)}
              </th>
              <td className="px-4 py-2.5 text-right tabular-nums text-slate-700 dark:text-slate-300">
                {section.rawScore}/{section.maxRawScore}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-slate-700 dark:text-slate-300">
                {section.accuracyPercent === null ? '-' : `${section.accuracyPercent}%`}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-emerald-700 dark:text-emerald-400">
                {section.correctCount}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-red-700 dark:text-red-400">
                {section.incorrectCount}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-slate-500 dark:text-slate-400">
                {section.unansweredCount}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Question card ───────────────────────────────────────────────────────────

function QuestionCard({ question }: { question: ReadingPrivilegedQuestion }) {
  const [open, setOpen] = useState(false);

  const statusIcon = useMemo(() => {
    if (question.isCorrect === true) {
      return <CheckCircle2 className="h-4 w-4 text-emerald-600 dark:text-emerald-400" aria-hidden="true" />;
    }
    if (question.isCorrect === false) {
      return <XCircle className="h-4 w-4 text-red-600 dark:text-red-400" aria-hidden="true" />;
    }
    return <MinusCircle className="h-4 w-4 text-slate-400" aria-hidden="true" />;
  }, [question.isCorrect]);

  return (
    <li className="rounded-xl border border-slate-200 bg-white dark:border-slate-700 dark:bg-slate-900">
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
        className="flex w-full items-start gap-3 px-4 py-3 text-left"
      >
        <span className="mt-0.5 shrink-0">{statusIcon}</span>
        <span className="min-w-0 flex-1">
          <span className="flex flex-wrap items-center gap-2">
            <span className="text-xs font-bold uppercase tracking-widest text-slate-400 dark:text-slate-500">
              {question.partCode} · Q{question.displayOrder}
            </span>
            {question.flaggedForReview ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-medium text-amber-800 dark:bg-amber-950/50 dark:text-amber-300">
                <Flag className="h-3 w-3" aria-hidden="true" /> Flagged
              </span>
            ) : null}
            {question.answerRevisionCount > 0 ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-0.5 text-[10px] font-medium text-slate-600 dark:bg-slate-800 dark:text-slate-300">
                <Pencil className="h-3 w-3" aria-hidden="true" /> {question.answerRevisionCount} revision
                {question.answerRevisionCount === 1 ? '' : 's'}
              </span>
            ) : null}
          </span>
          <span className="mt-1 block text-sm font-medium text-slate-900 dark:text-slate-100 line-clamp-2">
            {question.stem}
          </span>
        </span>
        <span className="ml-1 shrink-0 text-slate-400">
          {open ? <ChevronDown className="h-4 w-4" aria-hidden="true" /> : <ChevronRight className="h-4 w-4" aria-hidden="true" />}
        </span>
      </button>

      {open ? (
        <div className="space-y-3 border-t border-slate-100 px-4 py-3 text-sm dark:border-slate-800">
          <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
            <div>
              <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Learner answer</dt>
              <dd className="mt-0.5 text-slate-800 dark:text-slate-200">{formatValue(question.userAnswer)}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Correct answer</dt>
              <dd className="mt-0.5 font-medium text-emerald-700 dark:text-emerald-400">{formatValue(question.correctAnswer)}</dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Points</dt>
              <dd className="mt-0.5 text-slate-800 dark:text-slate-200">
                {question.pointsEarned}/{question.maxPoints}
              </dd>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Skill tag</dt>
              <dd className="mt-0.5 text-slate-800 dark:text-slate-200">{question.skillTag ?? '-'}</dd>
            </div>
            <div className="flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5 text-slate-400" aria-hidden="true" />
              <div>
                <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Time on question</dt>
                <dd className="mt-0.5 text-slate-800 dark:text-slate-200">{formatMs(question.elapsedMs)}</dd>
              </div>
            </div>
            <div>
              <dt className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Cumulative time</dt>
              <dd className="mt-0.5 text-slate-800 dark:text-slate-200">{formatMs(question.totalElapsedMs)}</dd>
            </div>
          </dl>

          {question.missReason ? (
            <div className="rounded-lg bg-red-50 px-3 py-2 dark:bg-red-950/30">
              <p className="text-xs font-semibold uppercase tracking-wide text-red-700 dark:text-red-400">Miss reason</p>
              <p className="mt-0.5 text-red-800 dark:text-red-300">{question.missReason}</p>
            </div>
          ) : null}

          {question.selectedDistractorCategory ? (
            <div className="rounded-lg bg-slate-50 px-3 py-2 dark:bg-slate-800/60">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">
                Distractor: {question.selectedDistractorCategory}
              </p>
              {formatValue(question.distractorRationale) !== '-' ? (
                <p className="mt-0.5 text-slate-700 dark:text-slate-300">
                  {formatValue(question.distractorRationale)}
                </p>
              ) : null}
            </div>
          ) : null}

          {question.explanationMarkdown ? (
            <div className="rounded-lg border border-slate-200 bg-slate-50 px-3 py-2 dark:border-slate-700 dark:bg-slate-800/60">
              <p className="text-xs font-semibold uppercase tracking-wide text-slate-500 dark:text-slate-400">Explanation</p>
              <p className="mt-0.5 whitespace-pre-wrap text-slate-700 dark:text-slate-300">{question.explanationMarkdown}</p>
            </div>
          ) : null}

          {question.acceptedSynonyms.length > 0 ? (
            <p className="text-xs text-slate-500 dark:text-slate-400">
              <span className="font-medium">Accepted synonyms:</span> {question.acceptedSynonyms.join(', ')}
            </p>
          ) : null}
        </div>
      ) : null}
    </li>
  );
}

// ── Root ─────────────────────────────────────────────────────────────────

export interface PrivilegedAttemptReviewProps {
  review: ReadingPrivilegedAttemptReview;
  className?: string;
}

export function PrivilegedAttemptReview({ review, className }: PrivilegedAttemptReviewProps) {
  return (
    <div className={cn('space-y-5', className)}>
      <OverrideBanner review={review} />

      <header className="space-y-1">
        <h2 className="text-lg font-semibold text-slate-900 dark:text-slate-100">{review.paperTitle}</h2>
        <p className="text-sm text-slate-500 dark:text-slate-400">
          Learner {review.userId} · {review.mode} · {review.status}
          {review.submittedAt ? ` · submitted ${new Date(review.submittedAt).toLocaleString()}` : ''}
        </p>
      </header>

      <ScoreSummary review={review} />

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-300">Sections</h3>
        <SectionTable sections={review.sections} />
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-slate-700 dark:text-slate-300">
          Questions ({review.questions.length})
        </h3>
        {review.questions.length === 0 ? (
          <p className="rounded-xl border border-dashed border-slate-300 px-4 py-6 text-center text-sm text-slate-500 dark:border-slate-700 dark:text-slate-400">
            No questions recorded for this attempt.
          </p>
        ) : (
          <ul className="space-y-2">
            {review.questions.map((question) => (
              <QuestionCard key={question.questionId} question={question} />
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
