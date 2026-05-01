'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { CalendarClock, MapPin, Plus, RefreshCw, X } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import {
  cancelMockBooking,
  fetchMockBookingList,
  rescheduleMockBooking,
} from '@/lib/api';
import type { MockBooking } from '@/lib/mock-data';

const STATUS_VARIANT: Record<string, 'success' | 'info' | 'warning' | 'danger' | 'default'> = {
  scheduled: 'info',
  confirmed: 'info',
  in_progress: 'warning',
  completed: 'success',
  cancelled: 'default',
  tutor_no_show: 'danger',
  learner_no_show: 'danger',
};

function formatScheduled(iso: string, tz: string): string {
  try {
    return new Intl.DateTimeFormat('en-GB', {
      dateStyle: 'full',
      timeStyle: 'short',
      timeZone: tz || 'UTC',
    }).format(new Date(iso));
  } catch {
    return new Date(iso).toLocaleString();
  }
}

export default function MockBookingsPage() {
  const [items, setItems] = useState<MockBooking[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [rescheduleDraft, setRescheduleDraft] = useState<Record<string, string>>({});

  const localTz = useMemo(() => {
    try {
      return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
    } catch {
      return 'UTC';
    }
  }, []);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const data = await fetchMockBookingList();
      setItems(data.items);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load bookings.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const handleCancel = useCallback(async (id: string) => {
    setBusyId(id);
    try {
      await cancelMockBooking(id);
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not cancel booking.');
    } finally {
      setBusyId(null);
    }
  }, [reload]);

  const handleReschedule = useCallback(async (id: string) => {
    const next = rescheduleDraft[id];
    if (!next) return;
    setBusyId(id);
    try {
      await rescheduleMockBooking(id, new Date(next).toISOString(), localTz);
      setRescheduleDraft((prev) => {
        const out = { ...prev };
        delete out[id];
        return out;
      });
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not reschedule booking.');
    } finally {
      setBusyId(null);
    }
  }, [rescheduleDraft, localTz, reload]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        eyebrow="Mocks"
        title="Your booked mocks"
        description="Speaking and final-readiness mocks are scheduled with a tutor. Reschedule up to 3 times — at least 12 hours before the slot."
        icon={CalendarClock}
      />

      <div className="mb-4 flex items-center gap-2">
        <Button variant="ghost" onClick={() => void reload()} disabled={loading}>
          <RefreshCw className="h-4 w-4 mr-1" /> Refresh
        </Button>
        <Link
          href="/mocks/setup"
          className="inline-flex items-center rounded-md bg-indigo-600 px-3 py-2 text-sm font-medium text-white hover:bg-indigo-500"
        >
          <Plus className="h-4 w-4 mr-1" /> Book a mock
        </Link>
      </div>

      {error ? <InlineAlert variant="error" className="mb-4">{error}</InlineAlert> : null}

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-32 w-full" />
          <Skeleton className="h-32 w-full" />
        </div>
      ) : items.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-slate-200 p-12 text-center text-slate-500">
          You have no upcoming bookings. Use the &quot;Book a mock&quot; button to schedule a Speaking or final-readiness mock.
        </div>
      ) : (
        <ul className="space-y-3">
          {items.map((b) => {
            const variant = STATUS_VARIANT[b.status] ?? 'default';
            const draft = rescheduleDraft[b.id] ?? '';
            const isTerminal = b.status === 'completed' || b.status === 'cancelled' || b.status === 'tutor_no_show' || b.status === 'learner_no_show';
            return (
              <li key={b.id} className="rounded-2xl border border-slate-200 bg-white p-5">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-slate-900">
                      {formatScheduled(b.scheduledStartAt, b.timezoneIana)}
                    </div>
                    <div className="mt-1 flex items-center gap-2 text-xs text-slate-500">
                      <MapPin className="h-3 w-3" /> {b.timezoneIana}
                      <span>·</span>
                      <span>{b.deliveryMode}</span>
                      <span>·</span>
                      <span>Reschedules used: {b.rescheduleCount}/3</span>
                    </div>
                  </div>
                  <Badge variant={variant}>{b.status.replace(/_/g, ' ')}</Badge>
                </div>

                {!isTerminal ? (
                  <div className="mt-4 flex flex-wrap items-end gap-2">
                    <div>
                      <label className="block text-xs font-medium text-slate-600 mb-1">
                        Reschedule to
                      </label>
                      <input
                        type="datetime-local"
                        className="rounded-md border border-slate-300 px-3 py-2 text-sm"
                        value={draft}
                        onChange={(e) => setRescheduleDraft((prev) => ({ ...prev, [b.id]: e.target.value }))}
                      />
                    </div>
                    <Button
                      variant="primary"
                      onClick={() => void handleReschedule(b.id)}
                      disabled={!draft || busyId === b.id || (b.rescheduleCount ?? 0) >= 3}
                    >
                      Reschedule
                    </Button>
                    <Button
                      variant="ghost"
                      onClick={() => void handleCancel(b.id)}
                      disabled={busyId === b.id}
                    >
                      <X className="h-4 w-4 mr-1" /> Cancel
                    </Button>
                    {b.zoomJoinUrl ? (
                      <a
                        href={b.zoomJoinUrl}
                        target="_blank"
                        rel="noopener noreferrer"
                        className="inline-flex items-center rounded-md border border-slate-300 px-3 py-2 text-sm font-medium text-slate-700 hover:bg-slate-50"
                      >
                        Join room
                      </a>
                    ) : null}
                    <Link
                      href={`/mocks/speaking-room/${encodeURIComponent(b.bookingId ?? b.id)}`}
                      className="inline-flex items-center rounded-md border border-primary/30 bg-primary/5 px-3 py-2 text-sm font-medium text-primary hover:bg-primary/10"
                    >
                      Open speaking room
                    </Link>
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </LearnerDashboardShell>
  );
}
