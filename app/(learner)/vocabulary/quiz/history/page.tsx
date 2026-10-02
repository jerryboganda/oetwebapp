'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, History, TrendingUp } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { fetchVocabularyQuizHistory } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type HistoryItem = {
  id: string;
  format: string;
  termsQuizzed: number;
  correctCount: number;
  score: number;
  durationSeconds: number;
  completedAt: string;
};

type HistoryResponse = {
  total: number;
  page: number;
  pageSize: number;
  items: HistoryItem[];
};

export default function VocabularyQuizHistoryPage() {
  const [data, setData] = useState<HistoryResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [page, setPage] = useState(1);
  const pageSize = 20;

  useEffect(() => {
    analytics.track('vocab_quiz_history_viewed');
    void load(page);
  }, [page]);

  async function load(p: number) {
    setLoading(true);
    try {
      const res = await fetchVocabularyQuizHistory({ page: p, pageSize });
      setData(res as HistoryResponse);
    } catch {
      setError('Could not load quiz history.');
    } finally {
      setLoading(false);
    }
  }

  const items = data?.items ?? [];
  const totalPages = data ? Math.max(1, Math.ceil(data.total / data.pageSize)) : 1;

  const avgScore = items.length > 0
    ? Math.round(items.reduce((acc, x) => acc + x.score, 0) / items.length)
    : 0;
  const totalTerms = items.reduce((acc, x) => acc + x.termsQuizzed, 0);

  return (
    <>
      <LearnerPageHero
        eyebrow="Vocabulary"
        title="Quiz History"
        description="Past sessions, scores, and time spent."
        icon={History}
        highlights={[
          { icon: TrendingUp, label: 'Avg score (page)', value: `${avgScore}%` },
          { icon: History, label: 'Terms on page', value: `${totalTerms}` },
        ]}
        aside={(
          <Button variant="ghost" size="sm" asChild>
            <Link href="/vocabulary">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to vocabulary
            </Link>
          </Button>
        )}
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}

      <MotionSection className="space-y-4">
        <LearnerSurfaceSectionHeader eyebrow="Sessions" title="Most recent first" />

        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-14 rounded-xl" />)}
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            icon={<History className="h-7 w-7" aria-hidden="true" />}
            title="No past quiz sessions yet."
            action={{ label: 'Start your first quiz', href: '/vocabulary/quiz' }}
          />
        ) : (
          <Card padding="none" className="overflow-hidden">
            {items.map((item, i) => (
              <MotionItem
                key={item.id}
                delayIndex={Math.min(i, 5)}
                className="flex flex-wrap items-center gap-3 border-b border-border px-4 py-3 last:border-0"
              >
                <div className="min-w-0 flex-1">
                  <div className="text-sm font-medium text-navy capitalize">{item.format.replace(/_/g, ' ')}</div>
                  <div className="text-xs tabular-nums text-muted">
                    {new Date(item.completedAt).toLocaleString()} · {item.durationSeconds}s
                  </div>
                </div>
                <div className="text-end">
                  <div className="text-sm font-bold tabular-nums text-navy">{item.correctCount}/{item.termsQuizzed}</div>
                  <div className={`text-xs font-medium tabular-nums ${item.score >= 80 ? 'text-success-strong' : item.score >= 60 ? 'text-warning-strong' : 'text-danger-strong'}`}>
                    {Math.round(item.score)}%
                  </div>
                </div>
              </MotionItem>
            ))}
          </Card>
        )}

        {totalPages > 1 && (
          <div className="flex items-center justify-center gap-2">
            <Button variant="outline" size="sm" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}>
              Prev
            </Button>
            <span className="text-sm tabular-nums text-muted">{page} / {totalPages}</span>
            <Button variant="outline" size="sm" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}>
              Next
            </Button>
          </div>
        )}
      </MotionSection>
    </>
  );
}
