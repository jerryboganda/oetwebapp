'use client';

import { useCallback, useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { Search, Plus, CheckCircle2, BookOpen, ArrowLeft } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
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
      <div className="flex items-center gap-3 mb-6">
        <Link href="/vocabulary" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero title="Browse Vocabulary" description="Explore OET medical vocabulary terms" icon={BookOpen} />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Filters */}
      <div className="flex flex-col sm:flex-row gap-3 mb-6">
        <div className="relative flex-1">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 w-4 h-4 text-gray-400" />
          <input
            type="text"
            placeholder="Search terms..."
            value={search}
            onChange={e => { setSearch(e.target.value); setPage(1); }}
            className="w-full pl-9 pr-4 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
          />
        </div>
        <select
          value={category}
          onChange={e => { setCategory(e.target.value); setPage(1); }}
          className="px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-800 text-gray-700 dark:text-gray-300 text-sm"
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

      {loading ? (
        <div className="space-y-3">
          {Array.from({ length: 8 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}
        </div>
      ) : terms.length === 0 ? (
        <div className="text-center py-12 text-gray-400">No terms found. Try a different search.</div>
      ) : (
        <>
          <div className="space-y-3 mb-6">
            {terms.map((term, i) => (
              <MotionItem
                key={term.id}
                delayIndex={i}
                className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 flex gap-4"
              >
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2 mb-1">
                    <span className="font-bold text-gray-900 dark:text-white">{term.word}</span>
                    {term.pronunciation && <span className="text-xs text-gray-400 italic">{term.pronunciation}</span>}
                    <span className="text-xs px-2 py-0.5 rounded-full bg-gray-100 dark:bg-gray-700 text-gray-500 capitalize">{term.category}</span>
                  </div>
                  <p className="text-sm text-gray-600 dark:text-gray-400">{term.definition}</p>
                  {term.exampleSentence && <p className="text-xs text-gray-400 italic mt-1">&quot;{term.exampleSentence}&quot;</p>}
                </div>
                <button
                  onClick={() => handleAdd(term.id)}
                  disabled={adding.has(term.id) || added.has(term.id)}
                  className={`flex-shrink-0 p-2 rounded-lg transition-colors ${added.has(term.id) ? 'text-green-500 bg-green-50 dark:bg-green-900/20' : 'text-gray-400 hover:text-indigo-600 hover:bg-indigo-50 dark:hover:bg-indigo-900/20'}`}
                  title="Add to my list"
                >
                  {added.has(term.id) ? <CheckCircle2 className="w-5 h-5" /> : <Plus className="w-5 h-5" />}
                </button>
              </MotionItem>
            ))}
          </div>

          {totalPages > 1 && (
            <div className="flex items-center justify-center gap-2">
              <button onClick={() => setPage(p => Math.max(1, p - 1))} disabled={page === 1} className="px-3 py-1.5 rounded-lg border border-gray-200 dark:border-gray-700 text-sm disabled:opacity-40 hover:bg-gray-50 dark:hover:bg-gray-700">Prev</button>
              <span className="text-sm text-gray-500">{page} / {totalPages}</span>
              <button onClick={() => setPage(p => Math.min(totalPages, p + 1))} disabled={page === totalPages} className="px-3 py-1.5 rounded-lg border border-gray-200 dark:border-gray-700 text-sm disabled:opacity-40 hover:bg-gray-50 dark:hover:bg-gray-700">Next</button>
            </div>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
