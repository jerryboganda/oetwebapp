'use client';

import { ArrowRight, CheckCircle2, Clock3, LayoutGrid, Sparkles, Target, Trophy, XCircle } from 'lucide-react';
import { useMemo, type ElementType } from 'react';
import { LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { MotionItem } from '@/components/ui/motion-primitives';
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
    <CardLink href={href} className="group flex h-full flex-col">
      <div className="flex items-start gap-4">
        <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-primary/15 text-primary shadow-inner">
          {topic.iconEmoji ? <span className="text-xl" aria-hidden>{topic.iconEmoji}</span> : <LayoutGrid className="h-5 w-5" aria-hidden />}
        </div>
        <div className="min-w-0 flex-1">
          <p className="eyebrow text-muted">{titleCase(topic.levelHint || 'OET')}</p>
          <h3 className="mt-1 text-lg font-bold leading-tight text-navy">{topic.name}</h3>
        </div>
        <Badge variant="info" className="shrink-0 tabular-nums">{topic.lessonCount} lessons</Badge>
      </div>

      {topic.description ? (
        <p className="mt-4 line-clamp-2 text-sm leading-6 text-muted">{topic.description}</p>
      ) : null}

      <div className="mt-5 grid grid-cols-[repeat(auto-fit,minmax(min(100%,6.5rem),1fr))] gap-2">
        <StatPill icon={LayoutGrid} label="Lessons" value={topic.lessonCount} />
        <StatPill icon={CheckCircle2} label="Done" value={topic.completedLessonCount} />
        <StatPill icon={Trophy} label="Mastered" value={topic.masteredLessonCount} />
      </div>

      <div className="mt-5">
        <div className="mb-1.5 flex items-center justify-between text-xs text-muted">
          <span>Mastery</span>
          <span className="font-bold tabular-nums text-navy">{masteryPct}%</span>
        </div>
        <ProgressBar value={masteryPct} ariaLabel={`${topic.name} mastery ${masteryPct}%`} color="primary" />
      </div>

      <div className="mt-auto pt-5">
        <div className="flex items-center justify-between rounded-xl border border-primary/15 bg-primary/10 px-4 py-3 text-sm font-bold text-primary">
          <span>Explore topic</span>
          <ArrowRight className="h-4 w-4 transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden="true" />
        </div>
      </div>
    </CardLink>
  );
}

// GrammarLessonCard
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
    <CardLink href={`/grammar/${encodeURIComponent(lesson.id)}`} className="group flex h-full flex-col">
      <div className="flex items-start gap-3">
        <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-primary/10 text-primary">
          <Sparkles className="h-5 w-5" aria-hidden="true" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="eyebrow text-muted">{titleCase(topicLabel)}</p>
          <h3 className="mt-0.5 text-base font-bold leading-snug text-navy">{lesson.title}</h3>
        </div>
        <Badge variant={status.variant} className="shrink-0">{status.label}</Badge>
      </div>

      {lesson.description ? (
        <p className="mt-3 line-clamp-2 text-sm leading-6 text-muted">{lesson.description}</p>
      ) : null}

      <div className="mt-4 flex flex-wrap items-center gap-x-4 gap-y-2 text-sm font-semibold text-muted">
        <span className="inline-flex items-center gap-1.5 tabular-nums">
          <Clock3 className="h-4 w-4" aria-hidden="true" />
          {lesson.estimatedMinutes} min
        </span>
        <span className="inline-flex items-center gap-1.5">
          <Target className="h-4 w-4" aria-hidden="true" />
          {titleCase(lesson.level)}
        </span>
        <span className="inline-flex items-center gap-1.5 tabular-nums">
          <Sparkles className="h-4 w-4" aria-hidden="true" />
          {lesson.exerciseCount} exercises
        </span>
      </div>

      {lesson.progress ? (
        <div className="mt-4">
          <div className="mb-1 flex items-center justify-between text-xs text-muted">
            <span>Mastery</span>
            <span className="font-semibold tabular-nums text-navy">{progressPct}%</span>
          </div>
          <ProgressBar
            value={progressPct}
            ariaLabel={`${lesson.title} mastery ${progressPct}%`}
            color={progressPct >= 80 ? 'success' : 'primary'}
          />
        </div>
      ) : null}

      <div className="mt-auto pt-4">
        <div className="flex items-center justify-end border-t border-border/60 pt-3 text-sm font-semibold text-primary">
          Open lesson <ArrowRight className="ms-1 h-4 w-4 transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden="true" />
        </div>
      </div>
    </CardLink>
  );
}

