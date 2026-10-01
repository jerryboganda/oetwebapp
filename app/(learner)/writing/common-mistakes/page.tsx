'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { AlertCircle, Filter } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { MistakeCard } from '@/components/domain/writing/MistakeCard';
import { listCommonMistakes } from '@/lib/writing/api';
import type { WritingCommonMistakeDto, WritingSubSkill } from '@/lib/writing/types';

const SKILLS: WritingSubSkill[] = ['W1', 'W2', 'W3', 'W4', 'W5', 'W6', 'W7', 'W8'];

export default function WritingCommonMistakesPage() {
  const t = useTranslations();
  const [items, setItems] = useState<WritingCommonMistakeDto[]>([]);
  const [subSkill, setSubSkill] = useState<WritingSubSkill | null>(null);
  const [category, setCategory] = useState<string>('');
  const [error, setError] = useState<string | null>(null);
  // The filter whose list last settled; anything else is still loading.
  const filterKey = `${subSkill ?? ''}|${category}`;
  const [settledKey, setSettledKey] = useState<string | null>(null);
  const loading = settledKey !== filterKey;

  useEffect(() => {
    let cancelled = false;
    listCommonMistakes({ subSkill: subSkill ?? undefined, category: category || undefined })
      .then((r) => {
        if (cancelled) return;
        setItems(r.items);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.mistakes.library.error.load'));
      })
      .finally(() => {
        if (!cancelled) setSettledKey(`${subSkill ?? ''}|${category}`);
      });
    return () => {
      cancelled = true;
    };
  }, [subSkill, category, t]);

  const categories = useMemo(() => {
    const s = new Set<string>();
    for (const m of items) s.add(m.category);
    return Array.from(s).sort();
  }, [items]);

  const pillClassName = (selected: boolean) => `pressable min-h-11 rounded-full border px-3 py-1.5 text-xs font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${selected ? 'border-primary bg-primary text-white dark:bg-primary-700' : 'hover-primary border-border bg-background text-navy hover:border-primary/40'}`;

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.mistakes.library.eyebrow')}
        icon={AlertCircle}
        accent="amber"
        title={t('writing.mistakes.library.title')}
        description={t('writing.mistakes.library.description')}
        aside={
          <Button asChild variant="outline">
            <Link href="/writing/common-mistakes/mine">{t('writing.mistakes.library.cta.mine')}</Link>
          </Button>
        }
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Card padding="sm" className="flex flex-wrap items-center justify-between gap-3">
        <fieldset className="flex min-w-0 flex-wrap items-center gap-2" aria-label={t('writing.mistakes.library.filters.legend')}>
          <legend className="sr-only">{t('writing.mistakes.library.filters.legend')}</legend>
          <span className="eyebrow text-muted">
            <Filter className="me-1 inline h-3 w-3" aria-hidden="true" /> {t('writing.mistakes.library.filters.skillLabel')}
          </span>
          <button
            type="button"
            onClick={() => setSubSkill(null)}
            aria-pressed={subSkill === null}
            className={pillClassName(subSkill === null)}
          >
            {t('writing.mistakes.library.filters.all')}
          </button>
          {SKILLS.map((skill) => (
            <button
              key={skill}
              type="button"
              onClick={() => setSubSkill(skill)}
              aria-pressed={subSkill === skill}
              className={pillClassName(subSkill === skill)}
            >
              {skill}
            </button>
          ))}
        </fieldset>

        <label className="flex items-center gap-2">
          <span className="eyebrow text-muted">{t('writing.mistakes.library.filters.categoryLabel')}</span>
          <select
            value={category}
            onChange={(e) => setCategory(e.target.value)}
            className="min-h-11 rounded-control border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
          >
            <option value="">{t('writing.mistakes.library.filters.all')}</option>
            {/* Category strings are OET-authored English content. */}
            {categories.map((c) => <option key={c} value={c}>{c}</option>)}
          </select>
        </label>
      </Card>

      {loading && items.length === 0 ? (
        <LearnerSkeleton variant="card-grid" />
      ) : items.length === 0 ? (
        error ? null : <EmptyState icon={<AlertCircle className="h-8 w-8" />} title={t('writing.mistakes.library.list.empty')} />
      ) : (
        <ul className="grid grid-cols-1 gap-3 md:grid-cols-2" aria-label={t('writing.mistakes.library.list.label')} aria-busy={loading}>
          {items.map((mistake, index) => (
            <li key={mistake.id} className="min-w-0">
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                <MistakeCard mistake={mistake} />
              </MotionItem>
            </li>
          ))}
        </ul>
      )}
    </>
  );
}
