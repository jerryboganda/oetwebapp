'use client';

import { useState, useEffect } from 'react';
import { useRouter, useParams } from 'next/navigation';
import { Card, CardContent } from '@/components/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Button } from '@/components/ui/button';
import { ArrowLeft, RotateCcw, History } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { EmptyState } from '@/components/ui/empty-error';
import { Toast } from '@/components/ui/alert';
import { mockContentRevisions, type AdminContentRevision } from '@/lib/mock-admin-data';
import { analytics } from '@/lib/analytics';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

export default function ContentRevisionsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const router = useRouter();
  const params = useParams();
  const id = params?.id as string;
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'empty'>('loading');
  const [revisions, setRevisions] = useState<AdminContentRevision[]>([]);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  useEffect(() => {
    const load = async () => {
      setPageStatus('loading');
      await new Promise(resolve => setTimeout(resolve, 300));
      const data = mockContentRevisions.filter(r => r.contentId === id || id);
      setRevisions(data);
      setPageStatus(data.length > 0 ? 'success' : 'empty');
    };
    load();
  }, [id]);

  if (!isAuthenticated || role !== 'admin') return null;

  const handleRestore = (revisionId: string) => {
    analytics.track('admin_revision_restored', { contentId: id, revisionId });
    setToast({ variant: 'success', message: `Revision ${revisionId} restored as new latest version.` });
  };

  const columns: Column<AdminContentRevision>[] = [
    { key: 'id', header: 'Version', render: (row) => <span className="font-mono font-bold text-navy">{row.id}</span> },
    { key: 'date', header: 'Date', render: (row) => <span className="text-muted">{new Date(row.date).toLocaleString()}</span> },
    { key: 'author', header: 'Author', render: (row) => <span>{row.author}</span> },
    { 
      key: 'state', 
      header: 'State', 
      render: (row) => {
        const variant = row.state === 'published' ? 'success' : row.state === 'archived' ? 'muted' : 'warning';
        return <Badge variant={variant as any} className="capitalize">{row.state}</Badge>;
      }
    },
    { key: 'note', header: 'Change Note', render: (row) => <span className="text-sm text-gray-600">{row.note}</span> },
    {
      key: 'actions',
      header: '',
      render: (row) => (
        <div className="flex justify-end gap-2">
          <Button variant="ghost" size="sm" className="text-primary hover:bg-primary/5">View Diff</Button>
          <Button variant="outline" size="sm" className="gap-1" onClick={() => handleRestore(row.id)}>
            <RotateCcw className="w-3.5 h-3.5" /> Restore
          </Button>
        </div>
      )
    }
  ];

  return (
    <div className="max-w-6xl mx-auto p-4 md:p-8 space-y-6" role="main" aria-label="Revision History">
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <div className="flex items-center gap-4">
        <Button variant="ghost" size="sm" onClick={() => router.back()} className="px-2">
          <ArrowLeft className="w-5 h-5" />
        </Button>
        <div>
          <h1 className="text-2xl font-bold text-navy tracking-tight">Revision History</h1>
          <p className="text-sm text-muted mt-1">Content ID: <span className="font-mono">{id}</span></p>
        </div>
      </div>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => window.location.reload()}
        emptyContent={
          <EmptyState
            icon={<History className="w-12 h-12 text-muted" />}
            title="No Revisions Yet"
            description="Revision history will appear after the first save or publish."
          />
        }
      >
        <Card padding="none" className="overflow-hidden mt-6">
          <CardContent className="p-0">
            <DataTable 
              data={revisions}
              columns={columns}
              keyExtractor={(item) => item.id}
            />
          </CardContent>
        </Card>
      </AsyncStateWrapper>
    </div>
  );
}
