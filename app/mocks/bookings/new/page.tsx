'use client';

// Mocks V2 Phase 5 — learner booking page.
// Lets a learner pick a date (next 14 days), a published Speaking bundle,
// and an available slot in IANA-local time. Submits through `createMockBookingV2`
// (POST /v1/mocks/bookings); on `slot_taken` it transparently re-fetches
// availability so the learner immediately sees the updated grid.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter } from 'next/navigation';
import {
  CalendarDays,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Layers,
  Mic,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Checkbox } from '@/components/ui/form-controls';
import {
  createMockBookingV2,
  fetchMockAvailability,
  fetchMockOptions,
  isApiError,
  type MockAvailabilitySlot,
} from '@/lib/api';
import type { MockBundleOption, MockOptions } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';

const DAYS_AHEAD = 14;

function pad2(value: number): string {
  return value.toString().padStart(2, '0');
}

function toDateInputValue(date: Date): string {
  return `${date.getFullYear()}-${pad2(date.getMonth() + 1)}-${pad2(date.getDate())}`;
}

function buildDateRange(start: Date, days: number): Date[] {
  const out: Date[] = [];
  for (let index = 0; index < days; index += 1) {
    const next = new Date(start);
    next.setHours(0, 0, 0, 0);
    next.setDate(start.getDate() + index);
    out.push(next);
  }
  return out;
}

function formatTimeSlot(iso: string, timezone: string): string {
  try {
    return new Intl.DateTimeFormat(undefined, {
      hour: '2-digit',
      minute: '2-digit',
      timeZone: timezone || 'UTC',
    }).format(new Date(iso));
  } catch {
    return new Date(iso).toLocaleTimeString();
  }
}

function formatLongDate(date: Date): string {
  try {
    return new Intl.DateTimeFormat(undefined, {
      weekday: 'short',
      day: '2-digit',
      month: 'short',
    }).format(date);
  } catch {
    return date.toDateString();
  }
}

function isSpeakingBundle(bundle: MockBundleOption): boolean {
  if (bundle.subtest === 'speaking') return true;
  return bundle.sections.some((section) => section.subtest === 'speaking');
}

