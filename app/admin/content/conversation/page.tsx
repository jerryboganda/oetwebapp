'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { MessageSquare, Plus, Archive, CheckCircle } from 'lucide-react';
import { AdminDashboardShell } from '@/components/layout';
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
import {
  fetchAdminConversationTemplates,
  publishAdminConversationTemplate,
  archiveAdminConversationTemplate,
} from '@/lib/api';

type TemplateRow = {
  id: string;
  title: string;
  taskTypeCode: string;
  professionId: string | null;
  difficulty: string;
  estimatedDurationSeconds: number;
  status: 'draft' | 'published' | 'archived';
  publishedAtUtc: string | null;
  createdAt: string;
  updatedAt: string;
};

const PAGE_SIZE = 25;

export default function AdminConversationTemplatesPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<TemplateRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [search, setSearch] = useState('');
  const [profession, setProfession] = useState('');
  const [status, setStatus] = useState('');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const res = (await fetchAdminConversationTemplates({
        profession: profession || undefined,
        status: status || undefined,
        search: search || undefined,
        page, pageSize: PAGE_SIZE,
      })) as { total: number; items: TemplateRow[] };
      setRows(res.items ?? []);
      setTotal(res.total ?? 0);
    } catch {
      setToast({ variant: 'error', message: 'Failed to load templates.' });
    } finally {
      setLoading(false);
    }
  }, [profession, status, search, page]);

  useEffect(() => {
    const t = setTimeout(load, 250);
    return () => clearTimeout(t);
  }, [load]);

  async function handlePublish(id: string) {
    try {
      await publishAdminConversationTemplate(id);
      setToast({ variant: 'success', message: 'Template published.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Publish failed.' });
    }
  }

  async function handleArchive(id: string) {
    try {
      await archiveAdminConversationTemplate(id);
      setToast({ variant: 'success', message: 'Template archived.' });
      await load();
    } catch {
      setToast({ variant: 'error', message: 'Archive failed.' });
    }
  }

  const totalPages = Math.max(1, Math.ceil(total / PAGE_SIZE));

  return (
    <AdminDashboardShell>
      <AdminRouteWorkspace>
        <AdminRoutePanel>
          <AdminRouteSectionHeader
            eyebrow="Content"
            title="AI Conversation Scenarios"
            description="Manage role-play and handover scenarios used by the learner AI Conversation module."
            icon={MessageSquare}
            actions={
              <div className="flex items-center gap-2">
                <Button variant="secondary" onClick={() => router.push('/admin/content/conversation/settings')}>
                  Settings
                </Button>
                <Button variant="secondary" onClick={() => router.push('/admin/content/conversation/sessions')}>
                  Sessions
                </Button>
                <Button variant="primary" onClick={() => router.push('/admin/content/conversation/new')}>
                  <Plus className="mr-1 h-4 w-4" /> New Scenario
                </Button>
              </div>
            }
          />

          <div className="mb-4 flex flex-wrap items-end gap-3">
            <Input label="Search" value={search}
              onChange={(e) => { setSearch(e.target.value); setPage(1); }}
              placeholder="Title or scenario body…" className="min-w-[200px]" />
            <Input label="Profession" value={profession}
              onChange={(e) => { setProfession(e.target.value); setPage(1); }}
              placeholder="medicine, nursing, pharmacy…" className="min-w-[180px]" />
            <div>
              <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-muted">Status</label>
              <select value={status} onChange={(e) => { setStatus(e.target.value); setPage(1); }}
                className="rounded-lg border border-border bg-surface px-3 py-2 text-sm">
                <option value="">All</option>
                <option value="draft">Draft</option>
                <option value="published">Published</option>
                <option value="archived">Archived</option>
              </select>
            </div>
          </div>

          {loading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (<Skeleton key={i} className="h-16 rounded-2xl" />))}
            </div>
          ) : rows.length === 0 ? (
            <EmptyState
              icon={<MessageSquare className="h-6 w-6" />}
              title="No conversation scenarios yet"
              description="Create your first scenario to make it available to learners."
              action={{
                label: 'New Scenario',
                onClick: () => router.push('/admin/content/conversation/new'),
              }}
            />
          ) : (
            <div className="space-y-2">
              {rows.map((row) => (
                <div key={row.id} className="flex items-center justify-between gap-3 rounded-2xl border border-border bg-surface p-4">
                  <div className="min-w-0">
                    <div className="flex flex-wrap items-center gap-2">
                      <Link href={`/admin/content/conversation/${row.id}`}
                        className="truncate text-sm font-semibold text-navy hover:text-primary">
                        {row.title}
                      </Link>
                      <Badge variant={row.status === 'published' ? 'success' : row.status === 'archived' ? 'muted' : 'info'} size="sm">
                        {row.status}
                      </Badge>
                      <Badge variant="default" size="sm">{row.taskTypeCode}</Badge>
                      {row.professionId && (<Badge variant="default" size="sm">{row.professionId}</Badge>)}
                      <Badge variant="default" size="sm">{row.difficulty}</Badge>
                    </div>
                    <div className="mt-1 text-xs text-muted">
                      {Math.round(row.estimatedDurationSeconds / 60)} min · updated {new Date(row.updatedAt).toLocaleDateString()}
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    {row.status === 'draft' && (
                      <Button variant="primary" onClick={() => handlePublish(row.id)}>
                        <CheckCircle className="mr-1 h-4 w-4" /> Publish
                      </Button>
                    )}
                    {row.status !== 'archived' && (
                      <Button variant="secondary" onClick={() => handleArchive(row.id)}>
                        <Archive className="mr-1 h-4 w-4" /> Archive
                      </Button>
                    )}
                  </div>
                </div>
              ))}
            </div>
          )}

          {!loading && total > PAGE_SIZE && (
            <div className="mt-4 flex items-center justify-between">
              <span className="text-xs text-muted">Page {page} of {totalPages} · {total} total</span>
              <div className="flex gap-2">
                <Button variant="secondary" onClick={() => setPage(Math.max(1, page - 1))} disabled={page === 1}>Previous</Button>
                <Button variant="secondary" onClick={() => setPage(Math.min(totalPages, page + 1))} disabled={page >= totalPages}>Next</Button>
              </div>
            </div>
          )}
        </AdminRoutePanel>
      </AdminRouteWorkspace>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </AdminDashboardShell>
  );
}
