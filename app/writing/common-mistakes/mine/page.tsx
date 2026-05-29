'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { TrendingUp, AlertCircle } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { MistakeCard } from '@/components/domain/writing/MistakeCard';
import { listMyCommonMistakes } from '@/lib/writing/api';
import type {
  WritingCommonMistakeDto,
  WritingLearnerMistakeStatDto,
} from '@/lib/writing/types';

interface MyMistakeRow extends WritingCommonMistakeDto {
  stat: WritingLearnerMistakeStatDto;
}

export default function WritingMyMistakesPage() {
  const t = useTranslations();
  const [rows, setRows] = useState<MyMistakeRow[]>([]);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    void listMyCommonMistakes()
      .then((r) => {
        if (cancelled) return;
        setRows(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.mistakes.mine.error.load'));
      });
    return () => {
      cancelled = true;
    };
  }, [t]);

  const top5 = useMemo(() => rows.slice().sort((a, b) => b.stat.occurrenceCount - a.stat.occurrenceCount).slice(0, 5), [rows]);
  const totalCount = useMemo(() => rows.reduce((sum, r) => sum + r.stat.occurrenceCount, 0), [rows]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.mistakes.mine.pageTitle')}>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={t('writing.mistakes.mine.eyebrow')}
          icon={TrendingUp}
          accent="amber"
          title={t('writing.mistakes.mine.title')}
          description={t('writing.mistakes.mine.description')}
          highlights={[
            { icon: AlertCircle, label: t('writing.mistakes.mine.highlights.tracked'), value: `${rows.length}` },
            { icon: TrendingUp, label: t('writing.mistakes.mine.highlights.total'), value: `${totalCount}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {top5.length > 0 ? (
          <section aria-labelledby="top5-heading" className="rounded-2xl border border-amber-200/60 bg-amber-50/40 p-5 shadow-sm">
            <h2 id="top5-heading" className="text-base font-bold text-amber-900">{t('writing.mistakes.mine.top5.heading')}</h2>
            <p className="mt-1 text-xs text-amber-800">{t('writing.mistakes.mine.top5.subtitle')}</p>
            <ol className="mt-3 space-y-2">
              {top5.map((row, idx) => (
                <li key={row.id} className="flex items-center justify-between gap-3 rounded-lg border border-amber-200/60 bg-surface p-3">
                  <div className="flex items-center gap-2">
                    <span className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-amber-600 text-xs font-bold text-white">#{idx + 1}</span>
                    <div>
                      {/* Mistake summary is OET-authored English content. */}
                      <p className="text-sm font-bold text-navy" dir="ltr">{row.summary}</p>
                      <p className="text-xs text-muted">{t('writing.mistakes.mine.top5.occurrence', { count: row.stat.occurrenceCount })}</p>
                    </div>
                  </div>
                  {row.canonRuleId ? (
                    <Button asChild size="sm" variant="outline">
                      <Link href={`/writing/canon/${encodeURIComponent(row.canonRuleId)}`}>{t('writing.mistakes.mine.top5.readRule')}</Link>
                    </Button>
                  ) : null}
                </li>
              ))}
            </ol>
          </section>
        ) : (
          <Card padding="lg">
            <CardContent>
              <p className="text-sm text-muted">{t('writing.mistakes.mine.empty.body')}</p>
              <div className="mt-3">
                <Button asChild><Link href="/writing/practice/library">{t('writing.mistakes.mine.empty.cta')}</Link></Button>
              </div>
            </CardContent>
          </Card>
        )}

        {rows.length > 0 ? (
          <section aria-labelledby="all-mine-heading">
            <h2 id="all-mine-heading" className="mb-3 text-base font-bold text-navy">{t('writing.mistakes.mine.all.heading')}</h2>
            <ul className="grid gap-3 md:grid-cols-2">
              {rows.map((row) => (
                <li key={row.id}>
                  <MistakeCard mistake={row} personalStat={row.stat} />
                </li>
              ))}
            </ul>
          </section>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
