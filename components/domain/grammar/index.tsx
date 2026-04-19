'use client';

import Link from 'next/link';
import { BookOpen, Sparkles, X } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import { GrammarContentRenderer, SafeRichText } from './grammar-content-renderer';
import {
  GrammarLessonEditor,
  draftToApi,
  emptyDraft,
  type ContentBlockDraft,
  type ExerciseDraft,
  type LessonDraft,
} from './grammar-lesson-editor';
import type {
  GrammarExerciseLearner,
  GrammarExerciseResult,
  GrammarLessonSummary,
  GrammarRecommendation,
  GrammarTopicLearner,
} from '@/lib/grammar/types';

export { GrammarContentRenderer, SafeRichText, GrammarLessonEditor, draftToApi, emptyDraft };
export type { ContentBlockDraft, ExerciseDraft, LessonDraft };

export function GrammarTopicCard({ topic }: { topic: GrammarTopicLearner }) {
  return (
    <Link href={`/grammar/topics/${encodeURIComponent(topic.slug)}`} className="block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2">
      <Card className="h-full p-5 transition-colors hover:border-primary/40 hover:bg-primary/5">
        <div className="flex items-start gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-primary/10 text-xl">
            {topic.iconEmoji ?? '📘'}
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-2">
              <h3 className="truncate text-base font-bold text-navy dark:text-white">{topic.name}</h3>
              <Badge variant="muted">{topic.levelHint}</Badge>
            </div>
            {topic.description ? <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted">{topic.description}</p> : null}
          </div>
        </div>
        <div className="mt-4 flex items-center justify-between text-xs text-muted">
          <span>{topic.lessonCount} lesson{topic.lessonCount === 1 ? '' : 's'}</span>
          <span>{topic.masteredCount} mastered</span>
        </div>
      </Card>
    </Link>
  );
}

export function GrammarLessonCard({ lesson }: { lesson: GrammarLessonSummary }) {
  return (
    <Link href={`/grammar/${encodeURIComponent(lesson.id)}`} className="block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2">
      <Card className="h-full p-5 transition-colors hover:border-primary/40 hover:bg-primary/5">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <BookOpen className="h-5 w-5" />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant="outline">{lesson.level}</Badge>
              <span className="text-xs text-muted">{lesson.estimatedMinutes} min</span>
            </div>
            <h3 className="mt-2 text-base font-bold text-navy dark:text-white">{lesson.title}</h3>
            <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted">{lesson.description}</p>
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
  onOpen: (recommendation: GrammarRecommendation) => void;
  onDismiss: (recommendation: GrammarRecommendation) => void;
}) {
  return (
    <Card className="border-primary/20 bg-primary/5 p-4">
      <div className="mb-3 flex items-center gap-2">
        <Sparkles className="h-4 w-4 text-primary" />
        <h2 className="text-sm font-semibold text-navy dark:text-white">Recommended next</h2>
      </div>
      <div className="grid gap-3 md:grid-cols-2">
        {recommendations.map((recommendation) => {
          const href = recommendation.lessonId
            ? `/grammar/${encodeURIComponent(recommendation.lessonId)}`
            : recommendation.topicSlug
              ? `/grammar/topics/${encodeURIComponent(recommendation.topicSlug)}`
              : '/grammar';
          return (
            <div key={recommendation.id} className="rounded-2xl border border-primary/15 bg-white p-4 shadow-sm dark:bg-gray-800">
              <div className="flex items-start gap-2">
                <div className="min-w-0 flex-1">
                  <Link href={href} onClick={() => onOpen(recommendation)} className="font-semibold text-navy hover:text-primary dark:text-white">
                    {recommendation.title}
                  </Link>
                  <p className="mt-1 text-sm leading-6 text-muted">{recommendation.reason}</p>
                </div>
                <button
                  type="button"
                  className="rounded-full p-1 text-muted hover:bg-background-light hover:text-navy"
                  onClick={() => onDismiss(recommendation)}
                  aria-label={`Dismiss ${recommendation.title}`}
                >
                  <X className="h-4 w-4" />
                </button>
              </div>
            </div>
          );
        })}
      </div>
    </Card>
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
  const answerString = typeof answer === 'string' ? answer : '';
  const options = Array.isArray(exercise.options) ? exercise.options : [];

  return (
    <Card className={cn('space-y-3 p-4', result ? (result.correct ? 'border-emerald-200' : 'border-amber-200') : '')}>
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant="outline">#{exercise.sortOrder}</Badge>
        <Badge variant="muted">{exercise.type}</Badge>
        {result ? <Badge variant={result.correct ? 'success' : 'warning'}>{result.correct ? 'Correct' : 'Review'}</Badge> : null}
      </div>
      <SafeRichText markdown={exercise.promptMarkdown} className="text-gray-800 dark:text-gray-200" />

      {exercise.type === 'mcq' && options.length > 0 ? (
        <div className="space-y-2">
          {options.map((option, index) => {
            const optionRecord = option as { id?: string; label?: string };
            const id = optionRecord.id ?? String(index);
            return (
              <label key={id} className="flex items-center gap-2 rounded-2xl border border-border p-3 text-sm">
                <input
                  type="radio"
                  name={exercise.id}
                  disabled={disabled}
                  checked={answerString === id}
                  onChange={() => onAnswer(id)}
                />
                <span>{optionRecord.label ?? id}</span>
              </label>
            );
          })}
        </div>
      ) : (
        <textarea
          className="min-h-24 w-full rounded-2xl border border-border bg-background-light p-3 text-sm text-navy focus:border-primary focus:outline-none focus:ring-4 focus:ring-primary/15"
          value={answerString}
          disabled={disabled}
          onChange={(event) => onAnswer(event.target.value)}
          placeholder="Type your answer"
        />
      )}

      {result?.explanationMarkdown ? (
        <div className="rounded-2xl bg-background-light p-3">
          <SafeRichText markdown={result.explanationMarkdown} />
        </div>
      ) : null}
    </Card>
  );
}
