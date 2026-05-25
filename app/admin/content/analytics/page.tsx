'use client';

import { useEffect, useState } from 'react';
import { BarChart3, TrendingUp, Users, Clock } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';

interface ContentAnalytics {
  contentId: string; title: string; subtestCode: string; status: string;
  metrics: { totalAttempts: number; completedAttempts: number; completionRate: number; averageTimeMinutes: number; uniqueLearners: number; averageScore: number | null; medianScore: number | null; scoreStdDev: number | null };
  monthlyTrend: { month: string; attempts: number; completed: number }[];
  evaluationCount: number;
}

const apiRequest = apiClient.request;

export default function ContentAnalyticsPage() {
  const { isAuthenticated, isLoading, role } = useAdminAuth();
  const { user } = useCurrentUser();
  const canViewAnalytics = hasPermission(user?.adminPermissions, AdminPermission.ContentRead);
  const [data, setData] = useState<ContentAnalytics | null>(null);
  const [loading, setLoading] = useState(false);
  const [contentId, setContentId] = useState('');

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin' || !canViewAnalytics) return;
    analytics.track('admin_content_analytics_viewed');
  }, [canViewAnalytics, isAuthenticated, role]);

  const load = async () => {
    if (!contentId || !canViewAnalytics) return;
    setLoading(true);
    try { setData(await apiRequest<ContentAnalytics>(`/v1/admin/content-analytics/${contentId}`)); }
    catch { setData(null); }
    finally { setLoading(false); }
  };

  if (isLoading) return null;
  if (!isAuthenticated || role !== 'admin') return null;

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Analytics' },
  ];

  if (!canViewAnalytics) {
    return (
      <AdminOperationsLayout title="Content Item Analytics" breadcrumbs={breadcrumbs} eyebrow="Analytics">
        <Card><CardContent className="pt-6"><p className="text-sm text-admin-fg-muted">Content read permission is required.</p></CardContent></Card>
      </AdminOperationsLayout>
    );
  }

  return (
    <AdminOperationsLayout
      title="Content Item Analytics"
      description="Deep-dive into per-item usage, completion rates, and learner outcomes."
      breadcrumbs={breadcrumbs}
      eyebrow="Analytics"
      kpis={data ? (
        <KpiStrip>
          <KpiTile label="Total Attempts" value={data.metrics.totalAttempts} />
          <KpiTile label="Completion Rate" value={`${data.metrics.completionRate}%`} tone="success" />
          <KpiTile label="Unique Learners" value={data.metrics.uniqueLearners} />
          <KpiTile label="Avg Time" value={`${data.metrics.averageTimeMinutes}m`} tone="warning" />
        </KpiStrip>
      ) : null}
    >
      <Card>
        <CardHeader><CardTitle>Lookup</CardTitle></CardHeader>
        <CardContent>
          <div className="flex flex-col sm:flex-row gap-3 sm:items-end">
            <div className="flex-1">
              <label className="text-sm font-medium text-admin-fg-default mb-1 block">Content ID</label>
              <Input placeholder="Enter content ID..." value={contentId} onChange={e => setContentId(e.target.value)} />
            </div>
            <Button onClick={load} disabled={loading} loading={loading}>
              {loading ? 'Loading...' : 'Analyze'}
            </Button>
          </div>
        </CardContent>
      </Card>

      {loading ? (
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-admin-lg" />)}</div>
      ) : data ? (
        <MotionSection className="space-y-6">
          <MotionItem>
            <Card>
              <CardHeader>
                <CardTitle>{data.title}</CardTitle>
              </CardHeader>
              <CardContent>
                <div className="flex gap-2">
                  <Badge variant="info" className="capitalize">{data.subtestCode}</Badge>
                  <Badge variant="default">{data.status}</Badge>
                </div>
              </CardContent>
            </Card>
          </MotionItem>

          {data.metrics.averageScore !== null && (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
              <KpiTile label="Avg Score" value={data.metrics.averageScore} />
              <KpiTile label="Median Score" value={data.metrics.medianScore ?? '--'} />
              <KpiTile label="Std Deviation" value={data.metrics.scoreStdDev ?? '--'} />
            </div>
          )}

          {data.monthlyTrend.length > 0 && (
            <Card>
              <CardHeader><CardTitle>Monthly Usage Trend</CardTitle></CardHeader>
              <CardContent>
                <div className="space-y-2">{data.monthlyTrend.map(m => (
                  <div key={m.month} className="flex items-center gap-3">
                    <span className="text-xs font-mono w-20 text-admin-fg-muted">{m.month}</span>
                    <div className="flex-1 h-4 rounded-full bg-admin-bg-subtle overflow-hidden"><div className="h-full rounded-full bg-[var(--admin-primary)]" style={{ width: `${Math.min(100, m.attempts * 10)}%` }} /></div>
                    <span className="text-xs text-admin-fg-muted w-16 text-right">{m.attempts} / {m.completed}</span>
                  </div>
                ))}</div>
              </CardContent>
            </Card>
          )}
        </MotionSection>
      ) : null}
    </AdminOperationsLayout>
  );
}
