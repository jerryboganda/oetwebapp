'use client';

import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, BookMarked, Sparkles } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { GrammarLessonCard } from '@/components/domain/grammar';
import { fetchGrammarTopicDetail } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type { GrammarLessonSummary } from '@/lib/grammar/types';

interface TopicDetailResponse {
  topic: { id: string; slug: string; name: string; description: string | null; iconEmoji: string | null; levelHint: string };
  lessons: GrammarLessonSummary[];
}

export default function GrammarTopicPage() {
  const params = useParams<{ slug: string }>();
  const slug = params?.slug ?? '';
  const [data, setData] = useState<TopicDetailResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

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
    return () => {
      cancelled = true;
    };
  }, [slug]);

  if (loading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="mb-4 h-8 w-48 rounded" />
        <Skeleton className="h-40 rounded-2xl" />
      </LearnerDashboardShell>
    );
  }

  if (error || !data) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="warning">{error ?? 'Topic not found.'}</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/grammar" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300" aria-label="Back to grammar">
          <ArrowLeft className="h-5 w-5" />
        </Link>
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary/10 text-lg">
            {data.topic.iconEmoji ?? '📘'}
          </div>
          <div>
            <p className="text-xs uppercase tracking-[0.15em] text-muted">{data.topic.levelHint}</p>
            <h1 className="text-2xl font-bold text-navy dark:text-white">{data.topic.name}</h1>
          </div>
        </div>
      </div>

      {data.topic.description ? (
        <p className="mb-6 max-w-3xl text-sm leading-6 text-muted">{data.topic.description}</p>
      ) : null}

      {data.lessons.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <div className="mx-auto flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Sparkles className="h-5 w-5" />
          </div>
          <h3 className="mt-4 text-lg font-bold text-navy">No published lessons yet</h3>
          <p className="mt-2 text-sm leading-6 text-muted">Content is being finalised for this topic.</p>
          <Link href="/grammar" className="mt-6 inline-block">
            <Button variant="outline" size="sm">Browse other topics</Button>
          </Link>
        </Card>
      ) : (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
          {data.lessons.map((lesson) => (
            <GrammarLessonCard key={lesson.id} lesson={lesson} />
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
