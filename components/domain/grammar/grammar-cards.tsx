'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2, Clock3, LayoutGrid, Sparkles, Target, Trophy, XCircle } from 'lucide-react';
import { useMemo, type ElementType } from 'react';import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { ProgressBar } from '@/components/ui/progress';
import { RadioGroup, Select, Textarea } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import type {
  GrammarExerciseChoiceOption,
  GrammarExerciseLearner,
  GrammarExerciseResult,
  GrammarMatchingPair,
  GrammarLessonSummary,
  GrammarRecommendation,
  GrammarTopicLearner,
} from '@/lib/grammar/types';
import { SafeRichText } from './grammar-content-renderer';

function isChoiceOption(option: GrammarExerciseChoiceOption | GrammarMatchingPair): option is GrammarExerciseChoiceOption {
  return 'id' in option;
}

function isMatchingPair(option: GrammarExerciseChoiceOption | GrammarMatchingPair): option is GrammarMatchingPair {
  return 'left' in option && 'right' in option;
}

function formatTopicLabel(topic: GrammarTopicLearner | undefined, fallback: string) {
  if (topic?.name) return topic.name;
  return fallback.replace(/_/g, ' ');
}

function titleCase(value: string) {
  return value
    .split(/[\s_-]+/)
    .filter(Boolean)
    .map((part) => part.charAt(0).toUpperCase() + part.slice(1))
    .join(' ');
}

// ─── GrammarTopicCard ─────────────────────────────────────────────────────
// Mirrors the dashboard's action cards: cream surface, soft border, violet
// icon tile, navy headline, muted metadata, primary link-action footer.
export function GrammarTopicCard({ topic }: { topic: GrammarTopicLearner }) {
  const href = `/grammar/topics/${encodeURIComponent(topic.slug)}`;
  const masteryPct = topic.lessonCount > 0
    ? Math.round((topic.masteredLessonCount / topic.lessonCount) * 100)
    : 0;

  return (
    <Link href={href} className="block focus-visible:outline-none">
      <Card hoverable className="h-full">
        <div className="flex h-full flex-col">
          <div className="flex items-start gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-lg">
              <span className="text-primary">{topic.iconEmoji ?? '📘'}</span>
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">{titleCase(topic.levelHint || 'All levels')}</p>
              <h3 className="mt-0.5 text-base font-bold leading-snug text-navy">{topic.name}</h3>
            </div>
            <Badge variant="muted">{topic.lessonCount} {topic.lessonCount === 1 ? 'lesson' : 'lessons'}</Badge>
          </div>

          {topic.description ? (
            <p className="mt-3 line-clamp-2 text-sm leading-6 text-muted">{topic.description}</p>
          ) : null}

          <div className="mt-4 grid grid-cols-3 gap-2">
            <StatPill icon={LayoutGrid} label="Lessons" value={topic.lessonCount} />
            <StatPill icon={CheckCircle2} label="Done" value={topic.completedLessonCount} />
            <StatPill icon={Trophy} label="Mastered" value={topic.masteredLessonCount} />
          </div>

          {topic.lessonCount > 0 ? (
            <div className="mt-4">
              <div className="mb-1 flex items-center justify-between text-xs text-muted">
                <span>Mastery</span>
                <span className="font-semibold text-navy">{masteryPct}%</span>
              </div>
              <ProgressBar value={masteryPct} ariaLabel={`${topic.name} mastery ${masteryPct}%`} color="primary" />
            </div>
          ) : null}

          <div className="mt-4 flex items-center justify-end border-t border-border/60 pt-3 text-sm font-semibold text-primary">
            Explore topic <ArrowRight className="ml-1 h-4 w-4" />
          </div>
        </div>
      </Card>
    </Link>
  );
}