// ─── GrammarRecommendationStrip ──────────────────────────────────────────
// A page section like the dashboard "Next action" rail: a section header over
// one row of hoverable recommendation cards (one surface level, no outer card).
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
    <section aria-label="Recommended next" className="space-y-4">
      <LearnerSurfaceSectionHeader
        eyebrow="Recommended next"
        icon={Sparkles}
        title="Pick up where you left off"
        action={<Badge variant="info" className="self-start tabular-nums sm:self-auto">{recommendations.length} ready</Badge>}
      />

      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3">
        {recommendations.slice(0, 3).map((rec, index) => (
          <MotionItem key={rec.id} delayIndex={index} className="relative h-full">
            <CardLink
              href={`/grammar/${encodeURIComponent(rec.lessonId)}`}
              onClick={() => onOpen?.(rec)}
              className="group flex h-full flex-col border-primary/15 hover:border-primary/35"
            >
              {/* pe-10 keeps the level badge clear of the dismiss button. */}
              <div className={cn('flex items-start gap-3', onDismiss && 'pe-10')}>
                <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/15 text-primary">
                  <Target className="h-4 w-4" aria-hidden="true" />
                </div>
                <div className="min-w-0 flex-1">
                  <p className="truncate eyebrow text-muted">
                    {rec.topicName ?? (rec.topicSlug ? titleCase(rec.topicSlug) : 'Grammar')}
                  </p>
                  <h3 className="mt-1 line-clamp-2 text-sm font-bold leading-snug text-navy">{rec.title}</h3>
                </div>
                <Badge variant="info" className="shrink-0">{titleCase(rec.level)}</Badge>
              </div>

              {rec.reason ? (
                <p className="mt-3 line-clamp-2 text-sm leading-6 text-muted">{rec.reason}</p>
              ) : null}

              <div className="mt-auto pt-3">
                <div className="flex items-center justify-between gap-2 border-t border-border/60 pt-3 text-xs text-muted">
                  <span className="inline-flex items-center gap-1.5 font-bold tabular-nums">
                    <Clock3 className="h-3.5 w-3.5" aria-hidden="true" /> {rec.estimatedMinutes} min
                  </span>
                  <span className="inline-flex items-center gap-1 font-bold text-primary">
                    Start <ArrowRight className="h-3.5 w-3.5 transition-transform group-hoverable:translate-x-0.5 rtl:rotate-180 rtl:group-hoverable:-translate-x-0.5" aria-hidden="true" />
                  </span>
                </div>
              </div>
            </CardLink>
            {onDismiss ? (
              <button
                type="button"
                aria-label="Dismiss recommendation"
                className="absolute end-1.5 top-1.5 inline-flex size-11 items-center justify-center rounded-full text-muted transition-colors hover:bg-background-light hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:size-9"
                onClick={(event) => {
                  event.preventDefault();
                  event.stopPropagation();
                  onDismiss(rec);
                }}
              >
                <XCircle className="h-4 w-4" aria-hidden="true" />
              </button>
            ) : null}
          </MotionItem>
        ))}
      </div>
    </section>
  );
}

// GrammarExerciseRunner
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
      ? 'border-success/30 bg-success/5'
      : 'border-danger/30 bg-danger/5'
    : '';

  return (
    <Card className={cn('space-y-4', tone)}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <Badge variant="muted">{exercise.type.replace(/_/g, ' ')}</Badge>
            <span className="text-xs font-semibold text-muted">{exercise.points} pts</span>
          </div>
          <SafeRichText markdown={exercise.promptMarkdown} className="max-w-3xl text-sm leading-6 text-navy" />
        </div>
        {result ? (
          <Badge variant={result.isCorrect ? 'success' : 'danger'}>
            {result.isCorrect ? 'Correct' : 'Review'}
          </Badge>
        ) : null}
      </div>

      <ExerciseInput exercise={exercise} answer={answer} disabled={disabled || isResultMode} onAnswer={onAnswer} result={result} />

      {result ? (
        <div className="space-y-3 border-t border-border pt-4 text-sm">
          <div className="flex flex-wrap gap-2">
            <Badge variant={result.isCorrect ? 'success' : 'danger'} className="tabular-nums">
              {result.pointsAwarded}/{result.pointsPossible} points
            </Badge>
            {result.reviewItemCreated ? <Badge variant="warning">Added to review</Badge> : null}
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            <ResultPanel title="Your answer" value={formatAnswer(answer, exercise.type)} />
            <ResultPanel title="Correct answer" value={formatAnswer(result.correctAnswer, exercise.type)} accent="success" />
          </div>
          {result.explanationMarkdown ? (
            <SafeRichText markdown={result.explanationMarkdown} className="max-w-3xl text-sm leading-6 text-muted" />
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
            <div key={`${pair.left}-${index}`} className="grid grid-cols-1 gap-3 md:grid-cols-[minmax(0,1fr)_auto_minmax(0,1fr)] md:items-center">
              <div className="rounded-xl border border-border bg-background-light px-4 py-3 text-sm text-navy">
                {pair.left}
              </div>
              <div className="hidden justify-center text-muted rtl:rotate-180 md:flex" aria-hidden="true">→</div>
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
        'rounded-xl border px-4 py-3 text-sm',
        accent === 'success'
          ? 'border-success/30 bg-success/10 text-navy'
          : 'border-border bg-background-light text-navy',
      )}
    >
      <p className="eyebrow text-muted">{title}</p>
      <p className="mt-1 whitespace-pre-wrap leading-6">{value || '-'}</p>
    </div>
  );
}

function StatPill({ icon: Icon, label, value }: { icon: ElementType; label: string; value: number }) {
  return (
    <div className="min-w-0 rounded-xl border border-border bg-background-light px-3 py-2">
      <div className="tile-label flex min-w-0 items-center gap-1.5 text-muted">
        <Icon className="h-3.5 w-3.5 shrink-0" aria-hidden="true" /> {label}
      </div>
      <div className="mt-0.5 text-base font-bold tabular-nums text-navy">{value}</div>
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

    return '-';
  }

  if (typeof answer === 'string') return answer || '-';
  if (answer == null) return '-';
  return JSON.stringify(answer);
}

export { SafeRichText } from './grammar-content-renderer';
