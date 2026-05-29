'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { AlertTriangle, CalendarClock, Gauge, RefreshCcw, Users, type LucideIcon } from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchAdminMockAnalytics,
  fetchAdminMockBookings,
  fetchAdminMockRiskList,
  transitionAdminMockBookingLiveRoomState,
  type MockLiveRoomTargetState,
} from '@/lib/api';

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
    <div className="rounded-admin border border-admin-border bg-admin-bg-surface p-4">
      <div className="flex items-center gap-2.5">
        <span className="inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]" aria-hidden="true">
          <Icon className="h-4 w-4" />
        </span>
        <p className="min-w-0 truncate text-xs font-black uppercase tracking-widest text-admin-fg-muted">{label}</p>
      </div>
      <p className="mt-3 text-2xl font-black text-admin-fg-strong">{value}</p>
      {detail ? <p className="mt-1 text-xs text-admin-fg-muted">{detail}</p> : null}
    </div>
  );
}

export default function AdminMockOperationsPage() {
  const [analytics, setAnalytics] = useState<Analytics | null>(null);
  const [riskItems, setRiskItems] = useState<RiskItem[]>([]);
  const [bookings, setBookings] = useState<BookingItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');
  const [transitioningBookingId, setTransitioningBookingId] = useState<string | null>(null);

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

  const transitionBooking = useCallback(async (booking: BookingItem, targetState: MockLiveRoomTargetState) => {
    const bookingId = booking.bookingId ?? booking.id;
    if (!bookingId) return;
    setTransitioningBookingId(bookingId);
    setError('');
    try {
      const updated = await transitionAdminMockBookingLiveRoomState(bookingId, targetState, {
        clientTransitionId: `admin-ops-${bookingId}-${targetState}-${Date.now()}`,
      });
      setBookings((current) => current.map((item) => {
        const itemId = item.bookingId ?? item.id;
        if (itemId !== bookingId) return item;
        return {
          ...item,
          status: updated.status,
          liveRoomState: updated.liveRoomState,
        };
      }));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update live-room state.');
    } finally {
      setTransitioningBookingId(null);
    }
  }, []);

  const delay = analytics?.markingDelayMetrics;
  const bookingCounts = useMemo(() => {
    return bookings.reduce<Record<string, number>>((acc, item) => {
      const key = item.status ?? 'scheduled';
      acc[key] = (acc[key] ?? 0) + 1;
      return acc;
    }, {});
  }, [bookings]);

  return (
    <AdminOperationsLayout
      eyebrow="Mocks V2 operations"
      title="Mock analytics, bookings, and learner risk"
      description="Monitor exam-fidelity usage, teacher-marking delays, scheduled Speaking/final-readiness sessions, and learners who need intervention."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Mocks', href: '/admin/content/mocks' },
        { label: 'Operations' },
      ]}
      actions={
        <Button type="button" variant="secondary" onClick={() => void load()} disabled={loading}>
          <RefreshCcw className={`mr-2 h-4 w-4 ${loading ? 'animate-spin' : ''}`} /> Refresh
        </Button>
      }
    >
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {loading ? (
        <div className="grid gap-4 md:grid-cols-3">
          {Array.from({ length: 6 }).map((_, index) => <Skeleton key={index} className="h-32 rounded-admin" />)}
        </div>
      ) : (
        <div className="space-y-6">
          <div className="grid gap-4 md:grid-cols-3 xl:grid-cols-6">
            <MetricCard icon={Gauge} label="Attempts" value={String(analytics?.attemptsStarted ?? 0)} detail={`${analytics?.windowDays ?? 30}-day window`} />
            <MetricCard icon={Gauge} label="Completed" value={String(analytics?.attemptsCompleted ?? 0)} detail={`${analytics?.completionRate ?? 0}% completion`} />
            <MetricCard icon={Gauge} label="Reports" value={String(analytics?.reportsGenerated ?? 0)} detail="Generated mock reports" />
            <MetricCard icon={Gauge} label="Avg readiness" value={analytics?.averageReadinessScore == null ? 'N/A' : String(analytics.averageReadinessScore)} detail={`${analytics?.greenReadinessCount ?? 0} green-ready`} />
            <MetricCard icon={Users} label="Review queue" value={String(delay?.queued ?? 0)} detail={`${delay?.inReview ?? 0} in review`} />
            <MetricCard icon={CalendarClock} label="Turnaround" value={`${Math.round(delay?.averageTurnaroundHours ?? 0)}h`} detail={`${delay?.completed ?? 0} completed`} />
          </div>

          <section className="grid gap-5 xl:grid-cols-[1.1fr_0.9fr]">
            <Card>
              <CardHeader>
                <div>
                  <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">Learner risk list</p>
                  <CardTitle className="mt-1">Needs remediation or teacher follow-up</CardTitle>
                </div>
                <Badge variant={riskItems.length > 0 ? 'warning' : 'success'}>{riskItems.length} learners</Badge>
              </CardHeader>
              <CardContent className="space-y-3">
                {riskItems.map((item) => (
                  <article key={`${item.reportId ?? item.mockAttemptId}-${item.learnerId}`} className="rounded-admin border border-admin-border bg-admin-bg-subtle p-4">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="font-mono text-xs font-bold text-admin-fg-strong">{item.learnerId ?? 'learner'}</p>
                      <Badge variant={item.risk === 'red' ? 'danger' : 'warning'}>{item.risk ?? 'pending'}</Badge>
                    </div>
                    <p className="mt-2 text-sm text-admin-fg-muted">Score: {item.overallScore ?? 'pending'} / Weakness: {item.weakness?.subtest ?? 'unknown'} {item.weakness?.criterion ? `- ${item.weakness.criterion}` : ''}</p>
                    <p className="mt-1 text-xs text-admin-fg-muted">{item.action ?? 'Assign remediation or teacher follow-up.'}</p>
                  </article>
                ))}
                {riskItems.length === 0 ? <InlineAlert variant="success">No red or amber mock learners in the current risk window.</InlineAlert> : null}
              </CardContent>
            </Card>

            <Card>
              <CardHeader>
                <div>
                  <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">Bookings</p>
                  <CardTitle className="mt-1">Scheduled Speaking and readiness sessions</CardTitle>
                </div>
                <div className="flex flex-wrap gap-1">
                  {Object.entries(bookingCounts).map(([status, count]) => <Badge key={status} variant="outline">{status}: {count}</Badge>)}
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                {bookings.map((booking) => (
                  <article key={booking.bookingId ?? booking.id} className="rounded-admin border border-admin-border bg-admin-bg-subtle p-4">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <p className="text-sm font-black text-admin-fg-strong">{booking.title ?? booking.mockBundleTitle ?? booking.bookingId}</p>
                      <Badge variant={booking.status === 'completed' ? 'success' : booking.status === 'cancelled' ? 'muted' : 'info'}>{booking.status ?? 'scheduled'}</Badge>
                    </div>
                    <p className="mt-2 text-xs text-admin-fg-muted">{formatDate(booking.scheduledStartAt)} / {booking.timezoneIana ?? 'UTC'}</p>
                    <p className="mt-1 text-xs text-admin-fg-muted">Tutor: {booking.assignedTutorId ?? 'unassigned'} / Interlocutor: {booking.assignedInterlocutorId ?? 'unassigned'} / Release: {booking.releasePolicy ?? 'instant'}</p>
                    <div className="mt-3 flex flex-wrap items-center gap-2">
                      <Badge variant="outline">Room: {booking.liveRoomState ?? 'waiting'}</Badge>
                      {(booking.liveRoomState ?? 'waiting') === 'waiting' ? (
                        <>
                          <Button size="sm" variant="outline" onClick={() => void transitionBooking(booking, 'in_progress')} loading={transitioningBookingId === (booking.bookingId ?? booking.id)}>
                            Start room
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => void transitionBooking(booking, 'tutor_no_show')} loading={transitioningBookingId === (booking.bookingId ?? booking.id)}>
                            Tutor no-show
                          </Button>
                          <Button size="sm" variant="outline" onClick={() => void transitionBooking(booking, 'learner_no_show')} loading={transitioningBookingId === (booking.bookingId ?? booking.id)}>
                            Learner no-show
                          </Button>
                        </>
                      ) : null}
                      {booking.liveRoomState === 'in_progress' ? (
                        <Button size="sm" variant="outline" onClick={() => void transitionBooking(booking, 'completed')} loading={transitioningBookingId === (booking.bookingId ?? booking.id)}>
                          Complete room
                        </Button>
                      ) : null}
                    </div>
                  </article>
                ))}
                {bookings.length === 0 ? <InlineAlert variant="info">No mock bookings are scheduled yet.</InlineAlert> : null}
              </CardContent>
            </Card>
          </section>

          <InlineAlert variant="info">
            <AlertTriangle className="mr-2 inline h-4 w-4" /> Proctoring remains advisory: events inform reports and risk review, but never auto-block submission.
          </InlineAlert>
        </div>
      )}
    </AdminOperationsLayout>
  );
}
