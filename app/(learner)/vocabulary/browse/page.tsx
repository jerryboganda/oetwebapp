'use client';

import { useCallback, useContext, useEffect, useMemo, useState } from 'react';
import { useQuery } from '@tanstack/react-query';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Search, Plus, CheckCircle2, BookOpen, ArrowLeft, Volume2, Lock } from 'lucide-react';
import Link from 'next/link';
import { AuthContext } from '@/contexts/auth-context';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, CategoryBadge, RecallTierBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { EmptyState } from '@/components/ui/empty-error';
import {
  fetchVocabularyTerms,
  addToMyVocabulary,
  fetchVocabularyCategories,
  fetchRecallsAudio,
  fetchVocabularyRecallSets,
  type RecallSetSummary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { queryKeys } from '@/lib/query/hooks';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';
import { playTransientAudio } from '@/lib/recalls-audio';
import { isEditableEventTarget } from '@/lib/is-editable-target';
import { cleanExampleSentencesForList } from '@/lib/vocabulary-example-sentence';
import { cn } from '@/lib/utils';
import type { VocabularyTerm, VocabularyCategoriesResponse } from '@/lib/types/vocabulary';

// Toggle pills: a tint plus a border when pressed; 44px tall on touch layouts.
function chipClass(active: boolean) {
  return cn(
    'inline-flex min-h-11 items-center rounded-full border px-3 py-1 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:min-h-8',
    active
      ? 'border-primary/30 bg-primary/10 text-primary'
      : 'border-border bg-background-light text-muted hover:border-border-hover hover:text-navy',
  );
}

// Mobile offline cache — lazy, best-effort; skipped in SSR and when IndexedDB is unavailable.
async function cacheVocabularyToIndexedDb(terms: unknown[]) {
  if (typeof window === 'undefined') return;
  if (typeof indexedDB === 'undefined') return;
  try {
    const mod: typeof import('@/lib/mobile/offline-sync') = await import('@/lib/mobile/offline-sync');
    await mod.cacheVocabularyTerms(terms);
  } catch {/* offline cache is best-effort */}
}

type TermRow = Pick<VocabularyTerm, 'id' | 'term' | 'definition' | 'category' | 'exampleSentence' | 'ipaPronunciation' | 'sourceProvenance' | 'isLocked' | 'isFreePreview' | 'examFrequencyCount' | 'recallSetOccurrences' | 'updatedAt'>;

export default function BrowseVocabularyPage() {
  const [terms, setTerms] = useState<TermRow[]>([]);
  const [recallSet, setRecallSet] = useState('');
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState<Set<string>>(new Set());
  const [added, setAdded] = useState<Set<string>>(new Set());
  // Free-preview locking: the backend redacts non-preview terms for
  // non-subscribed learners (`term.isLocked === true`). Clicking a locked term
  // opens this modal with the canonical subscribe prompt.
  const [showLockedModal, setShowLockedModal] = useState(false);
  const { guardAudio, modal: audioUpgradeModal } = useRecallsAudioUpgrade();

  // §3A — drop template/filler example copy, and any sentence shared verbatim by
  // several different words, so the list never repeats a meaningless line.
  const exampleSentences = useMemo(() => {
    const cleaned = cleanExampleSentencesForList(terms);
    return new Map(terms.map((term, index) => [term.id, cleaned[index] ?? '']));
  }, [terms]);
  const authContext = useContext(AuthContext);
  const queryUserId = authContext?.user?.userId ?? 'current';
  const referenceQueriesEnabled = authContext ? !authContext.loading && authContext.isAuthenticated : true;
  const categoriesQuery = useQuery({
    queryKey: queryKeys.vocabulary.categories(queryUserId, 'oet'),
    queryFn: () => fetchVocabularyCategories({ examTypeCode: 'oet' }),
    staleTime: 60_000,
    enabled: referenceQueriesEnabled,
  });
  const recallSetsQuery = useQuery({
    queryKey: queryKeys.vocabulary.recallSets(queryUserId, 'oet'),
    queryFn: () => fetchVocabularyRecallSets({ examTypeCode: 'oet' }),
    staleTime: 60_000,
    enabled: referenceQueriesEnabled,
  });
  const categories = ((categoriesQuery.data as VocabularyCategoriesResponse | undefined)?.categories ?? []);
  const recallSets = ((recallSetsQuery.data?.sets ?? []) as RecallSetSummary[]);
  const referenceError = categoriesQuery.error || recallSetsQuery.error
    ? 'Some vocabulary filters could not be loaded.'
    : null;
  const displayError = error ?? referenceError;

  const pageSize = 20;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchVocabularyTerms({
        examTypeCode: 'oet',
        category: category || undefined,
        recallSet: recallSet || undefined,
        search: search || undefined,
        page,
        pageSize,
      }) as { total?: number; terms?: TermRow[]; items?: TermRow[] } | TermRow[];
      const normalizedTerms = Array.isArray(data) ? data : (data.terms ?? data.items ?? []);
      setTerms(normalizedTerms as TermRow[]);
      setTotal(Array.isArray(data) ? normalizedTerms.length : data.total ?? normalizedTerms.length);
      // Fire-and-forget: cache these terms for offline use.
      void cacheVocabularyToIndexedDb(normalizedTerms);
    } catch {
      setError('Could not load vocabulary terms.');
    } finally {
      setLoading(false);
    }
  }, [category, page, pageSize, recallSet, search]);

  useEffect(() => {
    analytics.track('vocab_browse_viewed');
  }, []);

  useEffect(() => {
    const timer = setTimeout(() => {
      void load();
    }, 300);
    return () => clearTimeout(timer);
  }, [load]);

  async function handleAdd(termId: string) {
    if (adding.has(termId) || added.has(termId)) return;
    setAdding(prev => new Set(prev).add(termId));
    try {
      await addToMyVocabulary(termId, { sourceRef: 'browse' });
      analytics.track('vocab_added', { termId, source: 'browse' });
      setAdded(prev => new Set(prev).add(termId));
    } catch (e) {
      const message = e instanceof Error ? e.message : 'Could not add term.';
      setError(message);
    } finally {
      setAdding(prev => { const s = new Set(prev); s.delete(termId); return s; });
    }
  }

  async function playAudio(termId: string) {
    try {
      const response = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
      if (response) {
        playTransientAudio(response.url);
      }
    } catch {
      setError('Pronunciation audio is not ready yet.');
    }
  }

  const totalPages = Math.ceil(total / pageSize);

  return (
    <>
      <LearnerPageHero
        title="Browse Vocabulary"
        description="Explore OET medical vocabulary terms"
        icon={BookOpen}
        aside={(
          <Button variant="ghost" size="sm" asChild>
            <Link href="/vocabulary">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to vocabulary
            </Link>
          </Button>
        )}
      />

      {displayError && <InlineAlert variant="warning">{displayError}</InlineAlert>}
      {audioUpgradeModal}

      {/* Filters */}
      <Card>
        <LearnerSurfaceSectionHeader
          eyebrow="Search"
          title="Filter terms"
          description="Narrow by keyword or category before adding terms to your list."
          className="mb-4"
        />
        <div className="flex flex-col gap-3 sm:flex-row">
          <div className="relative flex-1">
            <Search className="absolute start-3 top-1/2 w-4 h-4 -translate-y-1/2 text-muted" aria-hidden="true" />
            <input
              type="text"
              aria-label="Search vocabulary terms"
              placeholder="Search terms..."
              value={search}
              onChange={e => { setSearch(e.target.value); setPage(1); }}
              className="min-h-11 w-full rounded-xl border border-border bg-background-light py-2.5 ps-9 pe-4 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/30"
            />
          </div>
          <select
            aria-label="Filter vocabulary by category"
            value={category}
            onChange={e => { setCategory(e.target.value); setPage(1); }}
            className="min-h-11 rounded-xl border border-border bg-background-light px-3 py-2.5 text-sm text-navy capitalize"
          >
            <option value="">All Categories ({total})</option>
            {categories.map(c => (
              <option key={c.category} value={c.category}>
                {c.category.replace(/_/g, ' ')} ({c.termCount})
              </option>
            ))}
          </select>
        </div>
        {recallSets.length > 0 && (
          <div className="mt-3 flex flex-wrap items-center gap-2">
            <span className="eyebrow text-muted">Practice collection:</span>
            <button
              type="button"
              aria-pressed={recallSet === ''}
              onClick={() => { setRecallSet(''); setPage(1); }}
              className={chipClass(recallSet === '')}
            >
              All
            </button>
            {recallSets.map((s) => (
              <button
                key={s.code}
                type="button"
                aria-pressed={recallSet === s.code}
                onClick={() => { setRecallSet(s.code); setPage(1); }}
                title={s.description}
                className={chipClass(recallSet === s.code)}
              >
                {s.shortLabel}{s.termCount > 0 ? ` (${s.termCount})` : ''}
              </button>
            ))}
          </div>
        )}
      </Card>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-2xl" />)}
        </div>
      ) : terms.length === 0 ? (
        <EmptyState
          icon={<Search className="h-7 w-7" aria-hidden="true" />}
          title="No terms found"
          description="Try a different search or clear the filters."
        />
      ) : (
        <MotionSection className="space-y-6">
          <div className="space-y-3">
            {terms.map((term, i) => {
              if (term.isLocked) {
                return (
                  <MotionItem key={term.id} delayIndex={Math.min(i, 5)}>
                    <Card
                      hoverable
                      role="group"
                      tabIndex={0}
                      onClick={() => setShowLockedModal(true)}
                      onKeyDown={(event: React.KeyboardEvent) => {
                        if (isEditableEventTarget(event.target)) return;
                        if (event.key === 'Enter' || event.key === ' ' || event.code === 'Space') {
                          event.preventDefault();
                          setShowLockedModal(true);
                        }
                      }}
                      aria-label={`${term.term} — locked. Subscribe to unlock the full Recall Vocabulary Bank.`}
                      className="group relative cursor-pointer overflow-hidden focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                    >
                      <div className="pointer-events-none select-none blur-sm" aria-hidden="true">
                        <div className="mb-1.5 flex flex-wrap items-center gap-2">
                          <span className="text-base font-bold text-navy">{term.term}</span>
                        </div>
                        <div className="mb-2 flex flex-wrap items-center gap-1.5">
                          <CategoryBadge category={term.category} size="sm" />
                        </div>
                        <p className="text-sm leading-relaxed text-muted">
                          Definition hidden — subscribe to reveal the full recall entry, audio and examples.
                        </p>
                      </div>
                      <div className="absolute inset-0 flex flex-col items-center justify-center gap-2 bg-surface/40 text-center backdrop-blur-[2px]">
                        <span className="inline-flex h-9 w-9 items-center justify-center rounded-full bg-primary/10 text-primary">
                          <Lock size={16} strokeWidth={2} aria-hidden="true" />
                        </span>
                        <span className="px-4 text-xs font-semibold text-navy">
                          Subscribe to unlock the full Recall Vocabulary Bank.
                        </span>
                      </div>
                    </Card>
                  </MotionItem>
                );
              }
              return (
                <MotionItem key={term.id} delayIndex={Math.min(i, 5)}>
                  <Card className="flex gap-4">
                    <div className="flex-1 min-w-0">
                      <div className="mb-1.5 flex flex-wrap items-center gap-2">
                        <Link
                          href={`/vocabulary/terms/${encodeURIComponent(term.id)}`}
                          className="rounded text-base font-bold text-navy transition-colors hover:text-primary focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        >
                          {term.term}
                        </Link>
                        <RecallTierBadge
                          count={term.examFrequencyCount ?? 0}
                          occurrences={term.recallSetOccurrences}
                          lastUpdatedAt={term.updatedAt}
                        />
                        {term.ipaPronunciation && (
                          <span className="text-xs italic text-muted">{term.ipaPronunciation}</span>
                        )}
                        <button
                          type="button"
                          onClick={() => void playAudio(term.id)}
                          className="pressable inline-flex size-11 items-center justify-center rounded-full bg-primary/10 text-primary transition-colors hover:bg-primary/20 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:size-8"
                          aria-label={`Play pronunciation of ${term.term}`}
                        >
                          <Volume2 className="h-3.5 w-3.5" aria-hidden="true" />
                        </button>
                      </div>
                      <div className="mb-2 flex flex-wrap items-center gap-1.5">
                        <CategoryBadge category={term.category} size="sm" />
                      </div>
                      <p className="text-sm leading-relaxed text-muted">{term.definition}</p>
                      {(exampleSentences.get(term.id) ?? '') && <p className="mt-1.5 text-xs italic leading-relaxed text-muted">&quot;{exampleSentences.get(term.id)}&quot;</p>}
                    </div>
                    <button
                      type="button"
                      onClick={() => handleAdd(term.id)}
                      disabled={adding.has(term.id) || added.has(term.id)}
                      className={cn(
                        'pressable inline-flex size-11 shrink-0 items-center justify-center self-start rounded-control border transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                        added.has(term.id)
                          ? 'border-success/20 bg-success/10 text-success-strong'
                          : 'border-transparent text-muted hover:border-primary/20 hover:bg-primary/10 hover:text-primary',
                      )}
                      title={added.has(term.id) ? 'Added to your list' : 'Add to my list'}
                      aria-label={added.has(term.id) ? `${term.term} added to your list` : `Add ${term.term} to your list`}
                    >
                      {added.has(term.id) ? <CheckCircle2 className="w-5 h-5" aria-hidden="true" /> : <Plus className="w-5 h-5" aria-hidden="true" />}
                    </button>
                  </Card>
                </MotionItem>
              );
            })}
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-2">
              <Button variant="outline" size="sm" onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1}>Prev</Button>
              <span className="text-sm tabular-nums text-muted">{page} / {totalPages}</span>
              <Button variant="outline" size="sm" onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages}>Next</Button>
            </div>
          )}
        </MotionSection>
      )}

      <Modal
        open={showLockedModal}
        onClose={() => setShowLockedModal(false)}
        title="Locked recall word"
        size="sm"
      >
        <div className="space-y-4">
          <div className="flex h-12 w-12 items-center justify-center rounded-full bg-primary/10 text-primary">
            <Lock className="h-5 w-5" />
          </div>
          <p className="text-sm font-semibold text-navy">
            Subscribe to unlock the full Recall Vocabulary Bank.
          </p>
          <p className="text-sm text-muted">
            Free learners can preview a curated selection of recall words. Subscribe to reveal every term, with
            definitions, examples, British clinical pronunciation, and the full Recalls drill set.
          </p>
          <div className="flex flex-col gap-2 sm:flex-row sm:justify-end">
            <Button variant="secondary" onClick={() => setShowLockedModal(false)}>
              Not now
            </Button>
            <Button asChild>
              <Link href="/catalog" onClick={() => setShowLockedModal(false)}>
                View upgrade options
              </Link>
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
}
