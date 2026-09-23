'use client';

import { useEffect, useState } from 'react';
import { Search } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { getDocumentationEvidencePageData, type DocumentationEvidenceRow } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

export default function DocumentationEvidencePage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [retryNonce, setRetryNonce] = useState(0);
  const [searchQuery, setSearchQuery] = useState('');
  const [rows, setRows] = useState<DocumentationEvidenceRow[]>([]);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const items = await getDocumentationEvidencePageData({ q: searchQuery || undefined });
        if (cancelled) return;
        setRows(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Unable to load the evidence register.' });
        }
      }
    }
    const handle = window.setTimeout(load, 200);
    return () => {
      cancelled = true;
      window.clearTimeout(handle);
    };
  }, [searchQuery, retryNonce]);

  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Documentation Center', href: '/admin/documentation-center' },
    { label: 'Evidence Register' },
  ];

  const columns: Column<DocumentationEvidenceRow>[] = [
    { key: 'evidenceId', header: 'Evidence ID', render: (e) => <span className="font-mono text-xs font-semibold text-admin-fg-strong">{e.evidenceId}</span> },
    { key: 'moduleId', header: 'Module', render: (e) => <span className="text-admin-fg-muted">{e.moduleId}</span> },
    { key: 'evidenceType', header: 'Type', render: (e) => <Badge variant="secondary" size="sm">{e.evidenceType}</Badge> },
    { key: 'description', header: 'Description', render: (e) => <span className="text-admin-fg-strong">{e.description}</span> },
    { key: 'sourceReference', header: 'Source', render: (e) => <span className="font-mono text-xs text-admin-fg-muted">{e.sourceReference}</span> },
    {
      key: 'isInternalOnly',
      header: 'Visibility',
      render: (e) => (e.isInternalOnly ? <Badge variant="warning" size="sm">Internal only</Badge> : <Badge variant="success" size="sm">External-safe</Badge>),
    },
  ];

  return (
    <AdminTableLayout
      title="Evidence Register"
      description="Every EV-[MODULE]-[NUMBER] citation used across the Documentation Center, resolved to a real, checkable source."
      breadcrumbs={breadcrumbs}
    >
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <CardHeader className="flex-col items-start gap-1">
        <CardTitle>Search evidence</CardTitle>
        <CardDescription>Search by feature, model, date, provider, test, profession or evidence ID.</CardDescription>
      </CardHeader>

      <CardContent className="space-y-4 pt-0">
        <div className="max-w-md">
          <div className="relative">
            <Search className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" />
            <Input
              placeholder="Search evidence, module, or source"
              value={searchQuery}
              onChange={(event) => setSearchQuery(event.target.value)}
              className="pl-9"
            />
          </div>
        </div>

        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => setRetryNonce((n) => n + 1)}
          emptyContent={<EmptyState title="No evidence found" description="Adjust your search terms." />}
        >
          <DataTable aria-label="Evidence register" columns={columns} data={rows} keyExtractor={(e) => e.evidenceId} />
        </AsyncStateWrapper>
      </CardContent>
    </AdminTableLayout>
  );
}
