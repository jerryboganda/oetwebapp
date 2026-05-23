'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';
import { Headphones, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { isApiError } from '@/lib/api';
import { getListeningExpertAttempts } from '@/lib/expert-listening-api';
import type { ListeningExpertAttemptSummary } from '@/lib/types/expert';

const PAGE_SIZE = 20;

export default function ExpertListeningAttemptsPage() {
  const router = useRouter();
  const [items, setItems] = useState<ListeningExpertAttemptSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [debouncedSearch, setDebouncedSearch] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  // Debounce search
  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 350);
    return () => {
      if (debounceRef.current) clearTimeout(debounceRef.current);
    };
  }, [search]);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const result = await getListeningExpertAttempts({
        page,
        ...(debouncedSearch ? { learnerId: debouncedSearch } : {}),
      });
      setItems(result.items);
      setTotal(result.total);
    } catch (err) {
      setError(
        isApiError(err) ? err.userMessage : 'Failed to load attempts.',
      );
    } finally {
      setLoading(false);
    }
  }, [debouncedSearch, page]);

  useEffect(() => {
    void load();
  }, [load]);

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <div className="mx-auto max-w-5xl space-y-6 p-6">
      {/* Header */}
      <div className="flex flex-col gap-4 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Headphones className="h-5 w-5" />
          </div>
          <div>
            <h1 className="text-xl font-bold text-navy">
              Listening Attempts
            </h1>
            <p className="text-sm text-muted">
              {total} submitted attempt{total !== 1 ? 's' : ''}
            </p>
          </div>
        </div>

        {/* Search */}
        <div className="relative w-full sm:w-72">
          <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
          <input
            type="text"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Search by learner name..."
            className="w-full rounded-xl border border-gray-200 bg-white py-2 pl-9 pr-3 text-sm text-navy placeholder:text-muted focus:border-primary/40 focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
        </div>
      </div>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {/* Table */}
      <div className="overflow-x-auto rounded-2xl border border-gray-200 bg-white shadow-sm">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-gray-100 text-left text-xs font-semibold uppercase tracking-wide text-muted">
              <th className="px-4 py-3">Learner</th>
              <th className="px-4 py-3">Paper</th>
              <th className="px-4 py-3">Submitted</th>
              <th className="px-4 py-3">Score</th>
              <th className="px-4 py-3">Feedback</th>
              <th className="px-4 py-3" />
            </tr>
          </thead>
          <tbody>
            {loading
              ? Array.from({ length: 5 }).map((_, i) => (
                  <tr key={i} className="border-b border-gray-50">
                    {Array.from({ length: 6 }).map((__, j) => (
                      <td key={j} className="px-4 py-3">
                        <Skeleton className="h-4 rounded" />
                      </td>
                    ))}
                  </tr>
                ))
              : items.map((item) => (
                  <tr
                    key={item.attemptId}
                    className="border-b border-gray-50 transition-colors hover:bg-gray-50"
                  >
                    <td className="px-4 py-3 font-medium text-navy">
                      {item.learnerDisplayName}
                    </td>
                    <td className="px-4 py-3 text-muted">
                      {item.paperTitle}
                    </td>
                    <td className="px-4 py-3 text-muted">
                      {new Date(item.submittedAt).toLocaleDateString(
                        undefined,
                        {
                          year: 'numeric',
                          month: 'short',
                          day: 'numeric',
                        },
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <span className="font-semibold text-navy">
                        {item.rawScore}/{item.maxRawScore}
                      </span>
                      <span className="ml-2 text-xs text-muted">
                        ({item.scaledScore} scaled)
                      </span>
                    </td>
                    <td className="px-4 py-3">
                      {item.hasFeedback ? (
                        <Badge variant="success">Done</Badge>
                      ) : (
                        <Badge variant="warning">Pending</Badge>
                      )}
                    </td>
                    <td className="px-4 py-3">
                      <Button
                        size="sm"
                        variant={item.hasFeedback ? 'outline' : 'default'}
                        onClick={() =>
                          router.push(
                            `/expert/review/listening/${item.attemptId}`,
                          )
                        }
                      >
                        {item.hasFeedback ? 'View' : 'Review'}
                      </Button>
                    </td>
                  </tr>
                ))}

            {!loading && items.length === 0 && (
              <tr>
                <td
                  colSpan={6}
                  className="px-4 py-12 text-center text-muted"
                >
                  No listening attempts found.
                </td>
              </tr>
            )}
          </tbody>
        </table>
      </div>

      {/* Pagination */}
      {totalPages > 1 && (
        <div className="flex items-center justify-between text-sm text-muted">
          <span>
            Page {page} of {totalPages}
          </span>
          <div className="flex gap-2">
            <Button
              size="sm"
              variant="outline"
              disabled={page <= 1}
              onClick={() => setPage((p) => p - 1)}
            >
              Previous
            </Button>
            <Button
              size="sm"
              variant="outline"
              disabled={page >= totalPages}
              onClick={() => setPage((p) => p + 1)}
            >
              Next
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}
