'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2, Clock3, LayoutGrid, Sparkles, Target, Trophy, XCircle } from 'lucide-react';
import { useMemo } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, RadioGroup, Select, Textarea } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import type {
  GrammarExerciseAuthoring,
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
  return fallback;
}

export function GrammarTopicCard({ topic }: { topic: GrammarTopicLearner }) {
  const href = `/grammar/topics/${encodeURIComponent(topic.slug)}`;

  return (
    <Link href={href} className="block">
      <Card hoverable className="h-full border-border transition-transform duration-200 hover:-translate-y-0.5">
        <div className="flex h-full flex-col gap-4">
          <div className="flex items-start justify-between gap-3">
            <div className="flex items-center gap-3">
              <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-primary/10 text-xl text-primary-dark">
                {topic.iconEmoji ?? '📘'}
              </div>
              <div className="min-w-0">
                <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{topic.levelHint}</p>
                <h3 className="truncate text-lg font-bold text-navy dark:text-white">{topic.name}</h3>
              </div>
            </div>
            <Badge variant="muted">{topic.lessonCount} lessons</Badge>
          </div>

          {topic.description ? <p className="line-clamp-3 text-sm leading-6 text-muted">{topic.description}</p> : null}

          <div className="mt-auto grid grid-cols-3 gap-2 text-center text-xs">
            <StatPill label="Lessons" value={topic.lessonCount} icon={LayoutGrid} />
            <StatPill label="Done" value={topic.completedLessonCount} icon={CheckCircle2} />
            <StatPill label="Mastered" value={topic.masteredLessonCount} icon={Trophy} />
          </div>

          <div className="flex items-center justify-end text-sm font-semibold text-primary">
            Explore <ArrowRight className="ml-1 h-4 w-4" />
          </div>
        </div>
      </Card>
    </Link>
  );
}

