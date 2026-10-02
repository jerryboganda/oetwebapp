'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { BookMarked, CheckCircle2, LayoutGrid, Sparkles, Trophy } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { GrammarLessonCard } from '@/components/domain/grammar';
import { fetchGrammarTopicDetail } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { GrammarLessonSummary } from '@/lib/grammar/types';

interface TopicMeta {
  id: string;
  slug: string;
  name: string;
  description: string | null;
  iconEmoji: string | null;
  levelHint: string;
}

interface TopicDetailResponse {
  topic: TopicMeta;
  lessons: GrammarLessonSummary[];
}

function titleCase(value: string) {
  return value.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());
}

// ─────────────────────────────────────────────────────────────────────────
export default function GrammarTopicPage() {
  const params   = useParams<{ slug: string }>();
  const slug     = params?.slug ?? '';

  const [data,    setData]    = useState<TopicDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error,   setError]   = useState<string | null>(null);

  useEffect(() => {
    if (!slug) return;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const d = (await fetchGrammarTopicDetail(slug)) as TopicDetailResponse;
        if (cancelled) return;
        setData(d);
        analytics.track('grammar_topic_viewed', { slug });
      } catch {
        if (!cancelled) setError('Could not load this topic.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [slug]);

  // ── loading skeleton ────────────────────────────────────────────────
  if (loading) {
    return (
      <>
        <Skeleton className="h-40 rounded-2xl" />
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {Array.from({ length: 4 }).map((_, i) => (
            <Skeleton key={i} className="h-52 rounded-2xl" />
          ))}
        </div>
      </>
    );
  }

  // ── error / not found ────────────────────────────────────────────────
  if (error || !data) {
    return (
      <EmptyState
        icon={<BookMarked className="h-7 w-7" aria-hidden="true" />}
        title={error ?? 'Topic not found.'}
        action={{ label: 'Back to grammar', href: '/grammar' }}
      />
    );
  }

  const { topic, lessons } = data;
  const completedCount = lessons.filter((l) => l.progress?.status === 'completed' || l.mastered).length;
  const masteredCount  = lessons.filter((l) => l.mastered).length;

  // Hero highlight chips — same pattern as dashboard + grammar overview page.
  const heroHighlights = [
    { icon: LayoutGrid,   label: 'Lessons',  value: `${lessons.length} available` },
    { icon: CheckCircle2, label: 'Completed', value: `${completedCount} done`      },
    { icon: Trophy,       label: 'Mastered',  value: `${masteredCount} mastered`   },
  ];

  // ── render ────────────────────────────────────────────────────────────
  // The shell breadcrumb (Dashboard › Grammar › …) is the way back.
  return (
    <>
      {/* ── Hero — same visual contract as every learner page ── */}
      <LearnerPageHero
        eyebrow={titleCase(topic.levelHint || 'Grammar topic')}
        icon={BookMarked}
        accent="primary"
        title={topic.name}
        description={topic.description ?? `Build mastery on ${topic.name} patterns through guided practice.`}
        highlights={heroHighlights}
      />

      {/* ── Lesson grid ── */}
      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Lessons"
          title={`${topic.name} lessons`}
          description={`${lessons.length} ${lessons.length === 1 ? 'lesson' : 'lessons'}. Every completed lesson improves your readiness score.`}
        />

        {lessons.length === 0 ? (
          <EmptyState
            icon={<Sparkles className="h-7 w-7 text-primary" aria-hidden="true" />}
            title="No published lessons yet"
            description="Content is being finalised for this topic. Check back soon, or explore the full grammar library."
            action={{ label: 'Browse other topics', href: '/grammar' }}
          />
        ) : (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {lessons.map((lesson, i) => (
              <MotionItem key={lesson.id} delayIndex={Math.min(i, 5)} className="h-full">
                <GrammarLessonCard lesson={lesson} />
              </MotionItem>
            ))}
          </div>
        )}
      </MotionSection>
    </>
  );
}
