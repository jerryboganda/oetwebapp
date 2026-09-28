'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { CalendarClock, MapPin, Plus, RefreshCw, X } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import {
  cancelMockBookingV2,
  fetchMockAvailability,
  fetchMockBookingList,
  rescheduleMockBookingV2,
} from '@/lib/api';
import type { MockAvailabilitySlot } from '@/lib/api';
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

function defaultRescheduleDate(): string {
  return new Date().toISOString().slice(0, 10);
}

function formatSlot(iso: string, timezone: string): string {
  try {
    return new Intl.DateTimeFormat(undefined, {
      weekday: 'short',
      day: 'numeric',
      month: 'short',
      hour: '2-digit',
      minute: '2-digit',
      timeZone: timezone || 'UTC',
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
  const [rescheduleDates, setRescheduleDates] = useState<Record<string, string>>({});
  const [availableSlots, setAvailableSlots] = useState<Record<string, MockAvailabilitySlot[]>>({});
  const [slotsLoadingId, setSlotsLoadingId] = useState<string | null>(null);

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
      await cancelMockBookingV2(id);
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
      await rescheduleMockBookingV2(id, next);
      setRescheduleDraft((prev) => {
        const out = { ...prev };
        delete out[id];
        return out;
      });
      setAvailableSlots((prev) => ({ ...prev, [id]: [] }));
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not reschedule booking.');
    } finally {
      setBusyId(null);
    }
  }, [rescheduleDraft, reload]);

  const findAvailableSlots = useCallback(async (booking: MockBooking) => {
    if (!booking.tutorProfileId) {
      setError('This booking has no assigned tutor calendar. Please contact support.');
      return;
    }
    const date = rescheduleDates[booking.id] ?? defaultRescheduleDate();
    setSlotsLoadingId(booking.id);
    setError(null);
    try {
      const result = await fetchMockAvailability(date, booking.timezoneIana, booking.mockBundleId);
      setAvailableSlots((prev) => ({
        ...prev,
        [booking.id]: result.slots.filter(
          (slot) => slot.isAvailable && slot.tutorProfileId === booking.tutorProfileId,
        ),
      }));
      setRescheduleDraft((prev) => ({ ...prev, [booking.id]: '' }));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load available tutor slots.');
    } finally {
      setSlotsLoadingId(null);
    }
  }, [rescheduleDates]);

  return (
    <>
      <LearnerPageHero
        eyebrow="Mocks"
        title="Your booked mocks"
        description="Speaking mocks use live tutor-calendar availability. You may reschedule before the session starts only to another currently available tutor slot."
        icon={CalendarClock}
      />

      <div className="mb-4 mt-6 flex flex-wrap items-center gap-2">
        <Button variant="ghost" onClick={() => void reload()} disabled={loading}>
          <RefreshCw className="h-4 w-4" aria-hidden="true" /> Refresh
        </Button>
        <Button asChild>
          <Link href="/mocks/setup">
            <Plus className="h-4 w-4" aria-hidden="true" /> Book a mock
          </Link>
        </Button>
      </div>

      {error ? <InlineAlert variant="error" className="mb-4">{error}</InlineAlert> : null}

      {loading ? (
        <div className="space-y-3">
          <Skeleton className="h-32 w-full" />
          <Skeleton className="h-32 w-full" />
        </div>
      ) : items.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border bg-background-light px-6 py-12 text-center text-sm text-muted">
          You have no upcoming bookings. Use the &quot;Book a mock&quot; button to schedule a Speaking or final-readiness mock.
        </div>
      ) : (
        <ul className="space-y-3">
          {items.map((b) => {
            const variant = STATUS_VARIANT[b.status] ?? 'default';
            const draft = rescheduleDraft[b.id] ?? '';
            const rescheduleDate = rescheduleDates[b.id] ?? defaultRescheduleDate();
            const slots = availableSlots[b.id] ?? [];
            const isTerminal = b.status === 'completed' || b.status === 'cancelled' || b.status === 'tutor_no_show' || b.status === 'learner_no_show';
            return (
              <li key={b.id} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                <div className="flex flex-wrap items-start justify-between gap-3">
                  <div>
                    <div className="text-sm font-semibold text-navy">
                      {formatScheduled(b.scheduledStartAt, b.timezoneIana)}
                    </div>
                    <div className="mt-1 flex flex-wrap items-center gap-2 text-xs text-muted">
                      <MapPin className="h-3 w-3" aria-hidden="true" /> {b.timezoneIana}
                      <span>·</span>
                      <span>{b.deliveryMode}</span>
                      <span>·</span>
                      <span>Reschedules used: {b.rescheduleCount ?? 0}</span>
                    </div>
                  </div>
                  <Badge variant={variant}>{b.status.replace(/_/g, ' ')}</Badge>
                </div>

                {!isTerminal ? (
                  <div className="mt-4 flex flex-wrap items-end gap-2">
                    <div>
                      <label htmlFor={`reschedule-date-${b.id}`} className="block text-xs font-medium text-muted mb-1">
                        Find another tutor slot
                      </label>
                      <input
                        id={`reschedule-date-${b.id}`}
                        type="date"
                        className="min-h-11 rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy focus:outline-none focus:ring-2 focus:ring-primary/30"
                        value={rescheduleDate}
                        min={defaultRescheduleDate()}
                        onChange={(e) => {
                          setRescheduleDates((prev) => ({ ...prev, [b.id]: e.target.value }));
                          setAvailableSlots((prev) => ({ ...prev, [b.id]: [] }));
                          setRescheduleDraft((prev) => ({ ...prev, [b.id]: '' }));
                        }}
                      />
                    </div>
                    <Button
                      variant="outline"
                      onClick={() => void findAvailableSlots(b)}
                      disabled={busyId === b.id || slotsLoadingId === b.id}
                    >
                      {slotsLoadingId === b.id ? 'Loading slots...' : 'Show available slots'}
                    </Button>
                    {slots.length > 0 ? (
                      <div className="basis-full grid gap-2 sm:grid-cols-2 lg:grid-cols-3">
                        {slots.map((slot) => (
                          <button
                            key={slot.startAt}
                            type="button"
                            onClick={() => setRescheduleDraft((prev) => ({ ...prev, [b.id]: slot.startAt }))}
                            className={`rounded-lg border px-3 py-2 text-left text-sm transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                              draft === slot.startAt
                                ? 'border-primary bg-primary/10 text-primary'
                                : 'border-border bg-surface text-navy hover:border-border-hover'
                            }`}
                            aria-pressed={draft === slot.startAt}
                          >
                            <span className="block font-semibold">{formatSlot(slot.startAt, b.timezoneIana)}</span>
                            <span className="block text-xs text-muted">{slot.tutorDisplayName}</span>
                          </button>
                        ))}
                      </div>
                    ) : null}
                    <Button
                      variant="primary"
                      onClick={() => void handleReschedule(b.id)}
                      disabled={!draft || busyId === b.id}
                    >
                      Reschedule
                    </Button>
                    <span className="basis-full text-xs text-muted">
                      Cancellation more than 24 hours before start: full refund eligible. At 24 hours or less: full refund unavailable.
                    </span>
                    <Button
                      variant="ghost"
                      onClick={() => void handleCancel(b.id)}
                      disabled={busyId === b.id}
                    >
                      <X className="h-4 w-4" aria-hidden="true" /> Cancel
                    </Button>
                    {b.tutorProfileId ? (
                      <Button variant="outline" asChild>
                        <Link href={`/mocks/speaking-room/${encodeURIComponent(b.bookingId ?? b.id)}`}>
                          Open LiveKit tutor room
                        </Link>
                      </Button>
                    ) : null}
                  </div>
                ) : null}
              </li>
            );
          })}
        </ul>
      )}
    </>
  );
}
