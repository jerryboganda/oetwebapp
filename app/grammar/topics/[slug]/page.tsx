'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookMarked, CheckCircle2, LayoutGrid, Sparkles, Trophy } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
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
      <LearnerDashboardShell>
        <div className="space-y-6">
          <Skeleton className="h-40 rounded-2xl" />
          <div className="grid gap-4 md:grid-cols-2">
            {Array.from({ length: 4 }).map((_, i) => (
              <Skeleton key={i} className="h-52 rounded-2xl" />
            ))}
          </div>
        </div>
      </LearnerDashboardShell>
    );
  }

  // ── error / not found ────────────────────────────────────────────────
  if (error || !data) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4">
          <BackLink />
          <InlineAlert variant="warning">{error ?? 'Topic not found.'}</InlineAlert>
        </div>
      </LearnerDashboardShell>
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
  return (
    <LearnerDashboardShell>
      <div className="space-y-8">

        {/* Back nav — sits above hero as a lightweight ghost link, matching dashboard back-nav convention. */}
        <BackLink />

        {/* ── Hero — same visual contract as every learner page ── */}
        <LearnerPageHero
          eyebrow={titleCase(topic.levelHint || 'Grammar topic')}
          icon={BookMarked}
          accent="primary"
          title={topic.name}
          description={topic.description ?? `Build mastery on ${topic.name} patterns. Every lesson is graded server-side.`}
          highlights={heroHighlights}
        />

        {/* ── Lesson grid ── */}
        <MotionSection className="space-y-5">
          <LearnerSurfaceSectionHeader
            eyebrow="Lessons"
            title={`${topic.name} lessons`}
            description={`${lessons.length} ${lessons.length === 1 ? 'lesson' : 'lessons'} — graded server-side, drives your readiness score.`}
          />

          {lessons.length === 0 ? (
            <Card className="border-dashed border-border p-10 text-center shadow-sm">
              <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                <Sparkles className="h-5 w-5" />
              </div>
              <h3 className="mt-4 text-lg font-bold text-navy">No published lessons yet</h3>
              <p className="mt-2 text-sm leading-6 text-muted">
                Content is being finalised for this topic. Check back soon, or explore the full grammar library.
              </p>
              <div className="mt-6 flex justify-center">
                <Link href="/grammar">
                  <Button variant="outline" size="sm">Browse other topics</Button>
                </Link>
              </div>
            </Card>
          ) : (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {lessons.map((lesson, i) => (
                <MotionItem key={lesson.id} delayIndex={i}>
                  <GrammarLessonCard lesson={lesson} />
                </MotionItem>
              ))}
            </div>
          )}
        </MotionSection>

      </div>
    </LearnerDashboardShell>
  );
}

/** Lightweight back-to-grammar breadcrumb. */
function BackLink() {
  return (
    <Link
      href="/grammar"
      className="inline-flex items-center gap-2 text-sm font-semibold text-muted transition-colors hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
    >
      <ArrowLeft className="h-4 w-4" />
      Back to grammar
    </Link>
  );
}
