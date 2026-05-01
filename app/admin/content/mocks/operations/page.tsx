'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { AlertTriangle, BarChart3, CalendarClock, Gauge, RefreshCcw, Users, type LucideIcon } from 'lucide-react';
import {
  AdminRoutePanel,
  AdminRouteSectionHeader,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchAdminMockAnalytics, fetchAdminMockBookings, fetchAdminMockRiskList } from '@/lib/api';

type Analytics = {
  windowDays?: number;
  attemptsStarted?: number;
  attemptsCompleted?: number;
  completionRate?: number;
  reportsGenerated?: number;
  averageReadinessScore?: number | null;
  greenReadinessCount?: number;
  markingDelayMetrics?: {
    queued?: number;
    inReview?: number;
    completed?: number;
    averageTurnaroundHours?: number;
  };
};

type RiskItem = {
  learnerId?: string;
  mockAttemptId?: string;
  reportId?: string;
  generatedAt?: string;
  overallScore?: number | null;
  risk?: string;
  weakness?: { subtest?: string; criterion?: string; description?: string };
  action?: string;
};

type BookingItem = {
  id?: string;
  bookingId?: string;
  title?: string;
  mockBundleTitle?: string;
  scheduledStartAt?: string;
  timezoneIana?: string;
  status?: string;
  liveRoomState?: string;
  assignedTutorId?: string | null;
  assignedInterlocutorId?: string | null;
  releasePolicy?: string;
};

function asItems<T>(value: unknown): T[] {
  if (!value || typeof value !== 'object' || !('items' in value)) return [];
  const items = (value as { items?: unknown }).items;
  return Array.isArray(items) ? (items as T[]) : [];
}

function formatDate(value?: string) {
  if (!value) return 'Not scheduled';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return value;
  }
}

function MetricCard({ icon: Icon, label, value, detail }: { icon: LucideIcon; label: string; value: string; detail?: string }) {
  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <div className="mb-3 flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
        <Icon className="h-5 w-5" />
      </div>
      <p className="text-xs font-black uppercase tracking-widest text-muted">{label}</p>
      <p className="mt-1 text-2xl font-black text-navy">{value}</p>
      {detail ? <p className="mt-1 text-xs text-muted">{detail}</p> : null}
    </div>
  );
}

