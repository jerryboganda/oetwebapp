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
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';

type ToastState = { variant: 'success' | 'error'; message: string } | null;
type PageStatus = 'loading' | 'success' | 'empty' | 'error';

export default function AdminStrategiesPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [guides, setGuides] = useState<StrategyGuideAdminItem[]>([]);
  const [status, setStatus] = useState('');
  const [search, setSearch] = useState('');
  const [toast, setToast] = useState<ToastState>(null);

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
          <Link href={`/admin/strategies/${encodeURIComponent(guide.id)}`} className="font-semibold text-primary hover:underline">
            {guide.title}
          </Link>
          <p className="mt-1 line-clamp-2 max-w-xl text-xs leading-5 text-muted">{guide.summary}</p>
        </div>
      ),
    },
    {
      key: 'subtest',
      header: 'Subtest',
      render: (guide) => <span className="capitalize text-muted">{guide.subtestCode ?? 'All'}</span>,
    },
    {
      key: 'category',
      header: 'Category',
      render: (guide) => <span className="capitalize text-muted">{guide.category.replace(/_/g, ' ')}</span>,
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
        <span className="text-xs text-muted">
          {guide.readingTimeMinutes} min / order {guide.sortOrder}
        </span>
      ),
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (guide) => (
        <div className="flex flex-wrap gap-2">
          <Link href={`/admin/strategies/${encodeURIComponent(guide.id)}`}>
            <Button variant="outline" size="sm">Edit</Button>
          </Link>
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
    <AdminRouteWorkspace role="main" aria-label="Strategy guide management">
      {toast ? <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} /> : null}

      <AdminRouteSectionHeader
        title="Strategy Guides"
        description="Manage guided learner articles, publish readiness, preview access, and content hierarchy placement."
        icon={BookOpenText}
        actions={
          <Link href="/admin/strategies/new">
            <Button className="gap-2">
              <FilePlus2 className="h-4 w-4" />
              New guide
            </Button>
          </Link>
        }
      />

      <div className="grid gap-4 md:grid-cols-4">
        <AdminRouteSummaryCard label="Guides" value={metrics.total} icon={<BookOpenText className="h-5 w-5" />} />
        <AdminRouteSummaryCard label="Published" value={metrics.published} tone="success" icon={<ShieldCheck className="h-5 w-5" />} />
        <AdminRouteSummaryCard label="Drafts" value={metrics.drafts} tone="warning" icon={<FilePlus2 className="h-5 w-5" />} />
        <AdminRouteSummaryCard label="Archived" value={metrics.archived} tone={metrics.archived > 0 ? 'danger' : 'default'} />
      </div>

      <AdminRoutePanel title="Filters">
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
          <Button type="button" variant="outline" onClick={() => void loadGuides()} className="gap-2">
            <Search className="h-4 w-4" />
            Search
          </Button>
        </div>
      </AdminRoutePanel>

      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => void loadGuides()}
        errorMessage="Could not load strategy guides."
        emptyContent={
          <AdminRoutePanel>
            <div className="flex flex-col items-start gap-3">
              <p className="text-sm text-muted">No strategy guides match the current filters.</p>
              <Link href="/admin/strategies/new">
                <Button className="gap-2">
                  <FilePlus2 className="h-4 w-4" />
                  Create guide
                </Button>
              </Link>
            </div>
          </AdminRoutePanel>
        }
      >
        <AdminRoutePanel title="Guides" description="Create, edit, publish, and archive learner strategy articles.">
          <DataTable columns={columns} data={guides} keyExtractor={(guide) => guide.id} aria-label="Strategy guides" />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
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
