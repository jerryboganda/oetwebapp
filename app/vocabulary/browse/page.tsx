'use client';

import { useCallback, useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Search, Plus, CheckCircle2, BookOpen, ArrowLeft, Volume2, Lock } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge, CategoryBadge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import {
  fetchVocabularyTerms,
  addToMyVocabulary,
  fetchVocabularyCategories,
  fetchRecallsAudio,
  fetchVocabularyRecallSets,
  type RecallSetSummary,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { useRecallsAudioUpgrade } from '@/components/domain/recalls/audio-upgrade-modal';
import { playTransientAudio } from '@/lib/recalls-audio';
import type { VocabularyTerm, VocabularyCategoriesResponse } from '@/lib/types/vocabulary';

// Mobile offline cache — lazy, best-effort; skipped in SSR and when IndexedDB is unavailable.
async function cacheVocabularyToIndexedDb(terms: unknown[]) {
  if (typeof window === 'undefined') return;
  if (typeof indexedDB === 'undefined') return;
  try {
    const mod: typeof import('@/lib/mobile/offline-sync') = await import(
      /* webpackChunkName: "vocab-offline-cache" */
      '@/lib/mobile/offline-sync'
    );
    await mod.cacheVocabularyTerms(terms);
  } catch {/* offline cache is best-effort */}
}

type TermRow = Pick<VocabularyTerm, 'id' | 'term' | 'definition' | 'category' | 'exampleSentence' | 'ipaPronunciation' | 'sourceProvenance' | 'isLocked' | 'isFreePreview'>;

export default function BrowseVocabularyPage() {
  const [terms, setTerms] = useState<TermRow[]>([]);
  const [categories, setCategories] = useState<Array<{ category: string; termCount: number }>>([]);
  const [recallSets, setRecallSets] = useState<RecallSetSummary[]>([]);
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
    // Load categories once on mount.
    fetchVocabularyCategories({ examTypeCode: 'oet' })
      .then((data) => {
        const d = data as VocabularyCategoriesResponse;
        setCategories(d.categories ?? []);
      })
      .catch(() => {/* non-fatal */});
    // Load recall-set registry (practice-collection dimension).
    fetchVocabularyRecallSets({ examTypeCode: 'oet' })
      .then((data) => setRecallSets(data.sets ?? []))
      .catch(() => {/* non-fatal */});
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
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" aria-label="Back to vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" aria-hidden="true" />
        </Link>
        <LearnerPageHero title="Browse Vocabulary" description="Explore OET medical vocabulary terms" icon={BookOpen} />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}
      {audioUpgradeModal}

      {/* Filters */}
      <Card className="mb-6 border-border bg-surface p-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Search"
          title="Filter terms"
          description="Narrow by keyword or category before adding terms to your list."
          className="mb-4"
        />
        <div className="flex flex-col gap-3 sm:flex-row">
          <div className="relative flex-1">
            <Search className="absolute left-3 top-1/2 w-4 h-4 -translate-y-1/2 text-muted" aria-hidden="true" />
            <input
              type="text"
              aria-label="Search vocabulary terms"
              placeholder="Search terms..."
              value={search}
              onChange={e => { setSearch(e.target.value); setPage(1); }}
              className="w-full rounded-xl border border-border bg-background-light pl-9 pr-4 py-2.5 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/30"
            />
          </div>
          <select
            aria-label="Filter vocabulary by category"
            value={category}
            onChange={e => { setCategory(e.target.value); setPage(1); }}
            className="rounded-xl border border-border bg-background-light px-3 py-2.5 text-sm text-navy capitalize"
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
            <span className="text-xs font-semibold uppercase tracking-wide text-muted">Practice collection:</span>
            <button
              type="button"
              aria-pressed={recallSet === ''}
              onClick={() => { setRecallSet(''); setPage(1); }}
              className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                recallSet === ''
                  ? 'border-primary bg-primary/10 text-primary'
                  : 'border-border bg-background-light text-muted hover:border-border-hover hover:text-navy'
              }`}
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
                className={`rounded-full border px-3 py-1 text-xs font-medium transition-colors ${
                  recallSet === s.code
                    ? 'border-primary bg-primary/10 text-primary'
                    : 'border-border bg-background-light text-muted hover:border-border-hover hover:text-navy'
                }`}
              >
                {s.shortLabel}{s.termCount > 0 ? ` (${s.termCount})` : ''}
              </button>
            ))}
          </div>
        )}
      </Card>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      ) : terms.length === 0 ? (
        <Card className="border-border bg-surface px-4 sm:px-8 py-6 sm:py-12 text-center text-muted">No terms found. Try a different search.</Card>
      ) : (
        <>
          <div className="space-y-3 mb-6">
            {terms.map((term, i) => {
              if (term.isLocked) {
                return (
                  <MotionItem
                    key={term.id}
                    delayIndex={i}
                    role="group"
                    tabIndex={0}
                    onClick={() => setShowLockedModal(true)}
                    onKeyDown={(event: React.KeyboardEvent) => {
                      if (event.key === 'Enter' || event.key === ' ' || event.code === 'Space') {
                        event.preventDefault();
                        setShowLockedModal(true);
                      }
                    }}
                    aria-label={`${term.term} — locked. Subscribe to unlock the full Recall Vocabulary Bank.`}
                    className="group relative cursor-pointer overflow-hidden rounded-2xl border border-border bg-surface p-5 transition-[color,background-color,border-color,box-shadow] duration-200 hover:border-primary/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
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
                        <Lock size={16} strokeWidth={2} />
                      </span>
                      <span className="px-4 text-xs font-semibold text-navy">
                        Subscribe to unlock the full Recall Vocabulary Bank.
                      </span>
                    </div>
                  </MotionItem>
                );
              }
              return (
                <MotionItem
                  key={term.id}
                  delayIndex={i}
                  className="group flex gap-4 rounded-2xl border border-border bg-surface p-5 transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 hover:border-primary/30 hover:shadow-md hoverable:-translate-y-0.5"
                >
                  <div className="flex-1 min-w-0">
                    <div className="mb-1.5 flex flex-wrap items-center gap-2">
                      <Link
                        href={`/vocabulary/terms/${encodeURIComponent(term.id)}`}
                        className="text-base font-bold text-navy hover:text-primary transition-colors"
                      >
                        {term.term}
                      </Link>
                      {term.ipaPronunciation && (

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
            <Link
              href="/billing/upgrade"
              className="inline-flex items-center justify-center rounded-xl bg-primary px-4 py-2 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
              onClick={() => setShowLockedModal(false)}
            >
              View upgrade options
            </Link>
          </div>
        </div>
      </Modal>
                        <span className="text-xs italic text-muted">{term.ipaPronunciation}</span>
                      )}
                      <button
                        onClick={() => void playAudio(term.id)}
                        className="inline-flex h-7 w-7 items-center justify-center rounded-full bg-primary/10 p-1.5 text-primary transition-colors hover:bg-primary/20"
                        aria-label={`Play pronunciation of ${term.term}`}
                      >
                        <Volume2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                    <div className="mb-2 flex flex-wrap items-center gap-1.5">
                      <CategoryBadge category={term.category} size="sm" />
                    </div>
                    <p className="text-sm leading-relaxed text-muted">{term.definition}</p>
                    {term.exampleSentence && <p className="mt-1.5 text-xs italic leading-relaxed text-muted/80">&quot;{term.exampleSentence}&quot;</p>}
                  </div>
                  <button
                    onClick={() => handleAdd(term.id)}
                    disabled={adding.has(term.id) || added.has(term.id)}
                    className={`flex-shrink-0 self-start rounded-xl p-2.5 transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 ${added.has(term.id) ? 'bg-emerald-50 text-emerald-600 border border-emerald-200/60' : 'text-muted border border-transparent hover:bg-primary/10 hover:text-primary hover:border-primary/20'}`}
                    title={added.has(term.id) ? 'Added to your list' : 'Add to my list'}
                    aria-label={added.has(term.id) ? `${term.term} added to your list` : `Add ${term.term} to your list`}
                  >
                    {added.has(term.id) ? <CheckCircle2 className="w-5 h-5" /> : <Plus className="w-5 h-5" />}
                  </button>
                </MotionItem>
              );
            })}
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-2">
              <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} className="rounded-lg border border-border bg-surface px-3 py-1.5 text-sm text-navy disabled:opacity-40 hover:bg-background-light">Prev</button>
              <span className="text-sm text-muted">{page} / {totalPages}</span>
              <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages} className="rounded-lg border border-border bg-surface px-3 py-1.5 text-sm text-navy disabled:opacity-40 hover:bg-background-light">Next</button>
            </div>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
