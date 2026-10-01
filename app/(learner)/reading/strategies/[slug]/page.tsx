'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { BookOpen, CheckCircle2, TrendingUp } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { ErrorState } from '@/components/ui/empty-error';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  getStrategy,
  markStrategyRead,
  type ReadingStrategyWithProgressDto,
} from '@/lib/reading-pathway-api';

// Extended type: the API may return skillCode alongside bodyMarkdown
type StrategyWithSkill = ReadingStrategyWithProgressDto & {
  strategy: { skillCode?: string };
};

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function StrategyDetailPage() {
  const params = useParams<{ slug: string }>();
  const slug = params?.slug ?? '';

  const [data, setData] = useState<StrategyWithSkill | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [isRead, setIsRead] = useState(false);
  const [markingRead, setMarkingRead] = useState(false);

  useEffect(() => {
    if (!slug) return;
    let cancelled = false;
    (async () => {
      try {
        const result = await getStrategy(slug);
        if (!cancelled) {
          setData(result as StrategyWithSkill);
          setIsRead(result.readAt !== null);
        }
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load strategy.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => { cancelled = true; };
  }, [slug]);

  const handleMarkRead = async () => {
    if (isRead || markingRead) return;
    setMarkingRead(true);
    try {
      await markStrategyRead(slug);
      setIsRead(true);
    } catch {
      // Best-effort — silently ignore
    } finally {
      setMarkingRead(false);
    }
  };

  if (error) return <ErrorState message={error} />;

  if (loading || !data) {
    return (
      <>
        <Skeleton className="h-32 rounded-2xl" />
        <Skeleton className="h-96 w-full rounded-2xl" />
      </>
    );
  }

  // The breadcrumb's "Strategies" crumb is the way back, so no back link above the header.
  return (
    <>
      <LearnerPageHero
        eyebrow="Reading strategies"
        icon={BookOpen}
        accent="reading"
        title={data.strategy.title}
        description={`${data.strategy.estimatedReadMinutes} min read`}
        footer={(
          <div className="flex flex-wrap items-center gap-2">
            <span className="inline-flex items-center rounded-full border border-skill-reading/20 bg-skill-reading/10 px-2 py-0.5 text-xs font-bold text-skill-reading">
              {data.strategy.category}
            </span>
            <Badge variant={data.strategy.difficulty === 'Advanced' ? 'warning' : 'default'}>
              {data.strategy.difficulty}
            </Badge>
            {isRead && (
              <span className="flex items-center gap-1 text-xs font-semibold text-success-strong">
                <CheckCircle2 className="h-3 w-3" aria-hidden />
                Read
              </span>
            )}
          </div>
        )}
      />

      <MotionSection>
        <Card padding="lg">
          {/* Long-form text: cap the line length, not the page. */}
          <MarkdownContent markdown={data.strategy.bodyMarkdown} className="max-w-prose text-navy" />
        </Card>
      </MotionSection>

      <div className="flex flex-wrap items-center gap-3">
        {isRead ? (
          <Badge variant="success" size="md" className="min-h-11 gap-2 rounded-control px-4">
            <CheckCircle2 className="h-4 w-4" aria-hidden />
            Marked as read
          </Badge>
        ) : (
          <Button
            variant="outline"
            size="sm"
            disabled={markingRead}
            onClick={() => void handleMarkRead()}
          >
            {markingRead ? 'Saving…' : 'Mark as Read'}
          </Button>
        )}

        {data.strategy.skillCode ? (
          <Button asChild variant="primary" size="sm">
            <Link href={`/reading/practice?skill=${data.strategy.skillCode}`}>
              <TrendingUp className="h-4 w-4" aria-hidden />
              Practice this skill
            </Link>
          </Button>
        ) : null}
      </div>

      {data.strategy.relatedSlugs.length > 0 && (
        <MotionSection>
          <Card padding="sm">
            <h2 className="eyebrow text-muted">Related strategies</h2>
            <ul className="mt-1">
              {data.strategy.relatedSlugs.map((related) => (
                <li key={related}>
                  <Link
                    href={`/reading/strategies/${related}`}
                    className="inline-flex min-h-11 items-center rounded-control text-sm font-medium text-primary underline-offset-2 hover:underline focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  >
                    {related}
                  </Link>
                </li>
              ))}
            </ul>
          </Card>
        </MotionSection>
      )}
    </>
  );
}