export default function AdminMockOperationsPage() {
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [riskItems, setRiskItems] = useState<RiskItem[]>([]);
  const [bookings, setBookings] = useState<BookingItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  const load = useCallback(async () => {
    setLoading(true);
    setError('');
    try {
      const [analyticsResponse, riskResponse, bookingsResponse] = await Promise.all([
        fetchAdminMockAnalytics() as Promise<Analytics>,
        fetchAdminMockRiskList(),
        fetchAdminMockBookings(),
      ]);
      setAnalytics(analyticsResponse);
      setRiskItems(asItems<RiskItem>(riskResponse));
      setBookings(asItems<BookingItem>(bookingsResponse).slice(0, 12));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load mock operations.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  const delay = analytics?.markingDelayMetrics;
  const bookingCounts = useMemo(() => {
    return bookings.reduce<Record<string, number>>((acc, item) => {
      const key = item.status ?? 'scheduled';
      acc[key] = (acc[key] ?? 0) + 1;
      return acc;
    }, {});
  }, [bookings]);

  return (
    <AdminRouteWorkspace>
      <AdminRoutePanel>
        <AdminRouteSectionHeader
          eyebrow="Mocks V2 operations"
          title="Mock analytics, bookings, and learner risk"
          description="Monitor exam-fidelity usage, teacher-marking delays, scheduled Speaking/final-readiness sessions, and learners who need intervention."
          icon={BarChart3}
          actions={
            <Button type="button" variant="secondary" onClick={() => void load()} disabled={loading}>
              <RefreshCcw className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} /> Refresh
            </Button>
          }
        />

        {error ? <InlineAlert variant="error" className="mb-4">{error}</InlineAlert> : null}
        {loading ? (
          <div className="grid gap-4 md:grid-cols-3">
            {Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-32 rounded-2xl" />)}
          </div>
        ) : (
          <div className="space-y-6">
            <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-6">
              <MetricCard icon={Gauge} label="Attempts" value={String(analytics?.attemptsStarted ?? 0)} detail={`${analytics?.windowDays ?? 30}-day window`} />
              <MetricCard icon={Gauge} label="Completed" value={String(analytics?.attemptsCompleted ?? 0)} detail={`${analytics?.completionRate ?? 0}% completion`} />
              <MetricCard icon={BarChart3} label="Reports" value={String(analytics?.reportsGenerated ?? 0)} detail="Generated mock reports" />
              <MetricCard icon={Gauge} label="Avg readiness" value={analytics?.averageReadinessScore == null ? 'N/A' : String(analytics.averageReadinessScore)} detail={`${analytics?.greenReadinessCount ?? 0} green-ready`} />
              <MetricCard icon={Users} label="Review queue" value={String(delay?.queued ?? 0)} detail={`${delay?.inReview ?? 0} in review`} />
              <MetricCard icon={CalendarClock} label="Turnaround" value={`${Math.round(delay?.averageTurnaroundHours ?? 0)}h`} detail={`${delay?.completed ?? 0} completed`} />
            </div>

            <section className="grid gap-5 xl:grid-cols-[1.1fr_0.9fr]">
              <div className="rounded-2xl border border-border bg-surface p-5">
                <div className="mb-4 flex items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-black uppercase tracking-widest text-muted">Learner risk list</p>
                    <h2 className="mt-1 text-lg font-black text-navy">Needs remediation or teacher follow-up</h2>
                  </div>
                  <Badge variant={riskItems.length > 0 ? 'warning' : 'success'}>{riskItems.length} learners</Badge>
                </div>
                <div className="space-y-3">
                  {riskItems.map((item) => (
                    <article key={`${item.reportId ?? item.mockAttemptId}-${item.learnerId}`} className="rounded-xl border border-border bg-background-light p-4">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <p className="font-mono text-xs font-bold text-navy">{item.learnerId ?? 'learner'}</p>
                        <Badge variant={item.risk === 'red' ? 'danger' : 'warning'}>{item.risk ?? 'pending'}</Badge>
                      </div>
                      <p className="mt-2 text-sm text-muted">Score: {item.overallScore ?? 'pending'} / Weakness: {item.weakness?.subtest ?? 'unknown'} {item.weakness?.criterion ? `- ${item.weakness.criterion}` : ''}</p>
                      <p className="mt-1 text-xs text-muted">{item.action ?? 'Assign remediation or teacher follow-up.'}</p>
                    </article>
                  ))}
                  {riskItems.length === 0 ? <InlineAlert variant="success">No red or amber mock learners in the current risk window.</InlineAlert> : null}
                </div>
              </div>

              <div className="rounded-2xl border border-border bg-surface p-5">
                <div className="mb-4 flex items-center justify-between gap-3">
                  <div>
                    <p className="text-xs font-black uppercase tracking-widest text-muted">Bookings</p>
                    <h2 className="mt-1 text-lg font-black text-navy">Scheduled Speaking and readiness sessions</h2>
                  </div>
                  <div className="flex flex-wrap gap-1">
                    {Object.entries(bookingCounts).map(([status, count]) => <Badge key={status} variant="outline">{status}: {count}</Badge>)}
                  </div>
                </div>
                <div className="space-y-3">
                  {bookings.map((booking) => (
                    <article key={booking.bookingId ?? booking.id} className="rounded-xl border border-border bg-background-light p-4">
                      <div className="flex flex-wrap items-center justify-between gap-2">
                        <p className="text-sm font-black text-navy">{booking.title ?? booking.mockBundleTitle ?? booking.bookingId}</p>
                        <Badge variant={booking.status === 'completed' ? 'success' : booking.status === 'cancelled' ? 'muted' : 'info'}>{booking.status ?? 'scheduled'}</Badge>
                      </div>
                      <p className="mt-2 text-xs text-muted">{formatDate(booking.scheduledStartAt)} / {booking.timezoneIana ?? 'UTC'}</p>
                      <p className="mt-1 text-xs text-muted">Tutor: {booking.assignedTutorId ?? 'unassigned'} / Interlocutor: {booking.assignedInterlocutorId ?? 'unassigned'} / Release: {booking.releasePolicy ?? 'instant'}</p>
                    </article>
                  ))}
                  {bookings.length === 0 ? <InlineAlert variant="info">No mock bookings are scheduled yet.</InlineAlert> : null}
                </div>
              </div>
            </section>

            <InlineAlert variant="info">
              <AlertTriangle className="mr-2 inline h-4 w-4" /> Proctoring remains advisory: events inform reports and risk review, but never auto-block submission.
            </InlineAlert>
          </div>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
