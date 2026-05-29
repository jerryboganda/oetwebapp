'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Radio, ChevronRight } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Toast } from '@/components/ui/alert';
import { Pagination } from '@/components/ui/pagination';
import { fetchAdminConversationSessions } from '@/lib/api';

type SessionRow = {
  id: string;
  userId: string;
  taskTypeCode: string;
  profession: string;
  state: string;
  turnCount: number;
  durationSeconds: number;
  templateId: string | null;
  lastErrorCode: string | null;
  createdAt: string;
  startedAt: string | null;
  completedAt: string | null;
};


export default function AdminConversationSessionsPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<SessionRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
  const [userId, setUserId] = useState('');
  const [state, setState] = useState('');
  const [taskTypeCode, setTaskTypeCode] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = (await fetchAdminConversationSessions({
        userId: userId || undefined,
        state: state || undefined,
        taskTypeCode: taskTypeCode || undefined,
        page,
        pageSize,
      })) as { total: number; items: SessionRow[] };
      setRows(res.items ?? []);
      setTotal(res.total ?? 0);
    } catch {
      setToast({ variant: 'error', message: 'Failed to load sessions.' });
    } finally {
      setLoading(false);
    }
  }, [userId, state, taskTypeCode, page, pageSize]);

  useEffect(() => {
    const t = setTimeout(load, 250);
    return () => clearTimeout(t);
  }, [load]);


  function stateBadge(s: string) {
    const variant =
      s === 'evaluated' ? 'success' :
      s === 'evaluating' ? 'warning' :
      s === 'active' ? 'info' :
      s === 'failed' ? 'danger' :
      'secondary';
    return <Badge variant={variant as 'success' | 'warning' | 'info' | 'danger' | 'secondary'} size="sm">{s}</Badge>;
  }

  const filters = (
    <div className="flex flex-wrap items-end gap-3">
      <Input
        label="User ID"
        value={userId}
        onChange={(e) => { setUserId(e.target.value); setPage(1); }}
        placeholder="learner_..."
        wrapperClassName="min-w-[200px]"
      />
      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--admin-fg-strong)]">State</label>
        <select
          value={state}
          onChange={(e) => { setState(e.target.value); setPage(1); }}
          className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
        >
          <option value="">All</option>
          <option value="preparing">preparing</option>
          <option value="active">active</option>
          <option value="evaluating">evaluating</option>
          <option value="evaluated">evaluated</option>
          <option value="failed">failed</option>
          <option value="abandoned">abandoned</option>
        </select>
      </div>
      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--admin-fg-strong)]">Task Type</label>
        <select
          value={taskTypeCode}
          onChange={(e) => { setTaskTypeCode(e.target.value); setPage(1); }}
          className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
        >
          <option value="">All</option>
          <option value="oet-roleplay">oet-roleplay</option>
          <option value="oet-handover">oet-handover</option>
        </select>
      </div>
      <a
        className="h-10 inline-flex items-center rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-xs font-medium text-[var(--admin-fg-strong)] hover:border-[var(--admin-border-strong)]"
        href={`${process.env.NEXT_PUBLIC_API_BASE_URL ?? ''}/v1/admin/conversation/evaluations.csv`}
        target="_blank"
        rel="noreferrer"
      >
        Export CSV
      </a>
    </div>
  );

  return (
    <>
      <AdminCatalogLayout
        eyebrow="Operations"
        title="AI Conversation: Sessions"
        description="Operational view of conversation sessions. Filter by user, state, or task type. Click a row to see full transcript + evaluation."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Conversation', href: '/admin/content/conversation' },
          { label: 'Sessions' },
        ]}
        actions={
          <Button variant="secondary" onClick={() => router.push('/admin/content/conversation')}>
            Back to Scenarios
          </Button>
        }
        filters={filters}
        hideViewModeToggle
        pagination={
          !loading ? (
            <Pagination
              page={page}
              pageSize={pageSize}
              total={total}
              onPageChange={setPage}
              onPageSizeChange={setPageSize}
              itemLabel="session"
              itemLabelPlural="sessions"
            />
          ) : null
        }
        itemsClassName="flex flex-col gap-2"
      >
        {loading ? (
          <div className="space-y-3">
            {Array.from({ length: 5 }).map((_, i) => (<Skeleton key={i} className="h-14 rounded-admin-lg" />))}
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            illustration={<Radio />}
            title="No sessions match"
            description="Adjust filters or wait for learners to run sessions."
          />
        ) : (
          rows.map((row) => (
            <Card key={row.id} interactive asChild>
              <Link href={`/admin/content/conversation/sessions/${row.id}`}>
                <CardContent className="flex items-center justify-between gap-3 p-4 pt-4">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="truncate font-mono text-xs text-admin-fg-strong">{row.id}</span>
                      {stateBadge(row.state)}
                      <Badge variant="default" size="sm">{row.taskTypeCode}</Badge>
                      <Badge variant="default" size="sm">{row.profession}</Badge>
                      {row.lastErrorCode && (<Badge variant="danger" size="sm">error: {row.lastErrorCode}</Badge>)}
                    </div>
                    <div className="mt-1 text-xs text-admin-fg-muted">
                      user: <span className="font-mono">{row.userId}</span> · turns: {row.turnCount} · duration: {row.durationSeconds}s · created: {new Date(row.createdAt).toLocaleString()}
                    </div>
                  </div>
                  <ChevronRight className="h-4 w-4 text-admin-fg-muted" />
                </CardContent>
              </Link>
            </Card>
          ))
        )}
      </AdminCatalogLayout>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </>
  );
}