// ─── GrammarLessonCard ────────────────────────────────────────────────────
// Same rhythm as dashboard task cards. Cream surface, soft border, small
// icon tile, navy headline, meta row, mastery bar when progress exists.
export function GrammarLessonCard({ lesson }: { lesson: GrammarLessonSummary }) {
  const topicLabel = useMemo(() => formatTopicLabel(undefined, lesson.topicName ?? lesson.category), [lesson.category, lesson.topicName]);
  const progressPct = Math.max(0, Math.min(100, lesson.progress?.masteryScore ?? 0));
  const status = lesson.mastered
    ? { label: 'Mastered', variant: 'success' as const }
    : lesson.progress?.status === 'in_progress'
      ? { label: 'In progress', variant: 'info' as const }
      : lesson.progress?.status === 'completed'
        ? { label: 'Completed', variant: 'success' as const }
        : { label: 'New', variant: 'muted' as const };

  return (
    <Link href={`/grammar/${encodeURIComponent(lesson.id)}`} className="block focus-visible:outline-none">
      <Card hoverable className="h-full">
        <div className="flex h-full flex-col">
          <div className="flex items-start gap-3">
            <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
              <Sparkles className="h-5 w-5" />
            </div>
            <div className="min-w-0 flex-1">
              <p className="text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">{titleCase(topicLabel)}</p>
              <h3 className="mt-0.5 text-base font-bold leading-snug text-navy">{lesson.title}</h3>
            </div>
            <Badge variant={status.variant}>{status.label}</Badge>
          </div>

          {lesson.description ? (
            <p className="mt-3 line-clamp-2 text-sm leading-6 text-muted">{lesson.description}</p>
          ) : null}

          <div className="mt-4 flex flex-wrap items-center gap-x-4 gap-y-2 text-sm font-semibold text-muted">
            <span className="inline-flex items-center gap-1.5">
              <Clock3 className="h-4 w-4" />
              {lesson.estimatedMinutes} min
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Target className="h-4 w-4" />
              {titleCase(lesson.level)}
            </span>
            <span className="inline-flex items-center gap-1.5">
              <Sparkles className="h-4 w-4" />
              {lesson.exerciseCount} exercises
            </span>
          </div>

          {lesson.progress ? (
            <div className="mt-4">
              <div className="mb-1 flex items-center justify-between text-xs text-muted">
                <span>Mastery</span>
                <span className="font-semibold text-navy">{progressPct}%</span>
              </div>
              <ProgressBar
                value={progressPct}
                ariaLabel={`${lesson.title} mastery ${progressPct}%`}
                color={progressPct >= 80 ? 'success' : 'primary'}
              />
            </div>
          ) : null}

          <div className="mt-4 flex items-center justify-end border-t border-border/60 pt-3 text-sm font-semibold text-primary">
            Open lesson <ArrowRight className="ml-1 h-4 w-4" />
          </div>
        </div>
      </Card>
    </Link>
  );
}

