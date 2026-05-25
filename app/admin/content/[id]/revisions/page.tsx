'use client';

import { useEffect, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowLeft, RotateCcw } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast } from '@/components/ui/alert';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge, statusToTone } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getAdminContentRevisionData } from '@/lib/admin';
import { restoreAdminContentRevision } from '@/lib/api';
import type { AdminRevisionRow } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminContentRevisionsPage() {
  const params = useParams<{ id: string }>();
  const contentId = params?.id ?? '';
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [revisions, setRevisions] = useState<AdminRevisionRow[]>([]);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const items = await getAdminContentRevisionData(contentId);
        if (cancelled) return;
        setRevisions(items);
        setPageStatus(items.length > 0 ? 'success' : 'empty');
      } catch (error) {
        console.error(error);
        if (!cancelled) {
          setPageStatus('error');
          setToast({ variant: 'error', message: 'Failed to load revisions.' });
        }
      }
    }

    load();
    return () => {
      cancelled = true;
    };
  }, [contentId]);

  async function handleRestore(revisionId: string) {
    try {
      await restoreAdminContentRevision(contentId, revisionId);
      setToast({ variant: 'success', message: 'Revision restored successfully.' });
      const items = await getAdminContentRevisionData(contentId);
      setRevisions(items);
    } catch (error) {
      console.error(error);
      setToast({ variant: 'error', message: 'Unable to restore that revision.' });
    }
  }

  const columns: Column<AdminRevisionRow>[] = [
    { key: 'id', header: 'Revision', render: (row) => <span className="font-mono text-xs">{row.id}</span> },
    { key: 'date', header: 'Date', render: (row) => <span>{new Date(row.date).toLocaleString()}</span> },
    { key: 'author', header: 'Author', render: (row) => <span>{row.author}</span> },
    {
      key: 'state',
      header: 'State',
      render: (row) => (
        <Badge variant={statusToTone(row.state) as any}>{row.state}</Badge>
      ),
    },
    { key: 'note', header: 'Change Note', render: (row) => <span className="text-admin-fg-muted">{row.note}</span> },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <Button variant="outline" size="sm" onClick={() => handleRestore(row.id)} startIcon={<RotateCcw className="h-4 w-4" />}>
          Restore
        </Button>
      ),
    },
  ];

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <>
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminTableLayout
        title="Revision History"
        description="Review the saved content history and restore a previous editorial state when needed."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Revisions' },
        ]}
        actions={
          <Button variant="outline" onClick={() => router.push(`/admin/content/${contentId}`)} startIcon={<ArrowLeft className="h-4 w-4" />}>
            Back to Content
          </Button>
        }
      >
        <AsyncStateWrapper
          status={pageStatus}
          onRetry={() => window.location.reload()}
          emptyContent={<EmptyState title="No revisions found" description="This content item has not accumulated revision history yet." />}
        >
          <div className="p-4 sm:p-5">
            <DataTable columns={columns} data={revisions} keyExtractor={(row) => row.id} />
          </div>
        </AsyncStateWrapper>
      </AdminTableLayout>
    </>
  );
}
