'use client';

import { useEffect, useMemo, useState } from 'react';
import { BookOpenCheck, ChevronRight, Clock, LockKeyhole, PlayCircle, RotateCcw, Video } from 'lucide-react';
import Link from 'next/link';
import Image from 'next/image';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { fetchVideoLessons } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VideoLessonListItem } from '@/lib/types/video-lessons';

type LessonView = 'all' | 'continue' | 'recommended';

const SUBTEST_LABELS: Record<string, string> = {
  writing: 'Writing',
  speaking: 'Speaking',
  reading: 'Reading',
  listening: 'Listening',
};

function formatDuration(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) return 'Self-paced';
  const minutes = Math.max(1, Math.round(seconds / 60));
  return `${minutes} min`;
}

function labelFor(value: string | null | undefined) {
  if (!value) return 'General';
  return value
    .replace(/[_-]/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
}

function hasProgress(lesson: VideoLessonListItem) {
  return (lesson.progress?.watchedSeconds ?? 0) > 0 && lesson.progress?.completed !== true;
}

function isRecommended(lesson: VideoLessonListItem, selectedSubtest: string) {
  if (lesson.progress?.completed) return false;
  if (selectedSubtest) return lesson.subtestCode === selectedSubtest;
  return lesson.subtestCode === 'writing' || lesson.subtestCode === 'speaking' || lesson.isPreviewEligible;
}

export default function LessonsPage() {
  const [lessons, setLessons] = useState<VideoLessonListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [subtest, setSubtest] = useState('');
  const [category, setCategory] = useState('');
  const [view, setView] = useState<LessonView>('all');

  useEffect(() => {
    analytics.track('lessons_page_viewed');
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      await Promise.resolve();
      if (cancelled) return;

      setLoading(true);
      setError(null);

      try {
        const data = await fetchVideoLessons({
          examTypeCode: 'oet',
          subtestCode: subtest || undefined,
          category: category || undefined,
        });
        if (!cancelled) setLessons(Array.isArray(data) ? data : []);
      } catch {
        if (!cancelled) {
          setLessons([]);
          setError('Could not load video lessons.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [subtest, category]);

  const categoryOptions = useMemo(() => {
    return Array.from(new Set(lessons.map((lesson) => lesson.category).filter(Boolean))).sort();
  }, [lessons]);

  const continueLessons = useMemo(() => lessons.filter(hasProgress), [lessons]);
  const recommendedLessons = useMemo(() => lessons.filter((lesson) => isRecommended(lesson, subtest)), [lessons, subtest]);

  const visibleLessons = useMemo(() => {
    if (view === 'continue') return continueLessons;
    if (view === 'recommended') return recommendedLessons;
    return lessons;
  }, [continueLessons, lessons, recommendedLessons, view]);

  const heroHighlights = [
    { icon: PlayCircle, label: 'Format', value: 'Video lessons' },
    { icon: Clock, label: 'Resume', value: `${continueLessons.length} in progress` },
    { icon: BookOpenCheck, label: 'Focus', value: subtest ? SUBTEST_LABELS[subtest] ?? labelFor(subtest) : 'All OET subtests' },
  ];

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Learn"
          title="Video Lessons"
          description="Watch expert-led OET preparation lessons with resume progress, previews, and clear next steps."
          icon={Video}
          highlights={heroHighlights}
        />

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <Card className="p-5 shadow-sm">
          <LearnerSurfaceSectionHeader
            eyebrow="OET video hub"
            title="Find the right lesson"
            description="Filter by subtest and lesson category."
            className="mb-4"
          />
          <div className="flex flex-col gap-3 md:flex-row md:items-center md:justify-between">
            <div className="flex flex-wrap gap-3">
              <select
                value={subtest}
                onChange={(event) => {
                  setSubtest(event.target.value);
                  setView('all');
                }}
                className="rounded-lg border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              >
                <option value="">All OET subtests</option>
                <option value="writing">Writing</option>
                <option value="speaking">Speaking</option>
                <option value="reading">Reading</option>
                <option value="listening">Listening</option>
              </select>
              <select
                value={category}
                onChange={(event) => {
                  setCategory(event.target.value);
                  setView('all');
                }}
                className="rounded-lg border border-border bg-surface px-4 py-3 text-sm text-navy shadow-sm focus:outline-none focus:ring-2 focus:ring-primary focus:ring-offset-2"
              >
                <option value="">All categories</option>
                {categoryOptions.map((option) => (
                  <option key={option} value={option}>{labelFor(option)}</option>
                ))}
              </select>
            </div>

            <div className="grid grid-cols-3 overflow-hidden rounded-lg border border-border bg-surface text-xs font-semibold text-muted">
              {([
                ['all', `All (${lessons.length})`],
                ['continue', `Continue (${continueLessons.length})`],
                ['recommended', `Recommended (${recommendedLessons.length})`],
              ] as const).map(([key, label]) => (
                <button
                  key={key}
                  type="button"
                  onClick={() => setView(key)}
                  className={`px-3 py-2 transition-colors ${view === key ? 'bg-primary text-white' : 'hover:bg-lavender/30'}`}
                >
                  {label}
                </button>
              ))}
            </div>
          </div>
        </Card>

        {loading ? (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => (
              <Skeleton key={index} className="h-72 rounded-2xl" />
            ))}
          </div>
        ) : visibleLessons.length === 0 ? (
          <Card className="border-dashed border-border p-8 text-center shadow-sm">
            <p className="text-sm font-semibold text-navy">No video lessons match this view.</p>
            <p className="mt-2 text-sm text-muted">Try all lessons or clear one of the filters.</p>
            <button
              type="button"
              onClick={() => {
                setSubtest('');
                setCategory('');
                setView('all');
              }}
              className="mt-4 inline-flex items-center gap-2 rounded-lg border border-border px-4 py-2 text-sm font-semibold text-navy hover:border-primary hover:text-primary"
            >
              <RotateCcw className="h-4 w-4" />
              Reset filters
            </button>
          </Card>
        ) : (
          <section>
            <LearnerSurfaceSectionHeader
              eyebrow={view === 'continue' ? 'Continue watching' : view === 'recommended' ? 'Recommended for your weak areas' : 'Lessons'}
              title={view === 'all' ? 'Curated video lessons' : view === 'continue' ? 'Pick up where you stopped' : 'Recommended lessons'}
              description="Completion here contributes to learning activity and study-plan evidence."
              className="mb-4"
            />
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
              {visibleLessons.map((lesson, index) => {
                const progress = lesson.progress?.percentComplete ?? 0;
                const locked = lesson.requiresUpgrade && !lesson.isAccessible;
                return (
                  <MotionItem key={lesson.id} delayIndex={index}>
                    <Link href={`/lessons/${lesson.id}`} className="group block h-full">
                      <article className="flex h-full flex-col overflow-hidden rounded-2xl border border-border bg-surface shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical active:scale-[0.99]">
                        <div className="relative flex h-40 items-center justify-center bg-navy">
                          {lesson.thumbnailUrl ? (
                            <Image
                              src={lesson.thumbnailUrl}
                              alt={lesson.title}
                              fill
                              unoptimized
                              sizes="(max-width: 1024px) 100vw, 33vw"
                              className="object-cover"
                            />
                          ) : (
                            <PlayCircle className="h-14 w-14 text-white/70 transition-colors group-hover:text-white" />
                          )}
                          <div className="absolute left-3 top-3 flex flex-wrap gap-2">
                            {locked ? (
                              <Badge variant="warning"><LockKeyhole className="mr-1 h-3 w-3" /> Locked</Badge>
                            ) : lesson.isPreviewEligible ? (
                              <Badge variant="info">Preview</Badge>
                            ) : lesson.progress?.completed ? (
                              <Badge variant="success">Completed</Badge>
                            ) : (
                              <Badge variant="default">Available</Badge>
                            )}
                          </div>
                          <div className="absolute bottom-3 right-3 flex items-center gap-1 rounded-full bg-navy/70 px-2 py-1 text-xs text-white">
                            <Clock className="h-3 w-3" />
                            {formatDuration(lesson.durationSeconds)}
                          </div>
                        </div>

                        <div className="flex flex-1 flex-col p-5">
                          <div className="mb-2 flex flex-wrap items-center gap-2">
                            <Badge variant="muted">{SUBTEST_LABELS[lesson.subtestCode ?? ''] ?? 'General'}</Badge>
                            <span className="text-xs font-semibold text-muted">{labelFor(lesson.difficultyLevel)}</span>
                          </div>
                          <h3 className="line-clamp-2 text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">
                            {lesson.title}
                          </h3>
                          {lesson.description && <p className="mt-2 line-clamp-2 text-xs leading-5 text-muted">{lesson.description}</p>}

                          <div className="mt-auto pt-4">
                            <div className="h-2 overflow-hidden rounded-full bg-background-light">
                              <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
                            </div>
                            <div className="mt-3 flex items-center justify-between text-xs font-semibold">
                              <span className="text-muted">{progress}% complete</span>
                              <span className="inline-flex items-center gap-1 text-primary">
                                {locked ? 'View options' : hasProgress(lesson) ? 'Resume' : 'Start'}
                                <ChevronRight className="h-3.5 w-3.5" />
                              </span>
                            </div>
                          </div>
                        </div>
                      </article>
                    </Link>
                  </MotionItem>
                );
              })}
            </div>
          </section>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
