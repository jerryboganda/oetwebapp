'use client';

import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { MockItemAnalysisActions } from '@/components/domain/admin/MockItemAnalysisActions';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchAdminMockItemAnalysis, type MockItemRetireResponse } from '@/lib/api';

/**
 * Mocks Module Phase 6 — admin item-analysis dashboard row shape. Includes
 * the new retire-tracking columns surfaced by the
 * `/v1/admin/mocks/item-analysis` projection so the per-row action UI can
 * disable + tombstone already-retired items.
 */
type ItemAnalysisRow = {
  id: string;
  bundleId?: string;
  subtest: string;
  label?: string | null;
  totalAttempts: number;
  correctCount: number;
  difficulty: number;
  discriminationIndex?: number | null;
  flag?: string | null;
  retiredAt?: string | null;
  retiredReason?: string | null;
  retiredByAdminId?: string | null;
};

export default function AdminMockItemAnalysisPage() {
  const [bundleId, setBundleId] = useState('');
  const [paperId, setPaperId] = useState('');
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [items, setItems] = useState<ItemAnalysisRow[]>([]);
  const [generatedAt, setGeneratedAt] = useState<string | null>(null);

  const load = useCallback(async (filters?: { bundleId?: string; paperId?: string }) => {
    setLoading(true);
    setError('');
    try {
      const response = await fetchAdminMockItemAnalysis({
        bundleId: filters?.bundleId?.trim() || undefined,
        paperId: filters?.paperId?.trim() || undefined,
      }) as {
        items?: ItemAnalysisRow[];
        generatedAt?: string | null;
      };
      setItems(response.items ?? []);
      setGeneratedAt(response.generatedAt ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load item analysis.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load({}); }, [load]);

  /**
   * Mocks Module Phase 6 — when a retire transition succeeds, patch the row
   * in-place so the UI shows the tombstone without a full reload. We still
   * surface `Refresh` for a server-truth refetch if the admin wants it.
   */
  const handleRetired = useCallback((rowId: string, response: MockItemRetireResponse) => {
    setItems((prev) => prev.map((row) => (
      row.id === rowId
        ? {
            ...row,
            retiredAt: response.retiredAt ?? new Date().toISOString(),
            retiredReason: response.reason ?? null,
            retiredByAdminId: response.retiredByAdminId ?? null,
          }
        : row
    )));
  }, []);

  return (
    <AdminOperationsLayout
      eyebrow="Mocks V2 QA"
      title="Item analysis dashboard"
      description="Review p-values, sample sizes, and distractor flags before publishing or retiring mock content."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Mocks', href: '/admin/content/mocks' },
        { label: 'Item analysis' },
      ]}
    >
      <Card>
        <CardContent className="p-5 space-y-4">
          <div className="grid gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-4 md:grid-cols-[1fr_1fr_auto]">
            <Input label="Bundle ID filter" value={bundleId} onChange={(event) => setBundleId(event.target.value)} placeholder="mock-bundle-..." />
            <Input label="Paper ID filter" value={paperId} onChange={(event) => setPaperId(event.target.value)} placeholder="content-paper-..." />
            <Button className="self-end" onClick={() => void load({ bundleId, paperId })}>
              <RefreshCw className="mr-2 h-4 w-4" /> Refresh
            </Button>
          </div>

          {generatedAt ? <p className="text-xs text-admin-fg-muted">Last generated: {new Date(generatedAt).toLocaleString()}</p> : null}
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {loading ? (
            <div className="space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-admin" />)}</div>
          ) : (
            <div className="overflow-hidden rounded-admin border border-admin-border bg-admin-bg-surface">
              <table className="w-full text-left text-sm">
                <thead className="bg-admin-bg-subtle text-xs uppercase tracking-widest text-admin-fg-muted">
                  <tr>
                    <th scope="col" className="px-4 py-3">Item</th>
                    <th scope="col" className="px-4 py-3">Subtest</th>
                    <th scope="col" className="px-4 py-3">N</th>
                    <th scope="col" className="px-4 py-3">Correct</th>
                    <th scope="col" className="px-4 py-3">Difficulty</th>
                    <th scope="col" className="px-4 py-3">Discrimination</th>
                    <th scope="col" className="px-4 py-3">Flag</th>
                    <th scope="col" className="px-4 py-3">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  {items.map((item) => (
                    <tr key={`${item.bundleId ?? ''}-${item.id}`} className="border-t border-admin-border">
                      <td className="px-4 py-3 font-semibold text-admin-fg-strong">{item.label ?? item.id}</td>
                      <td className="px-4 py-3 text-admin-fg-muted">{item.subtest}</td>
                      <td className="px-4 py-3 text-admin-fg-muted">{item.totalAttempts}</td>
                      <td className="px-4 py-3 text-admin-fg-muted">{item.correctCount}</td>
                      <td className="px-4 py-3 text-admin-fg-muted">{Math.round(item.difficulty * 100)}%</td>
                      <td className="px-4 py-3 text-admin-fg-muted">{typeof item.discriminationIndex === 'number' ? item.discriminationIndex.toFixed(2) : '-'}</td>
                      <td className="px-4 py-3">{item.flag ? <Badge variant="warning">{item.flag}</Badge> : <Badge variant="success">ok</Badge>}</td>
                      <td className="px-4 py-3">
                        <MockItemAnalysisActions
                          itemId={item.id}
                          itemLabel={item.label}
                          bundleId={item.bundleId ?? null}
                          retiredAt={item.retiredAt ?? null}
                          retiredReason={item.retiredReason ?? null}
                          retiredByAdminId={item.retiredByAdminId ?? null}
                          onRetired={(response) => handleRetired(item.id, response)}
                        />
                      </td>
                    </tr>
                  ))}
                  {items.length === 0 ? (
                    <tr><td colSpan={8} className="px-4 py-8 text-center text-admin-fg-muted">No item-analysis rows yet. Recompute from a bundle dashboard after attempts exist.</td></tr>
                  ) : null}
                </tbody>
              </table>
            </div>
          )}
        </CardContent>
      </Card>
    </AdminOperationsLayout>
  );
}
