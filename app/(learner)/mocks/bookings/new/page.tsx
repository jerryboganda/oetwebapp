'use client';

// Mocks V2 Phase 5 — learner booking page.
// Lets a learner pick a date (next 14 days), a published Speaking bundle,
// and an available slot in IANA-local time. Submits through `createMockBookingV2`
// (POST /v1/mocks/bookings); on `slot_taken` it transparently re-fetches
// availability so the learner immediately sees the updated grid.

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import {
  CalendarDays,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Clock,
  Layers,
  Mic,
} from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { Checkbox } from '@/components/ui/form-controls';
import {
  createMockBookingV2,
  fetchMockSpeakingAccess,
  fetchMockAvailability,
  fetchMockOptions,
  isApiError,
  type MockAvailabilitySlot,
} from '@/lib/api';
import type { MockBundleOption, MockOptions } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';

const DAYS_AHEAD = 14;
const TUTOR_ROOMS_UNAVAILABLE_MESSAGE = 'Live tutor sessions are temporarily unavailable.';

function isTutorRoomsUnavailable(err: unknown): boolean {
  return isApiError(err) && err.code === 'tutor_rooms_unavailable';
}

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
  const searchParams = useSearchParams();
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? undefined;
  const mockSectionId = searchParams?.get('mockSectionId') ?? undefined;

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
  const [selectedTutorProfileId, setSelectedTutorProfileId] = useState<string | null>(null);
  const [consent, setConsent] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error' | 'info'; message: string } | null>(null);
  const [speakingAccess, setSpeakingAccess] = useState<{ requiresAiOnly: boolean; daysUntilExam: number | null } | null>(null);
  const [speakingAccessError, setSpeakingAccessError] = useState<string | null>(null);
  // B9: LiveKit not configured → the server answers 503 tutor_rooms_unavailable.
  const [roomsUnavailable, setRoomsUnavailable] = useState(false);

  // Load bundles once, then auto-select a Speaking bundle (the main booking case).
  useEffect(() => {
    let cancelled = false;
    setOptionsLoading(true);
    fetchMockOptions()
      .then((result) => {
        if (cancelled) return;
        setOptions(result);
        const speaking = result.availableBundles.find(isSpeakingBundle);
        if (speaking) setBundleId(speaking.bundleId);
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

  useEffect(() => {
    let cancelled = false;
    fetchMockSpeakingAccess()
      .then((result) => {
        if (!cancelled) setSpeakingAccess(result);
      })
      .catch(() => {
        // Fail closed: do not offer a tutor booking until the authoritative
        // server decision is available.
        if (!cancelled) setSpeakingAccessError('Could not verify whether tutor booking is available. Please try again.');
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
        const stillAvailable = nextSlots.find(
          (slot) => slot.startAt === current && slot.isAvailable,
        );
        if (!stillAvailable) {
          setSelectedTutorProfileId(null);
          return null;
        }
        setSelectedTutorProfileId(stillAvailable.tutorProfileId);
        return current;
      });
    } catch (err) {
      setSlots([]);
      if (isTutorRoomsUnavailable(err)) setRoomsUnavailable(true);
      else setError(err instanceof Error ? err.message : 'Could not load slot availability.');
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

  const selectedBundle = useMemo(() => {
    if (!options || !bundleId) return null;
    return options.availableBundles.find((bundle) => bundle.bundleId === bundleId) ?? null;
  }, [bundleId, options]);

  const handleSubmit = useCallback(async () => {
    if (!selectedBundle || !selectedSlot || !selectedTutorProfileId) {
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
        mockAttemptId,
        mockSectionId,
        tutorProfileId: selectedTutorProfileId!,
      });
      analytics.track('mock_booking_created', {
        bundleId: selectedBundle.bundleId,
        scheduledStartAt: selectedSlot,
        source: 'availability_calendar',
      });
      router.push('/mocks/bookings');
    } catch (err) {
      const code = isApiError(err) ? err.code : null;
      if (code === 'tutor_rooms_unavailable') {
        setRoomsUnavailable(true);
        setSelectedSlot(null);
        setSelectedTutorProfileId(null);
      } else if (code === 'slot_taken' || code === 'conflict') {
        setToast({
          variant: 'error',
          message: 'That slot was taken just now. We refreshed the calendar, so please pick another time.',
        });
        await loadAvailability();
      } else {
        setError(err instanceof Error ? err.message : 'Could not create the booking.');
      }
      setSubmitting(false);
    }
  }, [consent, loadAvailability, router, selectedBundle, selectedSlot, selectedTutorProfileId, timezone, mockAttemptId, mockSectionId]);

  const dateIndex = dateRange.findIndex((day) => toDateInputValue(day) === date);
  const canShiftBackward = dateIndex > 0;
  const canShiftForward = dateIndex >= 0 && dateIndex < dateRange.length - 1;

  const shiftDate = (delta: number) => {
    if (dateIndex < 0) return;
    const next = dateRange[Math.min(dateRange.length - 1, Math.max(0, dateIndex + delta))];
    if (next) {
      setDate(toDateInputValue(next));
      setSelectedSlot(null);
      setSelectedTutorProfileId(null);
    }
  };

  const noSlotsBecauseOfDate = !slotsLoading && slots.length === 0 && !error && !roomsUnavailable;

  const hero = (
    <LearnerPageHero
      eyebrow="Mock Booking"
      icon={CalendarDays}
      accent="navy"
      title="Book a live mock with a tutor"
      description="Pick a date in the next two weeks and choose an available Speaking tutor slot. The session is recorded for tutor review."
      highlights={[
        { icon: Clock, label: 'Timezone', value: timezone },
        { icon: Layers, label: 'Bundles', value: `${speakingBundles.length} published` },
        { icon: Mic, label: 'Mode', value: 'Live + recorded' },
      ]}
    />
  );

  if (!speakingAccess) {
    return (
      <>
        {hero}
        <InlineAlert variant={speakingAccessError ? 'error' : 'info'} live={speakingAccessError ? 'assertive' : 'polite'}>
          {speakingAccessError ?? 'Checking whether tutor booking is available...'}
        </InlineAlert>
      </>
    );
  }

  return (
    <>
      {hero}

      {roomsUnavailable ? (
        <InlineAlert variant="warning" data-testid="tutor-rooms-unavailable">
          {TUTOR_ROOMS_UNAVAILABLE_MESSAGE}
        </InlineAlert>
      ) : null}

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <MotionSection>
        <Card padding="lg" role="region" aria-label="Choose a bundle">
          <LearnerSurfaceSectionHeader
            eyebrow="1. Bundle"
            title="Choose the mock you'd like to book"
            description="Only published bundles containing the Speaking section can use this tutor calendar."
            icon={Layers}
            className="mb-4"
          />
          {optionsLoading ? (
            <div className="grid grid-cols-1 gap-3 sm:grid-cols-2" role="status" aria-busy="true" aria-label="Loading bookable bundles">
              <Skeleton className="h-24 rounded-2xl" />
              <Skeleton className="h-24 rounded-2xl" />
            </div>
          ) : !options || options.availableBundles.length === 0 ? (
            <InlineAlert variant="info" live="polite">
              No published bundles are bookable right now. Ask an admin to publish a Speaking mock bundle.
            </InlineAlert>
          ) : (
            <div className="space-y-4">
              {speakingBundles.length > 0 ? (
                <div>
                  <p className="mb-2 eyebrow text-muted">Speaking bundles</p>
                  <div className="grid grid-cols-1 gap-3 lg:grid-cols-2">
                    {speakingBundles.map((bundle) => (
                      <button
                        key={bundle.bundleId}
                        type="button"
                        onClick={() => {
                          setBundleId(bundle.bundleId);
                          setSelectedSlot(null);
                        }}
                        className={`rounded-2xl border p-4 text-start transition-colors ${
                          bundleId === bundle.bundleId
                            ? 'border-primary bg-primary/5'
                            : 'border-border bg-surface hover:border-border-hover'
                        }`}
                        aria-pressed={bundleId === bundle.bundleId}
                      >
                        <div className="flex items-start justify-between gap-3">
                          <div className="min-w-0">
                            <p className="text-base font-bold text-navy">{bundle.title}</p>
                            <p className="mt-1 text-xs tabular-nums text-muted">
                              {bundle.sections.length} section{bundle.sections.length === 1 ? '' : 's'} / {bundle.estimatedDurationMinutes} min
                            </p>
                          </div>
                          {bundleId === bundle.bundleId ? <CheckCircle2 className="h-5 w-5 shrink-0 text-primary" aria-hidden="true" /> : null}
                        </div>
                        <div className="mt-3 flex flex-wrap gap-2">
                          <Badge className="border-skill-speaking/20 bg-skill-speaking/10 text-skill-speaking">Speaking</Badge>
                          {bundle.releasePolicy ? (
                            <Badge variant="warning">{bundle.releasePolicy.replace(/_/g, ' ')}</Badge>
                          ) : null}
                        </div>
                      </button>
                    ))}
                  </div>
                </div>
              ) : null}
            </div>
          )}
        </Card>
      </MotionSection>

      <MotionSection delayIndex={1}>
        <Card padding="lg" role="region" aria-label="Pick a date">
          <LearnerSurfaceSectionHeader
            eyebrow="2. Date"
            title="Pick the day you'd like to book"
            description="Bookings open for the next two weeks. Slots are 30 minutes each in your local timezone."
            icon={CalendarDays}
            className="mb-4"
          />
          <div className="flex items-center justify-between gap-2 sm:hidden">
            <Button variant="outline" onClick={() => shiftDate(-1)} disabled={!canShiftBackward}>
              <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" /> Prev
            </Button>
            <p className="text-sm font-bold tabular-nums text-navy">{date}</p>
            <Button variant="outline" onClick={() => shiftDate(1)} disabled={!canShiftForward}>
              Next <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
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
                  className={`rounded-control border px-3 py-2 text-center text-xs font-bold tabular-nums transition-colors ${
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
        </Card>
      </MotionSection>

      <MotionSection delayIndex={2}>
        <Card padding="lg" role="region" aria-label="Pick a time slot">
          <LearnerSurfaceSectionHeader
            eyebrow="3. Time"
            title="Pick an available slot"
            description="Greyed-out slots are unavailable. Hover the slot to see why."
            icon={Clock}
            className="mb-4"
          />
          {slotsLoading ? (
            <div className="grid grid-cols-1 gap-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6" role="status" aria-busy="true" aria-label="Loading available slots">
              {Array.from({ length: 12 }).map((_, index) => (
                <Skeleton key={index} className="h-11 rounded-control" />
              ))}
            </div>
          ) : noSlotsBecauseOfDate ? (
            <InlineAlert variant="info" live="polite">
              No slots are published for {date}. Pick another day or check back soon.
            </InlineAlert>
          ) : (
            <div
              className="grid grid-cols-1 gap-2 sm:grid-cols-3 lg:grid-cols-4 xl:grid-cols-6"
              role="radiogroup"
              aria-label="Available time slots"
            >
              {slots.map((slot) => {
                const isSelected = selectedSlot === slot.startAt && selectedTutorProfileId === slot.tutorProfileId;
                const disabled = !slot.isAvailable;
                const label = formatTimeSlot(slot.startAt, slot.tutorTimezone || timezone);
                return (
                  <button
                    key={`${slot.tutorProfileId}:${slot.startAt}`}
                    type="button"
                    role="radio"
                    aria-checked={isSelected}
                    onClick={() => {
                      if (!slot.isAvailable) return;
                      setSelectedSlot(slot.startAt);
                      setSelectedTutorProfileId(slot.tutorProfileId);
                    }}
                    disabled={disabled}
                    title={disabled ? slot.blockedReason ?? 'Not available' : undefined}
                    className={`min-h-11 rounded-control border px-3 py-2 text-sm font-bold tabular-nums transition-colors ${
                      isSelected
                        ? 'border-primary bg-primary/10 text-primary'
                        : disabled
                          ? 'cursor-not-allowed border-border bg-background-light text-muted opacity-60'
                          : 'border-border bg-surface text-navy hover:border-border-hover hover:bg-background-light'
                    }`}
                  >
                    {label} · {slot.tutorDisplayName}
                  </button>
                );
              })}
            </div>
          )}
        </Card>
      </MotionSection>

      <MotionSection delayIndex={3}>
        <Card padding="lg" role="region" aria-label="Consent and confirmation">
          <LearnerSurfaceSectionHeader
            eyebrow="4. Confirm"
            title="Confirm and book"
            description="Live mocks are recorded so your tutor can review your performance. You may reschedule before the session starts to another slot currently open in the tutor calendar."
            icon={CheckCircle2}
            className="mb-4"
          />
          <Checkbox
            label="I consent to recording for tutor review and to the platform's mock content policy."
            checked={consent}
            onChange={(event) => setConsent(event.target.checked)}
          />
          {selectedBundle && selectedSlot ? (
            <div className="mt-4 rounded-xl border border-border bg-background-light p-4 text-sm">
              <p className="font-bold text-navy">{selectedBundle.title}</p>
              <p className="mt-1 tabular-nums text-muted">
                {formatLongDate(new Date(selectedSlot))} / {formatTimeSlot(selectedSlot, timezone)} ({timezone})
              </p>
            </div>
          ) : null}
        </Card>
      </MotionSection>

      <div className="sticky bottom-4 z-10 rounded-2xl border border-border bg-surface/95 p-3 shadow-lg backdrop-blur">
        <Button
          onClick={handleSubmit}
          disabled={roomsUnavailable || submitting || !selectedBundle || !selectedSlot || !selectedTutorProfileId || !consent}
          loading={submitting}
          size="lg"
          fullWidth
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

      {toast ? (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      ) : null}
    </>
  );
}
