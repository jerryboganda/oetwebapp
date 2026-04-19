'use client';

import { useDeferredValue, useEffect, useMemo, useState } from 'react';
import { Clock, Filter, LockKeyhole, PlayCircle, Search as SearchIcon, Sparkles } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { fetchVideoLessons } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { VideoLessonListItem } from '@/lib/types/video-lessons';

const SUBTEST_LABELS: Record<string, string> = {
  writing: 'Writing',
  speaking: 'Speaking',
  reading: 'Reading',
  listening: 'Listening',
};

function labelFor(value: string | null | undefined) {
  if (!value) return 'General';
  return value.replace(/[_-]/g, ' ').replace(/\b\w/g, (char) => char.toUpperCase());
}

function formatDuration(seconds: number) {
  if (!Number.isFinite(seconds) || seconds <= 0) return 'Self-paced';
  return `${Math.max(1, Math.round(seconds / 60))} min`;
}

export default function DiscoverPage() {
  const [query, setQuery] = useState('');
  const [subtestFilter, setSubtestFilter] = useState('');
  const [difficultyFilter, setDifficultyFilter] = useState('');
  const [lessons, setLessons] = useState<VideoLessonListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const deferredQuery = useDeferredValue(query);

  useEffect(() => {
    analytics.track('discover_page_viewed', { scope: 'video_lessons' });
  }, []);

  useEffect(() => {
    let cancelled = false;

    void (async () => {
      await Promise.resolve();
      if (cancelled) return;

      setLoading(true);
      setError(null);

      try {
        const data = await fetchVideoLessons({ examTypeCode: 'oet', subtestCode: subtestFilter || undefined });
        if (!cancelled) setLessons(Array.isArray(data) ? data : []);
      } catch {
        if (!cancelled) {
          setLessons([]);
          setError('Search failed.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [subtestFilter]);

  const difficulties = useMemo(() => {
    return Array.from(new Set(lessons.map((lesson) => lesson.difficultyLevel).filter(Boolean))).sort();
  }, [lessons]);

  const filteredLessons = useMemo(() => {
    const normalizedQuery = deferredQuery.trim().toLowerCase();
    return lessons.filter((lesson) => {
      const matchesQuery = !normalizedQuery
        || lesson.title.toLowerCase().includes(normalizedQuery)
        || (lesson.description ?? '').toLowerCase().includes(normalizedQuery)
        || lesson.category.toLowerCase().includes(normalizedQuery);
      const matchesDifficulty = !difficultyFilter || lesson.difficultyLevel === difficultyFilter;
      return matchesQuery && matchesDifficulty;
    });
  }, [deferredQuery, difficultyFilter, lessons]);

  const recommended = useMemo(() => {
    return lessons
      .filter((lesson) => !lesson.progress?.completed)
      .sort((a, b) => {
        const aScore = (a.isPreviewEligible ? -2 : 0) + ((a.progress?.watchedSeconds ?? 0) > 0 ? -1 : 0);
        const bScore = (b.isPreviewEligible ? -2 : 0) + ((b.progress?.watchedSeconds ?? 0) > 0 ? -1 : 0);
        return aScore - bScore;
      })
      .slice(0, 6);
  }, [lessons]);

  const showRecommendations = !deferredQuery && !subtestFilter && !difficultyFilter && recommended.length > 0;
  const displayedLessons = useMemo(() => {
    if (!showRecommendations) return filteredLessons;
    const recommendedIds = new Set(recommended.map((lesson) => lesson.id));
    return filteredLessons.filter((lesson) => !recommendedIds.has(lesson.id));
  }, [filteredLessons, recommended, showRecommendations]);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          title="Discover Video Lessons"
          description="Search expert-led OET video lessons without sending non-video content into the video player."
        />

        <div className="relative">
          <SearchIcon className="absolute left-3 top-1/2 h-5 w-5 -translate-y-1/2 text-muted" />
          <input
            type="text"
            placeholder="Search video lessons, topics, or categories..."
            value={query}
            onChange={(event) => setQuery(event.target.value)}
            className="w-full rounded-xl border bg-background py-3 pl-10 pr-4 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
        </div>

        <div className="flex flex-wrap gap-2">
          <div className="mr-2 flex items-center gap-1 text-xs text-muted">
            <Filter className="h-3.5 w-3.5" />
            Subtest:
          </div>
          {[
            ['', 'All'],
            ['writing', 'Writing'],
            ['speaking', 'Speaking'],
            ['reading', 'Reading'],
            ['listening', 'Listening'],
          ].map(([value, label]) => (
            <button
              key={value || 'all'}
              type="button"
              onClick={() => setSubtestFilter(value)}
              className={`rounded-full px-3 py-2 text-xs font-medium transition-colors ${subtestFilter === value ? 'bg-primary text-white' : 'bg-muted text-muted-foreground'}`}
            >
              {label}
            </button>
          ))}

          <div className="ml-0 mr-2 flex items-center gap-1 text-xs text-muted md:ml-4">
            Difficulty:
          </div>
          <button
            type="button"
            onClick={() => setDifficultyFilter('')}
            className={`rounded-full px-3 py-2 text-xs font-medium transition-colors ${!difficultyFilter ? 'bg-primary text-white' : 'bg-muted text-muted-foreground'}`}
          >
            All
          </button>
          {difficulties.map((difficulty) => (
            <button
              key={difficulty}
              type="button"
              onClick={() => setDifficultyFilter(difficulty)}
              className={`rounded-full px-3 py-2 text-xs font-medium transition-colors ${difficultyFilter === difficulty ? 'bg-primary text-white' : 'bg-muted text-muted-foreground'}`}
            >
              {labelFor(difficulty)}
            </button>
          ))}
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {showRecommendations && (
          <section>
            <div className="mb-3 flex items-center gap-2">
              <Sparkles className="h-4 w-4 text-primary" />
              <h2 className="text-sm font-semibold">Recommended Video Lessons</h2>
            </div>
            <MotionSection>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {recommended.map((lesson) => (
                  <LessonCard key={lesson.id} lesson={lesson} />
                ))}
              </div>
            </MotionSection>
          </section>
        )}

        {loading ? (
          <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => (
              <Skeleton key={index} className="h-36 rounded-lg" />
            ))}
          </div>
        ) : filteredLessons.length === 0 ? (
          <div className="py-12 text-center text-muted">
            <SearchIcon className="mx-auto mb-3 h-12 w-12 opacity-40" />
            <p className="text-lg font-medium">No video lessons found</p>
            <p className="mt-1 text-sm">Try different filters or search terms.</p>
          </div>
        ) : displayedLessons.length > 0 ? (
          <section>
            <div className="mb-3 flex items-center justify-between">
              <span className="text-xs text-muted">{displayedLessons.length} video lesson{displayedLessons.length !== 1 ? 's' : ''}</span>
            </div>
            <MotionSection>
              <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
                {displayedLessons.map((lesson) => (
                  <LessonCard key={lesson.id} lesson={lesson} />
                ))}
              </div>
            </MotionSection>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}

function LessonCard({ lesson }: { lesson: VideoLessonListItem }) {
  const locked = lesson.requiresUpgrade && !lesson.isAccessible;
  return (
    <MotionItem>
      <Link href={`/lessons/${lesson.id}`} className="group block rounded-lg border bg-card p-4 transition-shadow hover:shadow-sm">
        <div className="mb-2 flex flex-wrap items-center gap-2">
          <Badge variant="outline" className="text-[10px]">{SUBTEST_LABELS[lesson.subtestCode ?? ''] ?? 'General'}</Badge>
          <Badge variant="muted" className="text-[10px]">{labelFor(lesson.difficultyLevel)}</Badge>
          {locked && <Badge variant="warning" className="text-[10px]"><LockKeyhole className="mr-1 h-3 w-3" /> Locked</Badge>}
          {!locked && lesson.isPreviewEligible && <Badge variant="info" className="text-[10px]">Preview</Badge>}
        </div>
        <h3 className="mb-1 text-sm font-medium leading-tight transition-colors group-hover:text-primary">{lesson.title}</h3>
        {lesson.description && <p className="mb-3 line-clamp-2 text-xs text-muted">{lesson.description}</p>}
        <div className="flex items-center gap-2 text-xs text-muted">
          <Clock className="h-3.5 w-3.5" />
          <span>{formatDuration(lesson.durationSeconds)}</span>
          <span>/</span>
          <span>{lesson.progress?.percentComplete ?? 0}% complete</span>
          <PlayCircle className="ml-auto h-4 w-4 text-primary" />
        </div>
      </Link>
    </MotionItem>
  );
}
