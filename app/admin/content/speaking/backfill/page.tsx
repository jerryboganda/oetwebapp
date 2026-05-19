'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { AlertTriangle, ArrowLeft, Wand2 } from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { adminListSpeakingPapers, type AdminSpeakingPaperRow } from '@/lib/api';
import { getContentPaper, type ContentPaperDto, type PaperAssetRole } from '@/lib/content-upload-api';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

const ROLES_TO_CHECK: PaperAssetRole[] = ['RoleCard', 'AssessmentCriteria', 'WarmUpQuestions'];

interface Row extends AdminSpeakingPaperRow {
  assets?: { role: PaperAssetRole }[];
}

export default function SpeakingBackfillPage() {
  const [rows, setRows] = useState<Row[]>([]);
  const [loading, setLoading] = useState(true);
  const [toast, setToast] = useState<ToastState>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = await adminListSpeakingPapers({ pageSize: 200 });
      // Fetch detail for asset status — bounded to first 50 to avoid N+1 blowup.
      const subset = res.items.slice(0, 50);
      const detailed: Row[] = await Promise.all(
        subset.map(async (r) => {
          try {
            const detail: ContentPaperDto = await getContentPaper(r.id);
            return { ...r, assets: detail.assets ?? [] };
          } catch {
            return r;
          }
        }),
      );
      const remainder = res.items.slice(50).map<Row>((r) => ({ ...r, assets: undefined }));
      setRows([...detailed, ...remainder]);
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load papers.' });
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  const summary = useMemo(() => {
    const total = rows.length;
    const missing: Record<PaperAssetRole, number> = {
      RoleCard: 0,
      AssessmentCriteria: 0,
      WarmUpQuestions: 0,
    } as Record<PaperAssetRole, number>;
    for (const r of rows) {
      if (!r.assets) continue;
      for (const role of ROLES_TO_CHECK) {
        if (!r.assets.some((a) => a.role === role)) missing[role]++;
      }
    }
    return { total, missing };
  }, [rows]);

  return (
    <AdminRouteWorkspace role="main" aria-label="Speaking asset backfill">
      <AdminRouteHero
        eyebrow="Tools"
        icon={Wand2}
        accent="navy"
        title="Speaking asset backfill"
        description="Audit speaking papers for missing role cards, assessment criteria, and warm-up questions."
        aside={(
          <Button variant="ghost" asChild>
            <Link href="/admin/content/speaking">
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to speaking
            </Link>
          </Button>
        )}
      />

      <InlineAlert variant="warning" title="Backend endpoint TBD">
        Bulk asset generation is currently CLI-only. Run{' '}
        <code className="rounded bg-background-light px-1.5 py-0.5 font-mono text-xs">
          node scripts/admin/generate-speaking-assets.mjs --limit 1
        </code>{' '}
        on a host with backend credentials. A future{' '}
        <code className="rounded bg-background-light px-1.5 py-0.5 font-mono text-xs">
          POST /v1/admin/papers/{'{id}'}/speaking-backfill
        </code>{' '}
        endpoint will let this page trigger generation directly. Until then, use the per-paper workspace to upload assets manually.
      </InlineAlert>

      <AdminRoutePanel
        title="Asset coverage (first 50 papers)"
        description="Detailed asset detection for the first 50 papers. Click into a paper to upload missing assets."
      >
        {loading ? (
          <div className="space-y-2">
            {Array.from({ length: 6 }).map((_, i) => (
              <Skeleton key={i} className="h-10 rounded-xl" />
            ))}
          </div>
        ) : (
          <>
            <div className="mb-3 flex flex-wrap gap-3 text-xs">
              <span>Total: {summary.total}</span>
              {ROLES_TO_CHECK.map((role) => (
                <span key={role}>
                  Missing {role}: <Badge variant={summary.missing[role] > 0 ? 'warning' : 'success'}>{summary.missing[role]}</Badge>
                </span>
              ))}
            </div>
            <Card className="overflow-hidden">
              <table className="min-w-full text-sm">
                <thead className="bg-background-light text-left text-xs uppercase tracking-[0.15em] text-muted">
                  <tr>
                    <th className="px-4 py-2">Title</th>
                    <th className="px-4 py-2">Status</th>
                    {ROLES_TO_CHECK.map((role) => (
                      <th key={role} className="px-4 py-2">{role}</th>
                    ))}
                    <th className="px-4 py-2 text-right">Open</th>
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr key={r.id} className="border-t border-border">
                      <td className="px-4 py-2">{r.title}</td>
                      <td className="px-4 py-2 text-xs">{r.status}</td>
                      {ROLES_TO_CHECK.map((role) => {
                        if (!r.assets) return (
                          <td key={role} className="px-4 py-2 text-xs text-muted">—</td>
                        );
                        const has = r.assets.some((a) => a.role === role);
                        return (
                          <td key={role} className="px-4 py-2">
                            <Badge variant={has ? 'success' : 'danger'}>{has ? '✓' : '✗'}</Badge>
                          </td>
                        );
                      })}
                      <td className="px-4 py-2 text-right">
                        <Button variant="ghost" size="sm" asChild>
                          <Link href={`/admin/content/speaking/${r.id}`}>Workspace</Link>
                        </Button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </Card>
          </>
        )}
      </AdminRoutePanel>

      <Card className="flex items-start gap-3 border-warning/40 bg-warning/5 p-4 text-sm">
        <AlertTriangle className="mt-0.5 h-4 w-4 text-warning" />
        <div>
          <div className="font-semibold">Why is this a stub?</div>
          <p className="text-xs text-muted">
            The Node CLI <code className="font-mono">generate-speaking-assets.mjs</code> calls the AI gateway directly from a privileged
            admin context with its own grounded prompts. Moving that to the backend will create a one-click action here. Until that
            endpoint ships, the safest path is to use the per-paper workspace and upload generated files manually.
          </p>
        </div>
      </Card>

      {toast && (
        <Toast variant="error" message={toast.message} onClose={() => setToast(null)} />
      )}
    </AdminRouteWorkspace>
  );
}
