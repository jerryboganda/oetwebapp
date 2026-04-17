'use client';

import { useEffect, useState, useDeferredValue } from 'react';
import { Search as SearchIcon, Filter, Sparkles, BookOpen } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { searchContent, fetchSearchFacets, fetchRecommendations } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import type {
  SearchResultItem,
  SearchFacets,
  RecommendationResult,
  PaginatedResponse,
} from '@/lib/types/content-hierarchy';

const SUBTEST_COLORS: Record<string, string> = {
  writing: 'bg-blue-100 text-blue-800 dark:bg-blue-900/30 dark:text-blue-400',
  speaking: 'bg-green-100 text-green-800 dark:bg-green-900/30 dark:text-green-400',
  reading: 'bg-purple-100 text-purple-800 dark:bg-purple-900/30 dark:text-purple-400',
  listening: 'bg-amber-100 text-amber-800 dark:bg-amber-900/30 dark:text-amber-400',
};

export default function DiscoverPage() {
  const [query, setQuery] = useState('');
  const [subtestFilter, setSubtestFilter] = useState('');
  const [difficultyFilter, setDifficultyFilter] = useState('');
  const [results, setResults] = useState<SearchResultItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [facets, setFacets] = useState<SearchFacets | null>(null);
  const [recommendations, setRecommendations] = useState<RecommendationResult | null>(null);
  const [error, setError] = useState<string | null>(null);
  const deferredQuery = useDeferredValue(query);

  const changeSubtest = (value: string) => { setLoading(true); setSubtestFilter(value); setPage(1); };
  const changeDifficulty = (value: string) => { setLoading(true); setDifficultyFilter(value); setPage(1); };

  useEffect(() => {
    analytics.track('discover_page_viewed');
    fetchSearchFacets().then(f => setFacets(f as SearchFacets)).catch(() => {});
    fetchRecommendations(6).then(r => setRecommendations(r as RecommendationResult)).catch(() => {});
  }, []);

  useEffect(() => {
    let cancelled = false;
    searchContent({
      q: deferredQuery || undefined,
      subtest: subtestFilter || undefined,
      difficulty: difficultyFilter || undefined,
      page,
      pageSize: 20,
    })
      .then((data) => {
        if (cancelled) return;
        const response = data as PaginatedResponse<SearchResultItem>;
        setResults(response.items ?? []);
        setTotal(response.total ?? 0);
        setLoading(false);
      })
      .catch(() => {
        if (cancelled) return;
        setError('Search failed.');
        setLoading(false);
      });
    return () => { cancelled = true; };
  }, [deferredQuery, subtestFilter, difficultyFilter, page]);

  const showRecommendations = !deferredQuery && !subtestFilter && !difficultyFilter && recommendations;

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Discover Content"
        description="Search, filter, and find the perfect practice material for your OET preparation."
      />

      {/* Search bar */}
      <div className="relative mb-6">
        <SearchIcon className="absolute left-3 top-1/2 -translate-y-1/2 w-5 h-5 text-muted-foreground" />
        <input
          type="text"
          placeholder="Search tasks, topics, scenarios…"
          value={query}
          onChange={(e) => { setQuery(e.target.value); setPage(1); }}
          className="w-full rounded-xl border bg-background pl-10 pr-4 py-3 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
        />
      </div>

      {/* Filter chips */}
      {facets && (
        <div className="flex flex-wrap gap-2 mb-6">
          <div className="flex items-center gap-1 text-xs text-muted-foreground mr-2">
            <Filter className="w-3.5 h-3.5" /> Subtest:
          </div>
          <button onClick={() => changeSubtest('')} className={`px-3 py-2 rounded-full text-xs font-medium transition-colors ${!subtestFilter ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground'}`}>
            All ({facets.totalPublished})
          </button>
          {facets.subtests.map((f) => (
            <button key={f.value} onClick={() => changeSubtest(f.value)} className={`px-3 py-2 rounded-full text-xs font-medium transition-colors ${subtestFilter === f.value ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground'}`}>
              {f.value} ({f.count})
            </button>
          ))}

          <div className="flex items-center gap-1 text-xs text-muted-foreground mr-2 ml-4">
            Difficulty:
          </div>
          <button onClick={() => changeDifficulty('')} className={`px-3 py-2 rounded-full text-xs font-medium transition-colors ${!difficultyFilter ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground'}`}>
            All
          </button>
          {facets.difficulties.map((f) => (
            <button key={f.value} onClick={() => changeDifficulty(f.value)} className={`px-3 py-2 rounded-full text-xs font-medium transition-colors ${difficultyFilter === f.value ? 'bg-primary text-primary-foreground' : 'bg-muted text-muted-foreground'}`}>
              {f.value} ({f.count})
            </button>
          ))}
        </div>
      )}

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {/* Recommendations section (when no active search) */}
      {showRecommendations && (
        <div className="mb-8">
          <div className="flex items-center gap-2 mb-3">
            <Sparkles className="w-4 h-4 text-primary" />
            <h2 className="text-sm font-semibold">Recommended for You</h2>
            {recommendations.weakestSubtest && (
              <Badge variant="muted" className="text-[10px]">Focus: {recommendations.weakestSubtest}</Badge>
            )}
          </div>
          <MotionSection>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {recommendations.recommended.map((item) => (
                <MotionItem key={item.id}>
                  <Link href={`/lessons/${item.id}`} className="group block rounded-lg border bg-card p-4 hover:shadow-sm transition-shadow">
                    <div className="flex items-center gap-2 mb-2">
                      <Badge className={`text-[10px] ${SUBTEST_COLORS[item.subtestCode] ?? 'bg-muted'}`}>{item.subtestCode}</Badge>
                      <Badge variant="muted" className="text-[10px]">{item.difficulty}</Badge>
                    </div>
                    <h3 className="text-sm font-medium leading-tight mb-1">{item.title}</h3>
                    <p className="text-xs text-muted-foreground">{item.reason}</p>
                  </Link>
                </MotionItem>
              ))}
            </div>
          </MotionSection>
        </div>
      )}

      {/* Search results */}
      {loading ? (
        <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {Array.from({ length: 6 }).map((_, i) => (
            <Skeleton key={i} className="h-32 rounded-lg" />
          ))}
        </div>
      ) : results.length === 0 && (deferredQuery || subtestFilter || difficultyFilter) ? (
        <div className="text-center py-12 text-muted-foreground">
          <SearchIcon className="w-12 h-12 mx-auto mb-3 opacity-40" />
          <p className="text-lg font-medium">No results found</p>
          <p className="text-sm mt-1">Try different filters or search terms.</p>
        </div>
      ) : results.length > 0 ? (
        <>
          <div className="flex items-center justify-between mb-3">
            <span className="text-xs text-muted-foreground">{total} result{total !== 1 ? 's' : ''}</span>
          </div>
          <MotionSection>
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
              {results.map((item) => (
                <MotionItem key={item.id}>
                  <Link href={`/lessons/${item.id}`} className="group block rounded-lg border bg-card p-4 hover:shadow-sm transition-shadow">
                    <div className="flex items-center gap-2 mb-2">
                      <Badge className={`text-[10px] ${SUBTEST_COLORS[item.subtestCode] ?? 'bg-muted'}`}>{item.subtestCode}</Badge>
                      <Badge variant="muted" className="text-[10px]">{item.difficulty}</Badge>
                      {item.isPreviewEligible && <Badge variant="muted" className="text-[10px] text-green-600">Free Preview</Badge>}
                    </div>
                    <h3 className="text-sm font-medium leading-tight mb-1 group-hover:text-primary transition-colors">{item.title}</h3>
                    <div className="flex gap-2 text-xs text-muted-foreground">
                      <span>{item.estimatedDurationMinutes}m</span>
                      <span>·</span>
                      <span>{item.sourceProvenance}</span>
                      <span>·</span>
                      <span>Q:{item.qualityScore}</span>
                    </div>
                  </Link>
                </MotionItem>
              ))}
            </div>
          </MotionSection>

          {total > 20 && (
            <div className="flex justify-center gap-2 mt-6">
              <button disabled={page <= 1} onClick={() => setPage(p => p - 1)} className="px-3 py-1.5 rounded-lg border text-sm disabled:opacity-40">Previous</button>
              <span className="text-sm text-muted-foreground self-center">Page {page}</span>
              <button disabled={results.length < 20} onClick={() => setPage(p => p + 1)} className="px-3 py-1.5 rounded-lg border text-sm disabled:opacity-40">Next</button>
            </div>
          )}
        </>
      ) : null}
    </LearnerDashboardShell>
  );
}
