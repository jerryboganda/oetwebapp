'use client';

import { useEffect, useState } from 'react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
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
  const [page] = useState(1);
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
    <>
      <AdminTableLayout
        title="Deduplication Review"
        description="Scan for duplicate content and designate canonical versions."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Deduplication' },
        ]}
        actions={
          <Button variant="primary" size="sm" onClick={handleScan} disabled={scanning} loading={scanning} startIcon={!scanning ? <RefreshCw className="w-4 h-4" /> : undefined}>
            {scanning ? 'Scanning…' : 'Run Dedup Scan'}
          </Button>
        }
      >
        <div className="p-4 sm:p-5">
          <div className="flex items-center justify-end mb-4">
            <span className="text-xs text-admin-fg-muted">{total} duplicate group{total !== 1 ? 's' : ''}</span>
          </div>

          <AsyncStateWrapper status={pageStatus} errorMessage="Failed to load duplicate groups.">
            {pageStatus === 'empty' ? (
              <EmptyState icon={<Copy className="w-8 h-8 text-admin-fg-muted" />} title="No duplicates found" description="Run a scan to detect duplicate content items." />
            ) : (
              <div className="space-y-4">
                {groups.map((group) => (
                  <Card key={group.duplicateGroupId}>
                    <CardHeader>
                      <CardTitle className="flex items-center gap-2 text-base">
                        <Copy className="w-4 h-4 text-admin-fg-muted" />
                        <span>Group: {group.duplicateGroupId}</span>
                        <Badge variant="default">{group.count} items</Badge>
                      </CardTitle>
                    </CardHeader>
                    <CardContent>
                      <div className="space-y-2">
                        {group.items.map((item) => (
                          <div key={item.id} className="flex items-center justify-between rounded-admin border border-admin-border p-2 text-sm">
                            <div className="flex-1">
                              <span className="font-medium text-admin-fg-strong">{item.title}</span>
                              <div className="flex gap-2 mt-0.5 text-xs text-admin-fg-muted">
                                <span>{item.subtestCode}</span>
                                <span>·</span>
                                <span>{item.sourceProvenance}</span>
                                <span>·</span>
                                <span>Q: {item.qualityScore}</span>
                                <span>·</span>
                                <Badge variant="default" size="sm">{item.status}</Badge>
                              </div>
                            </div>
                            <Button
                              variant="outline"
                              size="sm"
                              onClick={() => handleDesignate(group.duplicateGroupId, item.id)}
                              startIcon={<CheckCircle2 className="w-3 h-3" />}
                            >
                              Make Canonical
                            </Button>
                          </div>
                        ))}
                      </div>
                    </CardContent>
                  </Card>
                ))}
              </div>
            )}
          </AsyncStateWrapper>
        </div>
      </AdminTableLayout>

      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}
    </>
  );
}
