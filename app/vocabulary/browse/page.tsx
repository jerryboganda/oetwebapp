'use client';

import { useCallback, useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Search, Plus, CheckCircle2, BookOpen, ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchVocabularyTerms, addToMyVocabulary } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type VocabTerm = { id: string; word: string; definition: string; category: string; difficultyLevel: string; exampleSentence: string | null; pronunciation: string | null };

export default function BrowseVocabularyPage() {
  const [terms, setTerms] = useState<VocabTerm[]>([]);
  const [search, setSearch] = useState('');
  const [category, setCategory] = useState('');
  const [page, setPage] = useState(1);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [adding, setAdding] = useState<Set<string>>(new Set());
  const [added, setAdded] = useState<Set<string>>(new Set());

  const pageSize = 20;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchVocabularyTerms({ examTypeCode: 'oet', category: category || undefined, search: search || undefined, page, pageSize }) as { total?: number; terms?: VocabTerm[]; items?: VocabTerm[] } | VocabTerm[];
      const normalizedTerms = Array.isArray(data) ? data : (data.terms ?? data.items ?? []);
      setTerms(normalizedTerms as VocabTerm[]);
      setTotal(Array.isArray(data) ? normalizedTerms.length : data.total ?? normalizedTerms.length);
    } catch {
      setError('Could not load vocabulary terms.');
    } finally {
      setLoading(false);
    }
  }, [category, page, pageSize, search]);

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
      await addToMyVocabulary(termId);
      setAdded(prev => new Set(prev).add(termId));
    } catch {
      setError('Could not add term.');
    } finally {
      setAdding(prev => { const s = new Set(prev); s.delete(termId); return s; });
    }
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

      {/* Filters */}
      <Card className="mb-6 border-gray-200 bg-surface p-4">
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
              className="w-full rounded-xl border border-gray-200 bg-background-light pl-9 pr-4 py-2.5 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/30"
            />
          </div>
          <select
            value={category}
            onChange={e => { setCategory(e.target.value); setPage(1); }}
            className="rounded-xl border border-gray-200 bg-background-light px-3 py-2.5 text-sm text-navy"
          >
            <option value="">All Categories</option>
            <option value="symptoms">Symptoms</option>
            <option value="anatomy">Anatomy</option>
            <option value="pharmacology">Pharmacology</option>
            <option value="procedures">Procedures</option>
            <option value="conditions">Conditions</option>
            <option value="respiratory">Respiratory</option>
            <option value="cardiovascular">Cardiovascular</option>
            <option value="neurology">Neurology</option>
          </select>
        </div>
      </Card>

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      ) : terms.length === 0 ? (
        <Card className="border-gray-200 bg-surface px-8 py-12 text-center text-muted">No terms found. Try a different search.</Card>
      ) : (
        <>
          <div className="space-y-3 mb-6">
            {terms.map((term, i) => (
              <MotionItem
                key={term.id}
                delayIndex={i}
                className="flex gap-4 rounded-2xl border border-gray-200 bg-surface p-4"
              >
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <span className="font-bold text-navy">{term.word}</span>
                    {term.pronunciation && <span className="text-xs italic text-muted">{term.pronunciation}</span>}
                    <span className="rounded-full bg-background-light px-2 py-0.5 text-xs capitalize text-muted">{term.category}</span>
                  </div>
                  <p className="text-sm text-muted">{term.definition}</p>
                  {term.exampleSentence && <p className="mt-1 text-xs italic text-muted">&quot;{term.exampleSentence}&quot;</p>}
                </div>
                <button
                  onClick={() => handleAdd(term.id)}
                  disabled={adding.has(term.id) || added.has(term.id)}
                  className={`flex-shrink-0 rounded-lg p-2 transition-colors ${added.has(term.id) ? 'bg-green-50 text-green-500' : 'text-muted hover:bg-background-light hover:text-primary'}`}
                  title="Add to my list"
                >
                  {added.has(term.id) ? <CheckCircle2 className="w-5 h-5" /> : <Plus className="w-5 h-5" />}
                </button>
              </MotionItem>
            ))}
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-2">
              <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} className="rounded-lg border border-gray-200 bg-surface px-3 py-1.5 text-sm text-navy disabled:opacity-40 hover:bg-background-light">Prev</button>
              <span className="text-sm text-muted">{page} / {totalPages}</span>
              <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages} className="rounded-lg border border-gray-200 bg-surface px-3 py-1.5 text-sm text-navy disabled:opacity-40 hover:bg-background-light">Next</button>
            </div>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