export default function NewMockBookingPage() {
  const router = useRouter();

  const timezone = useMemo(() => {
    try {
      return Intl.DateTimeFormat().resolvedOptions().timeZone || 'UTC';
    } catch {
      return 'UTC';
    }
  }, []);

  const todayStart = useMemo(() => {
    const now = new Date();
    now.setHours(0, 0, 0, 0);
    return now;
  }, []);
  const dateRange = useMemo(() => buildDateRange(todayStart, DAYS_AHEAD), [todayStart]);

  const [options, setOptions] = useState<MockOptions | null>(null);
  const [optionsLoading, setOptionsLoading] = useState(true);
  const [bundleId, setBundleId] = useState<string | null>(null);
  const [date, setDate] = useState<string>(toDateInputValue(todayStart));
  const [slots, setSlots] = useState<MockAvailabilitySlot[]>([]);
  const [slotsLoading, setSlotsLoading] = useState(false);
  const [selectedSlot, setSelectedSlot] = useState<string | null>(null);
  const [consent, setConsent] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error' | 'info'; message: string } | null>(null);

  // Load bundles once, then auto-select a Speaking bundle (the main booking case).
  useEffect(() => {
    let cancelled = false;
    setOptionsLoading(true);
    fetchMockOptions()
      .then((result) => {
        if (cancelled) return;
        setOptions(result);
        const speaking = result.availableBundles.find(isSpeakingBundle);
        const fallback = result.availableBundles[0] ?? null;
        const chosen = speaking ?? fallback;
        if (chosen) setBundleId(chosen.bundleId);
      })
      .catch((err) => {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not load mock bundles.');
      })
      .finally(() => {
        if (!cancelled) setOptionsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, []);

  // Whenever date or bundle changes, refresh availability.
  const loadAvailability = useCallback(async () => {
    if (!date) return;
    setSlotsLoading(true);
    setError(null);
    try {
      const { slots: nextSlots } = await fetchMockAvailability(date, timezone, bundleId ?? undefined);
      setSlots(nextSlots);
      setSelectedSlot((current) => {
        if (!current) return null;
        const stillAvailable = nextSlots.some(
          (slot) => slot.startAt === current && slot.isAvailable,
        );
        return stillAvailable ? current : null;
      });
    } catch (err) {
      setSlots([]);
      setError(err instanceof Error ? err.message : 'Could not load slot availability.');
    } finally {
      setSlotsLoading(false);
    }
  }, [bundleId, date, timezone]);

  useEffect(() => {
    void loadAvailability();
  }, [loadAvailability]);

  const speakingBundles = useMemo(() => {
    if (!options) return [];
    return options.availableBundles.filter(isSpeakingBundle);
  }, [options]);

  const otherBundles = useMemo(() => {
    if (!options) return [];
    return options.availableBundles.filter((bundle) => !isSpeakingBundle(bundle));
  }, [options]);

  const selectedBundle = useMemo(() => {
    if (!options || !bundleId) return null;
    return options.availableBundles.find((bundle) => bundle.bundleId === bundleId) ?? null;
  }, [bundleId, options]);

  const handleSubmit = useCallback(async () => {
    if (!selectedBundle || !selectedSlot) {
      setError('Pick a bundle and an available slot before booking.');
      return;
    }
    if (!consent) {
      setError('Please confirm the recording consent before booking.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      await createMockBookingV2({
        bundleId: selectedBundle.bundleId,
        scheduledStartAt: selectedSlot,
        timezone,
        consentToRecording: true,
      });
      analytics.track('mock_booking_created', {
        bundleId: selectedBundle.bundleId,
        scheduledStartAt: selectedSlot,
        source: 'availability_calendar',
      });
      router.push('/mocks/bookings');
    } catch (err) {
      const code = isApiError(err) ? err.code : null;
      if (code === 'slot_taken' || code === 'conflict') {
        setToast({
          variant: 'error',
          message: 'That slot was taken just now. We refreshed the calendar — please pick another time.',
        });
        await loadAvailability();
      } else {
        setError(err instanceof Error ? err.message : 'Could not create the booking.');
      }
      setSubmitting(false);
    }
  }, [consent, loadAvailability, router, selectedBundle, selectedSlot, timezone]);

  const dateIndex = dateRange.findIndex((day) => toDateInputValue(day) === date);
  const canShiftBackward = dateIndex > 0;
  const canShiftForward = dateIndex >= 0 && dateIndex < dateRange.length - 1;

  const shiftDate = (delta: number) => {
    if (dateIndex < 0) return;
    const next = dateRange[Math.min(dateRange.length - 1, Math.max(0, dateIndex + delta))];
    if (next) {
      setDate(toDateInputValue(next));
      setSelectedSlot(null);
    }
  };

  const noSlotsBecauseOfDate = !slotsLoading && slots.length === 0 && !error;

  return (
    <LearnerDashboardShell pageTitle="Book a Mock" subtitle="Pick a slot for your live Speaking or final-readiness mock" backHref="/mocks/bookings">
      <div className="space-y-8 pb-24">
        <LearnerPageHero
          eyebrow="Mock Booking"
          icon={CalendarDays}
          accent="navy"
          title="Book a live mock with a tutor"
          description="Pick a date in the next two weeks and choose an available slot. Speaking and final-readiness mocks are conducted live with a recorded session for tutor review."
          highlights={[
            { icon: Clock, label: 'Timezone', value: timezone },
            { icon: Layers, label: 'Bundles', value: `${options?.availableBundles.length ?? 0} published` },
            { icon: Mic, label: 'Mode', value: 'Live + recorded' },
          ]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8" aria-label="Choose a bundle">
          <LearnerSurfaceSectionHeader
            eyebrow="1. Bundle"
            title="Choose the mock you'd like to book"
            description="Speaking mocks default first. Final-readiness and other live bundles are also bookable."
            icon={Layers}
            className="mb-4"
          />
          {optionsLoading ? (
            <div className="grid gap-3 sm:grid-cols-2">
              <Skeleton className="h-24 rounded-2xl" />
              <Skeleton className="h-24 rounded-2xl" />
            </div>
          ) : !options || options.availableBundles.length === 0 ? (
            <InlineAlert variant="info">
              No published bundles are bookable right now. Ask an admin to publish a Speaking mock bundle.
            </InlineAlert>
          ) : (
            <div className="space-y-4">
              {speakingBundles.length > 0 ? (
                <div>
                  <p className="mb-2 text-xs font-black uppercase tracking-widest text-muted">Speaking bundles</p>
                  <div className="grid gap-3 lg:grid-cols-2">
                    {speakingBundles.map((bundle) => (
                      <button
                        key={bundle.bundleId}
                        type="button"
                        onClick={() => {
                          setBundleId(bundle.bundleId);
                          setSelectedSlot(null);
                        }}
                        className={`rounded-2xl border p-4 text-left transition-colors ${
                          bundleId === bundle.bundleId
                            ? 'border-primary bg-primary/5'
                            : 'border-border bg-surface hover:border-border-hover'
                        }`}
                        aria-pressed={bundleId === bundle.bundleId}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <p className="text-base font-black text-navy">{bundle.title}</p>
                            <p className="mt-1 text-xs text-muted">
                              {bundle.sections.length} section{bundle.sections.length === 1 ? '' : 's'} / {bundle.estimatedDurationMinutes} min
                            </p>
                          </div>
                          {bundleId === bundle.bundleId ? <CheckCircle2 className="h-5 w-5 text-primary" /> : null}
                        </div>
                        <div className="mt-3 flex flex-wrap gap-2">
                          <Badge variant="info">Speaking</Badge>
                          {bundle.releasePolicy ? (
                            <Badge variant="warning">{bundle.releasePolicy.replace(/_/g, ' ')}</Badge>
                          ) : null}
                        </div>
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}
              {otherBundles.length > 0 ? (
                <div>
                  <p className="mb-2 text-xs font-black uppercase tracking-widest text-muted">Other bookable bundles</p>
                  <div className="grid gap-3 lg:grid-cols-2">
                    {otherBundles.map((bundle) => (
                      <button
                        key={bundle.bundleId}
                        type="button"
                        onClick={() => {
                          setBundleId(bundle.bundleId);
                          setSelectedSlot(null);
                        }}
                        className={`rounded-2xl border p-4 text-left transition-colors ${
                          bundleId === bundle.bundleId
                            ? 'border-primary bg-primary/5'
                            : 'border-border bg-surface hover:border-border-hover'
                        }`}
                        aria-pressed={bundleId === bundle.bundleId}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div>
                            <p className="text-base font-black text-navy">{bundle.title}</p>
                            <p className="mt-1 text-xs text-muted">
                              {bundle.mockType.replace(/_/g, ' ')} / {bundle.estimatedDurationMinutes} min
                            </p>
                          </div>
                          {bundleId === bundle.bundleId ? <CheckCircle2 className="h-5 w-5 text-primary" /> : null}
                        </div>
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          )}
        </section>

        <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8" aria-label="Pick a date">
          <LearnerSurfaceSectionHeader
            eyebrow="2. Date"
            title="Pick the day you'd like to book"
            description="Bookings open for the next two weeks. Slots are 30 minutes each in your local timezone."
            icon={CalendarDays}
            className="mb-4"
          />
          <div className="flex items-center justify-between gap-2 sm:hidden">
            <Button variant="outline" onClick={() => shiftDate(-1)} disabled={!canShiftBackward}>
              <ChevronLeft className="h-4 w-4" /> Prev
            </Button>
            <p className="text-sm font-bold text-navy">{date}</p>
            <Button variant="outline" onClick={() => shiftDate(1)} disabled={!canShiftForward}>
              Next <ChevronRight className="h-4 w-4" />
            </Button>
          </div>
          <div className="mt-4 hidden flex-wrap gap-2 sm:flex">
            {dateRange.map((day) => {
              const value = toDateInputValue(day);
              const isSelected = value === date;
              return (
                <button
                  key={value}
                  type="button"
                  onClick={() => {
                    setDate(value);
                    setSelectedSlot(null);
                  }}
                  className={`rounded-xl border px-3 py-2 text-center text-xs font-bold transition-colors ${
                    isSelected
                      ? 'border-primary bg-primary/10 text-primary'
                      : 'border-border bg-surface text-navy hover:border-border-hover hover:bg-background-light'
                  }`}
                  aria-pressed={isSelected}
                >
                  {formatLongDate(day)}
                </button>
              );
            })}
          </div>
        </section>

        <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8" aria-label="Pick a time slot">
          <LearnerSurfaceSectionHeader
            eyebrow="3. Time"
            title="Pick an available slot"
            description="Greyed-out slots are unavailable. Hover the slot to see why."
            icon={Clock}
            className="mb-4"
          />
          {slotsLoading ? (
            <div className="grid gap-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6">
              {Array.from({ length: 12 }).map((_, index) => (
                <Skeleton key={index} className="h-10 rounded-xl" />
              ))}
            </div>
          ) : noSlotsBecauseOfDate ? (
            <InlineAlert variant="info">
              No slots are published for {date}. Pick another day or check back soon.
            </InlineAlert>
          ) : (
            <div
              className="grid gap-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6"
              role="radiogroup"
              aria-label="Available time slots"
            >
              {slots.map((slot) => {
                const isSelected = selectedSlot === slot.startAt;
                const disabled = !slot.isAvailable;
                const label = formatTimeSlot(slot.startAt, timezone);
                return (
                  <button
                    key={slot.startAt}
                    type="button"
                    role="radio"
                    aria-checked={isSelected}
                    onClick={() => slot.isAvailable && setSelectedSlot(slot.startAt)}
                    disabled={disabled}
                    title={disabled ? slot.blockedReason ?? 'Not available' : undefined}
                    className={`rounded-xl border px-3 py-2 text-sm font-bold transition-colors ${
                      isSelected
                        ? 'border-primary bg-primary/10 text-primary'
                        : disabled
                          ? 'cursor-not-allowed border-border bg-background-light text-muted opacity-60'
                          : 'border-border bg-surface text-navy hover:border-border-hover hover:bg-background-light'
                    }`}
                  >
                    {label}
                  </button>
                );
              })}
            </div>
          )}
        </section>

        <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm sm:p-8" aria-label="Consent and confirmation">
          <LearnerSurfaceSectionHeader
            eyebrow="4. Confirm"
            title="Confirm and book"
            description="Live mocks are recorded so your tutor can review your performance. Reschedule limits are admin-configurable and default to two changes per booking."
            icon={CheckCircle2}
            className="mb-4"
          />
          <Checkbox
            label="I consent to recording for tutor review and to the platform's mock content policy."
            checked={consent}
            onChange={(event) => setConsent(event.target.checked)}
          />
          {selectedBundle && selectedSlot ? (
            <div className="mt-4 rounded-2xl border border-border bg-background-light p-4 text-sm">
              <p className="font-bold text-navy">{selectedBundle.title}</p>
              <p className="mt-1 text-muted">
                {formatLongDate(new Date(selectedSlot))} / {formatTimeSlot(selectedSlot, timezone)} ({timezone})
              </p>
            </div>
          ) : null}
        </section>

        <div className="sticky bottom-4 z-10 rounded-2xl border border-border bg-surface/95 p-3 shadow-lg backdrop-blur">
          <Button
            onClick={handleSubmit}
            disabled={submitting || !selectedBundle || !selectedSlot || !consent}
            loading={submitting}
            size="lg"
            className="w-full gap-2 py-5 text-base font-black"
          >
            {submitting ? 'Booking…' : 'Book this slot'}
          </Button>
          {!selectedBundle ? (
            <p className="mt-3 text-center text-xs text-muted">Pick a bundle to continue.</p>
          ) : !selectedSlot ? (
            <p className="mt-3 text-center text-xs text-muted">Pick an available slot to continue.</p>
          ) : !consent ? (
            <p className="mt-3 text-center text-xs text-muted">Confirm the recording consent to enable booking.</p>
          ) : null}
        </div>
      </div>

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      ) : null}
    </LearnerDashboardShell>
  );
}
