'use client';

import Link from 'next/link';
import { BookMarked, ChevronRight, Clock, Sparkles, Trophy } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { GrammarLessonSummary, GrammarTopicLearner, GrammarRecommendation } from '@/lib/grammar/types';

export function GrammarTopicCard({ topic }: { topic: GrammarTopicLearner }) {
  const pct = topic.lessonCount === 0 ? 0 : Math.round((topic.masteredCount / topic.lessonCount) * 100);
  const started = topic.completedCount > 0 || topic.masteredCount > 0;

  return (
    <Link
      href={`/grammar/topics/${encodeURIComponent(topic.slug)}`}
      className="group block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      aria-label={`Open topic ${topic.name}`}
    >
      <Card className="h-full overflow-hidden border-border bg-white p-5 transition hover:border-primary/30 hover:shadow-md dark:border-gray-700 dark:bg-gray-800">
        <div className="flex items-start justify-between gap-3">
          <div className="flex items-center gap-3">
            <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-primary/10 text-xl">
              {topic.iconEmoji ?? '📘'}
            </div>
            <div>
              <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-muted">{topic.examTypeCode.toUpperCase()}</p>
              <h3 className="text-base font-bold text-navy group-hover:text-primary dark:text-white">{topic.name}</h3>
            </div>
          </div>
          <ChevronRight className="h-5 w-5 text-muted transition-transform group-hover:translate-x-0.5" aria-hidden />
        </div>

        {topic.description ? (
          <p className="mt-3 line-clamp-2 text-sm leading-6 text-muted">{topic.description}</p>
        ) : null}

        <div className="mt-4 flex items-center justify-between text-[12px] text-muted">
          <span className="inline-flex items-center gap-1">
            <BookMarked className="h-3.5 w-3.5" /> {topic.lessonCount} lessons
          </span>
          {started ? (
            <span className="inline-flex items-center gap-1 font-medium text-primary-dark">
              <Trophy className="h-3.5 w-3.5" /> {topic.masteredCount}/{topic.lessonCount} mastered
            </span>
          ) : (
            <span className="inline-flex items-center gap-1">
              <Sparkles className="h-3.5 w-3.5" /> Not started
            </span>
          )}
        </div>

        <div className="mt-3 h-2 w-full overflow-hidden rounded-full bg-background-light dark:bg-gray-900">
          <div
            className="h-full rounded-full bg-gradient-to-r from-primary to-primary-dark transition-all"
            style={{ width: `${Math.min(100, Math.max(0, pct))}%` }}
            aria-hidden
          />
        </div>
        <p className="mt-1 text-right text-[11px] font-semibold text-muted">{pct}%</p>
      </Card>
    </Link>
  );
}

export function GrammarLessonCard({ lesson }: { lesson: GrammarLessonSummary }) {
  const variant = lesson.progressStatus === 'completed'
    ? (lesson.masteryScore >= 80 ? 'mastered' : 'completed')
    : lesson.progressStatus === 'in_progress' ? 'in_progress' : 'new';

  return (
    <Link
      href={`/grammar/${encodeURIComponent(lesson.id)}`}
      className="group block focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
      aria-label={`Open lesson ${lesson.title}`}
    >
      <Card className="h-full border-border bg-white p-5 transition hover:border-primary/30 hover:shadow-md dark:border-gray-700 dark:bg-gray-800">
        <div className="flex items-start justify-between gap-3">
          <div className="min-w-0 flex-1">
            <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-muted">
              {lesson.level}
              {lesson.topicSlug ? ` · ${lesson.topicSlug}` : ''}
            </p>
            <h3 className="mt-1 text-base font-bold text-navy group-hover:text-primary dark:text-white">{lesson.title}</h3>
            {lesson.description ? (
              <p className="mt-2 line-clamp-2 text-sm leading-6 text-muted">{lesson.description}</p>
            ) : null}
          </div>
          <StatusPill variant={variant} />
        </div>

        <div className="mt-4 flex items-center justify-between text-[12px] text-muted">
          <span className="inline-flex items-center gap-1">
            <Clock className="h-3.5 w-3.5" /> {lesson.estimatedMinutes} min
          </span>
          <span>{lesson.exerciseCount} exercises</span>
        </div>

        <div className="mt-3 h-1.5 w-full overflow-hidden rounded-full bg-background-light dark:bg-gray-900">
          <div
            className={cn(
              'h-full rounded-full transition-all',
              lesson.masteryScore >= 80 ? 'bg-emerald-500' : lesson.masteryScore >= 40 ? 'bg-amber-500' : 'bg-primary',
            )}
            style={{ width: `${Math.min(100, Math.max(0, lesson.masteryScore))}%` }}
            aria-hidden
          />
        </div>
      </Card>
    </Link>
  );
}

function StatusPill({ variant }: { variant: 'new' | 'in_progress' | 'completed' | 'mastered' }) {
  switch (variant) {
    case 'mastered':
      return <Badge className="bg-emerald-100 text-emerald-800">Mastered</Badge>;
    case 'completed':
      return <Badge className="bg-sky-100 text-sky-800">Completed</Badge>;
    case 'in_progress':
      return <Badge className="bg-amber-100 text-amber-800">In progress</Badge>;
    default:
      return <Badge className="bg-slate-100 text-slate-700">New</Badge>;
  }
}

export function GrammarRecommendationStrip({
  recommendations,
  onOpen,
  onDismiss,
}: {
  recommendations: GrammarRecommendation[];
  onOpen?: (r: GrammarRecommendation) => void;
  onDismiss?: (r: GrammarRecommendation) => void;
}) {
  if (!recommendations.length) return null;
  return (
    <section aria-label="Recommended grammar lessons" className="space-y-3">
      <header className="flex items-center justify-between">
        <div>
          <p className="text-[11px] font-semibold uppercase tracking-[0.18em] text-muted">Recommended for you</p>
          <h2 className="text-lg font-bold text-navy dark:text-white">Grammar gaps spotted in your work</h2>
        </div>
      </header>
      <div className="grid gap-3 md:grid-cols-2">
        {recommendations.slice(0, 4).map((r) => (
          <Card key={r.id} className="flex items-start justify-between gap-3 border-primary/20 bg-primary/5 p-4">
            <div className="min-w-0 flex-1">
              <p className="text-[10px] font-semibold uppercase tracking-[0.18em] text-primary-dark">
                From your {r.source === 'writing' ? 'writing feedback' : r.source === 'speaking' ? 'speaking review' : 'diagnostic'}
                {r.ruleId ? ` · ${r.ruleId}` : ''}
              </p>
              <h3 className="mt-1 line-clamp-1 text-sm font-bold text-navy dark:text-white">{r.lessonTitle}</h3>
            </div>
            <div className="flex flex-col gap-1">
              <Link
                href={`/grammar/${encodeURIComponent(r.lessonId)}`}
                onClick={() => onOpen?.(r)}
                className="rounded-full bg-primary px-3 py-1 text-xs font-semibold text-white hover:bg-primary-dark"
              >
                Open
              </Link>
              {onDismiss ? (
                <button
                  type="button"
                  onClick={() => onDismiss(r)}
                  className="rounded-full border border-border px-3 py-1 text-xs font-medium text-muted hover:text-navy"
                >
                  Dismiss
                </button>
              ) : null}
            </div>
          </Card>
        ))}
      </div>
    </section>
  );
}
