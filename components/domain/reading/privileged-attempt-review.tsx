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
      className="rounded-xl border border-warning/30 bg-warning/10 p-4 text-sm"
    >
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden="true" />
        <div className="space-y-1">
          <p className="font-semibold text-warning-strong">
            Manual score override is active
          </p>
          <p className="text-warning-strong">
            Effective score:{' '}
            <span className="font-semibold">
              {formatScore(review.effectiveRawScore, review.effectiveScaledScore, review.effectiveGradeLetter)}
            </span>
            {' · '}System-graded:{' '}
            {formatScore(review.gradedRawScore, review.gradedScaledScore, review.gradedGradeLetter)}
          </p>
          {review.overrideReason ? (
            <p className="text-warning-strong">
              <span className="font-medium">Reason:</span> {review.overrideReason}
            </p>
          ) : null}
          {review.overriddenAt ? (
            <p className="text-xs text-warning-strong">
              Set {new Date(review.overriddenAt).toLocaleString()}
              {review.overriddenByUserId ? ` by ${review.overriddenByUserId}` : ''}
            </p>
          ) : null}
        </div>
      </div>
    </div>
  );
}

function AdminReviewBanner({ review }: { review: ReadingPrivilegedAttemptReview }) {
  if (!review.requiresAdminReview && review.invalidCount === 0) return null;
  return (
    <div
      role="alert"
      data-testid="reading-privileged-admin-review-warning"
      className="rounded-xl border border-warning/30 bg-warning/10 p-4 text-sm"
    >
      <div className="flex items-start gap-3">
        <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden="true" />
        <div className="space-y-1">
          <p className="font-semibold text-warning-strong">
            Administrator review required ({review.invalidCount} invalid answer{review.invalidCount === 1 ? '' : 's'})
          </p>
          <p className="text-warning-strong">
            Invalid answers are excluded from ordinary accuracy and conversion evidence until a controlled review is completed.
          </p>
          {review.adminReviewReason ? (
            <p className="text-xs text-warning-strong">
              <span className="font-medium">Reason:</span> {review.adminReviewReason}
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
      <div className="rounded-xl border border-border bg-white p-4 dark:bg-slate-900">
        <p className="eyebrow text-muted">
          System-graded score
        </p>
        <p className="mt-1 text-lg font-bold text-navy">
          {formatScore(review.gradedRawScore, review.gradedScaledScore, review.gradedGradeLetter)}
        </p>
      </div>
      <div
        className={cn(
          'rounded-xl border p-4',
          review.hasOverride
            ? 'border-warning/30 bg-warning/10'
            : 'border-border bg-white dark:bg-slate-900',
        )}
      >
        <p className="eyebrow text-muted">
          Effective score
        </p>
        <p className="mt-1 text-lg font-bold text-navy">
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
    <div className="overflow-x-auto rounded-xl border border-border">
      <table className="w-full border-collapse text-sm">
        <caption className="sr-only">Per-section raw scores and accuracy</caption>
        <thead>
          <tr className="border-b border-border bg-background-light text-left eyebrow text-muted">
            <th scope="col" className="px-4 py-2.5">Section</th>
            <th scope="col" className="px-4 py-2.5 text-right">Raw</th>
            <th scope="col" className="px-4 py-2.5 text-right">Accuracy</th>
            <th scope="col" className="px-4 py-2.5 text-right">Correct</th>
            <th scope="col" className="px-4 py-2.5 text-right">Incorrect</th>
            <th scope="col" className="px-4 py-2.5 text-right">Invalid review</th>
            <th scope="col" className="px-4 py-2.5 text-right">Unanswered</th>
          </tr>
        </thead>
        <tbody>
          {sections.map((section) => (
            <tr
              key={section.partCode}
              className="border-b border-border last:border-b-0"
            >
              <th scope="row" className="px-4 py-2.5 text-left font-medium text-navy">
                {partLabel(section.partCode)}
              </th>
              <td className="px-4 py-2.5 text-right tabular-nums text-navy">
                {section.rawScore}/{section.maxRawScore}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-navy">
                {section.accuracyPercent === null ? '-' : `${section.accuracyPercent}%`}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-success-strong">
                {section.correctCount}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-danger-strong">
                {section.incorrectCount}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-warning-strong">
                {section.invalidCount}
              </td>
              <td className="px-4 py-2.5 text-right tabular-nums text-muted">
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
    if (question.isInvalid) {
      return <AlertTriangle className="h-4 w-4 text-warning-strong" aria-hidden="true" />;
    }
    if (question.isCorrect === true) {
      return <CheckCircle2 className="h-4 w-4 text-success-strong" aria-hidden="true" />;
    }
    if (question.isCorrect === false) {
      return <XCircle className="h-4 w-4 text-danger-strong" aria-hidden="true" />;
    }
    return <MinusCircle className="h-4 w-4 text-muted" aria-hidden="true" />;
  }, [question.isCorrect]);

  return (
    <li className="rounded-xl border border-border bg-white dark:bg-slate-900">
      <button
        type="button"
        onClick={() => setOpen((prev) => !prev)}
        aria-expanded={open}
        className="flex w-full items-start gap-3 px-4 py-3 text-left"
      >
        <span className="mt-0.5 shrink-0">{statusIcon}</span>
        <span className="min-w-0 flex-1">
          <span className="flex flex-wrap items-center gap-2">
            <span className="eyebrow text-muted">
              {question.partCode} · Q{question.displayOrder}
            </span>
            {question.flaggedForReview ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-warning/10 px-2 py-0.5 text-3xs font-medium text-warning-strong">
                <Flag className="h-3 w-3" aria-hidden="true" /> Flagged
              </span>
            ) : null}
            {question.isInvalid ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-warning/10 px-2 py-0.5 text-3xs font-medium text-warning-strong">
                <AlertTriangle className="h-3 w-3" aria-hidden="true" /> Invalid — admin review
              </span>
            ) : null}
            {question.answerRevisionCount > 0 ? (
              <span className="inline-flex items-center gap-1 rounded-full bg-background-light px-2 py-0.5 text-3xs font-medium text-muted">
                <Pencil className="h-3 w-3" aria-hidden="true" /> {question.answerRevisionCount} revision
                {question.answerRevisionCount === 1 ? '' : 's'}
              </span>
            ) : null}
          </span>
          <span className="mt-1 block text-sm font-medium text-navy line-clamp-2">
            {question.stem}
          </span>
        </span>
        <span className="ml-1 shrink-0 text-muted">
          {open ? <ChevronDown className="h-4 w-4" aria-hidden="true" /> : <ChevronRight className="h-4 w-4" aria-hidden="true" />}
        </span>
      </button>

      {open ? (
        <div className="space-y-3 border-t border-border px-4 py-3 text-sm">
          <dl className="grid gap-x-6 gap-y-2 sm:grid-cols-2">
            <div>
              <dt className="eyebrow text-muted">Learner answer</dt>
              <dd className="mt-0.5 text-navy">{formatValue(question.userAnswer)}</dd>
            </div>
            <div>
              <dt className="eyebrow text-muted">Correct answer</dt>
              <dd className="mt-0.5 font-medium text-success-strong">{formatValue(question.correctAnswer)}</dd>
            </div>
            <div>
              <dt className="eyebrow text-muted">Points</dt>
              <dd className="mt-0.5 text-navy">
                {question.pointsEarned}/{question.maxPoints}
              </dd>
            </div>
            <div>
              <dt className="eyebrow text-muted">Skill tag</dt>
              <dd className="mt-0.5 text-navy">{question.skillTag ?? '-'}</dd>
            </div>
            <div className="flex items-center gap-1.5">
              <Clock className="h-3.5 w-3.5 text-muted" aria-hidden="true" />
              <div>
                <dt className="eyebrow text-muted">Time on question</dt>
                <dd className="mt-0.5 text-navy">{formatMs(question.elapsedMs)}</dd>
              </div>
            </div>
            <div>
              <dt className="eyebrow text-muted">Cumulative time</dt>
              <dd className="mt-0.5 text-navy">{formatMs(question.totalElapsedMs)}</dd>
            </div>
          </dl>

          {question.isInvalid ? (
            <div className="rounded-lg bg-warning/10 px-3 py-2">
              <p className="eyebrow text-warning-strong">Invalid answer</p>
              <p className="mt-0.5 text-warning-strong">
                The persisted answer is indeterminate and requires controlled administrator review; it is not an ordinary incorrect response.
              </p>
            </div>
          ) : question.missReason ? (
            <div className="rounded-lg bg-danger/10 px-3 py-2">
              <p className="eyebrow text-danger-strong">Miss reason</p>
              <p className="mt-0.5 text-danger-strong">{question.missReason}</p>
            </div>
          ) : null}

          {question.selectedDistractorCategory ? (
            <div className="rounded-lg bg-background-light px-3 py-2">
              <p className="eyebrow text-muted">
                Distractor: {question.selectedDistractorCategory}
              </p>
              {formatValue(question.distractorRationale) !== '-' ? (
                <p className="mt-0.5 text-navy">
                  {formatValue(question.distractorRationale)}
                </p>
              ) : null}
            </div>
          ) : null}

          {question.explanationMarkdown ? (
            <div className="rounded-lg border border-border bg-background-light px-3 py-2">
              <p className="eyebrow text-muted">Explanation</p>
              <p className="mt-0.5 whitespace-pre-wrap text-navy">{question.explanationMarkdown}</p>
            </div>
          ) : null}

          {question.acceptedSynonyms.length > 0 ? (
            <p className="text-xs text-muted">
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
        <h2 className="text-lg font-semibold text-navy">{review.paperTitle}</h2>
        <p className="text-sm text-muted">
          Learner {review.userId} · {review.mode} · {review.status}
          {review.submittedAt ? ` · submitted ${new Date(review.submittedAt).toLocaleString()}` : ''}
        </p>
      </header>

      <AdminReviewBanner review={review} />

      <ScoreSummary review={review} />

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">Sections</h3>
        <SectionTable sections={review.sections} />
      </section>

      <section className="space-y-2">
        <h3 className="text-sm font-semibold text-navy">
          Questions ({review.questions.length})
        </h3>
        {review.questions.length === 0 ? (
          <p className="rounded-xl border border-dashed border-border-hover px-4 py-6 text-center text-sm text-muted">
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