// ─── GrammarRecommendationStrip ──────────────────────────────────────────
// Light cream card, violet eyebrow, inner cards on `bg-surface` so the
// composition feels like the dashboard "Next action" rail — not a dark panel.
export function GrammarRecommendationStrip({
  recommendations,
  onOpen,
  onDismiss,
}: {
  recommendations: GrammarRecommendation[];
  onOpen?: (recommendation: GrammarRecommendation) => void;
  onDismiss?: (recommendation: GrammarRecommendation) => void;
}) {
  if (recommendations.length === 0) return null;

  return (
    <Card padding="md" className="border-primary/15 bg-primary/5">
      <div className="mb-4 flex items-center gap-3">
        <div className="flex h-10 w-10 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Sparkles className="h-4 w-4" />
        </div>
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Recommended next</p>
          <h3 className="text-base font-bold text-navy">Pick up where you left off</h3>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {recommendations.slice(0, 3).map((rec) => (
          <div key={rec.id} className="relative">
            <Link
              href={`/grammar/${encodeURIComponent(rec.lessonId)}`}
              onClick={() => onOpen?.(rec)}
              className="block h-full focus-visible:outline-none"
            >
              <Card hoverable className="h-full bg-surface">
                <div className="flex h-full flex-col">
                  <div className="flex items-start gap-3">
                    <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
                      <Target className="h-4 w-4" />
                    </div>
                    <div className="min-w-0 flex-1">
                      <p className="truncate text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">
                        {rec.topicName ?? (rec.topicSlug ? titleCase(rec.topicSlug) : 'Grammar')}
                      </p>
                      <h4 className="mt-0.5 line-clamp-2 text-sm font-bold leading-snug text-navy">{rec.title}</h4>
                    </div>
                    <Badge variant="info">{titleCase(rec.level)}</Badge>
                  </div>

                  {rec.reason ? (
                    <p className="mt-3 line-clamp-2 text-sm leading-6 text-muted">{rec.reason}</p>
                  ) : null}

                  <div className="mt-auto flex items-center justify-between gap-2 border-t border-border/60 pt-3 text-xs text-muted">
                    <span className="inline-flex items-center gap-1.5 font-semibold">
                      <Clock3 className="h-3.5 w-3.5" /> {rec.estimatedMinutes} min
                    </span>
                    <span className="inline-flex items-center gap-1 font-semibold text-primary">
                      Start <ArrowRight className="h-3.5 w-3.5" />
                    </span>
                  </div>
                </div>
              </Card>
            </Link>
            {onDismiss ? (
              <button
                type="button"
                aria-label="Dismiss recommendation"
                className="absolute right-3 top-3 inline-flex h-7 w-7 items-center justify-center rounded-full border border-border bg-surface text-muted shadow-sm transition-colors hover:border-border-hover hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onDismiss(rec);
                }}
              >
                <XCircle className="h-4 w-4" />
              </button>
            ) : null}
          </div>
        ))}
      </div>
    </Card>
  );
}

// ─── GrammarExerciseRunner ───────────────────────────────────────────────
export function GrammarExerciseRunner({
  exercise,
  answer,
  disabled,
  onAnswer,
  result,
}: {
  exercise: GrammarExerciseLearner;
  answer: unknown;
  disabled?: boolean;
  onAnswer: (value: unknown) => void;
  result?: GrammarExerciseResult | null;
}) {
  const isResultMode = Boolean(result);
  const tone = result
    ? result.isCorrect
      ? 'border-emerald-200 bg-emerald-50/60'
      : 'border-rose-200 bg-rose-50/60'
    : '';

  return (
    <Card className={cn('space-y-4', tone)}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <Badge variant="muted">{exercise.type.replace(/_/g, ' ')}</Badge>
            <span className="text-xs font-semibold text-muted">{exercise.points} pts</span>
          </div>
          <SafeRichText markdown={exercise.promptMarkdown} className="text-sm leading-6 text-navy" />
        </div>
        {result ? (
          <Badge variant={result.isCorrect ? 'success' : 'danger'}>
            {result.isCorrect ? 'Correct' : 'Review'}
          </Badge>
        ) : null}
      </div>

      <ExerciseInput exercise={exercise} answer={answer} disabled={disabled || isResultMode} onAnswer={onAnswer} result={result} />

      {result ? (
        <div className="space-y-3 rounded-xl border border-border/60 bg-surface p-4 text-sm shadow-sm">
          <div className="flex flex-wrap gap-2">
            <Badge variant={result.isCorrect ? 'success' : 'danger'}>
              {result.pointsAwarded}/{result.pointsPossible} points
            </Badge>
            {result.reviewItemCreated ? <Badge variant="warning">Added to review</Badge> : null}
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            <ResultPanel title="Your answer" value={formatAnswer(answer, exercise.type)} />
            <ResultPanel title="Correct answer" value={formatAnswer(result.correctAnswer, exercise.type)} accent="success" />
          </div>
          {result.explanationMarkdown ? (
            <SafeRichText markdown={result.explanationMarkdown} className="text-sm leading-6 text-muted" />
          ) : null}
        </div>
      ) : null}
    </Card>
  );
}

