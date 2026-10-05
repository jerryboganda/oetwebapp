'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Mic, Calendar, Star, Clock, CreditCard, Video, X, ChevronLeft, ChevronRight, User, Download, ShoppingBag, Globe } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, cardClassName } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { Select, Textarea } from '@/components/ui/form-controls';
import { TabPanel, Tabs } from '@/components/ui/tabs';
import { cn } from '@/lib/utils';
import {
  fetchPrivateSpeakingConfig,
  fetchPrivateSpeakingTutors,
  fetchAllPrivateSpeakingSlots,
  fetchPrivateSpeakingSlots,
  createPrivateSpeakingBooking,
  reschedulePrivateSpeakingBooking,
  fetchLearnerPrivateSpeakingBookings,
  cancelPrivateSpeakingBooking,
  downloadPrivateSpeakingCalendarInvite,
  ratePrivateSpeakingSession,
  safePaymentRedirect,
  isApiError,
  type PaymentCaptureResult,
} from '@/lib/api';
import { LazyPayPalExpandedCheckout } from '@/components/billing/lazy-paypal-expanded-checkout';
import { createSpeakingExamFromBooking } from '@/lib/api/speaking-exams';
import { analytics } from '@/lib/analytics';
import { useAuth } from '@/contexts/auth-context';
import { useEntitlementSnapshot } from '@/lib/query/hooks';

type Config = {
  isEnabled: boolean; defaultPriceMinorUnits: number; currency: string;
  defaultSlotDurationMinutes: number; cancellationWindowHours: number;
  allowReschedule: boolean; rescheduleWindowHours: number; reservationTimeoutMinutes: number;
  /** False when no LiveKit provider is configured (B9). */
  liveRoomsAvailable?: boolean;
};

/** Tutor filter value for "Any available tutor" — the server assigns the least-loaded tutor. */
const ANY_TUTOR = 'any';
const TUTOR_ROOMS_UNAVAILABLE_MESSAGE = 'Live tutor sessions are temporarily unavailable.';

function isTutorRoomsUnavailable(err: unknown): boolean {
  return isApiError(err) && err.code === 'tutor_rooms_unavailable';
}

type Tutor = {
  id: string; displayName: string; bio: string | null; timezone: string;
  priceOverrideMinorUnits: number | null; slotDurationOverrideMinutes: number | null;
  specialtiesJson: string; averageRating: number; totalSessions: number;
};

type Slot = {
  tutorProfileId: string; tutorDisplayName: string; tutorTimezone: string;
  date: string; startTimeLocal: string; startTimeUtc: string; endTimeUtc: string;
  durationMinutes: number; priceMinorUnits: number; currency: string;
};

type Booking = {
  id: string; tutorProfileId: string; tutorName: string | null;
  status: string; sessionStartUtc: string; durationMinutes: number;
  tutorTimezone: string; learnerTimezone: string;
  priceMinorUnits: number; currency: string;
  paymentStatus: string;
  entitlementConsumed?: boolean;
  entitlementRestoredAt?: string | null;
  rescheduledFromBookingId?: string | null;
  rescheduledToBookingId?: string | null;
  googleCalendarSyncStatus?: string | null;
  learnerRating: number | null; learnerFeedback: string | null;
  refundIssued?: boolean | null;
  refundAmountMinorUnits?: number | null;
  penaltyAmountMinorUnits?: number | null;
  // Speaking module rebuild (2026-06-11): exam-format bookings + linked exam.
  sessionFormat?: string | null;
  examSessionId?: string | null;
  createdAt: string;
};

const PROFESSION_TRACKS = ['Medicine', 'Nursing', 'Pharmacy', 'Dentistry', 'Other'] as const;
type ProfessionTrack = (typeof PROFESSION_TRACKS)[number];

/** Formats a slot's minor-unit price as a localized currency string for the PayPal button. */
function formatSlotPrice(slot: Slot): string {
  const major = (slot.priceMinorUnits ?? 0) / 100;
  try {
    return major.toLocaleString(undefined, { style: 'currency', currency: slot.currency || 'GBP' });
  } catch {
    return `${slot.currency || 'GBP'} ${major.toFixed(2)}`;
  }
}

// PDF §12 — verbatim cancellation / reschedule policy text.
const CANCELLATION_POLICY_TEXT =
  'You may cancel your Speaking session with a full refund if the cancellation is made more than 24 hours before the scheduled start time. If you cancel 24 hours or less before the session, a full refund is not available.';
const RESCHEDULE_POLICY_TEXT =
  'You may reschedule your Speaking session any time before it starts, subject to an alternative slot currently available in the tutor calendar.';

// Statuses that count as an upcoming/active booking (PDF §11).
const UPCOMING_STATUSES = new Set(['Confirmed', 'PendingPayment', 'InProgress', 'Reserved']);

function isUpcomingBooking(booking: Booking): boolean {
  const inFuture = new Date(booking.sessionStartUtc).getTime() > Date.now();
  return UPCOMING_STATUSES.has(booking.status) && inFuture;
}

const STATUS_VARIANTS: Record<string, 'warning' | 'info' | 'default' | 'success' | 'danger' | 'muted'> = {
  Reserved: 'warning',
  PendingPayment: 'warning',
  Confirmed: 'info',
  InProgress: 'default',
  Completed: 'success',
  Cancelled: 'danger',
  Expired: 'muted',
  Failed: 'danger',
  Refunded: 'info',
  NoShow: 'danger',
};

