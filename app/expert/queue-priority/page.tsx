'use client';

import { useEffect, useState } from 'react';
import { AlertOctagon, AlertTriangle, Clock, CheckCircle2, Inbox } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface QueueItem {
  assignmentId: string; reviewRequestId: string; attemptId: string; subtestCode: string;
  priority: string; reasons: string[]; daysToExam: number | null; slaRemainingHours: number;
  isResubmission: boolean; turnaround: string; hoursWaiting: number; createdAt: string;
}

interface QueueData {
  items: QueueItem[];
  summary: { total: number; critical: number; high: number; normal: number };
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

const PRIORITY_CONFIG: Record<string, { icon: typeof AlertOctagon; color: string; bg: string }> = {
  critical: { icon: AlertOctagon, color: 'text-red-600', bg: 'bg-red-50 dark:bg-red-950 border-red-200 dark:border-red-800' },
  high: { icon: AlertTriangle, color: 'text-amber-600', bg: 'bg-amber-50 dark:bg-amber-950 border-amber-200 dark:border-amber-800' },
  normal: { icon: CheckCircle2, color: 'text-muted-foreground', bg: 'border-border' },
};

export default function QueuePriorityPage() {
  const [data, setData] = useState<QueueData | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('expert_queue_priority_viewed');
    apiRequest<QueueData>('/v1/expert/queue-priority').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-4xl mx-auto px-4 py-8">
        <div className="mb-8">
          <h1 className="text-2xl font-bold">Review Queue — Priority View</h1>
          <p className="text-muted-foreground mt-1">See why each review is prioritized and triage accordingly.</p>
        </div>

        {loading ? (
          <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
        ) : !data || data.items.length === 0 ? (
          <Card className="p-8 text-center text-muted-foreground"><Inbox className="w-8 h-8 mx-auto mb-3 opacity-50" /><p>No assigned reviews in your queue.</p></Card>
        ) : (
          <MotionSection className="space-y-6">
            {/* Summary */}
            <div className="grid grid-cols-4 gap-3">
              <Card className="p-3 text-center"><p className="text-2xl font-bold">{data.summary.total}</p><p className="text-xs text-muted-foreground">Total</p></Card>
              <Card className="p-3 text-center bg-red-50 dark:bg-red-950"><p className="text-2xl font-bold text-red-600">{data.summary.critical}</p><p className="text-xs text-red-600">Critical</p></Card>
              <Card className="p-3 text-center bg-amber-50 dark:bg-amber-950"><p className="text-2xl font-bold text-amber-600">{data.summary.high}</p><p className="text-xs text-amber-600">High</p></Card>
              <Card className="p-3 text-center"><p className="text-2xl font-bold">{data.summary.normal}</p><p className="text-xs text-muted-foreground">Normal</p></Card>
            </div>

            {/* Queue items */}
            <div className="space-y-3">
              {data.items.map(item => {
                const cfg = PRIORITY_CONFIG[item.priority] ?? PRIORITY_CONFIG.normal;
                const Icon = cfg.icon;
                return (
                  <MotionItem key={item.assignmentId}>
                    <Card className={`p-4 border ${cfg.bg}`}>
                      <div className="flex items-start gap-3">
                        <Icon className={`w-5 h-5 mt-0.5 flex-shrink-0 ${cfg.color}`} />
                        <div className="flex-1">
                          <div className="flex items-center gap-2 flex-wrap">
                            <Badge variant={item.priority === 'critical' ? 'danger' : item.priority === 'high' ? 'default' : 'outline'} className="uppercase text-[10px]">{item.priority}</Badge>
                            <span className="text-sm font-medium capitalize">{item.subtestCode}</span>
                            {item.isResubmission && <Badge variant="outline" className="text-[10px]">Re-submission</Badge>}
                            {item.turnaround === 'express' && <Badge className="text-[10px] bg-purple-100 text-purple-700 dark:bg-purple-950 dark:text-purple-300">Express</Badge>}
                          </div>
                          <div className="mt-2 space-y-1">
                            {item.reasons.map((reason, i) => <p key={i} className="text-sm text-muted-foreground">• {reason}</p>)}
                          </div>
                          <div className="flex gap-4 mt-2 text-xs text-muted-foreground">
                            <span><Clock className="w-3 h-3 inline mr-1" />{item.slaRemainingHours}h SLA remaining</span>
                            <span>Waiting: {item.hoursWaiting}h</span>
                            {item.daysToExam !== null && <span>Exam: {item.daysToExam}d away</span>}
                          </div>
                        </div>
                      </div>
                    </Card>
                  </MotionItem>
                );
              })}
            </div>
          </MotionSection>
        )}
      </div>
    </div>
  );
}
