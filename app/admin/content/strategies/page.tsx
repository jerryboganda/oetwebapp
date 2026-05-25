'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { BookOpenText, FilePlus2, Search, ShieldCheck } from 'lucide-react';
import {
  adminArchiveStrategyGuide,
  adminListStrategyGuides,
  adminPublishStrategyGuide,
} from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { StrategyGuideAdminItem } from '@/lib/types/strategies';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge, type BadgeProps } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminStrategiesPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [guides, setGuides] = useState<StrategyGuideAdminItem[]>([]);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [toast, setToast] = useState<ToastState>(null);
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

  const loadGuides = useCallback(async () => {
    setPageStatus('loading');
    try {
      const data = await adminListStrategyGuides({
        examTypeCode: 'oet',
        status: status || undefined,
        search: search || undefined,
      });
      setGuides(data);
      setPageStatus(data.length > 0 ? 'success' : 'empty');
    } catch (error) {
      setPageStatus('error');
      setToast({ variant: 'error', message: (error as Error).message || 'Failed to load strategy guides.' });
    }
  }, [search, status]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    queueMicrotask(() => void loadGuides());
  }, [isAuthenticated, loadGuides, role]);

  const metrics = useMemo(() => ({
    total: guides.length,
    published: guides.filter((guide) => guide.status === 'active').length,
    drafts: guides.filter((guide) => guide.status === 'draft').length,
    archived: guides.filter((guide) => guide.status === 'archived').length,
  }), [guides]);

  async function publishGuide(guideId: string) {
    try {
      const result = await adminPublishStrategyGuide(guideId);
      if (!result.published) {
        const firstError = result.validation.errors[0]?.message ?? 'Publish validation failed.';
        setToast({ variant: 'error', message: firstError });
        return;
      }
      setToast({ variant: 'success', message: 'Strategy guide published.' });
      await loadGuides();
    } catch (error) {
      setToast({ variant: 'error', message: (error as Error).message || 'Publish failed.' });
    }
  }

  async function archiveGuide(guideId: string) {
    if (!window.confirm('Archive this strategy guide? Learners will no longer see it.')) return;
    try {
      await adminArchiveStrategyGuide(guideId);
      setToast({ variant: 'success', message: 'Strategy guide archived.' });
      await loadGuides();
    } catch (error) {
      setToast({ variant: 'error', message: (error as Error).message || 'Archive failed.' });
    }
  }

  const columns: Column<StrategyGuideAdminItem>[] = [
    {
      key: 'title',
      header: 'Guide',
      render: (guide) => (
        <div className="min-w-0">
          <Link href={`/admin/content/strategies/${encodeURIComponent(guide.id)}`} className="font-semibold text-[var(--admin-primary)] hover:underline">
            {guide.title}
          </Link>
          <p className="mt-1 line-clamp-2 max-w-xl text-xs leading-5 text-admin-fg-muted">{guide.summary}</p>
        </div>
      ),
    },
    {
      key: 'subtest',
      header: 'Subtest',
      render: (guide) => <span className="capitalize text-admin-fg-muted">{guide.subtestCode ?? 'All'}</span>,
    },
    {
      key: 'category',
      header: 'Category',
      render: (guide) => <span className="capitalize text-admin-fg-muted">{guide.category.replace(/_/g, ' ')}</span>,
    },
    {
      key: 'status',
      header: 'State',
      render: (guide) => <Badge variant={statusTone(guide.status)}>{statusLabel(guide.status)}</Badge>,
    },
    {
      key: 'meta',
      header: 'Meta',
      render: (guide) => (
        <span className="text-xs text-admin-fg-muted">
          {guide.readingTimeMinutes} min / order {guide.sortOrder}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (guide) => (
        <div className="flex flex-wrap gap-2">
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/strategies/${encodeURIComponent(guide.id)}`}>Edit</Link>
          </Button>
          {guide.status !== 'active' && guide.status !== 'archived' ? (
            <Button size="sm" onClick={() => void publishGuide(guide.id)}>Publish</Button>
          ) : null}
          {guide.status !== 'archived' ? (
            <Button variant="outline" size="sm" onClick={() => void archiveGuide(guide.id)}>Archive</Button>
          ) : null}
        </div>
      ),
    },
  ];

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <>
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminTableLayout
        title="Strategy Guides"
        description="Manage guided learner articles, publish readiness, preview access, and content hierarchy placement."
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Strategies' },
        ]}
        icon={<BookOpenText className="h-5 w-5" />}
        actions={
          <Button asChild startIcon={<FilePlus2 className="h-4 w-4" />}>
            <Link href="/admin/content/strategies/new">New guide</Link>
          </Button>
        }
        banner={
          <div className="space-y-4">
            <KpiStrip>
              <KpiTile label="Guides" value={metrics.total} icon={<BookOpenText className="h-5 w-5" />} />
              <KpiTile label="Published" value={metrics.published} tone="success" icon={<ShieldCheck className="h-5 w-5" />} />
              <KpiTile label="Drafts" value={metrics.drafts} tone="warning" icon={<FilePlus2 className="h-5 w-5" />} />
              <KpiTile label="Archived" value={metrics.archived} tone={metrics.archived > 0 ? 'danger' : 'default'} />
            </KpiStrip>

            <Card>
              <CardHeader><CardTitle>Filters</CardTitle></CardHeader>
              <CardContent>
                <div className="grid gap-3 md:grid-cols-[220px_1fr_auto] md:items-end">
                  <Select
                    label="State"
                    value={status}
                    onChange={(event) => setStatus(event.target.value)}
                    options={[
                      { value: '', label: 'Any state' },
                      { value: 'draft', label: 'Draft' },
                      { value: 'active', label: 'Published' },
                      { value: 'archived', label: 'Archived' },
                    ]}
                  />
                  <Input
                    label="Search"
                    value={search}
                    onChange={(event) => setSearch(event.target.value)}
                    placeholder="title, summary, category"
                  />
                  <Button type="button" variant="outline" onClick={() => void loadGuides()} startIcon={<Search className="h-4 w-4" />}>
                    Search
                  </Button>
                </div>
              </CardContent>
            </Card>
          </div>
        }
      >
        <div className="p-4 sm:p-5">
          <AsyncStateWrapper
            status={pageStatus}
            onRetry={() => void loadGuides()}
            errorMessage="Could not load strategy guides."
            emptyContent={
              <Card>
                <CardContent className="pt-6">
                  <div className="flex flex-col items-start gap-3">
                    <p className="text-sm text-admin-fg-muted">No strategy guides match the current filters.</p>
                    <Button asChild startIcon={<FilePlus2 className="h-4 w-4" />}>
                      <Link href="/admin/content/strategies/new">Create guide</Link>
                    </Button>
                  </div>
                </CardContent>
              </Card>
            }
          >
            <DataTable columns={columns} data={guides} keyExtractor={(guide) => guide.id} aria-label="Strategy guides" selectable selectedKeys={selectedKeys} onSelectionChange={setSelectedKeys} />
            <BulkActionBar
              selectedCount={selectedKeys.size}
              onClearSelection={() => setSelectedKeys(new Set())}
              actions={[
                { key: 'archive', label: 'Archive selected', variant: 'danger', onClick: () => setToast({ variant: 'error', message: 'Bulk archive coming soon.' }) },
                { key: 'publish', label: 'Publish selected', onClick: () => setToast({ variant: 'error', message: 'Bulk publish coming soon.' }) },
              ]}
            />
          </AsyncStateWrapper>
        </div>
      </AdminTableLayout>
    </>
  );
}

function statusLabel(status: string) {
  if (status === 'active') return 'Published';
  return status.charAt(0).toUpperCase() + status.slice(1);
}

function statusTone(status: string): BadgeProps['variant'] {
  if (status === 'active') return 'success';
  if (status === 'archived') return 'danger';
  return 'warning';
}