const FRIENDLY_STATUS: Record<string, string> = {
  Reserved: 'Reserved',
  PendingPayment: 'Awaiting Payment',
  Confirmed: 'Confirmed',
  InProgress: 'In Progress',
  Completed: 'Completed',
  Cancelled: 'Cancelled',
  Expired: 'Expired',
  Failed: 'Failed',
  Refunded: 'Refunded',
  NoShow: 'No Show',
};

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('en-AU', { weekday: 'short', day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

function formatPrice(minorUnits: number, currency: string) {
  return new Intl.NumberFormat('en-AU', { style: 'currency', currency }).format(minorUnits / 100);
}

const UK_TIME_ZONE = 'Europe/London';

// PDF §3.3.4 — the Join button activates 15 minutes before the session start.
const JOIN_LEAD_MINUTES = 15;

/**
 * Format a UTC instant as a candidate-local + UK time pair (PDF §3.2.5 /
 * edge case #8). e.g. "6:00 PM (your time) · 11:00 AM UK".
 */
function formatLocalAndUkTime(utcIso: string): string {
  const instant = new Date(utcIso);
  const timeOpts: Intl.DateTimeFormatOptions = { hour: 'numeric', minute: '2-digit' };
  const local = new Intl.DateTimeFormat(undefined, timeOpts).format(instant);
  const uk = new Intl.DateTimeFormat('en-GB', { ...timeOpts, timeZone: UK_TIME_ZONE }).format(instant);
  return `${local} (your time) · ${uk} UK`;
}

/** Surface the refund / penalty outcome for a settled booking (PDF §11). */
function refundOutcome(booking: Booking): { label: string; tone: 'success' | 'danger' | 'info' | 'muted' } | null {
  if (booking.refundIssued || booking.status === 'Refunded') {
    const amount = booking.refundAmountMinorUnits
      ? ` (${formatPrice(booking.refundAmountMinorUnits, booking.currency)})`
      : '';
    return { label: `Refunded${amount}`, tone: 'success' };
  }
  if (booking.status === 'Cancelled') {
    return { label: 'Cancelled (no refund)', tone: 'muted' };
  }
  if (booking.status === 'NoShow') {
    return { label: 'No show (no refund)', tone: 'danger' };
  }
  return null;
}

const OUTCOME_TONE: Record<'success' | 'danger' | 'info' | 'muted', string> = {
  success: 'text-success-strong',
  danger: 'text-danger-strong',
  info: 'text-info',
  muted: 'text-muted',
};

function getWeekRange(offset: number): { from: string; to: string; label: string } {
  const now = new Date();
  const start = new Date(now);
  start.setDate(start.getDate() + offset * 7);
  start.setHours(0, 0, 0, 0);
  const end = new Date(start);
  end.setDate(end.getDate() + 6);
  const from = start.toISOString().split('T')[0];
  const to = end.toISOString().split('T')[0];
  const label = `${start.toLocaleDateString('en-AU', { day: 'numeric', month: 'short' })} – ${end.toLocaleDateString('en-AU', { day: 'numeric', month: 'short', year: 'numeric' })}`;
  return { from, to, label };
}

type ViewMode = 'browse' | 'bookings';

export default function PrivateSpeakingPage() {
  const [config, setConfig] = useState<Config | null>(null);
  const [tutors, setTutors] = useState<Tutor[]>([]);
  const [slots, setSlots] = useState<Slot[]>([]);
  const [bookings, setBookings] = useState<Booking[]>([]);
  const { user } = useAuth();
  const userId = user?.userId ?? '';
  // The entitlement snapshot is the dashboard's own query (same shared key, cached),
  // not a private uncached fetch on every visit to this page.
  const entitlementQuery = useEntitlementSnapshot(userId, { enabled: Boolean(userId) });
  const entitlement = entitlementQuery.data ?? null;
  const [dataLoading, setDataLoading] = useState(true);
  // Eligibility and the session count are part of what the first paint needs, so the
  // page stays in its skeleton until the snapshot is in (as when it was fetched here).
  const loading = dataLoading || (Boolean(userId) && entitlementQuery.isPending);
  const [slotsLoading, setSlotsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<ViewMode>('browse');
  const [weekOffset, setWeekOffset] = useState(0);
  const [selectedTutor, setSelectedTutor] = useState<string | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<Slot | null>(null);
  const [rescheduleTarget, setRescheduleTarget] = useState<Booking | null>(null);
  const [bookingNotes, setBookingNotes] = useState('');
  const [professionTrack, setProfessionTrack] = useState<ProfessionTrack>('Medicine');
  // Speaking module rebuild (2026-06-11): book a free practice session or a
  // structured two-card exam (tutor plays the patient; tutor marks it).
  const [bookingFormat, setBookingFormat] = useState<'practice' | 'exam'>('practice');
  const [bookingInProgress, setBookingInProgress] = useState(false);
  // When the learner chooses Pay with PayPal, the created (PendingPayment) order id the
  // embedded buttons capture against. Cleared on success/cancel.
  const [paypalOrderId, setPaypalOrderId] = useState<string | null>(null);
  // Policy-confirmation modals (PDF §12). Hold the booking pending confirmation.
  const [cancelConfirm, setCancelConfirm] = useState<Booking | null>(null);
  const [cancelInProgress, setCancelInProgress] = useState(false);
  const [rescheduleConfirmOpen, setRescheduleConfirmOpen] = useState(false);
  // A booking answers with the new balance; until then it comes from the snapshot.
  const [remainingAfterBooking, setRemainingAfterBooking] = useState<number | null | undefined>(undefined);
  const entitlementRemaining = remainingAfterBooking !== undefined
    ? remainingAfterBooking
    : (entitlement?.speakingSessionsRemaining ?? null);
  // FINAL 2026-09-06: Live Tutor booking is entitlement-gated. Only holders of
  // an eligible main course/package or the Speaking Crash Course
  // (plan flag SpeakingAddonsEnabled, resolved server-side) may book.
  const liveTutorEligible: boolean | null = entitlement ? entitlement.speakingAddonsEnabled === true : null;
  // B9: LiveKit not configured → no slot browsing, booking or joining.
  const [roomsUnavailable, setRoomsUnavailable] = useState(false);
  const [joiningBookingId, setJoiningBookingId] = useState<string | null>(null);
  const [ratingSession, setRatingSession] = useState<string | null>(null);
  const [ratingValue, setRatingValue] = useState(5);
  const [ratingFeedback, setRatingFeedback] = useState('');
  // Live clock so the Join button activates exactly 15 minutes before start (PDF §3.3.4).
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    const id = setInterval(() => setNow(Date.now()), 30_000);
    return () => clearInterval(id);
  }, []);

  // Initial data load
  useEffect(() => {
    analytics.track('private_speaking_page_viewed');
    Promise.all([
      fetchPrivateSpeakingConfig(),
      fetchPrivateSpeakingTutors(),
      fetchLearnerPrivateSpeakingBookings(),
    ]).then(([cfg, tut, bk]) => {
      setConfig(cfg as Config);
      if ((cfg as Config).liveRoomsAvailable === false) setRoomsUnavailable(true);
      setTutors(tut as Tutor[]);
      setBookings(bk as Booking[]);
      setDataLoading(false);
    }).catch(() => {
      setError('Could not load private speaking sessions.');
      setDataLoading(false);
    });
  }, []);

  // An unreadable snapshot fails the page exactly as it did when it was fetched above.
  useEffect(() => {
    if (entitlementQuery.isError) setError('Could not load private speaking sessions.');
  }, [entitlementQuery.isError]);

  // Ineligible learners land on My Bookings (slot browsing is hidden for them).
  useEffect(() => {
    if (liveTutorEligible === false || roomsUnavailable) setViewMode('bookings');
  }, [liveTutorEligible, roomsUnavailable]);

  const canBrowse = liveTutorEligible !== false && !roomsUnavailable;

  // Load slots when week or tutor changes
  const loadSlots = useCallback(async () => {
    setSlotsLoading(true);
    try {
      const { from, to } = getWeekRange(weekOffset);
      if (selectedTutor === ANY_TUTOR) {
        // One slot per start time across every tutor; booking it auto-assigns a tutor.
        setSlots(await fetchPrivateSpeakingSlots(ANY_TUTOR, from, to) as Slot[]);
        return;
      }
      const data = await fetchAllPrivateSpeakingSlots(from, to) as Slot[];
      setSlots(selectedTutor ? data.filter(s => s.tutorProfileId === selectedTutor) : data);
    } catch (err: unknown) {
      if (isTutorRoomsUnavailable(err)) setRoomsUnavailable(true);
      else setError('Could not load available slots.');
    } finally {
      setSlotsLoading(false);
    }
  }, [weekOffset, selectedTutor]);

  useEffect(() => {
    if (viewMode === 'browse' && !loading && !roomsUnavailable) loadSlots();
  }, [viewMode, loading, roomsUnavailable, loadSlots]);

  async function handleBook() {
    if (!selectedSlot || bookingInProgress) return;
    setBookingInProgress(true);
    setError(null);
    try {
      const result = await createPrivateSpeakingBooking({
        tutorProfileId: selectedSlot.tutorProfileId,
        sessionStartUtc: selectedSlot.startTimeUtc,
        durationMinutes: selectedSlot.durationMinutes,
        learnerTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        learnerNotes: bookingNotes || undefined,
        professionTrack,
        sessionFormat: bookingFormat,
        idempotencyKey: crypto.randomUUID(),
      });

      analytics.track('private_speaking_booking_created', { bookingId: result.bookingId });

      // Redirect to Stripe Checkout
      if (result.checkoutUrl) {
        window.location.href = result.checkoutUrl;
        return;
      }

      // Fallback: refresh bookings
      setSelectedSlot(null);
      setBookingNotes('');
      if (result.speakingSessionsRemaining !== undefined) {
        setRemainingAfterBooking(result.speakingSessionsRemaining ?? null);
      }
      const updated = await fetchLearnerPrivateSpeakingBookings() as Booking[];
      setBookings(updated);
      setViewMode('bookings');
    } catch (err: unknown) {
      if (isTutorRoomsUnavailable(err)) {
        setRoomsUnavailable(true);
        setSelectedSlot(null);
        return;
      }
      const message = err instanceof Error ? err.message : 'Could not book session.';
      setError(message);
    } finally {
      setBookingInProgress(false);
    }
  }

  // Pay for the session with embedded PayPal instead of a credit: reserves the slot as a
  // PendingPayment booking and returns the order id the embedded buttons capture against.
  async function handleBookWithPaypal() {
    if (!selectedSlot || bookingInProgress) return;
    setBookingInProgress(true);
    setError(null);
    setPaypalOrderId(null);
    try {
      const result = await createPrivateSpeakingBooking({
        tutorProfileId: selectedSlot.tutorProfileId,
        sessionStartUtc: selectedSlot.startTimeUtc,
        durationMinutes: selectedSlot.durationMinutes,
        learnerTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        learnerNotes: bookingNotes || undefined,
        professionTrack,
        sessionFormat: bookingFormat,
        idempotencyKey: crypto.randomUUID(),
        paymentMethod: 'paypal',
      });
      analytics.track('private_speaking_booking_created', { bookingId: result.bookingId });
      if (!result.checkoutSessionId) {
        throw new Error('We could not start your PayPal payment. Please try again.');
      }
      setPaypalOrderId(result.checkoutSessionId);
    } catch (err: unknown) {
      if (isTutorRoomsUnavailable(err)) {
        setRoomsUnavailable(true);
        setSelectedSlot(null);
        return;
      }
      setError(err instanceof Error ? err.message : 'Could not start PayPal payment.');
    } finally {
      setBookingInProgress(false);
    }
  }

  async function handlePaypalBookingCaptured(result: PaymentCaptureResult) {
    setPaypalOrderId(null);
    setSelectedSlot(null);
    setBookingNotes('');
    const target = safePaymentRedirect(result.redirectTo, '/private-speaking');
    if (typeof window !== 'undefined' && target !== '/private-speaking') {
      window.location.href = target;
      return;
    }
    try {
      const updated = (await fetchLearnerPrivateSpeakingBookings()) as Booking[];
      setBookings(updated);
    } catch {
      /* non-fatal — the booking is confirmed server-side regardless */
    }
    setViewMode('bookings');
  }

  async function handleReschedule() {
    if (!selectedSlot || !rescheduleTarget || bookingInProgress) return;
    setBookingInProgress(true);
    setError(null);
    try {
      const result = await reschedulePrivateSpeakingBooking(rescheduleTarget.id, {
        sessionStartUtc: selectedSlot.startTimeUtc,
        learnerTimezone: Intl.DateTimeFormat().resolvedOptions().timeZone,
        learnerNotes: bookingNotes || undefined,
        idempotencyKey: crypto.randomUUID(),
      });

      analytics.track('private_speaking_booking_rescheduled', { bookingId: rescheduleTarget.id, newBookingId: result.bookingId });
      setRescheduleConfirmOpen(false);

      // Rescheduling is always free before the session starts; the server
      // enforces the current tutor-calendar availability.
      setSelectedSlot(null);
      setRescheduleTarget(null);
      setBookingNotes('');
      const updated = await fetchLearnerPrivateSpeakingBookings() as Booking[];
      setBookings(updated);
      setViewMode('bookings');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not reschedule session.');
    } finally {
      setBookingInProgress(false);
    }
  }

  async function handleCancelConfirmed() {
    if (!cancelConfirm || cancelInProgress) return;
    const bookingId = cancelConfirm.id;
    setCancelInProgress(true);
    setError(null);
    try {
      await cancelPrivateSpeakingBooking(bookingId);
      setCancelConfirm(null);
      // Refresh from the server so refund/penalty outcome fields are surfaced.
      const updated = await fetchLearnerPrivateSpeakingBookings() as Booking[];
      setBookings(updated);
    } catch {
      setError('Could not cancel booking.');
    } finally {
      setCancelInProgress(false);
    }
  }

  async function handleJoin(booking: Booking) {
    setJoiningBookingId(booking.id);
    setError(null);
    try {
      const exam = await createSpeakingExamFromBooking(booking.id);
      window.location.href = `/speaking/exam/${exam.examId}`;
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not prepare the LiveKit tutor room.');
    } finally {
      setJoiningBookingId(null);
    }
  }

  async function handleDownloadInvite(bookingId: string) {
    try {
      const blob = await downloadPrivateSpeakingCalendarInvite(bookingId);
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement('a');
      anchor.href = url;
      anchor.download = `oet-private-speaking-${bookingId}.ics`;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch {
      setError('Could not download the calendar invite.');
    }
  }

  function startReschedule(booking: Booking) {
    setRescheduleTarget(booking);
    setSelectedTutor(booking.tutorProfileId);
    setSelectedSlot(null);
    setBookingNotes('');
    setRescheduleConfirmOpen(false);
    setViewMode('browse');
  }

  async function handleRate(bookingId: string) {
    try {
      await ratePrivateSpeakingSession(bookingId, ratingValue, ratingFeedback || undefined);
      setBookings(prev => prev.map(b => b.id === bookingId ? { ...b, learnerRating: ratingValue, learnerFeedback: ratingFeedback } : b));
      setRatingSession(null);
      setRatingFeedback('');
    } catch {
      setError('Could not submit rating.');
    }
  }

  const { label: weekLabel } = getWeekRange(weekOffset);

  // Group slots by date
  const slotsByDate = slots.reduce<Record<string, Slot[]>>((acc, slot) => {
    (acc[slot.date] ??= []).push(slot);
    return acc;
  }, {});

  // Split bookings into Upcoming / Past sections (PDF §11).
  const upcomingBookings = bookings.filter(isUpcomingBooking);
  const pastBookings = bookings.filter(b => !isUpcomingBooking(b));

  // Edge case #5 — candidate has no remaining speaking entitlement.
  const hasNoEntitlement = (entitlementRemaining ?? 0) <= 0;

  function renderBookingCard(booking: Booking, i: number) {
    const outcome = refundOutcome(booking);
    // Join activates 15 min before start (PDF §3.3.4); `now` ticks so it auto-enables.
    const joinOpen = new Date(booking.sessionStartUtc).getTime() - now <= JOIN_LEAD_MINUTES * 60_000;
    return (
      <MotionItem key={booking.id} delayIndex={Math.min(i, 5)}>
        <Card padding="md" className="flex flex-col gap-3 sm:flex-row sm:items-center">
          <div className="min-w-0 flex-1">
            <div className="mb-1 flex flex-wrap items-center gap-2">
              <span className="text-sm font-medium text-navy">{booking.tutorName ?? 'Tutor'}</span>
              <Badge variant={STATUS_VARIANTS[booking.status] ?? 'muted'}>
                {FRIENDLY_STATUS[booking.status] ?? booking.status}
              </Badge>
              {outcome && (
                <span className={`text-xs font-medium ${OUTCOME_TONE[outcome.tone]}`}>{outcome.label}</span>
              )}
            </div>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted tabular-nums">
              <span className="flex items-center gap-1"><Calendar className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />{formatDate(booking.sessionStartUtc)}</span>
              <span>{booking.durationMinutes} min</span>
              <span>{booking.entitlementConsumed ? 'Session credit' : formatPrice(booking.priceMinorUnits, booking.currency)}</span>
            </div>
            <div className="mt-1 flex items-center gap-1 text-xs text-muted tabular-nums">
              <Globe className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />{formatLocalAndUkTime(booking.sessionStartUtc)}
            </div>
          </div>

          <div className="flex flex-wrap items-center gap-2">
            {(booking.status === 'Confirmed' || booking.status === 'InProgress') && (
              // pointer-events stay on while disabled so the title explains when Join opens.
              <Button size="sm" className="disabled:pointer-events-auto disabled:cursor-not-allowed" onClick={() => handleJoin(booking)} disabled={roomsUnavailable || !joinOpen || joiningBookingId === booking.id}
                title={roomsUnavailable ? TUTOR_ROOMS_UNAVAILABLE_MESSAGE : joinOpen ? undefined : `The Join button activates ${JOIN_LEAD_MINUTES} minutes before the session starts.`}>
                <Video className="h-3.5 w-3.5" aria-hidden="true" /> {joiningBookingId === booking.id ? 'Opening...' : joinOpen ? 'Join LiveKit' : 'Join soon'}
              </Button>
            )}

            {config?.allowReschedule && booking.status === 'Confirmed' && (
              <Button variant="ghost" size="sm" onClick={() => startReschedule(booking)} className="text-primary">
                Reschedule
              </Button>
            )}

            {booking.status === 'Confirmed' && (
              <Button variant="ghost" size="sm" onClick={() => handleDownloadInvite(booking.id)} className="text-muted">
                <Download className="h-3.5 w-3.5" aria-hidden="true" /> Calendar
              </Button>
            )}

            {/* Cancel button — opens the policy confirmation modal (PDF §12). */}
            {booking.status === 'Confirmed' && (
              <Button variant="ghost" size="sm" onClick={() => setCancelConfirm(booking)} className="text-danger-strong">
                Cancel
              </Button>
            )}

            {/* Rating */}
            {booking.status === 'Completed' && booking.learnerRating === null && (
              ratingSession === booking.id ? (
                <div className="flex flex-wrap items-center gap-1">
                  {[1, 2, 3, 4, 5].map(v => (
                    <button key={v} type="button" onClick={() => setRatingValue(v)}
                      aria-label={`${v} star${v === 1 ? '' : 's'}`} aria-pressed={ratingValue === v}
                      className="pressable flex h-11 w-11 items-center justify-center rounded-control hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
                      <Star aria-hidden="true" className={`h-5 w-5 ${ratingValue >= v ? 'fill-warning text-warning-strong' : 'text-muted'}`} />
                    </button>
                  ))}
                  <input type="text" placeholder="Feedback" aria-label="Feedback" value={ratingFeedback}
                    onChange={e => setRatingFeedback(e.target.value)}
                    className="min-h-11 w-32 rounded-control border border-border bg-surface px-3 text-xs text-navy focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/20" />
                  <Button size="sm" onClick={() => handleRate(booking.id)}>Submit</Button>
                  <Button variant="ghost" size="sm" onClick={() => setRatingSession(null)}>Cancel</Button>
                </div>
              ) : (
                <Button variant="ghost" size="sm" onClick={() => setRatingSession(booking.id)} className="text-warning-strong">
                  <Star className="h-4 w-4" aria-hidden="true" /> Rate
                </Button>
              )
            )}

            {booking.learnerRating !== null && (
              <div role="img" aria-label={`${booking.learnerRating}/5`} className="flex items-center gap-1 text-sm text-warning-strong">
                {'★'.repeat(booking.learnerRating)}{'☆'.repeat(5 - booking.learnerRating)}
              </div>
            )}
          </div>
        </Card>
      </MotionItem>
    );
  }

  if (loading) {
    return (
      <div className="space-y-4" role="status" aria-busy="true" aria-label="Loading">
        <Skeleton aria-hidden className="h-20 rounded-2xl" />
        <Skeleton aria-hidden className="h-64 rounded-2xl" />
      </div>
    );
  }

  if (!config?.isEnabled) {
    return (
      <>
        <LearnerPageHero title="Private Speaking Sessions" description="This feature is not currently available." icon={Mic} />
        <InlineAlert variant="info" live="polite">Private speaking sessions are temporarily unavailable. Check back once tutor availability is enabled.</InlineAlert>
      </>
    );
  }

  const viewTabs = [
    { id: 'browse', label: 'Browse Slots' },
    { id: 'bookings', label: 'My Bookings', count: bookings.length > 0 ? bookings.length : undefined },
  ];

  const browseView = (
    <div className="space-y-5">
      {/* Week navigation + tutor filter */}
      <div className="flex flex-col items-start gap-3 sm:flex-row sm:items-center">
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" className="w-11 px-0" onClick={() => setWeekOffset(Math.max(0, weekOffset - 1))} disabled={weekOffset === 0}
            aria-label="Previous week">
            <ChevronLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          </Button>
          <span className="min-w-[200px] text-center text-sm font-medium tabular-nums text-navy">{weekLabel}</span>
          <Button variant="outline" size="sm" className="w-11 px-0" onClick={() => setWeekOffset(weekOffset + 1)}
            aria-label="Next week">
            <ChevronRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          </Button>
        </div>
        <Select
          aria-label="Tutor"
          value={selectedTutor ?? ''}
          onChange={e => setSelectedTutor(e.target.value || null)}
          options={[
            { value: '', label: 'All tutors' },
            ...(!rescheduleTarget ? [{ value: ANY_TUTOR, label: 'Any available tutor' }] : []),
            ...tutors.map(t => ({ value: t.id, label: t.displayName })),
          ]}
          className="py-2.5"
        />
      </div>

      {/* Tutor spotlight cards */}
      {tutors.length > 0 && !selectedTutor && (
        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2 lg:grid-cols-3">
          {tutors.map((t, index) => (
            <MotionItem key={t.id} delayIndex={Math.min(index, 5)}>
              <button type="button" onClick={() => setSelectedTutor(t.id)}
                className={cn(cardClassName({ hoverable: true, interactive: true }), 'h-full w-full text-start')}>
                <span className="mb-2 flex items-center gap-3">
                  <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-primary/10">
                    <User className="h-5 w-5 text-primary" aria-hidden="true" />
                  </span>
                  <span className="min-w-0">
                    <span className="block text-sm font-medium text-navy">{t.displayName}</span>
                    <span className="flex items-center gap-1 text-xs text-muted tabular-nums">
                      {t.averageRating > 0 && <><Star className="h-3 w-3 fill-warning text-warning-strong" aria-hidden="true" /> {t.averageRating.toFixed(1)}</>}
                      {t.totalSessions > 0 && <span className="ms-1">· {t.totalSessions} sessions</span>}
                    </span>
                  </span>
                </span>
                {t.bio && <span className="line-clamp-2 block text-xs text-muted">{t.bio}</span>}
                <span className="mt-2 block text-xs text-primary tabular-nums">
                  {formatPrice(t.priceOverrideMinorUnits ?? config.defaultPriceMinorUnits, config.currency)} · {t.slotDurationOverrideMinutes ?? config.defaultSlotDurationMinutes} min
                </span>
              </button>
            </MotionItem>
          ))}
        </div>
      )}

      {slotsLoading ? (
        <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading"><Skeleton aria-hidden className="h-32 rounded-2xl" /><Skeleton aria-hidden className="h-32 rounded-2xl" /></div>
      ) : Object.keys(slotsByDate).length === 0 ? (
        <EmptyState icon={<Calendar className="h-8 w-8" />} title="No available slots this week. Try another week or tutor." />
      ) : (
        <div className="space-y-4">
          {Object.entries(slotsByDate).sort(([a], [b]) => a.localeCompare(b)).map(([date, daySlots], dayIndex) => (
            <MotionItem key={date} delayIndex={Math.min(dayIndex, 5)}>
              <h2 className="mb-2 text-sm font-semibold text-navy">
                {new Date(date + 'T00:00:00').toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long' })}
              </h2>
              <div className="grid grid-cols-2 gap-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5">
                {daySlots.map((slot, i) => {
                  const isSelected = selectedSlot?.startTimeUtc === slot.startTimeUtc && selectedSlot?.tutorProfileId === slot.tutorProfileId;
                  return (
                    <button key={`${slot.tutorProfileId}-${slot.startTimeUtc}-${i}`}
                      type="button"
                      aria-pressed={isSelected}
                      onClick={() => setSelectedSlot(isSelected ? null : slot)}
                      className={`min-w-0 rounded-control border p-3 text-start text-sm transition-[color,background-color,border-color,box-shadow] duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                        isSelected
                          ? 'border-primary bg-primary/10 ring-2 ring-primary/30'
                          : 'border-border bg-surface hover:border-primary/30'
                      }`}>
                      <span className="block font-medium tabular-nums text-navy">{slot.startTimeLocal}</span>
                      <span className="mt-0.5 flex items-start gap-1 text-2xs text-muted">
                        <Globe className="mt-0.5 h-3 w-3 shrink-0" aria-hidden="true" />
                        <span>{formatLocalAndUkTime(slot.startTimeUtc)}</span>
                      </span>
                      <span className="mt-0.5 block text-xs text-muted">{slot.tutorDisplayName}</span>
                      <span className="mt-1 block text-xs text-primary tabular-nums">
                        {formatPrice(slot.priceMinorUnits, slot.currency)} · {slot.durationMinutes}m
                      </span>
                    </button>
                  );
                })}
              </div>
            </MotionItem>
          ))}
        </div>
      )}
    </div>
  );

  const bookingsView = bookings.length === 0 ? (
    <section className="space-y-4">
      <LearnerSurfaceSectionHeader title="Your Bookings" />
      <EmptyState icon={<Mic className="h-8 w-8" />} title="No bookings yet. Browse available slots to get started." />
    </section>
  ) : (
    <div className="space-y-6 sm:space-y-8">
      {/* Upcoming Speaking Sessions (PDF §11) */}
      <section className="space-y-3">
        <LearnerSurfaceSectionHeader title="Upcoming Speaking Sessions" />
        {upcomingBookings.length === 0 ? (
          <p className="py-2 text-sm text-muted">No upcoming sessions. Browse slots to book one.</p>
        ) : (
          <div className="space-y-3">
            {upcomingBookings.map((booking, i) => renderBookingCard(booking, i))}
          </div>
        )}
      </section>

      {/* Past Speaking Sessions (PDF §11) */}
      {pastBookings.length > 0 && (
        <section className="space-y-3">
          <LearnerSurfaceSectionHeader title="Past Speaking Sessions" />
          <div className="space-y-3">
            {pastBookings.map((booking, i) => renderBookingCard(booking, i))}
          </div>
        </section>
      )}
    </div>
  );

  return (
    <>
      <LearnerPageHero
        title="Private Speaking Sessions"
        description="Use your included private speaking sessions for one-to-one realtime practice with expert OET tutors"
        icon={Mic}
        aside={(
          <div className="flex flex-col items-start gap-2 lg:items-end">
            <p className="text-sm text-muted">Prefer an AI speaking practice session?</p>
            <Button asChild variant="outline" size="sm">
              <Link href="/speaking/selection">Practice with AI</Link>
            </Button>
          </div>
        )}
      />

      <Card padding="md">
        {liveTutorEligible === false ? (
          <div data-testid="live-tutor-ineligible">
            <p className="text-sm font-semibold text-navy">You are not eligible to book a session with a tutor.</p>
            <p className="mt-1 text-xs text-muted">
              Live tutor sessions are available only with an eligible course or package, or the Speaking Crash Course.
            </p>
            <div className="mt-3 flex flex-col gap-2 sm:flex-row">
              <Button asChild size="sm" className="w-fit">
                <Link href="/catalog">View eligible courses</Link>
              </Button>
              <Button asChild size="sm" variant="outline" className="w-fit">
                <Link href="/marketplace">Go to course catalogue</Link>
              </Button>
            </div>
          </div>
        ) : (
          <>
            <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
              <div>
                <p className="text-sm font-semibold text-navy">Speaking session credits</p>
                <p className="text-xs text-muted">Bookings use one bundled or add-on private speaking session.</p>
              </div>
              <Badge size="md" className="w-fit">
                <CountUp value={entitlementRemaining ?? 0} suffix=" remaining" />
              </Badge>
            </div>

            {/* Edge case #5 — no remaining entitlement → purchase CTA. */}
            {hasNoEntitlement && (
              <div className="mt-3 flex flex-col gap-2 border-t border-border pt-3 sm:flex-row sm:items-center sm:justify-between">
                <p className="text-xs text-warning-strong">
                  You have no speaking sessions left. Buy more to book a 1-on-1 session with a tutor.
                </p>
                <Button asChild size="sm" className="w-fit shrink-0">
                  <Link href="/catalog">
                    <ShoppingBag className="h-3.5 w-3.5" aria-hidden="true" /> Buy speaking sessions
                  </Link>
                </Button>
              </div>
            )}
          </>
        )}
      </Card>

      {roomsUnavailable && (
        <InlineAlert variant="warning" data-testid="tutor-rooms-unavailable">
          {TUTOR_ROOMS_UNAVAILABLE_MESSAGE}
        </InlineAlert>
      )}

      {error && (
        <InlineAlert
          variant="warning"
          action={<Button variant="ghost" size="sm" onClick={() => setError(null)}>Dismiss</Button>}
        >
          {error}
        </InlineAlert>
      )}

      {/* View mode toggle: slot browsing is hidden while ineligible (the server
          also blocks direct booking attempts), and a single view needs no
          switcher. Past bookings stay visible. */}
      {canBrowse ? (
        <>
          <Tabs tabs={viewTabs} activeTab={viewMode} onChange={(id) => setViewMode(id as ViewMode)} />
          <TabPanel id="browse" activeTab={viewMode}>{browseView}</TabPanel>
          <TabPanel id="bookings" activeTab={viewMode}>{bookingsView}</TabPanel>
        </>
      ) : viewMode === 'bookings' ? bookingsView : null}

      {/* Booking confirmation panel */}
      {viewMode === 'browse' && canBrowse && selectedSlot && (
        <MotionSection className="fixed start-4 end-4 bottom-[calc(var(--bottom-nav-height)+0.5rem)] z-40 max-h-[75dvh] overflow-y-auto overscroll-contain rounded-surface border border-primary/30 bg-surface p-5 shadow-lg sm:start-auto sm:end-6 sm:max-w-md lg:bottom-6">
          <div className="mb-3 flex items-center justify-between gap-3">
            <h2 className="font-semibold text-navy">{rescheduleTarget ? 'Confirm Reschedule' : 'Confirm Booking'}</h2>
            <Button variant="ghost" size="sm" className="-me-2 w-11 px-0" onClick={() => { setSelectedSlot(null); setRescheduleTarget(null); }} aria-label="Close booking panel">
              <X className="h-5 w-5" aria-hidden="true" />
            </Button>
          </div>
          <div className="mb-4 space-y-2 text-sm text-muted">
            <div className="flex items-center gap-2"><User className="h-4 w-4 shrink-0" aria-hidden="true" /> {selectedSlot.tutorDisplayName}</div>
            <div className="flex items-center gap-2"><Calendar className="h-4 w-4 shrink-0" aria-hidden="true" /> {formatDate(selectedSlot.startTimeUtc)}</div>
            <div className="flex items-start gap-2"><Globe className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" /> {formatLocalAndUkTime(selectedSlot.startTimeUtc)}</div>
            <div className="flex items-center gap-2"><Clock className="h-4 w-4 shrink-0" aria-hidden="true" /> {selectedSlot.durationMinutes} minutes</div>
            <div className="flex items-center gap-2"><CreditCard className="h-4 w-4 shrink-0" aria-hidden="true" /> Uses 1 session credit</div>
          </div>
          {/* Profession track (booking only — reschedule keeps the original track). */}
          {!rescheduleTarget && (
            <div className="mb-3">
              <Select
                id="profession-track"
                label="Profession track"
                value={professionTrack}
                onChange={e => setProfessionTrack(e.target.value as ProfessionTrack)}
                options={PROFESSION_TRACKS.map(track => ({ value: track, label: track }))}
                className="py-2.5"
              />
            </div>
          )}
          {!rescheduleTarget && (
            <div className="mb-3" role="group" aria-labelledby="session-format-label">
              <p id="session-format-label" className="mb-1 text-xs font-medium text-navy">Session format</p>
              <div className="grid grid-cols-2 gap-2">
                {(['practice', 'exam'] as const).map(fmt => (
                  <button
                    key={fmt}
                    type="button"
                    aria-pressed={bookingFormat === fmt}
                    onClick={() => setBookingFormat(fmt)}
                    className={`min-h-11 rounded-control border px-3 py-2 text-xs font-medium transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${
                      bookingFormat === fmt
                        ? 'border-primary bg-primary/10 text-primary'
                        : 'hover-primary border-border bg-surface text-muted'
                    }`}
                  >
                    {fmt === 'practice' ? 'Practice' : 'Full exam (Card A + B)'}
                  </button>
                ))}
              </div>
              {bookingFormat === 'exam' && (
                <p className="mt-1 text-2xs text-muted">
                  A structured two-card exam. Your tutor plays the patient and marks your result.
                </p>
              )}
            </div>
          )}
          <Textarea
            aria-label="Notes for the tutor (optional)"
            placeholder="Notes for the tutor (optional)"
            value={bookingNotes}
            onChange={e => setBookingNotes(e.target.value)}
            rows={2}
            className="mb-3 resize-none"
          />
          {rescheduleTarget ? (
            <>
              <Button fullWidth onClick={() => setRescheduleConfirmOpen(true)} disabled={bookingInProgress}>
                {bookingInProgress ? 'Processing...' : 'Confirm Reschedule'}
              </Button>
              <p className="mt-2 text-center text-xs text-muted">
                Your original booking will close and this slot will replace it.
              </p>
            </>
          ) : paypalOrderId ? (
            <div className="rounded-xl border border-border bg-surface p-3">
              <div className="mb-2 flex items-center justify-between gap-2">
                <span className="text-xs font-medium text-navy">
                  Pay {selectedSlot ? formatSlotPrice(selectedSlot) : ''} with PayPal
                </span>
                <Button type="button" variant="ghost" size="sm" onClick={() => setPaypalOrderId(null)}>
                  Cancel
                </Button>
              </div>
              <LazyPayPalExpandedCheckout
                createOrder={() => Promise.resolve(paypalOrderId ?? '')}
                onCaptured={handlePaypalBookingCaptured}
                onError={(message) => setError(message)}
                onUnavailable={() => {
                  setPaypalOrderId(null);
                  setError('PayPal is unavailable right now. Please use a session credit or try again.');
                }}
                amountLabel={selectedSlot ? formatSlotPrice(selectedSlot) : ''}
                disabled={bookingInProgress}
              />
            </div>
          ) : (
            <>
              <Button fullWidth onClick={handleBook} disabled={bookingInProgress || (entitlementRemaining ?? 0) <= 0}>
                {bookingInProgress ? 'Processing...' : 'Use Session Credit & Book'}
              </Button>
              {selectedSlot && selectedSlot.priceMinorUnits > 0 ? (
                <Button variant="outline" fullWidth onClick={handleBookWithPaypal} disabled={bookingInProgress} className="mt-2">
                  Pay {formatSlotPrice(selectedSlot)} with PayPal
                </Button>
              ) : null}
              <p className="mt-2 text-center text-xs text-muted">
                Use a session credit, or pay per session with PayPal.
              </p>
            </>
          )}
        </MotionSection>
      )}

      {/* Cancellation policy confirmation modal (PDF §12) */}
      <Modal open={cancelConfirm !== null} onClose={() => { if (!cancelInProgress) setCancelConfirm(null); }} title="Cancel Speaking session" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">{CANCELLATION_POLICY_TEXT}</p>
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button type="button" variant="outline" onClick={() => setCancelConfirm(null)} disabled={cancelInProgress}>
              Keep booking
            </Button>
            <Button type="button" variant="destructive" onClick={handleCancelConfirmed} disabled={cancelInProgress}>
              {cancelInProgress ? 'Cancelling...' : 'Confirm cancellation'}
            </Button>
          </div>
        </div>
      </Modal>

      {/* Reschedule policy confirmation modal (PDF §12) */}
      <Modal open={rescheduleConfirmOpen} onClose={() => { if (!bookingInProgress) setRescheduleConfirmOpen(false); }} title="Reschedule Speaking session" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">{RESCHEDULE_POLICY_TEXT}</p>
          {selectedSlot && (
            <p className="text-xs text-muted">
              New time: {formatLocalAndUkTime(selectedSlot.startTimeUtc)}
            </p>
          )}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <Button type="button" variant="outline" onClick={() => setRescheduleConfirmOpen(false)} disabled={bookingInProgress}>
              Back
            </Button>
            <Button type="button" onClick={handleReschedule} disabled={bookingInProgress}>
              {bookingInProgress ? 'Processing...' : 'Confirm reschedule'}
            </Button>
          </div>
        </div>
      </Modal>
    </>
  );
}
