'use client';

/**
 * Generic "republish all drafts" page (SUBAGENT_E).
 *
 * Generalised replacement for `scripts/admin/republish-listening-drafts.mjs`:
 * pick a subtest, see every Draft paper for that subtest, multi-select, then
 * bulk-publish via /v1/admin/papers/bulk-publish (warnings only — backend
 * uses the soft publish gate so warnings never block).
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
import { BulkRunner } from '@/components/admin/bulk-runner/BulkRunner';
import {
  adminBulkListPapers,
  adminPublishPaperWithWarnings,
  type AdminBulkPaperRow,
} from '@/lib/api';
import type { BulkSubtest } from '@/lib/types/admin/bulk-ops';

const SUBTESTS: BulkSubtest[] = ['listening', 'reading', 'writing', 'speaking'];

export default function RepublishDraftsPage() {
  const [subtest, setSubtest] = useState<BulkSubtest>('listening');
  const [loading, setLoading] = useState(false);
  const [rows, setRows] = useState<AdminBulkPaperRow[]>([]);
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await adminBulkListPapers({ subtest, status: 'Draft', pageSize: 200 });
      setRows(Array.isArray(data) ? data : []);
      setSelected(new Set());
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load draft papers.');
      setRows([]);
    } finally {
      setLoading(false);
    }
  }, [subtest]);

  useEffect(() => { void refresh(); }, [refresh]);

  const allSelected = rows.length > 0 && selected.size === rows.length;
  const toggleAll = () => {
    setSelected(allSelected ? new Set() : new Set(rows.map(r => r.id)));
  };
  const toggleOne = (id: string) => {
    setSelected(prev => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id); else next.add(id);
      return next;
    });
  };

  const itemsToRun = useMemo(() => rows.filter(r => selected.has(r.id)), [rows, selected]);

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Republish draft papers"
        description="Bulk-publish Draft papers for a chosen subtest. Backend uses the soft publish gate — warnings never block."
      />

      {toast && (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      )}

      <AdminRoutePanel>
        <div className="flex flex-wrap items-center gap-2">
          {SUBTESTS.map(s => (
            <Button
              key={s}
              type="button"
              size="sm"
              variant={subtest === s ? 'primary' : 'outline'}
              onClick={() => setSubtest(s)}
            >
              {s.charAt(0).toUpperCase() + s.slice(1)}
            </Button>
          ))}
          <div className="ml-auto">
            <Button type="button" size="sm" variant="ghost" onClick={() => void refresh()}>
              <RefreshCw className="mr-1 h-4 w-4" /> Refresh
            </Button>
          </div>
        </div>

        {error && <InlineAlert variant="error" className="mt-3">{error}</InlineAlert>}

        {loading ? (
          <div className="mt-4 space-y-2">
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
            <Skeleton className="h-8 w-full" />
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            className="mt-4"
            title={`No Draft papers for ${subtest}.`}
            description="All papers in this subtest are already Published or Archived."
          />
        ) : (
          <div className="mt-4 overflow-x-auto rounded-lg border border-slate-200 dark:border-slate-700">
            <table className="w-full text-sm">
              <thead className="bg-slate-50 text-left text-xs uppercase text-slate-600 dark:bg-slate-900/50 dark:text-slate-400">
                <tr>
                  <th className="w-10 px-3 py-2">
                    <input
                      type="checkbox"
                      aria-label="Select all"
                      checked={allSelected}
                      onChange={toggleAll}
                    />
                  </th>
                  <th className="px-3 py-2">Title</th>
                  <th className="px-3 py-2">Profession</th>
                  <th className="px-3 py-2">Updated</th>
                </tr>
              </thead>
              <tbody>
                {rows.map(r => (
                  <tr key={r.id} className="border-t border-slate-100 dark:border-slate-800">
                    <td className="px-3 py-2">
                      <input
                        type="checkbox"
                        aria-label={`Select ${r.title}`}
                        checked={selected.has(r.id)}
                        onChange={() => toggleOne(r.id)}
                      />
                    </td>
                    <td className="px-3 py-2">
                      <div className="font-medium text-slate-900 dark:text-slate-100">{r.title}</div>
                      <div className="text-xs text-slate-500">{r.id}</div>
                    </td>
                    <td className="px-3 py-2">
                      {r.appliesToAllProfessions
                        ? <Badge variant="info">All</Badge>
                        : (r.professionId ?? '—')}
                    </td>
                    <td className="px-3 py-2 text-xs text-slate-500">
                      {r.updatedAt ? new Date(r.updatedAt).toLocaleString() : '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </AdminRoutePanel>

      <AdminRoutePanel>
        <AdminRouteSectionHeader
          title="Bulk publish"
          description={`Selected: ${selected.size}/${rows.length}`}
        />
        <BulkRunner
          items={itemsToRun}
          getKey={(r) => r.id}
          startLabel="Publish selected"
          renderRow={(r) => (
            <div>
              <div className="font-medium">{r.title}</div>
              <div className="text-xs text-slate-500">{r.subtestCode} · {r.id}</div>
            </div>
          )}
          run={async (r) => {
            try {
              const res = await adminPublishPaperWithWarnings(r.id);
              return {
                ok: true,
                warnings: res.warnings ?? [],
                detail: res.status ? `status=${res.status}` : undefined,
              };
            } catch (e) {
              return { ok: false, error: e instanceof Error ? e.message : String(e) };
            }
          }}
          onFinished={(summary) => {
            setToast({
              variant: summary.failed > 0 ? 'error' : 'success',
              message: `Done — ${summary.ok} ok, ${summary.failed} failed, ${summary.warnings} with warnings.`,
            });
            // Refresh the draft list so successfully-published rows drop out.
            void refresh();
          }}
        />
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
