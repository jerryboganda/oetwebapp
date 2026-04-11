'use client';

import { useEffect, useState } from 'react';
import { AdminRouteSectionHeader, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { fetchAdminDedupGroups, adminDedupScan, adminDesignateCanonical } from '@/lib/api';
import type { DuplicateGroupSummary, PaginatedResponse } from '@/lib/types/content-hierarchy';
import { Copy, RefreshCw, CheckCircle2 } from 'lucide-react';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminDedupPage() {
  const { isAuthenticated } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [groups, setGroups] = useState<DuplicateGroupSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [scanning, setScanning] = useState(false);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [reloadNonce, setReloadNonce] = useState(0);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const raw = await fetchAdminDedupGroups(page, 20) as PaginatedResponse<DuplicateGroupSummary>;
        if (cancelled) return;
        setGroups(raw.items ?? []);
        setTotal(raw.total ?? 0);
        setPageStatus((raw.items?.length ?? 0) > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => { cancelled = true; };
  }, [page, reloadNonce]);

  async function handleScan() {
    setScanning(true);
    try {
      const result = await adminDedupScan() as { groupsFound: number; itemsTagged: number };
      setToast({ variant: 'success', message: `Scan complete: ${result.groupsFound} groups, ${result.itemsTagged} items tagged.` });
      setReloadNonce(n => n + 1);
    } catch {
      setToast({ variant: 'error', message: 'Dedup scan failed.' });
    } finally {
      setScanning(false);
    }
  }

  async function handleDesignate(groupId: string, itemId: string) {
    try {
      await adminDesignateCanonical(groupId, itemId);
      setToast({ variant: 'success', message: 'Canonical item designated.' });
      setReloadNonce(n => n + 1);
    } catch {
      setToast({ variant: 'error', message: 'Failed to designate canonical.' });
    }
  }

  if (!isAuthenticated) return null;

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        title="Deduplication Review"
        description="Scan for duplicate content and designate canonical versions."
      />

      <AdminRoutePanel title="Duplicate Groups">
        <div className="flex items-center justify-between mb-4">
          <Button variant="primary" size="sm" onClick={handleScan} disabled={scanning}>
            <RefreshCw className={`w-4 h-4 mr-1 ${scanning ? 'animate-spin' : ''}`} />
            {scanning ? 'Scanning…' : 'Run Dedup Scan'}
          </Button>
          <span className="text-xs text-muted">{total} duplicate group{total !== 1 ? 's' : ''}</span>
        </div>

        <AsyncStateWrapper status={pageStatus} errorMessage="Failed to load duplicate groups.">
          {pageStatus === 'empty' ? (
            <EmptyState icon={<Copy className="w-8 h-8 text-muted" />} title="No duplicates found" description="Run a scan to detect duplicate content items." />
          ) : (
            <div className="space-y-4">
              {groups.map((group) => (
                <div key={group.duplicateGroupId} className="rounded-lg border p-4">
                  <div className="flex items-center gap-2 mb-3">
                    <Copy className="w-4 h-4 text-muted" />
                    <span className="text-sm font-medium">Group: {group.duplicateGroupId}</span>
                    <Badge variant="muted">{group.count} items</Badge>
                  </div>
                  <div className="space-y-2">
                    {group.items.map((item) => (
                      <div key={item.id} className="flex items-center justify-between rounded-md border p-2 text-sm">
                        <div className="flex-1">
                          <span className="font-medium">{item.title}</span>
                          <div className="flex gap-2 mt-0.5 text-xs text-muted">
                            <span>{item.subtestCode}</span>
                            <span>·</span>
                            <span>{item.sourceProvenance}</span>
                            <span>·</span>
                            <span>Q: {item.qualityScore}</span>
                            <span>·</span>
                            <Badge variant="muted" className="text-[10px]">{item.status}</Badge>
                          </div>
                        </div>
                        <Button
                          variant="outline"
                          size="sm"
                          onClick={() => handleDesignate(group.duplicateGroupId, item.id)}
                        >
                          <CheckCircle2 className="w-3 h-3 mr-1" /> Make Canonical
                        </Button>
                      </div>
                    ))}
                  </div>
                </div>
              ))}
            </div>
          )}
        </AsyncStateWrapper>
      </AdminRoutePanel>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </AdminRouteWorkspace>
  );
}
