'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { AlertCircle, Filter } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
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

  return (
    <LearnerDashboardShell pageTitle={t('writing.mistakes.library.pageTitle')}>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow={t('writing.mistakes.library.eyebrow')}
          icon={AlertCircle}
          accent="amber"
          title={t('writing.mistakes.library.title')}
          description={t('writing.mistakes.library.description')}
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <div className="flex flex-wrap items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-3 shadow-sm">
          <fieldset className="flex flex-wrap items-center gap-2" aria-label={t('writing.mistakes.library.filters.legend')}>
            <legend className="sr-only">{t('writing.mistakes.library.filters.legend')}</legend>
            <span className="text-xs font-bold uppercase tracking-wider text-muted">
              <Filter className="mr-1 inline h-3 w-3" aria-hidden="true" /> {t('writing.mistakes.library.filters.skillLabel')}
            </span>
            <button
              type="button"
              onClick={() => setSubSkill(null)}
              aria-pressed={subSkill === null}
              className={`rounded-full border px-3 py-1.5 text-xs font-bold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${subSkill === null ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
            >
              {t('writing.mistakes.library.filters.all')}
            </button>
            {SKILLS.map((skill) => (
              <button
                key={skill}
                type="button"
                onClick={() => setSubSkill(skill)}
                aria-pressed={subSkill === skill}
                className={`rounded-full border px-3 py-1.5 text-xs font-bold focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${subSkill === skill ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
              >
                {skill}
              </button>
            ))}
          </fieldset>

          <label className="flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-muted">
            {t('writing.mistakes.library.filters.categoryLabel')}
            <select
              value={category}
              onChange={(e) => setCategory(e.target.value)}
              className="min-h-9 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">{t('writing.mistakes.library.filters.all')}</option>
              {/* Category strings are OET-authored English content. */}
              {categories.map((c) => <option key={c} value={c}>{c}</option>)}
            </select>
          </label>
        </div>

        <ul className="grid gap-3 md:grid-cols-2" aria-label={t('writing.mistakes.library.list.label')}>
          {items.length === 0 ? (
            <li className="col-span-full"><p className="text-sm text-muted">{t('writing.mistakes.library.list.empty')}</p></li>
          ) : null}
          {items.map((mistake) => (
            <li key={mistake.id}>
              <MistakeCard mistake={mistake} />
            </li>
          ))}
        </ul>

        <div className="flex justify-end">
          <Button asChild variant="outline">
            <Link href="/writing/common-mistakes/mine">{t('writing.mistakes.library.cta.mine')}</Link>
          </Button>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
