'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useSearchParams } from 'next/navigation';
import { motion } from 'motion/react';
import { Check, Loader2, Lock, Plus, Search as SearchIcon, Star, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import {
  fetchRecallsToday,
  fetchRecallsQueue,
  starRecall,
  fetchRecallsAudio,
  fetchVocabularyCategories,
  fetchVocabularyTerms,
  fetchMyVocabulary,
  addToMyVocabulary,
  removeFromMyVocabulary,
  isApiError,
  type RecallsTodayResponse,
  type RecallsQueueItem,
  type RecallsStarReason,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { playTransientAudio } from '@/lib/recalls-audio';
import { vocabularyProvenanceLabel } from '@/lib/vocabulary-provenance';
import type { VocabularyCategoriesResponse, VocabularyTerm } from '@/lib/types/vocabulary';

const STAR_REASONS: { key: RecallsStarReason; label: string }[] = [
  { key: 'spelling', label: 'Spelling' },
  { key: 'pronunciation', label: 'Pronunciation' },
  { key: 'meaning', label: 'Meaning' },
  { key: 'hearing', label: 'Hearing' },
  { key: 'confused', label: 'Confused' },
];

const SUBTEST_FILTERS = [
  { key: 'all', label: 'All subtests' },
  { key: 'listening_a', label: 'Listening A' },
  { key: 'listening_b', label: 'Listening B' },
  { key: 'listening_c', label: 'Listening C' },
  { key: 'reading_a', label: 'Reading A' },
  { key: 'reading_b', label: 'Reading B' },
  { key: 'reading_c', label: 'Reading C' },
  { key: 'writing', label: 'Writing' },
  { key: 'speaking', label: 'Speaking' },
];

interface VocabularyTermsPage {
  total: number;
  terms?: VocabularyTerm[];
  items?: VocabularyTerm[];
}

function formatCategoryLabel(category: string, count: number) {
  const label = category
    .replace(/[_-]/g, ' ')
    .replace(/\b\w/g, (char) => char.toUpperCase());
  return `${label} (${count})`;
}

/**
 * /recalls/words — vocabulary-side card list.
 *
 * Surfaces the queued cards (vocab + review) with a star toggle, audio button,
 * and direct link into the runner. The full quiz UX lives at /recalls/cards.
 */
export default function RecallsWordsPage() {
  const searchParams = useSearchParams();
  const initialRecallSet = searchParams?.get('recallSet') ?? null;

  const [today, setToday] = useState<RecallsTodayResponse | null>(null);
  const [items, setItems] = useState<RecallsQueueItem[] | null>(null);
  const [catalogTerms, setCatalogTerms] = useState<VocabularyTerm[]>([]);
  const [catalogTotal, setCatalogTotal] = useState(0);
  const [catalogLoading, setCatalogLoading] = useState(true);
  const [catalogLoadingMore, setCatalogLoadingMore] = useState(false);
  const [catalogPage, setCatalogPage] = useState(1);
  const [catalogError, setCatalogError] = useState<string | null>(null);
  const [categoryFilters, setCategoryFilters] = useState([{ key: 'all', label: 'All categories' }]);
  const [selectedCategory, setSelectedCategory] = useState('all');
  const [selectedSubtest, setSelectedSubtest] = useState('all');
  const [selectedRecallSet, setSelectedRecallSet] = useState<string | null>(initialRecallSet);
  const [searchQuery, setSearchQuery] = useState('');
  const [searchDraft, setSearchDraft] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [starOpenFor, setStarOpenFor] = useState<string | null>(null);
  // Per-card Add/Remove state. `addedIds` is hydrated on mount from
  // /v1/vocabulary/my-list so the catalog correctly shows "Added" for terms
  // already in the learner's recalls queue (e.g. auto-seeded from drills).
  const [addedIds, setAddedIds] = useState<Set<string>>(new Set());
  const [addingId, setAddingId] = useState<string | null>(null);
  const [addError, setAddError] = useState<string | null>(null);
  // Per PRD Phase 2 §2 the click-to-hear feature is paid-only. The backend is
  // authoritative (returns 402/403 for free learners) — we surface that as an
  // upgrade modal instead of a silent failure so candidates understand why.
  const [showUpgradeModal, setShowUpgradeModal] = useState(false);

  const CATALOG_PAGE_SIZE = 24;

  useEffect(() => {
    analytics.track('recalls_words_viewed');
    Promise.all([fetchRecallsToday(), fetchRecallsQueue(40)])
      .then(([t, q]) => {
        setToday(t);
        setItems(q);
      })
      .catch(() => setError('Could not load your recalls list.'))
      .finally(() => setLoading(false));

    fetchVocabularyCategories({ examTypeCode: 'oet' })
      .then((categories) => {
        const categoryPage = categories as VocabularyCategoriesResponse;
        setCategoryFilters([
          { key: 'all', label: 'All categories' },
          ...(categoryPage.categories ?? []).map((item) => ({
            key: item.category,
            label: formatCategoryLabel(item.category, item.termCount),
          })),
        ]);
      })
      .catch(() => {
        // Non-fatal: the matrix still works with the all-categories fallback.
      });

    // Hydrate the "Added" toggle state from the learner's existing my-list so
    // the catalog correctly reflects what is already queued (auto-seeded or
    // previously added). Best-effort — a failure here just means the first
    // click on an already-queued term will 409 and we recover from that.
    fetchMyVocabulary()
      .then((rows) => {
        const list = Array.isArray(rows)
          ? rows
          : ((rows as { items?: Array<{ termId?: string }> })?.items ?? []);
        const ids = new Set<string>();
        for (const r of list) {
          const tid = (r as { termId?: string; TermId?: string })?.termId
            ?? (r as { termId?: string; TermId?: string })?.TermId;
          if (typeof tid === 'string' && tid.length > 0) ids.add(tid);
        }
        setAddedIds(ids);
      })
      .catch(() => {
        // Non-fatal — Add button still works; 409s on already-queued terms are
        // handled gracefully in handleAddToRecalls.
      });
  }, []);

  // Catalog fetch: reset to page 1 whenever any filter (category, subtest,
  // recall set, or search) changes. The "Load more" button bumps catalogPage
  // and is handled in the separate pagination effect below.
  useEffect(() => {
    let active = true;
    setCatalogLoading(true);
    setCatalogError(null);
    setCatalogPage(1);
    fetchVocabularyTerms({
      examTypeCode: 'oet',
      category: selectedCategory === 'all' ? undefined : selectedCategory,
      oetSubtestTag: selectedSubtest === 'all' ? undefined : selectedSubtest,
      recallSet: selectedRecallSet ?? undefined,
      search: searchQuery.trim() || undefined,
      page: 1,
      pageSize: CATALOG_PAGE_SIZE,
    })
      .then((response) => {
        if (!active) return;
        const page = response as VocabularyTermsPage;
        setCatalogTerms(page.terms ?? page.items ?? []);
        setCatalogTotal(page.total ?? 0);
      })
      .catch(() => {
        if (!active) return;
        setCatalogTerms([]);
        setCatalogTotal(0);
        setCatalogError('Could not load the vocabulary matrix.');
      })
      .finally(() => {
        if (active) setCatalogLoading(false);
      });

    return () => {
      active = false;
    };
  }, [selectedCategory, selectedSubtest, selectedRecallSet, searchQuery]);

  // Load-more pagination: appends the next page to the existing list.
  async function handleLoadMore() {
    if (catalogLoadingMore) return;
    const nextPage = catalogPage + 1;
    setCatalogLoadingMore(true);
    setCatalogError(null);
    try {
      const response = (await fetchVocabularyTerms({
        examTypeCode: 'oet',
        category: selectedCategory === 'all' ? undefined : selectedCategory,
        oetSubtestTag: selectedSubtest === 'all' ? undefined : selectedSubtest,
        recallSet: selectedRecallSet ?? undefined,
        search: searchQuery.trim() || undefined,
        page: nextPage,
        pageSize: CATALOG_PAGE_SIZE,
      })) as VocabularyTermsPage;
      const more = response.terms ?? response.items ?? [];
      setCatalogTerms((prev) => [...prev, ...more]);
      setCatalogPage(nextPage);
      analytics.track('recalls_catalog_load_more', { page: nextPage, total: response.total ?? 0 });
    } catch {
      setCatalogError('Could not load more terms. Try again.');
    } finally {
      setCatalogLoadingMore(false);
    }
  }

  function handleSearchSubmit(event: React.FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const trimmed = searchDraft.trim();
    if (trimmed === searchQuery) return;
    setSearchQuery(trimmed);
  }

  function handleClearSearch() {
    setSearchDraft('');
    if (searchQuery !== '') setSearchQuery('');
  }

  function handleRecallSetChange(nextRecallSet: string | null) {
    if (nextRecallSet === selectedRecallSet) return;
    setSelectedRecallSet(nextRecallSet);
  }

  // Add a catalog term to the learner's recalls queue. Backend (POST
  // /v1/vocabulary/my-list/{termId}) is authoritative: it dedupes (409 on
  // already-queued), records source provenance for the SM-2 scheduler, and
  // seeds the first review window. We refresh the queue/today snapshot on
  // success so the queue list and hero counts update without a page reload.
  async function handleAddToRecalls(termId: string, term: string) {
    if (addedIds.has(termId) || addingId) return;
    setAddingId(termId);
    setAddError(null);
    try {
      await addToMyVocabulary(termId, { sourceRef: 'recalls_catalog_matrix' });
      setAddedIds((prev) => {
        const next = new Set(prev);
        next.add(termId);
        return next;
      });
      analytics.track('recalls_word_added', { termId, source: 'catalog_matrix' });
      // Best-effort refresh — don't await so the button doesn't appear stuck.
      void fetchRecallsToday().then(setToday).catch(() => undefined);
      void fetchRecallsQueue(40).then(setItems).catch(() => undefined);
    } catch (err) {
      // 409 means it's already in the queue (e.g. auto-seeded). Treat that as
      // success so the toggle reflects reality.
      if (isApiError(err) && err.status === 409) {
        setAddedIds((prev) => {
          const next = new Set(prev);
          next.add(termId);
          return next;
        });
      } else if (isApiError(err) && (err.status === 401 || err.status === 403)) {
        setAddError('Sign in as a learner to add words to your recalls queue.');
      } else {
        setAddError(`Could not add "${term}". Try again.`);
      }
    } finally {
      setAddingId(null);
    }
  }

  async function handleRemoveFromRecalls(termId: string, term: string) {
    if (!addedIds.has(termId) || addingId) return;
    setAddingId(termId);
    setAddError(null);
    try {
      await removeFromMyVocabulary(termId);
      setAddedIds((prev) => {
        const next = new Set(prev);
        next.delete(termId);
        return next;
      });
      analytics.track('recalls_word_removed', { termId, source: 'catalog_matrix' });
      void fetchRecallsToday().then(setToday).catch(() => undefined);
      void fetchRecallsQueue(40).then(setItems).catch(() => undefined);
    } catch (err) {
      if (isApiError(err) && err.status === 404) {
        // Server says it's not in the list — sync our state.
        setAddedIds((prev) => {
          const next = new Set(prev);
          next.delete(termId);
          return next;
        });
      } else {
        setAddError(`Could not remove "${term}". Try again.`);
      }
    } finally {
      setAddingId(null);
    }
  }

  function handleCategoryChange(nextCategory: string) {
    if (nextCategory === selectedCategory) return;
    setSelectedCategory(nextCategory);
  }

  function handleSubtestChange(nextSubtest: string) {
    if (nextSubtest === selectedSubtest) return;
    setSelectedSubtest(nextSubtest);
  }

  async function handleStar(it: RecallsQueueItem, reason: RecallsStarReason) {
    setStarOpenFor(null);
    setItems((prev) =>
      prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: true, starReason: reason } : p)) : prev,
    );
    try {
      await starRecall(it.kind, it.id, true, reason);
    } catch {
      setItems((prev) =>
        prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: it.starred, starReason: it.starReason } : p)) : prev,
      );
    }
  }

  async function handleUnstar(it: RecallsQueueItem) {
    setItems((prev) =>
      prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: false, starReason: null } : p)) : prev,
    );
    try {
      await starRecall(it.kind, it.id, false);
    } catch {
      setItems((prev) =>
        prev ? prev.map((p) => (p.id === it.id ? { ...p, starred: true, starReason: it.starReason } : p)) : prev,
      );
    }
  }

  async function handlePlay(it: RecallsQueueItem) {
    if (it.kind !== 'vocab' || !it.termId) return;
    try {
      const url = (await fetchRecallsAudio(it.termId, 'normal')).url;
      playTransientAudio(url);
      analytics.track('recalls_word_audio_played', { termId: it.termId });
    } catch (err) {
      // Backend gates audio behind an active subscription. Surface 402/403 as
      // an upgrade prompt; treat anything else as a quiet best-effort failure.
      if (isApiError(err) && (err.status === 402 || err.status === 403)) {
        analytics.track('recalls_word_audio_blocked', { termId: it.termId, status: err.status });
        setShowUpgradeModal(true);
      }
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Recalls / Words"
          title="Your active vocabulary"
          description="Every term you've added, every card seeded from your drills, all in one starrable, drillable list."
          icon={Star}
          highlights={[
            { icon: Star, label: 'Starred', value: `${today?.starred ?? 0}` },
            { icon: Star, label: 'Due today', value: `${today?.dueToday ?? 0}` },
            { icon: Star, label: 'Mastered', value: `${today?.mastered ?? 0}` },
          ]}
        />

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <LearnerSurfaceSectionHeader
          eyebrow="Catalog matrix"
          title="Browse active vocabulary by category and OET subtest"
          description="Use the functional category × OET subtest filters to inspect the active recall vocabulary catalog."
        />

        <section id="catalog" className="space-y-4 rounded-2xl border border-border bg-surface p-4">
          <div className="space-y-3">
            <fieldset>
              <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Recall set</legend>
              <div className="flex flex-wrap gap-2">
                {[
                  { key: null, label: 'All sets' },
                  { key: 'recalls_2026', label: 'Recalls 2026' },
                  { key: 'recalls_2023_2025', label: 'Recalls 2023–2025' },
                  { key: 'old', label: 'Older sets' },
                ].map((filter) => (
                  <button
                    key={filter.key ?? 'all'}
                    type="button"
                    aria-pressed={selectedRecallSet === filter.key}
                    onClick={() => handleRecallSetChange(filter.key)}
                    className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                      selectedRecallSet === filter.key
                        ? 'border-warning bg-warning text-white'
                        : 'border-border text-muted hover:border-warning hover:text-warning'
                    }`}
                  >
                    {filter.label}
                  </button>
                ))}
              </div>
            </fieldset>
            <fieldset>
              <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">Functional category</legend>
              <div className="flex flex-wrap gap-2">
                {categoryFilters.map((filter) => (
                  <button
                    key={filter.key}
                    type="button"
                    aria-pressed={selectedCategory === filter.key}
                    onClick={() => handleCategoryChange(filter.key)}
                    className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                      selectedCategory === filter.key
                        ? 'border-primary bg-primary text-white'
                        : 'border-border text-muted hover:border-primary hover:text-primary'
                    }`}
                  >
                    {filter.label}
                  </button>
                ))}
              </div>
            </fieldset>
            <fieldset>
              <legend className="mb-2 text-xs font-semibold uppercase tracking-wide text-muted">OET subtest</legend>
              <div className="flex flex-wrap gap-2">
                {SUBTEST_FILTERS.map((filter) => (
                  <button
                    key={filter.key}
                    type="button"
                    aria-pressed={selectedSubtest === filter.key}
                    onClick={() => handleSubtestChange(filter.key)}
                    className={`rounded-full border px-3 py-1 text-xs font-medium transition ${
                      selectedSubtest === filter.key
                        ? 'border-info bg-info text-white'
                        : 'border-border text-muted hover:border-info hover:text-info'
                    }`}
                  >
                    {filter.label}
                  </button>
                ))}
              </div>
            </fieldset>
            <form onSubmit={handleSearchSubmit} className="flex items-center gap-2">
              <label htmlFor="recalls-catalog-search" className="sr-only">
                Search vocabulary
              </label>
              <div className="relative flex-1">
                <SearchIcon className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" aria-hidden="true" />
                <input
                  id="recalls-catalog-search"
                  type="search"
                  value={searchDraft}
                  onChange={(event) => setSearchDraft(event.target.value)}
                  placeholder="Search vocabulary by term or keyword…"
                  className="w-full rounded-xl border border-border bg-background py-2 pl-9 pr-3 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/30"
                />
              </div>
              <Button type="submit" variant="primary" className="h-9 px-4 text-xs">
                Search
              </Button>
              {searchQuery && (
                <Button type="button" variant="secondary" onClick={handleClearSearch} className="h-9 px-3 text-xs">
                  Clear
                </Button>
              )}
            </form>
          </div>

          {addError && <InlineAlert variant="warning">{addError}</InlineAlert>}
          {catalogError && <InlineAlert variant="warning">{catalogError}</InlineAlert>}

          {catalogLoading ? (
            <div className="grid gap-3 md:grid-cols-2">
              {Array.from({ length: 4 }).map((_, i) => (
                <Skeleton key={i} className="h-24 rounded-xl" />
              ))}
            </div>
          ) : catalogTerms.length > 0 ? (
            <div className="space-y-3">
              <div className="text-sm text-muted">
                Showing {catalogTerms.length} of {catalogTotal} active terms for this matrix slice.
              </div>
              <div className="grid gap-3 md:grid-cols-2">
                {catalogTerms.map((term) => {
                  const provenanceLabel = vocabularyProvenanceLabel(term.sourceProvenance);
                  const isAdded = addedIds.has(term.id);
                  const isBusy = addingId === term.id;
                  return (
                    <article key={term.id} className="rounded-xl border border-border bg-background/70 p-4">
                      <div className="flex flex-wrap items-center gap-2">
                        <h3 className="font-semibold text-navy">{term.term}</h3>
                        <Badge variant="info">{term.category}</Badge>
                        <Badge variant="muted">{term.difficulty}</Badge>
                        {provenanceLabel && <Badge variant="warning">{provenanceLabel}</Badge>}
                      </div>
                      <p className="mt-2 text-sm text-muted">{term.definition}</p>
                      <p className="mt-2 text-xs italic text-muted">{term.exampleSentence}</p>
                      {term.oetSubtestTags && term.oetSubtestTags.length > 0 && (
                        <div className="mt-3 flex flex-wrap gap-1">
                          {term.oetSubtestTags.map((tag) => (
                            <Badge key={tag} variant="warning">
                              {tag.replace('_', ' ')}
                            </Badge>
                          ))}
                        </div>
                      )}
                      <div className="mt-3 flex items-center justify-between gap-2">
                        <span className="text-xs text-muted">
                          {isAdded ? 'In your queue' : 'Not yet in your queue'}
                        </span>
                        {isAdded ? (
                          <Button
                            type="button"
                            variant="secondary"
                            onClick={() => handleRemoveFromRecalls(term.id, term.term)}
                            disabled={isBusy}
                            aria-label={`Remove ${term.term} from my recalls`}
                            className="h-8 px-3 text-xs"
                          >
                            {isBusy ? (
                              <>
                                <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                                Removing…
                              </>
                            ) : (
                              <>
                                <Check className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                                Added
                              </>
                            )}
                          </Button>
                        ) : (
                          <Button
                            type="button"
                            variant="primary"
                            onClick={() => handleAddToRecalls(term.id, term.term)}
                            disabled={isBusy}
                            aria-label={`Add ${term.term} to my recalls`}
                            className="h-8 px-3 text-xs"
                          >
                            {isBusy ? (
                              <>
                                <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                                Adding…
                              </>
                            ) : (
                              <>
                                <Plus className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                                Add to my recalls
                              </>
                            )}
                          </Button>
                        )}
                      </div>
                    </article>
                  );
                })}
              </div>
              {catalogTerms.length < catalogTotal && (
                <div className="flex justify-center pt-2">
                  <Button
                    type="button"
                    variant="secondary"
                    onClick={handleLoadMore}
                    disabled={catalogLoadingMore}
                    className="h-9 px-4 text-xs"
                  >
                    {catalogLoadingMore ? (
                      <>
                        <Loader2 className="mr-1.5 h-3.5 w-3.5 animate-spin" aria-hidden="true" />
                        Loading…
                      </>
                    ) : (
                      <>Load {Math.min(CATALOG_PAGE_SIZE, catalogTotal - catalogTerms.length)} more</>
                    )}
                  </Button>
                </div>
              )}
            </div>
          ) : (
            <div className="rounded-xl border border-dashed border-border p-4 text-center text-sm text-muted">
              No active terms match this filter combination yet.
            </div>
          )}
        </section>

        <LearnerSurfaceSectionHeader
          eyebrow="Queue"
          title="Today's recall queue"
          description="Starred cards float to the top. Click play to hear the British pronunciation."
        />

        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-16 rounded-xl" />
            ))}
          </div>
        ) : items && items.length > 0 ? (
          <ul className="divide-y divide-border rounded-2xl border border-border bg-surface">
            {items.map((it) => (
              <li key={`${it.kind}:${it.id}`} className="flex items-center gap-3 p-3">
                {it.kind === 'vocab' ? (
                  <Button
                    type="button"
                    variant="primary"
                    onClick={() => handlePlay(it)}
                    aria-label={`Play ${it.title}`}
                    className="flex h-10 w-10 items-center justify-center rounded-full p-0"
                  >
                    <Volume2 className="h-4 w-4" />
                  </Button>
                ) : (
                  <div className="flex h-10 w-10 items-center justify-center rounded-full bg-info/10 text-info">
                    <Star className="h-4 w-4" aria-hidden="true" />
                  </div>
                )}
                <div className="flex-1">
                  <div className="flex items-center gap-2">
                    {it.kind === 'vocab' ? (
                      // PRD Phase 2 §2: clicking the word itself plays audio.
                      <button
                        type="button"
                        onClick={() => handlePlay(it)}
                        className="rounded font-semibold text-navy hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        aria-label={`Play pronunciation of ${it.title}`}
                      >
                        {it.title}
                      </button>
                    ) : (
                      <span className="font-semibold text-navy">{it.title}</span>
                    )}
                    <Badge variant={it.kind === 'vocab' ? 'info' : 'muted'}>{it.kind}</Badge>
                    {it.starred && <Badge variant="warning">Starred · {it.starReason ?? '—'}</Badge>}
                  </div>
                  {it.subtitle && <div className="text-xs text-muted">{it.subtitle}</div>}
                </div>
                <div className="relative">
                  {it.starred ? (
                    <button
                      type="button"
                      onClick={() => handleUnstar(it)}
                      className="rounded-full border border-border px-3 py-1 text-xs font-medium text-muted hover:border-warning hover:text-warning"
                    >
                      Unstar
                    </button>
                  ) : (
                    <button
                      type="button"
                      onClick={() => setStarOpenFor((cur) => (cur === it.id ? null : it.id))}
                      onKeyDown={(event) => {
                        if (event.key === 'Escape') setStarOpenFor(null);
                      }}
                      aria-haspopup="menu"
                      aria-expanded={starOpenFor === it.id}
                      aria-controls={`star-reason-menu-${it.id}`}
                      className="rounded-full border border-border px-3 py-1 text-xs font-medium text-muted hover:border-warning hover:text-warning"
                    >
                      Star
                    </button>
                  )}
                  {starOpenFor === it.id && (
                    <motion.div
                      initial={{ opacity: 0, y: -4 }}
                      animate={{ opacity: 1, y: 0 }}
                      id={`star-reason-menu-${it.id}`}
                      role="menu"
                      className="absolute right-0 top-full z-10 mt-1 w-44 overflow-hidden rounded-xl border border-border bg-surface shadow-lg"
                    >
                      {STAR_REASONS.map((r) => (
                        <button
                          key={r.key}
                          type="button"
                          role="menuitem"
                          onClick={() => handleStar(it, r.key)}
                          className="block w-full px-3 py-2 text-left text-sm text-navy hover:bg-lavender/40"
                        >
                          {r.label}
                        </button>
                      ))}
                    </motion.div>
                  )}
                </div>
              </li>
            ))}
          </ul>
        ) : (
          <div className="rounded-2xl border border-border bg-surface p-6 text-center text-sm text-muted">
            Nothing in your recall queue yet. Add words from the{' '}
            <Link
              href="#catalog"
              className="font-medium text-primary underline decoration-primary/40 underline-offset-2 hover:text-primary-dark"
            >
              vocabulary catalog above
            </Link>{' '}
            or complete a Listening drill — wrong free-text answers seed cards automatically.
          </div>
        )}
      </div>

      <Modal
        open={showUpgradeModal}
        onClose={() => setShowUpgradeModal(false)}
        title="Unlock click-to-hear pronunciation"
        size="sm"
      >
        <div className="space-y-4">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Lock className="h-5 w-5" />
          </div>
          <p className="text-sm text-muted">
            Pronunciation audio for recall words is part of the paid plan. Upgrade to listen to every term in your recall
            list, hear British clinical pronunciation, and unlock the full Recalls drill set.
          </p>
          <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => setShowUpgradeModal(false)}>
              Not now
            </Button>
            <Link
              href="/billing/upgrade"
              className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark"
              onClick={() => setShowUpgradeModal(false)}
            >
              View upgrade options
            </Link>
          </div>
        </div>
      </Modal>
    </LearnerDashboardShell>
  );
}
