'use client';

import { useEffect, useState } from 'react';
import { Users, BarChart3, Activity } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface Cohort { cohortKey: string; cohortName: string; learnerCount: number; averageScore: number | null; evaluationCount: number; activeLastMonth: number }
interface CohortData { groupBy: string; cohorts: Cohort[]; totalLearners: number; generatedAt: string }

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function CohortAnalysisPage() {
  const [data, setData] = useState<CohortData | null>(null);
  const [loading, setLoading] = useState(true);
  const [groupBy, setGroupBy] = useState('profession');

  const load = (g: string) => {
    setLoading(true); setGroupBy(g);
    apiRequest<CohortData>(`/v1/admin/analytics/cohort?groupBy=${g}`).then(setData).catch(() => {}).finally(() => setLoading(false));
  };

  // eslint-disable-next-line react-hooks/set-state-in-effect
  useEffect(() => { analytics.track('admin_cohort_analysis_viewed'); load('profession'); }, []);

  return (
    <div className="min-h-screen bg-background-light">
      <div className="max-w-5xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold mb-1">Learner Cohort Analysis</h1>
        <p className="text-muted mb-6">Compare outcomes across professions and subscription tiers.</p>

        <div className="flex gap-2 mb-6">
          <button onClick={() => load('profession')} className={`px-4 py-2 rounded-lg text-sm font-medium ${groupBy === 'profession' ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>By Profession</button>
          <button onClick={() => load('plan')} className={`px-4 py-2 rounded-lg text-sm font-medium ${groupBy === 'plan' ? 'bg-primary text-primary-foreground' : 'bg-muted'}`}>By Plan</button>
        </div>

        {loading ? <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-4">
            <Card className="p-4"><p className="text-sm text-muted">Total learners: <strong>{data.totalLearners}</strong> • Grouped by: <strong className="capitalize">{data.groupBy}</strong></p></Card>
            {data.cohorts.map(c => (
              <MotionItem key={c.cohortKey}>
                <Card className="p-4">
                  <div className="flex items-center justify-between mb-2">
                    <h3 className="font-semibold">{c.cohortName}</h3>
                    <Badge variant="outline">{c.learnerCount} learners</Badge>
                  </div>
                  <div className="grid grid-cols-3 gap-4 text-center">
                    <div><p className="text-lg font-bold">{c.averageScore ?? '--'}</p><p className="text-xs text-muted">Avg Score</p></div>
                    <div><p className="text-lg font-bold">{c.evaluationCount}</p><p className="text-xs text-muted">Evaluations</p></div>
                    <div><p className="text-lg font-bold">{c.activeLastMonth}</p><p className="text-xs text-muted">Active (30d)</p></div>
                  </div>
                </Card>
              </MotionItem>
            ))}
          </MotionSection>
        ) : <Card className="p-8 text-center text-muted"><p>No data available.</p></Card>}
      </div>
    </div>
  );
}
