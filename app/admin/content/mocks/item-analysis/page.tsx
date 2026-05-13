'use client';

import { useCallback, useEffect, useState } from 'react';
import { BarChart3, RefreshCw } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchAdminMockItemAnalysis } from '@/lib/api';

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

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminRouteSectionHeader
          eyebrow="Mocks V2 QA"
          title="Item analysis dashboard"
          description="Review p-values, sample sizes, and distractor flags before publishing or retiring mock content."
          icon={BarChart3}
        />

        <div className="mb-6 grid gap-3 rounded-2xl border border-border bg-background-light p-4 md:grid-cols-[1fr_1fr_auto]">
          <Input label="Bundle ID filter" value={bundleId} onChange={(event) => setBundleId(event.target.value)} placeholder="mock-bundle-..." />
          <Input label="Paper ID filter" value={paperId} onChange={(event) => setPaperId(event.target.value)} placeholder="content-paper-..." />
          <Button className="self-end" onClick={() => void load({ bundleId, paperId })}>
            <RefreshCw className="mr-2 h-4 w-4" /> Refresh
          </Button>
        </div>

        {generatedAt ? <p className="mb-4 text-xs text-muted">Last generated: {new Date(generatedAt).toLocaleString()}</p> : null}
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {loading ? (
          <div className="space-y-3">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-2xl" />)}</div>
        ) : (
          <div className="overflow-hidden rounded-2xl border border-border bg-surface">
            <table className="w-full text-left text-sm">
              <thead className="bg-background-light text-xs uppercase tracking-widest text-muted">
                <tr>
                  <th className="px-4 py-3">Item</th>
                  <th className="px-4 py-3">Subtest</th>
                  <th className="px-4 py-3">N</th>
                  <th className="px-4 py-3">Correct</th>
                  <th className="px-4 py-3">Difficulty</th>
                  <th className="px-4 py-3">Discrimination</th>
                  <th className="px-4 py-3">Flag</th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => (
                  <tr key={`${item.bundleId ?? ''}-${item.id}`} className="border-t border-border">
                    <td className="px-4 py-3 font-semibold text-navy">{item.label ?? item.id}</td>
                    <td className="px-4 py-3 text-muted">{item.subtest}</td>
                    <td className="px-4 py-3 text-muted">{item.totalAttempts}</td>
                    <td className="px-4 py-3 text-muted">{item.correctCount}</td>
                    <td className="px-4 py-3 text-muted">{Math.round(item.difficulty * 100)}%</td>
                    <td className="px-4 py-3 text-muted">{typeof item.discriminationIndex === 'number' ? item.discriminationIndex.toFixed(2) : '—'}</td>
                    <td className="px-4 py-3">{item.flag ? <Badge variant="warning">{item.flag}</Badge> : <Badge variant="success">ok</Badge>}</td>
                  </tr>
                ))}
                {items.length === 0 ? (
                  <tr><td colSpan={7} className="px-4 py-8 text-center text-muted">No item-analysis rows yet. Recompute from a bundle dashboard after attempts exist.</td></tr>
                ) : null}
              </tbody>
            </table>
          </div>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
