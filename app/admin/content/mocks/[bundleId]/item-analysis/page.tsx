'use client';

import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, RefreshCcw } from 'lucide-react';
import Link from 'next/link';

import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import {
  fetchAdminMockBundleItemAnalysis,
  recomputeAdminMockBundleItemAnalysis,
  type AdminMockItemAnalysisResponse,
} from '@/lib/api';

/**
 * Mocks V2 Wave 3 — Item Analysis dashboard.
 * Shows per-item difficulty / distractor stats for a published mock bundle.
 * Admins click "Recompute" to refresh aggregations from submitted attempts.
 */
export default function MockBundleItemAnalysisPage() {
  const params = useParams<{ bundleId: string }>();
  const bundleId = params?.bundleId ?? '';

  const [data, setData] = useState<AdminMockItemAnalysisResponse | null>(null);
  const [loading, setLoading] = useState<boolean>(true);
  const [refreshing, setRefreshing] = useState<boolean>(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!bundleId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await fetchAdminMockBundleItemAnalysis(bundleId);
      setData(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load item analysis.');
    } finally {
      setLoading(false);
    }
  }, [bundleId]);

  useEffect(() => { void load(); }, [load]);

  const recompute = useCallback(async () => {
    if (!bundleId) return;
    setRefreshing(true);
    setError(null);
    try {
      const res = await recomputeAdminMockBundleItemAnalysis(bundleId);
      setData(res);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to recompute item analysis.');
    } finally {
      setRefreshing(false);
    }
  }, [bundleId]);

  const items = data?.items ?? [];

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminRouteSectionHeader
          eyebrow="Mock bundle"
          title="Item analysis"
          description="Per-item difficulty and distractor diagnostics aggregated from submitted attempts."
          actions={
            <div className="flex items-center gap-2">
              <Link href="/admin/content/mocks" className="inline-flex items-center gap-1 text-sm text-muted hover:text-primary">
                <ArrowLeft className="h-3.5 w-3.5" /> Back
              </Link>
              <Button
                type="button"
                variant="primary"
                onClick={recompute}
                disabled={loading || refreshing}
              >
                <RefreshCcw className={`mr-1 h-4 w-4 ${refreshing ? 'animate-spin' : ''}`} />
                {refreshing ? 'Recomputing…' : 'Recompute'}
              </Button>
            </div>
          }
        />

        {error ? <InlineAlert variant="error" className="mb-4">{error}</InlineAlert> : null}

        {loading ? (
          <div className="space-y-3">
            <Skeleton className="h-12 w-full rounded-xl" />
            <Skeleton className="h-12 w-full rounded-xl" />
            <Skeleton className="h-12 w-full rounded-xl" />
          </div>
        ) : items.length === 0 ? (
          <EmptyState
            title="No item analysis yet"
            description="Click Recompute once attempts have been submitted on this bundle’s reading paper."
          />
        ) : (
          <div className="overflow-x-auto rounded-xl border border-border bg-surface">
            <table className="min-w-full divide-y divide-border text-sm">
              <thead className="bg-background-light">
                <tr className="text-left text-muted">
                  <th className="px-3 py-2 font-medium">Item</th>
                  <th className="px-3 py-2 font-medium">Subtest</th>
                  <th className="px-3 py-2 font-medium">N</th>
                  <th className="px-3 py-2 font-medium">% correct</th>
                  <th className="px-3 py-2 font-medium">Flag</th>
                  <th className="px-3 py-2 font-medium">Top distractors</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {items.map((row) => {
                  const pct = Math.round(row.difficulty * 100);
                  let distractors: Array<[string, number]> = [];
                  try {
                    const obj = JSON.parse(row.distractor) as Record<string, number>;
                    distractors = Object.entries(obj).sort((a, b) => b[1] - a[1]).slice(0, 3);
                  } catch {
                    /* ignore malformed JSON */
                  }
                  return (
                    <tr key={row.id} className="text-navy">
                      <td className="px-3 py-2 font-medium">{row.label ?? row.id}</td>
                      <td className="px-3 py-2 capitalize">{row.subtest}</td>
                      <td className="px-3 py-2 tabular-nums">{row.totalAttempts}</td>
                      <td className="px-3 py-2 tabular-nums">{pct}%</td>
                      <td className="px-3 py-2">
                        {row.flag === 'too_easy' ? <Badge variant="warning">Too easy</Badge>
                          : row.flag === 'too_hard' ? <Badge variant="danger">Too hard</Badge>
                          : row.flag === 'tempting_distractor' ? <Badge variant="warning">Tempting distractor</Badge>
                          : <span className="text-muted">—</span>}
                      </td>
                      <td className="px-3 py-2 text-xs text-muted">
                        {distractors.length === 0 ? '—' : distractors.map(([k, v]) => `${k}: ${v}`).join(' · ')}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}

        {data?.generatedAt ? (
          <p className="mt-3 text-xs text-muted">Last computed {new Date(data.generatedAt).toLocaleString()}.</p>
        ) : null}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
