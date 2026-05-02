'use client';

import { useCallback, useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Search, Plus, CheckCircle2, BookOpen, ArrowLeft, Volume2 } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchVocabularyTerms,
  addToMyVocabulary,
  fetchVocabularyCategories,
  fetchRecallsAudio,
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

type TermRow = Pick<VocabularyTerm, 'id' | 'term' | 'definition' | 'category' | 'difficulty' | 'exampleSentence' | 'ipaPronunciation'>;

export default function BrowseVocabularyPage() {
  const [terms, setTerms] = useState<TermRow[]>([]);
  const [categories, setCategories] = useState<Array<{ category: string; termCount: number }>>([]);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState<Set<string>>(new Set());
  const [added, setAdded] = useState<Set<string>>(new Set());
  const { guardAudio, modal: audioUpgradeModal } = useRecallsAudioUpgrade();

  const pageSize = 20;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchVocabularyTerms({
        examTypeCode: 'oet',
        category: category || undefined,
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
  }, [category, page, pageSize, search]);

  useEffect(() => {
    analytics.track('vocab_browse_viewed');
    // Load categories once on mount.
    fetchVocabularyCategories({ examTypeCode: 'oet' })
      .then((data) => {
        const d = data as VocabularyCategoriesResponse;
        setCategories(d.categories ?? []);
      })
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
    const response = await guardAudio(() => fetchRecallsAudio(termId, 'normal'), { termId });
    if (!response) return;
    playTransientAudio(response.url);
  }

  const totalPages = Math.ceil(total / pageSize);

  return (
    <LearnerDashboardShell>
      <div className="mb-6 flex items-center gap-3">
        <Link href="/vocabulary" className="text-muted transition-colors hover:text-navy">
          <ArrowLeft className="w-5 h-5" />
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
            <Search className="absolute left-3 top-1/2 w-4 h-4 -translate-y-1/2 text-muted" />
            <input
              type="text"
              placeholder="Search terms..."
              value={search}
              onChange={e => { setSearch(e.target.value); setPage(1); }}
              className="w-full rounded-xl border border-border bg-background-light pl-9 pr-4 py-2.5 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/30"
            />
          </div>
          <select
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
            {terms.map((term, i) => (
              <MotionItem
                key={term.id}
                delayIndex={i}
                className="flex gap-4 rounded-2xl border border-border bg-surface p-4"
              >
                <div className="flex-1 min-w-0">
                  <div className="mb-1 flex flex-wrap items-center gap-2">
                    <Link
                      href={`/vocabulary/terms/${encodeURIComponent(term.id)}`}
                      className="font-bold text-navy hover:underline"
                    >
                      {term.term}
                    </Link>
                    {term.ipaPronunciation && (
                      <span className="text-xs italic text-muted">{term.ipaPronunciation}</span>
                    )}
                    <button
                      onClick={() => void playAudio(term.id)}
                      className="inline-flex items-center rounded-full p-1 text-muted transition-colors hover:bg-background-light hover:text-primary"
                      aria-label={`Play pronunciation of ${term.term}`}
                    >
                      <Volume2 className="h-3.5 w-3.5" />
                    </button>
                    <span className="rounded-full bg-background-light px-2 py-0.5 text-xs capitalize text-muted">
                      {term.category.replace(/_/g, ' ')}
                    </span>
                    <span className="rounded-full bg-background-light px-2 py-0.5 text-xs capitalize text-muted">
                      {term.difficulty}
                    </span>
                  </div>
                  <p className="text-sm text-muted">{term.definition}</p>
                  {term.exampleSentence && <p className="mt-1 text-xs italic text-muted">&quot;{term.exampleSentence}&quot;</p>}
                </div>
                <button
                  onClick={() => handleAdd(term.id)}
                  disabled={adding.has(term.id) || added.has(term.id)}
                  className={`flex-shrink-0 rounded-lg p-2 transition-colors ${added.has(term.id) ? 'bg-success/10 text-success' : 'text-muted hover:bg-background-light hover:text-primary'}`}
                  title="Add to my list"
                  aria-label={added.has(term.id) ? `${term.term} added to your list` : `Add ${term.term} to your list`}
                >
                  {added.has(term.id) ? <CheckCircle2 className="w-5 h-5" /> : <Plus className="w-5 h-5" />}
                </button>
              </MotionItem>
            ))}
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
