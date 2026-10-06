'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { TrendingUp, AlertCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { cardClassName } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { cn } from '@/lib/utils';
import { MistakeCard } from '@/components/domain/writing/MistakeCard';
import { listMyCommonMistakes } from '@/lib/writing/api';
import { toCandidateSafeWritingErrorMessage } from '@/lib/writing/submit-keys';
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
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;
    void listMyCommonMistakes()
      .then((r) => {
        if (cancelled) return;
        setRows(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(toCandidateSafeWritingErrorMessage(err, t('writing.mistakes.mine.error.load')));
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [t]);

  const top5 = useMemo(() => rows.slice().sort((a, b) => b.stat.occurrenceCount - a.stat.occurrenceCount).slice(0, 5), [rows]);
  const totalCount = useMemo(() => rows.reduce((sum, r) => sum + r.stat.occurrenceCount, 0), [rows]);

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.mistakes.mine.eyebrow')}
        icon={TrendingUp}
        accent="writing"
        title={t('writing.mistakes.mine.title')}
        description={t('writing.mistakes.mine.description')}
        highlights={[
          { icon: AlertCircle, label: t('writing.mistakes.mine.highlights.tracked'), value: `${rows.length}` },
          { icon: TrendingUp, label: t('writing.mistakes.mine.highlights.total'), value: `${totalCount}` },
        ]}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {loading ? (
        <LearnerSkeleton variant="list" />
      ) : top5.length > 0 ? (
        <MotionSection delayIndex={0}>
          <section aria-labelledby="top5-heading" className={cn(cardClassName({ padding: 'lg' }), 'border-warning/20 bg-warning/10')}>
            <h2 id="top5-heading" className="text-lg font-bold text-warning-strong">{t('writing.mistakes.mine.top5.heading')}</h2>
            <p className="mt-1 text-xs text-warning-strong">{t('writing.mistakes.mine.top5.subtitle')}</p>
            <ol className="mt-3 space-y-2">
              {top5.map((row, idx) => (
                <li key={row.id}>
                  <MotionItem delayIndex={Math.min(idx, 5)} className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-surface p-3">
                    <div className="flex min-w-0 items-center gap-2">
                      <span className="inline-flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-warning/20 text-xs font-bold tabular-nums text-warning-strong">#{idx + 1}</span>
                      <div className="min-w-0">
                        {/* Mistake summary is OET-authored English content. */}
                        <p className="text-sm font-bold text-navy" dir="ltr">{row.summary}</p>
                        <p className="text-xs tabular-nums text-muted">{t('writing.mistakes.mine.top5.occurrence', { count: row.stat.occurrenceCount })}</p>
                      </div>
                    </div>
                    {row.canonRuleId ? (
                      <Button asChild size="sm" variant="outline">
                        <Link href={`/writing/canon/${encodeURIComponent(row.canonRuleId)}`}>{t('writing.mistakes.mine.top5.readRule')}</Link>
                      </Button>
                    ) : null}
                  </MotionItem>
                </li>
              ))}
            </ol>
          </section>
        </MotionSection>
      ) : error ? null : (
        <EmptyState
          icon={<TrendingUp className="h-8 w-8" />}
          title={t('writing.mistakes.mine.empty.body')}
          action={{ label: t('writing.mistakes.mine.empty.cta'), href: '/writing/practice/library' }}
        />
      )}

      {rows.length > 0 ? (
        <MotionSection delayIndex={1}>
          <section aria-labelledby="all-mine-heading" className="space-y-3">
            <h2 id="all-mine-heading" className="text-lg font-bold text-navy sm:text-xl">{t('writing.mistakes.mine.all.heading')}</h2>
            <ul className="grid grid-cols-1 gap-3 md:grid-cols-2">
              {rows.map((row, index) => (
                <li key={row.id} className="min-w-0">
                  <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                    <MistakeCard mistake={row} personalStat={row.stat} />
                  </MotionItem>
                </li>
              ))}
            </ul>
          </section>
        </MotionSection>
      ) : null}
    </>
  );
}
