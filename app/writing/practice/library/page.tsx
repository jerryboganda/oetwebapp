'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowRight, FilterIcon, Layers, Library, Search } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { listWritingScenarios } from '@/lib/writing/api';
import type {
  WritingLetterType,
  WritingProfession,
  WritingScenarioDto,
} from '@/lib/writing/types';

const PROFESSIONS: WritingProfession[] = ['medicine', 'pharmacy', 'nursing', 'other'];
const LETTER_TYPES: WritingLetterType[] = ['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-RP', 'LT-NM'];
const DIFFICULTIES = [1, 2, 3, 4, 5] as const;

export default function WritingPracticeLibraryPage() {
  const t = useTranslations();
  const [scenarios, setScenarios] = useState<WritingScenarioDto[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [profession, setProfession] = useState<WritingProfession | null>(null);
  const [letterType, setLetterType] = useState<WritingLetterType | null>(null);
  const [difficulty, setDifficulty] = useState<number | null>(null);
  const [search, setSearch] = useState('');

  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    listWritingScenarios({
      profession: profession ?? undefined,
      letterType: letterType ?? undefined,
      difficulty: difficulty ?? undefined,
      search: search.trim() || undefined,
      pageSize: 50,
    })
      .then((result) => {
        if (cancelled) return;
        setScenarios(result.items);
        setError(null);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : t('writing.practice.library.error.load'));
      })
      .finally(() => {
        if (cancelled) return;
        setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [profession, letterType, difficulty, search, t]);

  const topicSet = useMemo(() => {
    const s = new Set<string>();
    for (const sc of scenarios) {
      for (const topic of sc.topics) s.add(topic);
    }
    return Array.from(s).sort();
  }, [scenarios]);

  return (
    <LearnerDashboardShell pageTitle={t('writing.practice.library.pageTitle')}>
      <div className="space-y-6" aria-busy={loading}>
        <LearnerPageHero
          eyebrow={t('writing.practice.library.eyebrow')}
          icon={Library}
          accent="amber"
          title={t('writing.practice.library.title')}
          description={t('writing.practice.library.description')}
          highlights={[
            { icon: Layers, label: t('writing.practice.library.highlights.total'), value: `${scenarios.length}` },
            { icon: FilterIcon, label: t('writing.practice.library.highlights.activeFilters'), value: `${[profession, letterType, difficulty, search].filter(Boolean).length}` },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <LearnerSurfaceSectionHeader
          eyebrow={t('writing.practice.library.filters.eyebrow')}
          title={t('writing.practice.library.filters.title')}
          description={t('writing.practice.library.filters.description')}
        />

        <fieldset className="grid gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm md:grid-cols-2" aria-label={t('writing.practice.library.filters.legend')}>
          <legend className="sr-only">{t('writing.practice.library.filters.legend')}</legend>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            {t('writing.practice.library.filters.profession')}
            <select
              value={profession ?? ''}
              onChange={(e) => setProfession((e.target.value || null) as WritingProfession | null)}
              className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">{t('writing.practice.library.filters.all')}</option>
              {PROFESSIONS.map((p) => (
                <option key={p} value={p}>{t(`writing.practice.library.profession.${p}`)}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            {t('writing.practice.library.filters.letterType')}
            <select
              value={letterType ?? ''}
              onChange={(e) => setLetterType((e.target.value || null) as WritingLetterType | null)}
              className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">{t('writing.practice.library.filters.all')}</option>
              {LETTER_TYPES.map((lt) => (
                <option key={lt} value={lt}>{t(`writing.practice.library.letterType.${lt}`)} ({lt})</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            {t('writing.practice.library.filters.difficulty')}
            <select
              value={difficulty ?? ''}
              onChange={(e) => setDifficulty(e.target.value ? Number(e.target.value) : null)}
              className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <option value="">{t('writing.practice.library.filters.all')}</option>
              {DIFFICULTIES.map((d) => (
                <option key={d} value={d}>{t('writing.practice.library.filters.level', { n: d })}</option>
              ))}
            </select>
          </label>

          <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
            {t('writing.practice.library.filters.search')}
            <span className="relative">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
              <input
                type="search"
                value={search}
                onChange={(e) => setSearch(e.target.value)}
                placeholder={t('writing.practice.library.filters.searchPlaceholder')}
                className="min-h-11 w-full rounded-lg border border-border bg-background pl-9 pr-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </span>
          </label>
        </fieldset>

        {topicSet.length > 0 ? (
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
            <span className="font-bold uppercase tracking-wider">{t('writing.practice.library.topics.label')}</span>
            {/* Topics are OET-authored English content; force LTR for badge contents. */}
            {topicSet.slice(0, 12).map((topic) => (
              <Badge key={topic} variant="muted" size="sm"><span dir="ltr">{topic}</span></Badge>
            ))}
            {topicSet.length > 12 ? <span>{t('writing.practice.library.topics.more', { count: topicSet.length - 12 })}</span> : null}
          </div>
        ) : null}

        <ul className="grid gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label={t('writing.practice.library.list.label')}>
          {scenarios.length === 0 && !loading ? (
            <li className="col-span-full">
              <Card padding="lg">
                <CardContent>
                  <p className="text-sm text-muted">{t('writing.practice.library.list.empty')}</p>
                </CardContent>
              </Card>
            </li>
          ) : null}
          {scenarios.map((scenario) => (
            <li key={scenario.id}>
              <Card padding="md" aria-label={t('writing.practice.library.cardAria', { title: scenario.title })}>
                <CardContent>
                  <header className="flex flex-wrap items-center justify-between gap-1">
                    <div className="flex flex-wrap items-center gap-1">
                      <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
                      <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                      <Badge variant={scenario.difficulty >= 4 ? 'danger' : scenario.difficulty >= 3 ? 'warning' : 'success'} size="sm">
                        {t('writing.practice.library.list.levelBadge', { n: scenario.difficulty })}
                      </Badge>
                    </div>
                  </header>
                  {/* Scenario title and topics are OET-authored English content. */}
                  <h2 className="mt-2 text-base font-bold text-navy" dir="ltr">{scenario.title}</h2>
                  {scenario.topics.length > 0 ? (
                    <p className="mt-1 text-xs text-muted">
                      <span>{t('writing.practice.library.list.topics')}</span>{' '}
                      <span dir="ltr">{scenario.topics.slice(0, 4).join(', ')}</span>
                    </p>
                  ) : null}
                  <div className="mt-3">
                    <Button asChild size="sm">
                      <Link href={`/writing/practice/session/${encodeURIComponent(scenario.id)}`} aria-label={t('writing.practice.library.cta.practiceAria', { title: scenario.title })}>
                        {t('writing.practice.library.cta.practice')} <ArrowRight className="h-3 w-3" aria-hidden="true" />
                      </Link>
                    </Button>
                  </div>
                </CardContent>
              </Card>
            </li>
          ))}
        </ul>
      </div>
    </LearnerDashboardShell>
  );
}
