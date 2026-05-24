'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { MessageSquare, Plus, Archive, CheckCircle } from 'lucide-react';
import { AdminCatalogLayout } from '@/components/admin/layout/admin-catalog-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Toast } from '@/components/ui/alert';
import { Pagination } from '@/components/ui/pagination';
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


export default function AdminConversationTemplatesPage() {
  const router = useRouter();
  const [loading, setLoading] = useState(true);
  const [rows, setRows] = useState<TemplateRow[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [pageSize, setPageSize] = useState(25);
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
        page, pageSize,
      })) as { total: number; items: TemplateRow[] };
      setRows(res.items ?? []);
      setTotal(res.total ?? 0);
    } catch {
      setToast({ variant: 'error', message: 'Failed to load templates.' });
    } finally {
      setLoading(false);
    }
  }, [profession, status, search, page, pageSize]);

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

  const filters = (
    <div className="flex flex-wrap items-end gap-3">
      <Input
        label="Search"
        value={search}
        onChange={(e) => { setSearch(e.target.value); setPage(1); }}
        placeholder="Title or scenario body…"
        wrapperClassName="min-w-[200px]"
      />
      <Input
        label="Profession"
        value={profession}
        onChange={(e) => { setProfession(e.target.value); setPage(1); }}
        placeholder="medicine, nursing, pharmacy…"
        wrapperClassName="min-w-[180px]"
      />
      <div className="flex flex-col gap-1.5">
        <label className="text-sm font-medium text-[var(--admin-fg-strong)]">Status</label>
        <select
          value={status}
          onChange={(e) => { setStatus(e.target.value); setPage(1); }}
          className="h-10 rounded-[var(--admin-radius-lg)] border border-[var(--admin-border)] bg-[var(--admin-bg-surface)] px-3 text-sm text-[var(--admin-fg-default)]"
        >
          <option value="">All</option>
          <option value="draft">Draft</option>
          <option value="published">Published</option>
          <option value="archived">Archived</option>
        </select>
      </div>
    </div>
  );

  return (
    <>
      <AdminCatalogLayout
        eyebrow="Content"
        title="AI Conversation Scenarios"
        description="Manage role-play and handover scenarios used by the learner AI Conversation module."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Conversation' },
        ]}
        actions={
          <>
            <Button variant="secondary" onClick={() => router.push('/admin/content/conversation/settings')}>
              Settings
            </Button>
            <Button variant="secondary" onClick={() => router.push('/admin/content/conversation/sessions')}>
              Sessions
            </Button>
            <Button variant="primary" onClick={() => router.push('/admin/content/conversation/new')} startIcon={<Plus className="h-4 w-4" />}>
              New Scenario
            </Button>
          </>
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
              itemLabel="template"
              itemLabelPlural="templates"
            />
          ) : null
        }
        itemsClassName="flex flex-col gap-2"
      >
        {loading ? (
          <div className="space-y-3">
            {Array.from({ length: 5 }).map((_, i) => (<Skeleton key={i} className="h-16 rounded-[var(--admin-radius-lg)]" />))}
          </div>
        ) : rows.length === 0 ? (
          <EmptyState
            illustration={<MessageSquare />}
            title="No conversation scenarios yet"
            description="Create your first scenario to make it available to learners."
            primaryAction={{
              label: 'New Scenario',
              onClick: () => router.push('/admin/content/conversation/new'),
            }}
          />
        ) : (
          rows.map((row) => (
            <Card key={row.id}>
              <CardContent className="flex items-center justify-between gap-3 p-4 pt-4">
                <div className="min-w-0">
                  <div className="flex flex-wrap items-center gap-2">
                    <Link
                      href={`/admin/content/conversation/${row.id}`}
                      className="truncate text-sm font-semibold text-[var(--admin-fg-strong)] hover:text-[var(--admin-primary)]"
                    >
                      {row.title}
                    </Link>
                    <Badge variant={row.status === 'published' ? 'success' : row.status === 'archived' ? 'secondary' : 'info'} size="sm">
                      {row.status}
                    </Badge>
                    <Badge variant="default" size="sm">{row.taskTypeCode}</Badge>
                    {row.professionId && (<Badge variant="default" size="sm">{row.professionId}</Badge>)}
                    <Badge variant="default" size="sm">{row.difficulty}</Badge>
                  </div>
                  <div className="mt-1 text-xs text-[var(--admin-fg-muted)]">
                    {Math.round(row.estimatedDurationSeconds / 60)} min · updated {new Date(row.updatedAt).toLocaleDateString()}
                  </div>
                </div>
                <div className="flex items-center gap-2">
                  {row.status === 'draft' && (
                    <Button variant="primary" size="sm" onClick={() => handlePublish(row.id)} startIcon={<CheckCircle className="h-4 w-4" />}>
                      Publish
                    </Button>
                  )}
                  {row.status !== 'archived' && (
                    <Button variant="secondary" size="sm" onClick={() => handleArchive(row.id)} startIcon={<Archive className="h-4 w-4" />}>
                      Archive
                    </Button>
                  )}
                </div>
              </CardContent>
            </Card>
          ))
        )}
      </AdminCatalogLayout>

      {toast && (<Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />)}
    </>
  );
}
