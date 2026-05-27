'use client';

// Mocks V2 Phase 5 — admin week-view booking calendar.
// Shows seven days x 30-minute rows (08:00 → 22:00) populated from
// `fetchAdminMockBookings(from, to)`. Clicking a chip opens an assignment
// modal where the admin can set the tutor / interlocutor via
// `assignAdminMockBooking`. Layout follows the existing admin route surface
// (AdminRouteHero + AdminRoutePanel) and uses the global admin shell that
// wraps every page under /app/admin.

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  AlertTriangle,
  ArrowLeft,
  CalendarRange,
  ChevronLeft,
  ChevronRight,
  RefreshCw,
} from 'lucide-react';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { EmptyState } from '@/components/ui/empty-error';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  assignAdminMockBooking,
  fetchAdminMockBookings,
  type AdminMockBookingRow,
} from '@/lib/api';

// Calendar layout constants.
const START_HOUR = 8;
const END_HOUR = 22; // exclusive upper bound; final row is 21:30
const ROW_MINUTES = 30;
const DAYS_PER_WEEK = 7;

const STATUS_VARIANT: Record<string, 'success' | 'info' | 'warning' | 'danger' | 'default'> = {
  scheduled: 'info',
  confirmed: 'info',
  in_progress: 'warning',
  completed: 'success',
  cancelled: 'default',
  tutor_no_show: 'danger',
  learner_no_show: 'danger',
};

function startOfMonday(date: Date): Date {
  const out = new Date(date);
  out.setHours(0, 0, 0, 0);
  const day = out.getDay(); // 0=Sun..6=Sat
  const offset = (day + 6) % 7; // Monday=0
  out.setDate(out.getDate() - offset);
  return out;
}

function addDays(date: Date, days: number): Date {
  const out = new Date(date);
  out.setDate(out.getDate() + days);
  return out;
}

function toIsoBoundary(date: Date): string {
  return date.toISOString();
}

function pad2(value: number): string {
  return value.toString().padStart(2, '0');
}

function formatSlotLabel(hour: number, minute: number): string {
  return `${pad2(hour)}:${pad2(minute)}`;
}

function formatDayHeader(day: Date): { weekday: string; date: string } {
  try {
    return {
      weekday: new Intl.DateTimeFormat(undefined, { weekday: 'short' }).format(day),
      date: new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short' }).format(day),
    };
  } catch {
    return { weekday: day.toDateString().slice(0, 3), date: day.toDateString().slice(4, 10) };
  }
}

interface CalendarRowSlot {
  hour: number;
  minute: number;
  label: string;
}

function buildRows(): CalendarRowSlot[] {
  const out: CalendarRowSlot[] = [];
  for (let hour = START_HOUR; hour < END_HOUR; hour += 1) {
    for (let minute = 0; minute < 60; minute += ROW_MINUTES) {
      out.push({ hour, minute, label: formatSlotLabel(hour, minute) });
    }
  }
  return out;
}

function slotKey(day: Date, hour: number, minute: number): string {
  return `${day.getFullYear()}-${pad2(day.getMonth() + 1)}-${pad2(day.getDate())}T${pad2(hour)}:${pad2(minute)}`;
}

function bookingSlotKey(iso: string): string {
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return '';
  // Snap to the 30-minute bucket the booking falls into so chips line up with
  // the grid even when the backend reports e.g. :05 due to rounding drift.
  const minute = date.getMinutes() < 30 ? 0 : 30;
  return slotKey(date, date.getHours(), minute);
}

interface AssignDraft {
  tutor: string;
  interlocutor: string;
}

export default function AdminMockBookingsCalendarPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const isAdmin = isAuthenticated && role === 'admin';

  const [weekStart, setWeekStart] = useState<Date>(() => startOfMonday(new Date()));
  const [bookings, setBookings] = useState<AdminMockBookingRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const [activeBooking, setActiveBooking] = useState<AdminMockBookingRow | null>(null);
  const [assignDraft, setAssignDraft] = useState<AssignDraft>({ tutor: '', interlocutor: '' });
  const [savingAssignment, setSavingAssignment] = useState(false);

  const weekDays = useMemo(() => {
    return Array.from({ length: DAYS_PER_WEEK }, (_, index) => addDays(weekStart, index));
  }, [weekStart]);

  const rows = useMemo(() => buildRows(), []);

  // Index bookings by their 30-min bucket so each cell render is O(1).
  const bucketed = useMemo(() => {
    const out = new Map<string, AdminMockBookingRow[]>();
    for (const booking of bookings) {
      const key = bookingSlotKey(booking.scheduledStartAt);
      if (!key) continue;
      const existing = out.get(key);
      if (existing) existing.push(booking);
      else out.set(key, [booking]);
    }
    return out;
  }, [bookings]);

  const load = useCallback(async () => {
    if (!isAdmin) return;
    setLoading(true);
    setError(null);
    try {
      const from = toIsoBoundary(weekStart);
      const to = toIsoBoundary(addDays(weekStart, DAYS_PER_WEEK));
      const response = await fetchAdminMockBookings({ from, to });
      setBookings(response.items ?? []);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load mock bookings for this week.');
      setBookings([]);
    } finally {
      setLoading(false);
    }
  }, [isAdmin, weekStart]);

  useEffect(() => {
    void load();
  }, [load]);

  const handleShiftWeek = (deltaWeeks: number) => {
    setWeekStart((current) => addDays(current, deltaWeeks * DAYS_PER_WEEK));
  };

  const handleThisWeek = () => {
    setWeekStart(startOfMonday(new Date()));
  };

  const openBookingModal = useCallback((booking: AdminMockBookingRow) => {
    setActiveBooking(booking);
    setAssignDraft({
      tutor: booking.assignedTutorId ?? '',
      interlocutor: booking.assignedInterlocutorId ?? '',
    });
  }, []);

  const closeBookingModal = useCallback(() => {
    setActiveBooking(null);
    setAssignDraft({ tutor: '', interlocutor: '' });
    setSavingAssignment(false);
  }, []);

  const handleAssign = useCallback(async () => {
    if (!activeBooking) return;
    setSavingAssignment(true);
    try {
      const payload: { assignedTutorId?: string | null; assignedInterlocutorId?: string | null } = {};
      const tutor = assignDraft.tutor.trim();
      const interlocutor = assignDraft.interlocutor.trim();
      payload.assignedTutorId = tutor.length > 0 ? tutor : null;
      payload.assignedInterlocutorId = interlocutor.length > 0 ? interlocutor : null;
      await assignAdminMockBooking(activeBooking.bookingId || activeBooking.id, payload);
      setToast({ variant: 'success', message: 'Assignment saved.' });
      closeBookingModal();
      await load();
    } catch (err) {
      setToast({
        variant: 'error',
        message: err instanceof Error ? err.message : 'Could not save assignment.',
      });
      setSavingAssignment(false);
    }
  }, [activeBooking, assignDraft, closeBookingModal, load]);

  const totalBookings = bookings.length;
  const unassignedCount = useMemo(
    () => bookings.filter((booking) => !booking.assignedTutorId).length,
    [bookings],
  );
  const startLabel = useMemo(
    () => new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short', year: 'numeric' }).format(weekStart),
    [weekStart],
  );
  const endLabel = useMemo(
    () => new Intl.DateTimeFormat(undefined, { day: '2-digit', month: 'short', year: 'numeric' }).format(
      addDays(weekStart, DAYS_PER_WEEK - 1),
    ),
    [weekStart],
  );

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminOperationsLayout
      eyebrow="Mock Operations"
      title="Mock booking calendar"
      description="Week view of every learner-booked mock. Click a chip to assign a tutor or interlocutor."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Content', href: '/admin/content' },
        { label: 'Mocks', href: '/admin/content/mocks' },
        { label: 'Bookings' },
      ]}
      actions={
        <Link
          href="/admin/content/mocks"
          className="inline-flex items-center text-sm font-bold text-admin-fg-muted hover:text-admin-fg-strong"
        >
          <ArrowLeft className="mr-1 h-4 w-4" /> Back to Mock Bundles
        </Link>
      }
    >
      <div className="flex flex-wrap items-center gap-2">
        <div className="ml-auto flex flex-wrap items-center gap-2">
          <Button variant="secondary" onClick={() => handleShiftWeek(-1)} disabled={loading}>
            <ChevronLeft className="h-4 w-4" /> Prev week
          </Button>
          <Button variant="ghost" onClick={handleThisWeek} disabled={loading}>
            This week
          </Button>
          <Button variant="secondary" onClick={() => handleShiftWeek(1)} disabled={loading}>
            Next week <ChevronRight className="h-4 w-4" />
          </Button>
          <Button variant="ghost" onClick={() => void load()} disabled={loading}>
            <RefreshCw className="h-4 w-4" /> Refresh
          </Button>
        </div>
      </div>

      <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
        <KpiTile
          label="Bookings this week"
          value={totalBookings}
          hint={`${startLabel} → ${endLabel}`}
          icon={<CalendarRange className="h-5 w-5" />}
        />
        <KpiTile
          label="Unassigned"
          value={unassignedCount}
          hint="No tutor selected yet"
          icon={<AlertTriangle className="h-5 w-5" />}
          tone={unassignedCount > 0 ? 'warning' : 'success'}
        />
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Card>
        <CardHeader>
          <div>
            <CardTitle>Week view</CardTitle>
            <p className="mt-1 text-sm text-admin-fg-muted">Rows are 30-minute slots from 08:00 to 22:00 in the operator&apos;s local timezone.</p>
          </div>
        </CardHeader>
        <CardContent>
        {loading ? (
          <div className="space-y-2">
            <Skeleton className="h-10 w-full rounded-xl" />
            <Skeleton className="h-10 w-full rounded-xl" />
            <Skeleton className="h-10 w-full rounded-xl" />
            <Skeleton className="h-10 w-full rounded-xl" />
            <Skeleton className="h-10 w-full rounded-xl" />
            <Skeleton className="h-10 w-full rounded-xl" />
          </div>
        ) : totalBookings === 0 ? (
          <EmptyState
            title="No bookings this week"
            description="No learner bookings fall inside this 7-day window. Shift the week or refresh."
            icon={<CalendarRange className="h-8 w-8" />}
          />
        ) : (
          <div className="overflow-x-auto">
            <table className="min-w-full border-collapse text-xs" aria-label="Mock booking calendar grid">
              <thead>
                <tr>
                  <th className="sticky left-0 z-10 w-20 border-b border-admin-border bg-admin-bg-surface px-2 py-2 text-left text-[10px] font-black uppercase tracking-widest text-admin-fg-muted">
                    Time
                  </th>
                  {weekDays.map((day) => {
                    const header = formatDayHeader(day);
                    return (
                      <th
                        key={day.toISOString()}
                        className="min-w-[140px] border-b border-admin-border px-2 py-2 text-left"
                      >
                        <p className="text-[10px] font-black uppercase tracking-widest text-admin-fg-muted">
                          {header.weekday}
                        </p>
                        <p className="text-xs font-bold text-admin-fg-strong">{header.date}</p>
                      </th>
                    );
                  })}
                </tr>
              </thead>
              <tbody>
                {rows.map((row) => (
                  <tr key={`${row.hour}-${row.minute}`} className="align-top">
                    <th
                      scope="row"
                      className="sticky left-0 z-10 w-20 border-b border-admin-border/60 bg-admin-surface px-2 py-1 text-left text-[11px] font-bold text-admin-fg-muted"
                    >
                      {row.label}
                    </th>
                    {weekDays.map((day) => {
                      const key = slotKey(day, row.hour, row.minute);
                      const cellBookings = bucketed.get(key) ?? [];
                      return (
                        <td
                          key={`${day.toISOString()}-${row.hour}-${row.minute}`}
                          className="border-b border-r border-admin-border/40 px-1 py-1 align-top"
                        >
                          <div className="flex flex-col gap-1">
                            {cellBookings.map((booking) => {
                              const variant = STATUS_VARIANT[booking.status] ?? 'default';
                              const tutorMissing = !booking.assignedTutorId;
                              return (
                                <button
                                  key={booking.bookingId || booking.id}
                                  type="button"
                                  onClick={() => openBookingModal(booking)}
                                  className={`group flex flex-col rounded-lg border px-2 py-1 text-left text-[11px] transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-violet-400 ${
                                    tutorMissing
                                      ? 'border-amber-500/40 bg-amber-500/10 hover:bg-amber-500/20'
                                      : 'border-admin-border bg-admin-bg-surface-raised hover:bg-admin-bg-elevated/60'
                                  }`}
                                  aria-label={`Open booking ${booking.bookingId || booking.id}`}
                                >
                                  <span className="font-bold text-admin-fg-strong">
                                    {booking.learnerDisplayName ?? booking.learnerEmail ?? 'Learner'}
                                  </span>
                                  <span className="truncate text-admin-fg-muted">
                                    {booking.mockBundleTitle ?? booking.title ?? booking.mockBundleId}
                                  </span>
                                  <div className="mt-1 flex flex-wrap items-center gap-1">
                                    <Badge variant={variant} className="text-[9px]">
                                      {booking.status.replace(/_/g, ' ')}
                                    </Badge>
                                    {tutorMissing ? (
                                      <Badge variant="warning" className="text-[9px]">unassigned</Badge>
                                    ) : null}
                                  </div>
                                </button>
                              );
                            })}
                          </div>
                        </td>
                      );
                    })}
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
        </CardContent>
      </Card>

      <Modal
        open={activeBooking !== null}
        onClose={closeBookingModal}
        title="Mock booking detail"
        size="lg"
      >
        {activeBooking ? (
          <div className="space-y-4 text-sm">
            <div className="rounded-xl border border-border bg-background-light p-3">
              <p className="text-xs font-black uppercase tracking-widest text-muted">Bundle</p>
              <p className="font-bold text-navy">
                {activeBooking.mockBundleTitle ?? activeBooking.title ?? activeBooking.mockBundleId}
              </p>
              <p className="mt-1 text-xs text-muted">{activeBooking.mockBundleId}</p>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <div className="rounded-xl border border-border bg-background-light p-3">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Learner</p>
                <p className="font-bold text-navy">
                  {activeBooking.learnerDisplayName ?? activeBooking.learnerEmail ?? 'Unknown'}
                </p>
                {activeBooking.learnerEmail ? (
                  <p className="mt-1 text-xs text-muted">{activeBooking.learnerEmail}</p>
                ) : null}
              </div>
              <div className="rounded-xl border border-border bg-background-light p-3">
                <p className="text-xs font-black uppercase tracking-widest text-muted">When</p>
                <p className="font-bold text-navy">
                  {new Date(activeBooking.scheduledStartAt).toLocaleString()}
                </p>
                <p className="mt-1 text-xs text-muted">{activeBooking.timezoneIana}</p>
              </div>
            </div>

            <div className="grid gap-3 sm:grid-cols-2">
              <div className="rounded-xl border border-border bg-background-light p-3">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Status</p>
                <Badge variant={STATUS_VARIANT[activeBooking.status] ?? 'default'}>
                  {activeBooking.status.replace(/_/g, ' ')}
                </Badge>
                {typeof activeBooking.rescheduleCount === 'number' ? (
                  <p className="mt-2 text-xs text-muted">
                    Reschedules used: {activeBooking.rescheduleCount}/3
                  </p>
                ) : null}
              </div>
              <div className="rounded-xl border border-border bg-background-light p-3">
                <p className="text-xs font-black uppercase tracking-widest text-muted">Delivery</p>
                <p className="font-bold text-navy">{activeBooking.deliveryMode ?? '—'}</p>
                {activeBooking.consentToRecording ? (
                  <p className="mt-1 text-xs text-muted">Consent to record: yes</p>
                ) : null}
              </div>
            </div>

            <div className="space-y-3 rounded-xl border border-border bg-surface p-3">
              <p className="text-xs font-black uppercase tracking-widest text-muted">Assign staff</p>
              <div className="grid gap-3 sm:grid-cols-2">
                <label className="flex flex-col gap-1.5 text-xs">
                  <span className="font-semibold text-navy">Tutor user ID</span>
                  <input
                    type="text"
                    value={assignDraft.tutor}
                    onChange={(event) => setAssignDraft((prev) => ({ ...prev, tutor: event.target.value }))}
                    placeholder="tutor-user-id"
                    className="rounded-md border border-border bg-background-light px-3 py-2 text-sm text-navy"
                  />
                  {activeBooking.assignedTutorDisplayName ? (
                    <span className="text-muted">Current: {activeBooking.assignedTutorDisplayName}</span>
                  ) : null}
                </label>
                <label className="flex flex-col gap-1.5 text-xs">
                  <span className="font-semibold text-navy">Interlocutor user ID</span>
                  <input
                    type="text"
                    value={assignDraft.interlocutor}
                    onChange={(event) => setAssignDraft((prev) => ({ ...prev, interlocutor: event.target.value }))}
                    placeholder="interlocutor-user-id"
                    className="rounded-md border border-border bg-background-light px-3 py-2 text-sm text-navy"
                  />
                  {activeBooking.assignedInterlocutorDisplayName ? (
                    <span className="text-muted">Current: {activeBooking.assignedInterlocutorDisplayName}</span>
                  ) : null}
                </label>
              </div>
              <p className="text-xs text-muted">
                Leave either field blank to clear the existing assignment.
              </p>
            </div>

            <div className="flex flex-wrap justify-end gap-2">
              <Button variant="secondary" onClick={closeBookingModal} disabled={savingAssignment}>
                Cancel
              </Button>
              <Button
                variant="primary"
                onClick={() => void handleAssign()}
                disabled={savingAssignment}
                loading={savingAssignment}
              >
                Save assignment
              </Button>
            </div>
          </div>
        ) : null}
      </Modal>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      ) : null}
    </AdminOperationsLayout>
  );
}
