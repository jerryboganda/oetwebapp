'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, History, TrendingUp } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
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
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero
          eyebrow="Vocabulary"
          title="Quiz History"
          description="Past sessions, scores, and time spent."
          icon={History}
          highlights={[
            { icon: TrendingUp, label: 'Avg score (page)', value: `${avgScore}%` },
            { icon: History, label: 'Terms on page', value: `${totalTerms}` },
          ]}
        />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <LearnerSurfaceSectionHeader
        eyebrow="Sessions"
        title="Most recent first"
        description="Tap any session to see a breakdown in a future update."
        className="mb-4"
      />

      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-14 rounded-2xl" />)}
        </div>
      ) : items.length === 0 ? (
        <Card className="border-dashed border-border p-8 text-center shadow-sm">
          <History className="mx-auto mb-3 h-10 w-10 text-gray-300" />
          <p className="text-muted">No past quiz sessions yet.</p>
          <Link href="/vocabulary/quiz" className="mt-3 inline-flex items-center gap-1 text-sm text-primary hover:underline">
            Start your first quiz
          </Link>
        </Card>
      ) : (
        <div className="overflow-hidden rounded-3xl border border-border bg-surface shadow-sm">
          {items.map(item => (
            <div key={item.id} className="flex flex-wrap items-center gap-3 border-b border-border px-4 py-3 last:border-0">
              <div className="flex-1">
                <div className="text-sm font-medium text-navy capitalize">{item.format.replace(/_/g, ' ')}</div>
                <div className="text-xs text-muted">
                  {new Date(item.completedAt).toLocaleString()} · {item.durationSeconds}s
                </div>
              </div>
              <div className="text-right">
                <div className="text-sm font-bold text-navy">{item.correctCount}/{item.termsQuizzed}</div>
                <div className={`text-xs font-medium ${item.score >= 80 ? 'text-green-600' : item.score >= 60 ? 'text-amber-600' : 'text-red-600'}`}>
                  {Math.round(item.score)}%
                </div>
              </div>
            </div>
          ))}
        </div>
      )}

      {totalPages > 1 && (
        <div className="mt-4 flex items-center justify-center gap-2">
          <button
            onClick={() => setPage(p => Math.max(1, p - 1))}
            disabled={page === 1}
            className="rounded-lg border border-gray-200 bg-surface px-3 py-1.5 text-sm text-navy disabled:opacity-40 hover:bg-background-light"
          >
            Prev
          </button>
          <span className="text-sm text-muted">{page} / {totalPages}</span>
          <button
            onClick={() => setPage(p => Math.min(totalPages, p + 1))}
            disabled={page === totalPages}
            className="rounded-lg border border-gray-200 bg-surface px-3 py-1.5 text-sm text-navy disabled:opacity-40 hover:bg-background-light"
          >
            Next
          </button>
        </div>
      )}
    </LearnerDashboardShell>
  );
}