function ExerciseInput({
  exercise,
  answer,
  disabled,
  onAnswer,
  result,
}: {
  exercise: GrammarExerciseLearner;
  answer: unknown;
  disabled?: boolean;
  onAnswer: (value: unknown) => void;
  result?: GrammarExerciseResult | null;
}) {
  switch (exercise.type) {
    case 'mcq': {
      const options = exercise.options.filter(isChoiceOption);
      const value = typeof answer === 'string' ? answer : '';
      return (
        <RadioGroup
          name={exercise.id}
          label="Select the best answer"
          value={value}
          onChange={(next) => onAnswer(next)}
          options={options.map((option) => ({ value: option.id, label: option.label }))}
          className="gap-3"
        />
      );
    }

    case 'matching': {
      const pairs = exercise.options.filter(isMatchingPair);
      const rightOptions = Array.from(new Set(pairs.map((pair) => pair.right))).filter(Boolean);
      const selected = isRecord(answer) ? answer : {};
      return (
        <div className="space-y-3">
          {pairs.map((pair, index) => (
            <div key={`${pair.left}-${index}`} className="grid gap-3 md:grid-cols-[1fr_auto_1fr] md:items-center">
              <div className="rounded-lg border border-border/60 bg-surface px-4 py-3 text-sm text-navy shadow-sm">
                {pair.left}
              </div>
              <div className="hidden justify-center text-muted md:flex">→</div>
              <Select
                label=""
                value={typeof selected[pair.left] === 'string' ? selected[pair.left] : ''}
                onChange={(event) => onAnswer({ ...selected, [pair.left]: event.target.value })}
                disabled={disabled}
                options={rightOptions.map((item) => ({ value: item, label: item }))}
                placeholder="Choose a match"
              />
            </div>
          ))}
          {result ? null : <p className="text-xs text-muted">Match each left half to the correct right half.</p>}
        </div>
      );
    }

    case 'fill_blank':
    case 'error_correction':
    case 'sentence_transformation': {
      const value = typeof answer === 'string' ? answer : '';
      const rows = exercise.type === 'sentence_transformation' ? 3 : 2;
      return (
        <Textarea
          label={exercise.type === 'error_correction' ? 'Write the corrected sentence' : 'Enter your answer'}
          value={value}
          onChange={(event) => onAnswer(event.target.value)}
          disabled={disabled}
          rows={rows}
        />
      );
    }

    default:
      return null;
  }
}

function ResultPanel({ title, value, accent = 'default' }: { title: string; value: string; accent?: 'default' | 'success' }) {
  return (
    <div
      className={cn(
        'rounded-lg border px-4 py-3 text-sm shadow-sm',
        accent === 'success'
          ? 'border-emerald-200 bg-emerald-50/80 text-emerald-900'
          : 'border-border/70 bg-surface text-navy',
      )}
    >
      <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{title}</p>
      <p className="mt-1 whitespace-pre-wrap leading-6">{value || '—'}</p>
    </div>
  );
}

function StatPill({ icon: Icon, label, value }: { icon: ElementType; label: string; value: number }) {
  return (
    <div className="rounded-lg border border-border/60 bg-surface px-3 py-2 shadow-sm">
      <div className="flex items-center gap-1.5 text-[10px] font-semibold uppercase tracking-[0.14em] text-muted">
        <Icon className="h-3.5 w-3.5" /> {label}
      </div>
      <div className="mt-0.5 text-base font-bold text-navy">{value}</div>
    </div>
  );
}

function isRecord(value: unknown): value is Record<string, string> {
  return Boolean(value) && typeof value === 'object' && !Array.isArray(value);
}

function formatAnswer(answer: unknown, exerciseType: GrammarExerciseLearner['type']) {
  if (exerciseType === 'matching') {
    if (Array.isArray(answer)) {
      const pairs = answer.filter(isMatchingPair);
      if (pairs.length > 0) {
        return pairs.map((pair) => `${pair.left} → ${pair.right}`).join('\n');
      }
    }

    if (isRecord(answer)) {
      return Object.entries(answer)
        .map(([left, right]) => `${left} → ${right}`)
        .join('\n');
    }

    return '—';
  }

  if (typeof answer === 'string') return answer || '—';
  if (answer == null) return '—';
  return JSON.stringify(answer);
}

export { SafeRichText } from './grammar-content-renderer';