export function GrammarLessonCard({ lesson }: { lesson: GrammarLessonSummary }) {
  const topicLabel = useMemo(() => formatTopicLabel(undefined, lesson.topicName ?? lesson.category), [lesson.category, lesson.topicName]);
  const progressPct = lesson.progress?.masteryScore ?? 0;

  return (
    <Link href={`/grammar/${encodeURIComponent(lesson.id)}`} className="block">
      <Card hoverable className="h-full border-border transition-transform duration-200 hover:-translate-y-0.5">
        <div className="flex h-full flex-col gap-4">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <Badge variant="outline" className="mb-2">{topicLabel}</Badge>
              <h3 className="truncate text-lg font-bold text-navy dark:text-white">{lesson.title}</h3>
            </div>
            {lesson.mastered ? <Badge variant="success">Mastered</Badge> : lesson.progress?.status === 'in_progress' ? <Badge variant="info">In progress</Badge> : <Badge variant="muted">New</Badge>}
          </div>

          {lesson.description ? <p className="line-clamp-3 text-sm leading-6 text-muted">{lesson.description}</p> : null}

          <div className="flex flex-wrap gap-2 text-xs text-muted">
            <MetaBadge icon={Clock3} label={`${lesson.estimatedMinutes} min`} />
            <MetaBadge icon={Target} label={lesson.level} />
            <MetaBadge icon={Sparkles} label={`${lesson.exerciseCount} exercises`} />
          </div>

          <div className="mt-auto space-y-2">
            {lesson.progress ? (
              <div>
                <div className="flex items-center justify-between text-xs text-muted">
                  <span>Mastery</span>
                  <span className="font-semibold text-navy dark:text-white">{Math.max(0, Math.min(100, progressPct))}%</span>
                </div>
                <div className="mt-1 h-2 overflow-hidden rounded-full bg-background-light dark:bg-gray-900">
                  <div className="h-full rounded-full bg-gradient-to-r from-primary to-primary-dark" style={{ width: `${Math.max(0, Math.min(100, progressPct))}%` }} />
                </div>
              </div>
            ) : null}

            <div className="flex items-center justify-end text-sm font-semibold text-primary">
              Open lesson <ArrowRight className="ml-1 h-4 w-4" />
            </div>
          </div>
        </div>
      </Card>
    </Link>
  );
}

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
    <div className="space-y-3 rounded-[24px] border border-primary/15 bg-primary/5 p-4 sm:p-5">
      <div className="flex items-center gap-2">
        <div className="flex h-9 w-9 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <Sparkles className="h-4 w-4" />
        </div>
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Recommended next</p>
          <h3 className="text-sm font-bold text-navy dark:text-white">Pick up where you left off</h3>
        </div>
      </div>

      <div className="grid gap-3 md:grid-cols-2 xl:grid-cols-3">
        {recommendations.map((rec) => (
          <Link
            key={rec.id}
            href={`/grammar/${encodeURIComponent(rec.lessonId)}`}
            onClick={() => onOpen?.(rec)}
            className="block"
          >
            <Card hoverable className="h-full border-border bg-white/90 transition-transform duration-200 hover:-translate-y-0.5 dark:bg-gray-800">
              <div className="flex h-full flex-col gap-3">
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{rec.topicName ?? rec.topicSlug ?? 'Grammar'}</p>
                    <h4 className="truncate text-sm font-bold text-navy dark:text-white">{rec.title}</h4>
                  </div>
                  <Badge variant="info">{rec.level}</Badge>
                </div>

                <p className="line-clamp-3 text-sm leading-6 text-muted">{rec.reason}</p>

                <div className="mt-auto flex items-center justify-between gap-2">
                  <div className="inline-flex items-center gap-2 text-xs text-muted">
                    <Clock3 className="h-3.5 w-3.5" /> {rec.estimatedMinutes} min
                  </div>
                  <Button
                    type="button"
                    size="sm"
                    variant="ghost"
                    onClick={(event) => {
                      event.preventDefault();
                      event.stopPropagation();
                      onDismiss?.(rec);
                    }}
                    className="px-2 text-muted hover:text-navy"
                  >
                    <XCircle className="h-4 w-4" />
                  </Button>
                </div>
              </div>
            </Card>
          </Link>
        ))}
      </div>
    </div>
  );
}

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
  const tone = result ? (result.isCorrect ? 'border-emerald-200 bg-emerald-50/70 dark:border-emerald-900/40 dark:bg-emerald-900/10' : 'border-rose-200 bg-rose-50/70 dark:border-rose-900/40 dark:bg-rose-900/10') : 'border-border bg-white dark:bg-gray-800';

  return (
    <Card className={cn('space-y-4 border p-5 shadow-sm', tone)}>
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <div className="flex items-center gap-2 text-xs uppercase tracking-[0.18em] text-muted">
            <Badge variant="muted">{exercise.type.replace(/_/g, ' ')}</Badge>
            <span>{exercise.points} pts</span>
          </div>
          <SafeRichText markdown={exercise.promptMarkdown} className="text-sm leading-6 text-navy dark:text-white" />
        </div>
        {result ? <Badge variant={result.isCorrect ? 'success' : 'danger'}>{result.isCorrect ? 'Correct' : 'Review'}</Badge> : null}
      </div>

      <ExerciseInput exercise={exercise} answer={answer} disabled={disabled || isResultMode} onAnswer={onAnswer} result={result} />

      {result ? (
        <div className="space-y-2 rounded-2xl border border-border bg-white/80 p-4 text-sm dark:border-gray-700 dark:bg-gray-900/40">
          <div className="flex flex-wrap gap-2 text-xs">
            <Badge variant={result.isCorrect ? 'success' : 'danger'}>{result.pointsAwarded}/{result.pointsPossible} points</Badge>
            {result.reviewItemCreated ? <Badge variant="warning">Added to review</Badge> : null}
          </div>
          <div className="grid gap-3 md:grid-cols-2">
            <ResultPanel title="Your answer" value={formatAnswer(answer, exercise.type)} />
            <ResultPanel title="Correct answer" value={formatAnswer(result.correctAnswer, exercise.type)} accent="success" />
          </div>
          {result.explanationMarkdown ? <SafeRichText markdown={result.explanationMarkdown} className="text-muted" /> : null}
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
              <div className="rounded-2xl border border-border bg-background-light px-4 py-3 text-sm text-navy dark:bg-gray-900/40">
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
    <div className={cn('rounded-2xl border px-4 py-3 text-sm', accent === 'success' ? 'border-emerald-200 bg-emerald-50/80 text-emerald-950 dark:border-emerald-900/40 dark:bg-emerald-900/10 dark:text-emerald-50' : 'border-border bg-background-light text-navy dark:bg-gray-900/40 dark:text-white')}>
      <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">{title}</p>
      <p className="mt-1 whitespace-pre-wrap leading-6">{value || '—'}</p>
    </div>
  );
}

function MetaBadge({ icon: Icon, label }: { icon: typeof Clock3; label: string }) {
  return (
    <span className="inline-flex items-center gap-1.5 rounded-full border border-border bg-background-light px-3 py-1 font-medium text-navy dark:bg-gray-900/40 dark:text-white">
      <Icon className="h-3.5 w-3.5 text-muted" />
      {label}
    </span>
  );
}

function StatPill({ icon: Icon, label, value }: { icon: typeof Clock3; label: string; value: number }) {
  return (
    <div className="rounded-2xl border border-border bg-background-light px-3 py-2 text-left dark:bg-gray-900/40">
      <div className="flex items-center gap-1.5 text-[11px] font-semibold uppercase tracking-[0.16em] text-muted">
        <Icon className="h-3.5 w-3.5" /> {label}
      </div>
      <div className="mt-1 text-sm font-bold text-navy dark:text-white">{value}</div>
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
