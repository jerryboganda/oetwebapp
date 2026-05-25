'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, CheckCircle2, Clock, TrendingUp } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import {
  getStrategy,
  markStrategyRead,
  type ReadingStrategyWithProgressDto,
} from '@/lib/reading-pathway-api';

// Extended type: the API may return skillCode alongside bodyMarkdown
type StrategyWithSkill = ReadingStrategyWithProgressDto & {
  strategy: { skillCode?: string };
};

// ─── Simple markdown renderer (react-markdown not installed) ─────────────────
// Converts the most common markdown patterns to HTML without a dependency.

function renderMarkdown(md: string): string {
  return md
    // Headings
    .replace(/^### (.+)$/gm, '<h3 class="text-base font-bold mt-4 mb-1">$1</h3>')
    .replace(/^## (.+)$/gm, '<h2 class="text-lg font-bold mt-5 mb-2">$1</h2>')
    .replace(/^# (.+)$/gm, '<h1 class="text-xl font-bold mt-6 mb-2">$1</h1>')
    // Bold / italic
    .replace(/\*\*(.+?)\*\*/g, '<strong>$1</strong>')
    .replace(/\*(.+?)\*/g, '<em>$1</em>')
    // Inline code
    .replace(/`(.+?)`/g, '<code class="rounded bg-slate-100 dark:bg-slate-800 px-1 py-0.5 text-xs font-mono">$1</code>')
    // Unordered lists
    .replace(/^\s*[-*] (.+)$/gm, '<li class="ml-4 list-disc">$1</li>')
    // Ordered lists
    .replace(/^\s*\d+\. (.+)$/gm, '<li class="ml-4 list-decimal">$1</li>')
    // Wrap adjacent li's in ul/ol (simple heuristic)
    .replace(/(<li[^>]*>[\s\S]+?<\/li>)/g, '<ul class="my-2 space-y-1">$1</ul>')
    // Blockquotes
    .replace(/^> (.+)$/gm, '<blockquote class="border-l-4 border-primary/30 pl-4 italic text-muted-foreground">$1</blockquote>')
    // Paragraphs — blank-line separated blocks that aren't already HTML
    .replace(/\n\n(?!<)/g, '</p><p class="mt-2">')
    // Wrap top level
    .replace(/^(?!<)/, '<p class="mt-2">');
}

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

  return (
    <LearnerDashboardShell pageTitle={data?.strategy.title ?? 'Strategy'}>
      <div className="mx-auto max-w-2xl space-y-8">
        {/* Back link */}
        <Link
          href="/reading/strategies"
          className="inline-flex items-center gap-1 text-sm font-medium text-muted-foreground hover:text-foreground"
        >
          <ArrowLeft className="h-4 w-4" aria-hidden />
          Back to Strategy Library
        </Link>

        {error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : loading || !data ? (
          <div className="space-y-4">
            <Skeleton className="h-8 w-3/4" />
            <Skeleton className="h-4 w-1/2" />
            <Skeleton className="h-96 w-full" />
          </div>
        ) : (
          <>
            {/* Header */}
            <div className="space-y-3">
              <h1 className="text-2xl font-bold text-foreground">{data.strategy.title}</h1>

              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="info">{data.strategy.category}</Badge>
                <Badge variant={data.strategy.difficulty === 'Advanced' ? 'warning' : 'default'}>
                  {data.strategy.difficulty}
                </Badge>
                <span className="flex items-center gap-1 text-xs text-muted-foreground">
                  <Clock className="h-3 w-3" aria-hidden />
                  {data.strategy.estimatedReadMinutes} min read
                </span>
                {isRead && (
                  <span className="flex items-center gap-1 text-xs font-semibold text-emerald-600 dark:text-emerald-400">
                    <CheckCircle2 className="h-3 w-3" aria-hidden />
                    Read
                  </span>
                )}
              </div>
            </div>

            {/* Body */}
            <div
              className="prose prose-sm max-w-none rounded-2xl border border-border bg-surface p-6 text-foreground dark:prose-invert"
              // eslint-disable-next-line react/no-danger
              dangerouslySetInnerHTML={{ __html: renderMarkdown(data.strategy.bodyMarkdown) }}
            />

            {/* Actions */}
            <div className="flex flex-wrap items-center gap-3">
              {isRead ? (
                <span className="flex items-center gap-2 rounded-lg border border-emerald-200 bg-emerald-50 px-4 py-2 text-sm font-semibold text-emerald-700 dark:border-emerald-800 dark:bg-emerald-900/20 dark:text-emerald-400">
                  <CheckCircle2 className="h-4 w-4" aria-hidden />
                  Marked as read
                </span>
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

            {/* Related strategies */}
            {data.strategy.relatedSlugs.length > 0 && (
              <div className="space-y-2 rounded-xl border border-border bg-surface p-4">
                <h2 className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Related strategies</h2>
                <ul className="space-y-1">
                  {data.strategy.relatedSlugs.map((related) => (
                    <li key={related}>
                      <Link
                        href={`/reading/strategies/${related}`}
                        className="text-sm font-medium text-primary underline-offset-2 hover:underline"
                      >
                        {related}
                      </Link>
                    </li>
                  ))}
                </ul>
              </div>
            )}
          </>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
