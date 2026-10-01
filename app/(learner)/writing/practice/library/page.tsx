'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useTranslations } from 'next-intl';
import { ArrowRight, FilterIcon, Layers, Library, PenTool, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain/learner-surface';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { FreeSampleLauncher } from '@/components/domain/free-sample-launcher';
import { listWritingScenarios } from '@/lib/writing/api';
import type {
  WritingLetterType,
  WritingScenarioDto,
} from '@/lib/writing/types';

const LETTER_TYPES: WritingLetterType[] = ['LT-RR', 'LT-UR', 'LT-DG', 'LT-TR', 'LT-NM', 'LT-OT'];
const PAGE_SIZE = 50;

export default function WritingPracticeLibraryPage() {
  const t = useTranslations();
  const [scenarios, setScenarios] = useState<WritingScenarioDto[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadingMore, setLoadingMore] = useState(false);
  const [letterType, setLetterType] = useState<WritingLetterType | null>(null);
  const [search, setSearch] = useState('');

  // Load page 1 (replacing the list) whenever a filter changes. Profession is
  // never a learner filter: the server scopes the catalogue to the learner's
  // registered profession (owner, 23 Sep 2026).
  useEffect(() => {
    let cancelled = false;
    setLoading(true);
    setPage(1);
    listWritingScenarios({
      letterType: letterType ?? undefined,
      search: search.trim() || undefined,
      page: 1,
      pageSize: PAGE_SIZE,
    })
      .then((result) => {
        if (cancelled) return;
        setScenarios(result.items);
        setTotal(result.total);
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
  }, [letterType, search, t]);

  const loadMore = () => {
    const nextPage = page + 1;
    setLoadingMore(true);
    listWritingScenarios({
      letterType: letterType ?? undefined,
      search: search.trim() || undefined,
      page: nextPage,
      pageSize: PAGE_SIZE,
    })
      .then((result) => {
        setScenarios((prev) => [...prev, ...result.items]);
        setTotal(result.total);
        setPage(nextPage);
        setError(null);
      })
      .catch((err) => {
        setError(err instanceof Error ? err.message : t('writing.practice.library.error.load'));
      })
      .finally(() => {
        setLoadingMore(false);
      });
  };

  const hasMore = scenarios.length < total;

  const topicSet = useMemo(() => {
    const s = new Set<string>();
    for (const sc of scenarios) {
      for (const topic of sc.topics) s.add(topic);
    }
    return Array.from(s).sort();
  }, [scenarios]);

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.practice.library.eyebrow')}
        icon={Library}
        accent="writing"
        title={t('writing.practice.library.title')}
        description={t('writing.practice.library.description')}
        highlights={[
          { icon: Layers, label: t('writing.practice.library.highlights.total'), value: `${scenarios.length} / ${total}` },
          { icon: FilterIcon, label: t('writing.practice.library.highlights.activeFilters'), value: `${[letterType, search].filter(Boolean).length}` },
        ]}
      />

      {/* Same free-sample state as the Writing hub card (one source: /v1/free-samples/writing). */}
      <FreeSampleLauncher
        subtest="writing"
        icon={PenTool}
        testId="writing-library-free-sample-card"
        title={t('writing.hub.freeSample.title')}
        description={t('writing.hub.freeSample.description')}
        badgeLabel={t('writing.hub.freeSample.badge')}
        className=""
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <MotionSection delayIndex={0} className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow={t('writing.practice.library.filters.eyebrow')}
          title={t('writing.practice.library.filters.title')}
          description={t('writing.practice.library.filters.description')}
        />

        <Card padding="md">
          <fieldset className="grid grid-cols-1 gap-3 md:grid-cols-2" aria-label={t('writing.practice.library.filters.legend')}>
            <legend className="sr-only">{t('writing.practice.library.filters.legend')}</legend>

            <label className="flex min-w-0 flex-col gap-1">
              <span className="eyebrow text-muted">{t('writing.practice.library.filters.letterType')}</span>
              <select
                value={letterType ?? ''}
                onChange={(e) => setLetterType((e.target.value || null) as WritingLetterType | null)}
                className="min-h-11 rounded-control border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <option value="">{t('writing.practice.library.filters.all')}</option>
                {LETTER_TYPES.map((lt) => (
                  <option key={lt} value={lt}>{t(`writing.practice.library.letterType.${lt}`)} ({lt})</option>
                ))}
              </select>
            </label>

            <label className="flex min-w-0 flex-col gap-1">
              <span className="eyebrow text-muted">{t('writing.practice.library.filters.search')}</span>
              <span className="relative">
                <Search className="absolute start-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
                <input
                  type="search"
                  value={search}
                  onChange={(e) => setSearch(e.target.value)}
                  placeholder={t('writing.practice.library.filters.searchPlaceholder')}
                  className="min-h-11 w-full rounded-control border border-border bg-background ps-9 pe-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                />
              </span>
            </label>
          </fieldset>
        </Card>

        {topicSet.length > 0 ? (
          <div className="flex flex-wrap items-center gap-2 text-xs text-muted">
            <span className="eyebrow">{t('writing.practice.library.topics.label')}</span>
            {/* Topics are OET-authored English content; force LTR for badge contents. */}
            {topicSet.slice(0, 12).map((topic) => (
              <Badge key={topic} variant="muted" size="sm"><span dir="ltr">{topic}</span></Badge>
            ))}
            {topicSet.length > 12 ? <span>{t('writing.practice.library.topics.more', { count: topicSet.length - 12 })}</span> : null}
          </div>
        ) : null}
      </MotionSection>

      {loading && scenarios.length === 0 ? (
        <LearnerSkeleton variant="card-grid" />
      ) : scenarios.length === 0 ? (
        <EmptyState icon={<Library className="h-8 w-8" />} title={t('writing.practice.library.list.empty')} />
      ) : (
        <ul className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-3" aria-label={t('writing.practice.library.list.label')} aria-busy={loading}>
          {scenarios.map((scenario, index) => (
            <li key={scenario.id}>
              <MotionItem delayIndex={Math.min(index, 5)} className="h-full">
                <Card padding="md" className="flex h-full flex-col" aria-label={t('writing.practice.library.cardAria', { title: scenario.title })}>
                  <header className="flex flex-wrap items-center gap-1">
                    <Badge variant="muted" size="sm">{scenario.letterType}</Badge>
                    <Badge variant="info" size="sm" className="capitalize">{scenario.profession}</Badge>
                  </header>
                  {/* Scenario title and topics are OET-authored English content. */}
                  <h2 className="mt-2 text-base font-bold text-navy" dir="ltr">{scenario.title}</h2>
                  {scenario.topics.length > 0 ? (
                    <p className="mt-1 text-xs text-muted">
                      <span>{t('writing.practice.library.list.topics')}</span>{' '}
                      <span dir="ltr">{scenario.topics.slice(0, 4).join(', ')}</span>
                    </p>
                  ) : null}
                  <div className="mt-auto pt-3">
                    <Button asChild size="sm">
                      <Link href={`/writing/practice/session/${encodeURIComponent(scenario.id)}`} aria-label={t('writing.practice.library.cta.practiceAria', { title: scenario.title })}>
                        {t('writing.practice.library.cta.practice')} <ArrowRight className="h-3 w-3 rtl:rotate-180" aria-hidden="true" />
                      </Link>
                    </Button>
                  </div>
                </Card>
              </MotionItem>
            </li>
          ))}
        </ul>
      )}

      {hasMore ? (
        <div className="flex justify-center">
          <Button variant="outline" onClick={loadMore} disabled={loadingMore}>
            {loadingMore
              ? t('writing.practice.library.list.loadingMore')
              : t('writing.practice.library.list.loadMore', { count: total - scenarios.length })}
          </Button>
        </div>
      ) : null}
    </>
  );
}
