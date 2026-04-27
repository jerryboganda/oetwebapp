'use client';

import { useCallback, useEffect, useState } from 'react';
import { FileSearch, RefreshCw } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

interface ContentQualityItem {
  id: string;
  title: string;
  subtestCode: string;
  contentType: string;
  qaStatus: string;
  qaReviewedBy: string | null;
  qaReviewedAt: string | null;
  sourceType: string;
  performanceMetrics: string | null;
  difficultyRating: number;
  status: string;
  updatedAt: string;
}

const QA_BADGE: Record<string, { label: string; variant: 'default' | 'success' | 'danger' | 'outline' }> = {
  approved: { label: 'Approved', variant: 'success' },
  needs_review: { label: 'Needs Review', variant: 'default' },
  rejected: { label: 'Rejected', variant: 'danger' },
};

function adminRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

export default function ContentQualityPage() {
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canViewQuality = hasPermission(user?.adminPermissions, AdminPermission.ContentRead);
  const canScoreQuality = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);
  const [items, setItems] = useState<ContentQualityItem[]>([]);
  const [total, setTotal] = useState(0);
  const [status, setStatus] = useState<PageStatus>('loading');
  const [toast, setToast] = useState<ToastState>(null);
  const [scoring, setScoring] = useState<string | null>(null);

  const loadData = useCallback(async () => {
    if (!canViewQuality) return;
    try {
      setStatus('loading');
      const data = await adminRequest<{ items: ContentQualityItem[]; total: number }>('/v1/admin/content-quality?pageSize=50');
      setItems(data.items);
      setTotal(data.total);
      setStatus(data.items.length > 0 ? 'success' : 'empty');
    } catch {
      setStatus('error');
    }
  }, [canViewQuality]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin' || !canViewQuality) return;
    analytics.track('admin_view', { page: 'content-quality' });
    loadData();
  }, [canViewQuality, isAuthenticated, loadData, role]);

  async function handleScore(contentId: string) {
    if (!canScoreQuality) return;
    setScoring(contentId);
    try {
      const result = await adminRequest<{ contentId: string; qualityScore: number; qaStatus: string; factors: string[] }>(
        `/v1/admin/content-quality/${contentId}/score`,
        { method: 'POST' },
      );
      setItems((prev) => prev.map((i) => (i.id === contentId ? { ...i, qaStatus: result.qaStatus, performanceMetrics: JSON.stringify({ qualityScore: result.qualityScore, factors: result.factors }) } : i)));
      setToast({ variant: 'success', message: `Quality score: ${result.qualityScore}/100 → ${result.qaStatus}` });
    } catch {
      setToast({ variant: 'error', message: 'Failed to score content quality.' });
    } finally {
      setScoring(null);
    }
  }

  const columns: Column<ContentQualityItem>[] = [
    { key: 'title', header: 'Title', render: (r) => <span className="font-medium text-sm">{r.title}</span> },
    { key: 'subtestCode', header: 'Subtest', render: (r) => <span className="capitalize">{r.subtestCode}</span> },
    { key: 'qaStatus', header: 'QA Status', render: (r) => { const b = QA_BADGE[r.qaStatus] ?? { label: r.qaStatus, variant: 'outline' as const }; return <Badge variant={b.variant}>{b.label}</Badge>; } },
    { key: 'sourceType', header: 'Source', render: (r) => <span className="capitalize text-sm">{r.sourceType}</span> },
    {
      key: 'score',
      header: 'Quality Score',
      render: (r) => {
        if (!r.performanceMetrics) return <span className="text-muted">—</span>;
        try {
          const pm = JSON.parse(r.performanceMetrics);
          return <span className="font-mono text-sm">{pm.qualityScore}/100</span>;
        } catch {
          return <span className="text-muted">—</span>;
        }
      },
    },
    {
      key: 'actions',
      header: '',
      render: (r) => canScoreQuality ? (
        <Button size="sm" variant="outline" onClick={() => handleScore(r.id)} disabled={scoring === r.id}>
          <RefreshCw className={`w-3.5 h-3.5 mr-1 ${scoring === r.id ? 'animate-spin' : ''}`} />
          Score
        </Button>
      ) : <span className="text-xs text-muted">Read only</span>,
    },
  ];

  const approvedCount = items.filter((i) => i.qaStatus === 'approved').length;
  const needsReviewCount = items.filter((i) => i.qaStatus === 'needs_review').length;

  if (isLoading) return null;

  if (!isAuthenticated || role !== 'admin') return null;

  if (!canViewQuality) {
    return (
      <AdminRouteWorkspace>
        <p className="text-sm text-muted">Content read permission is required.</p>
      </AdminRouteWorkspace>
    );
  }

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />}

      <AdminRouteSectionHeader
        title="Content quality scoring"
        description="Run automated QA scoring across recent content and surface items that still need review."
        icon={FileSearch}
      />

      <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
        <AdminRouteSummaryCard label="Total content" value={total} hint="All items in QA pipeline" />
        <AdminRouteSummaryCard label="Approved" value={approvedCount} hint="Passed automated QA" tone="success" />
        <AdminRouteSummaryCard label="Needs review" value={needsReviewCount} hint="Awaiting human reviewer" tone={needsReviewCount > 0 ? 'warning' : 'default'} />
      </div>

      <AsyncStateWrapper status={status} errorMessage="Failed to load content quality data." onRetry={loadData}>
        <AdminRoutePanel>
          <DataTable columns={columns} data={items} keyExtractor={(row) => row.id} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
