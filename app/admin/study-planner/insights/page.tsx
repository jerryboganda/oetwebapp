'use client';

import { useCallback, useEffect, useState } from 'react';
import { BarChart3 } from 'lucide-react';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { getStudyPlannerInsights, type StudyPlannerInsights } from '@/lib/study-planner-admin-api';

export default function StudyPlannerInsightsPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [data, setData] = useState<StudyPlannerInsights | null>(null);

  const load = useCallback(async () => {
    setStatus('loading');
    try {
      const d = await getStudyPlannerInsights();
      setData(d);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  if (!isAuthenticated || role !== 'admin') {
    return <AdminRouteWorkspace><p className="text-sm text-muted">Admin access required.</p></AdminRouteWorkspace>;
  }

  return (
    <AdminRouteWorkspace>
      <AdminRouteSectionHeader
        icon={<BarChart3 className="w-6 h-6" />}
        title="Study Planner Insights"
        description="Fleet-wide telemetry for plan health, completion, and regeneration activity."
      />
      <AsyncStateWrapper status={status}>
        {data && (
          <div className="grid grid-cols-2 md:grid-cols-5 gap-4">
            <Metric label="Total plans" value={data.totalPlans} />
            <Metric label="Total items" value={data.totalItems} />
            <Metric label="Completed items" value={data.completedItems} />
            <Metric label="Completion rate" value={`${data.completionRate}%`} />
            <Metric label="Overdue items" value={data.overdueItems} tone={data.overdueItems > 0 ? 'warning' : 'default'} />
            <Metric label="Regens (last 7 days)" value={data.regenLast7d} />
          </div>
        )}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}

function Metric({ label, value, tone }: { label: string; value: number | string; tone?: 'default' | 'warning' }) {
  return (
    <div className={`rounded-2xl border p-4 ${tone === 'warning' ? 'border-amber-200 bg-amber-50 dark:border-amber-800 dark:bg-amber-950/30' : 'border-border bg-background-light'}`}>
      <p className="text-xs font-semibold uppercase tracking-wider text-muted">{label}</p>
      <p className={`mt-1 text-3xl font-bold ${tone === 'warning' ? 'text-amber-700 dark:text-amber-400' : 'text-navy'}`}>{value}</p>
    </div>
  );
}
