'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { Radio, ChevronRight } from 'lucide-react';
import {
  AdminRouteWorkspace,
  AdminRoutePanel,
  AdminRouteSectionHeader,
} from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { EmptyState } from '@/components/ui/empty-error';
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
      'muted';
    return <Badge variant={variant} size="sm">{s}</Badge>;
  }

  return (
    <>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Operations"
            title="AI Conversation — Sessions"
            description="Operational view of conversation sessions. Filter by user, state, or task type. Click a row to see full transcript + evaluation."
            icon={Radio}
            actions={
              <Button variant="secondary" onClick={() => router.push('/admin/content/conversation')}>
                Back to Scenarios
              </Button>
            }
          />

          <div className="mb-4 flex flex-wrap items-end gap-3">
            <Input label="User ID" value={userId} onChange={(e) => { setUserId(e.target.value); setPage(1); }} placeholder="learner_..." className="min-w-[200px]" />
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">State</label>
              <select value={state} onChange={(e) => { setState(e.target.value); setPage(1); }}
                className="rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="">All</option>
                <option value="preparing">preparing</option>
                <option value="active">active</option>
                <option value="evaluating">evaluating</option>
                <option value="evaluated">evaluated</option>
                <option value="failed">failed</option>
                <option value="abandoned">abandoned</option>
              </select>
            </div>
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Task Type</label>
              <select value={taskTypeCode} onChange={(e) => { setTaskTypeCode(e.target.value); setPage(1); }}
                className="rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="">All</option>
                <option value="oet-roleplay">oet-roleplay</option>
                <option value="oet-handover">oet-handover</option>
              </select>
            </div>
            <a
              className="rounded-lg border border-border bg-surface px-3 py-2 text-xs font-medium text-navy hover:border-border-hover"
              href={`${process.env.NEXT_PUBLIC_API_BASE_URL ?? ''}/v1/admin/conversation/evaluations.csv`}
              target="_blank"
              rel="noreferrer"
            >
              Export CSV
            </a>
          </div>

          {loading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (<Skeleton key={i} className="h-14 rounded-2xl" />))}
            </div>
          ) : rows.length === 0 ? (
            <EmptyState icon={<Radio className="h-6 w-6" />} title="No sessions match" description="Adjust filters or wait for learners to run sessions." />
          ) : (
            <div className="space-y-2">
              {rows.map((row) => (
                <Link key={row.id} href={`/admin/content/conversation/sessions/${row.id}`}
                  className="flex items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4 hover:border-border-hover">
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="truncate font-mono text-xs text-navy">{row.id}</span>
                      {stateBadge(row.state)}
                      <Badge variant="default" size="sm">{row.taskTypeCode}</Badge>
                      <Badge variant="default" size="sm">{row.profession}</Badge>
                      {row.lastErrorCode && (<Badge variant="danger" size="sm">error: {row.lastErrorCode}</Badge>)}
                    </div>
                    <div className="mt-1 text-xs text-muted">
                      user: <span className="font-mono">{row.userId}</span> · turns: {row.turnCount} · duration: {row.durationSeconds}s · created: {new Date(row.createdAt).toLocaleString()}
                    </div>
                  </div>
                  <ChevronRight className="h-4 w-4 text-muted" />
                </Link>
              ))}
            </div>
          )}

          {!loading && (
            <div className="mt-4">
              <Pagination
                page={page}
                pageSize={pageSize}
                total={total}
                onPageChange={setPage}
                onPageSizeChange={setPageSize}
                itemLabel="session"
                itemLabelPlural="sessions"
              />
            </div>
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </>
  );
}
