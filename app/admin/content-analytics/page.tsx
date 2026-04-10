'use client';

import { useEffect, useState } from 'react';
import { BarChart3, TrendingUp, Users, Clock } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface ContentAnalytics {
  contentId: string; title: string; subtestCode: string; status: string;
  metrics: { totalAttempts: number; completedAttempts: number; completionRate: number; averageTimeMinutes: number; uniqueLearners: number; averageScore: number | null; medianScore: number | null; scoreStdDev: number | null };
  monthlyTrend: { month: string; attempts: number; completed: number }[];
  evaluationCount: number;
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function ContentAnalyticsPage() {
  const [data, setData] = useState<ContentAnalytics | null>(null);
  const [loading, setLoading] = useState(false);
  const [contentId, setContentId] = useState('');

  useEffect(() => { analytics.track('admin_content_analytics_viewed'); }, []);

  const load = async () => {
    if (!contentId) return;
    setLoading(true);
    try { setData(await apiRequest<ContentAnalytics>(`/v1/admin/content-analytics/${contentId}`)); }
    catch { setData(null); }
    finally { setLoading(false); }
  };

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-5xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold mb-1">Content Item Analytics</h1>
        <p className="text-muted-foreground mb-6">Deep-dive into per-item usage, completion rates, and learner outcomes.</p>

        <div className="flex gap-3 items-end mb-6">
          <div className="flex-1"><label className="text-sm font-medium text-muted-foreground mb-1 block">Content ID</label><input className="w-full border rounded-lg px-3 py-2 text-sm" placeholder="Enter content ID..." value={contentId} onChange={e => setContentId(e.target.value)} /></div>
          <button onClick={load} disabled={loading} className="px-4 py-2 bg-primary text-primary-foreground rounded-lg text-sm font-medium hover:bg-primary/90 disabled:opacity-50">{loading ? 'Loading...' : 'Analyze'}</button>
        </div>

        {loading ? <div className="grid grid-cols-4 gap-4">{Array.from({ length: 4 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-6">
            <MotionItem><Card className="p-4"><h2 className="text-lg font-semibold">{data.title}</h2><div className="flex gap-2 mt-1"><Badge variant="outline" className="capitalize">{data.subtestCode}</Badge><Badge variant="outline">{data.status}</Badge></div></Card></MotionItem>

            <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
              <MotionItem><Card className="p-4 text-center"><BarChart3 className="w-5 h-5 mx-auto mb-2 text-primary" /><p className="text-2xl font-bold">{data.metrics.totalAttempts}</p><p className="text-xs text-muted-foreground">Total Attempts</p></Card></MotionItem>
              <MotionItem><Card className="p-4 text-center"><TrendingUp className="w-5 h-5 mx-auto mb-2 text-emerald-500" /><p className="text-2xl font-bold">{data.metrics.completionRate}%</p><p className="text-xs text-muted-foreground">Completion Rate</p></Card></MotionItem>
              <MotionItem><Card className="p-4 text-center"><Users className="w-5 h-5 mx-auto mb-2 text-blue-500" /><p className="text-2xl font-bold">{data.metrics.uniqueLearners}</p><p className="text-xs text-muted-foreground">Unique Learners</p></Card></MotionItem>
              <MotionItem><Card className="p-4 text-center"><Clock className="w-5 h-5 mx-auto mb-2 text-amber-500" /><p className="text-2xl font-bold">{data.metrics.averageTimeMinutes}m</p><p className="text-xs text-muted-foreground">Avg Time</p></Card></MotionItem>
            </div>

            {data.metrics.averageScore !== null && (
              <div className="grid grid-cols-3 gap-4">
                <Card className="p-4 text-center"><p className="text-xl font-bold">{data.metrics.averageScore}</p><p className="text-xs text-muted-foreground">Avg Score</p></Card>
                <Card className="p-4 text-center"><p className="text-xl font-bold">{data.metrics.medianScore}</p><p className="text-xs text-muted-foreground">Median Score</p></Card>
                <Card className="p-4 text-center"><p className="text-xl font-bold">{data.metrics.scoreStdDev}</p><p className="text-xs text-muted-foreground">Std Deviation</p></Card>
              </div>
            )}

            {data.monthlyTrend.length > 0 && (
              <Card className="p-4">
                <h3 className="font-semibold mb-3">Monthly Usage Trend</h3>
                <div className="space-y-2">{data.monthlyTrend.map(m => (
                  <div key={m.month} className="flex items-center gap-3">
                    <span className="text-xs font-mono w-20 text-muted-foreground">{m.month}</span>
                    <div className="flex-1 h-4 rounded-full bg-muted overflow-hidden"><div className="h-full rounded-full bg-primary" style={{ width: `${Math.min(100, m.attempts * 10)}%` }} /></div>
                    <span className="text-xs text-muted-foreground w-16 text-right">{m.attempts} / {m.completed}</span>
                  </div>
                ))}</div>
              </Card>
            )}
          </MotionSection>
        ) : null}
      </div>
    </div>
  );
}
