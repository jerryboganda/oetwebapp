'use client';

import { useEffect, useState } from 'react';
import { FileSearch, CheckCircle2, AlertCircle, RefreshCw } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteSummaryCard, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';

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

const QA_BADGE: Record<string, { label: string; variant: 'default' | 'success' | 'destructive' | 'outline' }> = {
  approved: { label: 'Approved', variant: 'success' },
  needs_review: { label: 'Needs Review', variant: 'default' },
  rejected: { label: 'Rejected', variant: 'destructive' },
};

async function adminRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: { 'Content-Type': 'application/json', Authorization: `Bearer ${token}`, ...init?.headers },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function ContentQualityPage() {
  useAdminAuth();
  const [items, setItems] = useState<ContentQualityItem[]>([]);
  const [total, setTotal] = useState(0);
  const [status, setStatus] = useState<PageStatus>('loading');
  const [toast, setToast] = useState<ToastState>(null);
  const [scoring, setScoring] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('admin_view', { page: 'content-quality' });
    loadData();
  }, []);

  async function loadData() {
    try {
      setStatus('loading');
      const data = await adminRequest<{ items: ContentQualityItem[]; total: number }>('/v1/admin/content-quality?pageSize=50');
      setItems(data.items);
      setTotal(data.total);
      setStatus(data.items.length > 0 ? 'success' : 'empty');
    } catch {
      setStatus('error');
    }
  }

  async function handleScore(contentId: string) {
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
        if (!r.performanceMetrics) return <span className="text-gray-400">—</span>;
        try {
          const pm = JSON.parse(r.performanceMetrics);
          return <span className="font-mono text-sm">{pm.qualityScore}/100</span>;
        } catch {
          return <span className="text-gray-400">—</span>;
        }
      },
    },
    {
      key: 'actions',
      header: '',
      render: (r) => (
        <Button size="sm" variant="outline" onClick={() => handleScore(r.id)} disabled={scoring === r.id}>
          <RefreshCw className={`w-3.5 h-3.5 mr-1 ${scoring === r.id ? 'animate-spin' : ''}`} />
          Score
        </Button>
      ),
    },
  ];

  const approvedCount = items.filter((i) => i.qaStatus === 'approved').length;
  const needsReviewCount = items.filter((i) => i.qaStatus === 'needs_review').length;

  return (
    <AdminRouteWorkspace>
      {toast && <Toast variant={toast.variant} onDismiss={() => setToast(null)}>{toast.message}</Toast>}

      <AdminRouteSectionHeader title="Content Quality Scoring" icon={<FileSearch className="w-5 h-5" />} />

      <div className="grid grid-cols-3 gap-4 mb-6">
        <AdminRouteSummaryCard label="Total Content" value={total} />
        <AdminRouteSummaryCard label="Approved" value={approvedCount} />
        <AdminRouteSummaryCard label="Needs Review" value={needsReviewCount} />
      </div>

      <AsyncStateWrapper status={status} errorMessage="Failed to load content quality data." onRetry={loadData}>
        <AdminRoutePanel>
          <DataTable columns={columns} data={items} />
        </AdminRoutePanel>
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
