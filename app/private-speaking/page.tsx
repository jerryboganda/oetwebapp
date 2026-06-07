'use client';

import { useEffect, useState, useCallback } from 'react';
import Link from 'next/link';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Mic, Calendar, Star, Clock, CreditCard, Video, X, ChevronLeft, ChevronRight, User, Download, ShoppingBag, Globe } from 'lucide-react';
import { ZoomMeetingEmbed } from '@/components/class/ZoomMeetingEmbed';
import { Modal } from '@/components/ui/modal';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  type LiveClassJoinToken,
  fetchPrivateSpeakingConfig,
  fetchPrivateSpeakingTutors,
  fetchAllPrivateSpeakingSlots,
  createPrivateSpeakingBooking,
  reschedulePrivateSpeakingBooking,
  fetchLearnerPrivateSpeakingBookings,
  cancelPrivateSpeakingBooking,
  fetchPrivateSpeakingJoinToken,
  downloadPrivateSpeakingCalendarInvite,
  fetchMyEntitlementSnapshot,
  ratePrivateSpeakingSession,
} from '@/lib/api';
import { safeZoomUrl } from '@/lib/zoom-url';
import { analytics } from '@/lib/analytics';

type Config = {
  isEnabled: boolean; defaultPriceMinorUnits: number; currency: string;
  defaultSlotDurationMinutes: number; cancellationWindowHours: number;
  allowReschedule: boolean; rescheduleWindowHours: number; reservationTimeoutMinutes: number;
};

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
  paymentStatus: string; zoomStatus: string;
  zoomJoinUrl: string | null; zoomMeetingPassword: string | null;
  entitlementConsumed?: boolean;
  entitlementRestoredAt?: string | null;
  rescheduledFromBookingId?: string | null;
  rescheduledToBookingId?: string | null;
  googleCalendarSyncStatus?: string | null;
  learnerRating: number | null; learnerFeedback: string | null;
  refundIssued?: boolean | null;
  refundAmountMinorUnits?: number | null;
  penaltyAmountMinorUnits?: number | null;
  createdAt: string;
};

const PROFESSION_TRACKS = ['Medicine', 'Nursing', 'Pharmacy', 'Dentistry', 'Other'] as const;
type ProfessionTrack = (typeof PROFESSION_TRACKS)[number];

// PDF §12 — verbatim cancellation / reschedule policy text.
const CANCELLATION_POLICY_TEXT =
  'You may cancel your Speaking session with a full refund if the cancellation is made more than 48 hours before the scheduled start time. If you cancel less than 48 hours before the session, the booking will be cancelled without refund.';
const RESCHEDULE_POLICY_TEXT =
  'You may reschedule your Speaking session before the session starts, subject to available tutor slots. Same-day rescheduling is allowed; however, 50% of the session fee will be lost according to the platform policy.';

// Statuses that count as an upcoming/active booking (PDF §11).
const UPCOMING_STATUSES = new Set(['Confirmed', 'ZoomCreated', 'PendingPayment', 'InProgress', 'Reserved']);

function isUpcomingBooking(booking: Booking): boolean {
  const inFuture = new Date(booking.sessionStartUtc).getTime() > Date.now();
  return UPCOMING_STATUSES.has(booking.status) && inFuture;
}

type ActiveMeeting = {
  token: LiveClassJoinToken;
  title: string;
  startsAt: string;
};

const STATUS_COLORS: Record<string, string> = {
  Reserved: 'bg-warning/10 text-warning',
  PendingPayment: 'bg-warning/10 text-warning',
  Confirmed: 'bg-info/10 text-info',
  ZoomCreated: 'bg-success/10 text-success',
  InProgress: 'bg-primary/10 text-primary',
  Completed: 'bg-success/10 text-success',
  Cancelled: 'bg-danger/10 text-danger',
  Expired: 'bg-background-light text-muted',
  Failed: 'bg-danger/10 text-danger',
  Refunded: 'bg-info/10 text-info',
  NoShow: 'bg-danger/10 text-danger',
};

const FRIENDLY_STATUS: Record<string, string> = {
  Reserved: 'Reserved',
  PendingPayment: 'Awaiting Payment',
  Confirmed: 'Confirmed',
  ZoomCreated: 'Ready',
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
  if (booking.penaltyAmountMinorUnits && booking.penaltyAmountMinorUnits > 0) {
    return { label: `Penalty: ${formatPrice(booking.penaltyAmountMinorUnits, booking.currency)}`, tone: 'danger' };
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
  success: 'text-success',
  danger: 'text-danger',
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
  const [loading, setLoading] = useState(true);
  const [slotsLoading, setSlotsLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [viewMode, setViewMode] = useState<ViewMode>('browse');
  const [weekOffset, setWeekOffset] = useState(0);
  const [selectedTutor, setSelectedTutor] = useState<string | null>(null);
  const [selectedSlot, setSelectedSlot] = useState<Slot | null>(null);
  const [rescheduleTarget, setRescheduleTarget] = useState<Booking | null>(null);
  const [bookingNotes, setBookingNotes] = useState('');
  const [professionTrack, setProfessionTrack] = useState<ProfessionTrack>('Medicine');
  const [bookingInProgress, setBookingInProgress] = useState(false);
  // Policy-confirmation modals (PDF §12). Hold the booking pending confirmation.
  const [cancelConfirm, setCancelConfirm] = useState<Booking | null>(null);
  const [cancelInProgress, setCancelInProgress] = useState(false);
  const [rescheduleConfirmOpen, setRescheduleConfirmOpen] = useState(false);
  const [entitlementRemaining, setEntitlementRemaining] = useState<number | null>(null);
  const [joiningBookingId, setJoiningBookingId] = useState<string | null>(null);
  const [activeMeeting, setActiveMeeting] = useState<ActiveMeeting | null>(null);
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
      fetchMyEntitlementSnapshot(),
    ]).then(([cfg, tut, bk, entitlement]) => {
      setConfig(cfg as Config);
      setTutors(tut as Tutor[]);
      setBookings(bk as Booking[]);
      setEntitlementRemaining(entitlement.speakingSessionsRemaining);
      setLoading(false);
    }).catch(() => {
      setError('Could not load private speaking sessions.');
      setLoading(false);
    });
  }, []);

  // Load slots when week or tutor changes
  const loadSlots = useCallback(async () => {
    setSlotsLoading(true);
    try {
      const { from, to } = getWeekRange(weekOffset);
      const data = await fetchAllPrivateSpeakingSlots(from, to) as Slot[];
      setSlots(selectedTutor ? data.filter(s => s.tutorProfileId === selectedTutor) : data);
    } catch {
      setError('Could not load available slots.');
    } finally {
      setSlotsLoading(false);
    }
  }, [weekOffset, selectedTutor]);

  useEffect(() => {
    if (viewMode === 'browse' && !loading) loadSlots();
  }, [viewMode, loading, loadSlots]);

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
        setEntitlementRemaining(result.speakingSessionsRemaining ?? null);
      }
      const updated = await fetchLearnerPrivateSpeakingBookings() as Booking[];
      setBookings(updated);
      setViewMode('bookings');
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Could not book session.';
      setError(message);
    } finally {
      setBookingInProgress(false);
    }
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

      // Same-day reschedule incurs a 50% Stripe penalty — redirect to pay it.
      if (result.checkoutUrl) {
        window.location.href = result.checkoutUrl;
        return;
      }

      // Free reschedule completed.
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
      const token = await fetchPrivateSpeakingJoinToken(booking.id);
      if (token.sdkKey && token.signature) {
        setActiveMeeting({ token, title: booking.tutorName ?? 'Private Speaking Session', startsAt: booking.sessionStartUtc });
        return;
      }

      const joinUrl = safeZoomUrl(token.joinUrl);
      if (joinUrl) {
        window.open(joinUrl, '_blank', 'noopener,noreferrer');
        return;
      }

      setError('Zoom details are not ready for this session yet.');
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Could not prepare the Zoom room.');
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
      <MotionItem key={booking.id} delayIndex={i}
        className="bg-surface rounded-xl border border-border p-4">
        <div className="flex flex-col sm:flex-row sm:items-center gap-3">
          <div className="flex-1">
            <div className="flex items-center gap-2 mb-1">
              <span className="font-medium text-navy text-sm">{booking.tutorName ?? 'Tutor'}</span>
              <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_COLORS[booking.status] ?? 'bg-background-light text-muted'}`}>
                {FRIENDLY_STATUS[booking.status] ?? booking.status}
              </span>
              {outcome && (
                <span className={`text-xs font-medium ${OUTCOME_TONE[outcome.tone]}`}>{outcome.label}</span>
              )}
            </div>
            <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted/60">
              <span className="flex items-center gap-1"><Calendar className="w-3.5 h-3.5" />{formatDate(booking.sessionStartUtc)}</span>
              <span>{booking.durationMinutes} min</span>
              <span>{booking.entitlementConsumed ? 'Session credit' : formatPrice(booking.priceMinorUnits, booking.currency)}</span>
            </div>
            <div className="flex items-center gap-1 text-xs text-muted/60 mt-1">
              <Globe className="w-3.5 h-3.5 shrink-0" aria-hidden />{formatLocalAndUkTime(booking.sessionStartUtc)}
            </div>
          </div>

          <div className="flex items-center gap-2">
            {(booking.status === 'ZoomCreated' || booking.status === 'InProgress') && (
              <button onClick={() => handleJoin(booking)} disabled={!joinOpen || joiningBookingId === booking.id}
                title={joinOpen ? undefined : `The Join button activates ${JOIN_LEAD_MINUTES} minutes before the session starts.`}
                className="flex items-center gap-1.5 px-3 py-1.5 bg-info hover:bg-info/90 text-white rounded-lg text-xs font-medium transition-colors disabled:opacity-50">
                <Video className="w-3.5 h-3.5" /> {joiningBookingId === booking.id ? 'Opening...' : joinOpen ? 'Join' : 'Join soon'}
              </button>
            )}

            {config.allowReschedule && (booking.status === 'Confirmed' || booking.status === 'ZoomCreated') && (
              <button onClick={() => startReschedule(booking)}
                className="text-xs text-primary hover:text-primary font-medium">
                Reschedule
              </button>
            )}

            {(booking.status === 'Confirmed' || booking.status === 'ZoomCreated') && (
              <button onClick={() => handleDownloadInvite(booking.id)}
                className="flex items-center gap-1 text-xs text-muted hover:text-navy font-medium">
                <Download className="w-3.5 h-3.5" /> Calendar
              </button>
            )}

            {/* Cancel button — opens the policy confirmation modal (PDF §12). */}
            {(booking.status === 'Confirmed' || booking.status === 'ZoomCreated') && (
              <button onClick={() => setCancelConfirm(booking)}
                className="text-xs text-danger hover:text-danger font-medium">
                Cancel
              </button>
            )}

            {/* Rating */}
            {booking.status === 'Completed' && booking.learnerRating === null && (
              ratingSession === booking.id ? (
                <div className="flex flex-wrap items-center gap-2">
                  {[1, 2, 3, 4, 5].map(v => (
                    <button key={v} onClick={() => setRatingValue(v)}
                      className={`w-10 h-10 rounded-full text-sm ${ratingValue >= v ? 'text-warning' : 'text-muted/40'}`}>★</button>
                  ))}
                  <input type="text" placeholder="Feedback" value={ratingFeedback}
                    onChange={e => setRatingFeedback(e.target.value)}
                    className="px-2 py-1 border border-border rounded text-xs w-24" />
                  <button onClick={() => handleRate(booking.id)} className="text-xs px-3 py-2.5 bg-warning hover:bg-warning/90 text-white rounded-lg">Submit</button>
                  <button onClick={() => setRatingSession(null)} className="text-xs text-muted/60 py-2 px-1">Cancel</button>
                </div>
              ) : (
                <button onClick={() => setRatingSession(booking.id)} className="flex items-center gap-1.5 text-sm text-warning hover:text-warning font-medium py-2 px-1">
                  <Star className="w-4 h-4" /> Rate
                </button>
              )
            )}

            {booking.learnerRating !== null && (
              <div className="flex items-center gap-1 text-warning text-sm">
                {'★'.repeat(booking.learnerRating)}{'☆'.repeat(5 - booking.learnerRating)}
              </div>
            )}
          </div>
        </div>
      </MotionItem>
    );
  }

  if (loading) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4">
          <Skeleton className="h-20 rounded-xl" />
          <Skeleton className="h-64 rounded-xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!config?.isEnabled) {
    return (
      <LearnerDashboardShell>
        <LearnerPageHero title="Private Speaking Sessions" description="This feature is not currently available." icon={Mic} />
        <InlineAlert variant="info" className="mt-4">Private speaking sessions are temporarily unavailable. Check back once tutor availability is enabled.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  if (activeMeeting && activeMeeting.token.sdkKey && activeMeeting.token.signature) {
    return (
      <LearnerDashboardShell>
        <div className="space-y-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <div>
              <p className="text-xs font-semibold uppercase tracking-wide text-primary">Private Speaking</p>
              <h1 className="text-xl font-semibold text-navy">{activeMeeting.title}</h1>
              <p className="text-sm text-muted">{formatDate(activeMeeting.startsAt)}</p>
            </div>
            <button type="button" onClick={() => setActiveMeeting(null)} className="rounded-lg border border-border px-4 py-2 text-sm font-medium text-muted hover:text-navy">
              Close meeting
            </button>
          </div>
          <ZoomMeetingEmbed joinToken={activeMeeting.token} onLeave={() => setActiveMeeting(null)} />
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Private Speaking Sessions"
          description="Use your included private speaking sessions for 1-on-1 Zoom practice with expert OET tutors"
          icon={Mic}
        />
      </div>

      <div className="mb-6 rounded-xl border border-primary/20 bg-primary/5 p-4">
        <div className="flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
          <div>
            <p className="text-sm font-semibold text-navy">Speaking session credits</p>
            <p className="text-xs text-muted">Bookings use one bundled or add-on private speaking session.</p>
          </div>
          <span className="inline-flex w-fit items-center rounded-full bg-surface px-3 py-1 text-sm font-semibold text-primary ring-1 ring-primary/20">
            {entitlementRemaining ?? 0} remaining
          </span>
        </div>

        {/* Edge case #5 — no remaining entitlement → purchase CTA. */}
        {hasNoEntitlement && (
          <div className="mt-3 flex flex-col gap-2 rounded-lg border border-warning/30 bg-warning/10 p-3 sm:flex-row sm:items-center sm:justify-between">
            <p className="text-xs text-warning">
              You have no speaking sessions left. Buy more to book a 1-on-1 session with a tutor.
            </p>
            <Link
              href="/cart"
              className="inline-flex w-fit shrink-0 items-center gap-1.5 rounded-lg bg-primary px-3 py-1.5 text-xs font-medium text-white transition-colors hover:bg-primary/90 dark:bg-violet-700 dark:hover:bg-violet-600"
            >
              <ShoppingBag className="h-3.5 w-3.5" aria-hidden /> Buy speaking sessions
            </Link>
          </div>
        )}
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}<button onClick={() => setError(null)} aria-label="Dismiss error" className="ml-2"><X className="w-4 h-4 inline" aria-hidden /></button></InlineAlert>}

      {/* View mode toggle */}
      <div className="flex gap-2 mb-6">
        <button onClick={() => setViewMode('browse')} className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${viewMode === 'browse' ? 'bg-primary text-white dark:bg-violet-700' : 'bg-background-light text-muted hover:bg-border'}`}>
          Browse Slots
        </button>
        <button onClick={() => setViewMode('bookings')} className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${viewMode === 'bookings' ? 'bg-primary text-white dark:bg-violet-700' : 'bg-background-light text-muted hover:bg-border'}`}>
          My Bookings {bookings.length > 0 && <span className="ml-1 bg-white/20 px-1.5 rounded-full text-xs">{bookings.length}</span>}
        </button>
      </div>

      {/* ── Browse Slots ──────────────────────────────── */}
      {viewMode === 'browse' && (
        <>
          {/* Week navigation + tutor filter */}
          <div className="flex flex-col sm:flex-row items-start sm:items-center gap-3 mb-5">
            <div className="flex items-center gap-2">
              <button onClick={() => setWeekOffset(Math.max(0, weekOffset - 1))} disabled={weekOffset === 0}
                aria-label="Previous week"
                className="p-2.5 rounded-lg border border-border hover:bg-background-light disabled:opacity-30 transition-colors">
                <ChevronLeft className="w-4 h-4" aria-hidden />
              </button>
              <span className="text-sm font-medium text-navy min-w-[200px] text-center">{weekLabel}</span>
              <button onClick={() => setWeekOffset(weekOffset + 1)}
                aria-label="Next week"
                className="p-2.5 rounded-lg border border-border hover:bg-background-light transition-colors">
                <ChevronRight className="w-4 h-4" aria-hidden />
              </button>
            </div>
            <select value={selectedTutor ?? ''} onChange={e => setSelectedTutor(e.target.value || null)}
              className="px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy">
              <option value="">All tutors</option>
              {tutors.map(t => (
                <option key={t.id} value={t.id}>{t.displayName}</option>
              ))}
            </select>
          </div>

          {/* Tutor spotlight cards */}
          {tutors.length > 0 && !selectedTutor && (
            <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3 mb-6">
              {tutors.map(t => (
                <button key={t.id} onClick={() => setSelectedTutor(t.id)}
                  className="text-left bg-surface rounded-xl border border-border p-4 hover:border-primary/30 transition-colors">
                  <div className="flex items-center gap-3 mb-2">
                    <div className="w-10 h-10 rounded-full bg-primary/10 flex items-center justify-center">
                      <User className="w-5 h-5 text-primary" />
                    </div>
                    <div>
                      <h4 className="font-medium text-navy text-sm">{t.displayName}</h4>
                      <div className="flex items-center gap-1 text-xs text-muted/60">
                        {t.averageRating > 0 && <><Star className="w-3 h-3 text-warning fill-warning" /> {t.averageRating.toFixed(1)}</>}
                        {t.totalSessions > 0 && <span className="ml-1">· {t.totalSessions} sessions</span>}
                      </div>
                    </div>
                  </div>
                  {t.bio && <p className="text-xs text-muted line-clamp-2">{t.bio}</p>}
                  <div className="mt-2 text-xs text-primary">
                    {formatPrice(t.priceOverrideMinorUnits ?? config.defaultPriceMinorUnits, config.currency)} · {t.slotDurationOverrideMinutes ?? config.defaultSlotDurationMinutes} min
                  </div>
                </button>
              ))}
            </div>
          )}

          {slotsLoading ? (
            <div className="space-y-3"><Skeleton className="h-32 rounded-xl" /><Skeleton className="h-32 rounded-xl" /></div>
          ) : Object.keys(slotsByDate).length === 0 ? (
            <div className="text-center py-12 text-muted/60">
              <Calendar className="w-10 h-10 mx-auto mb-3 opacity-30" aria-hidden />
              <p>No available slots this week. Try another week or tutor.</p>
            </div>
          ) : (
            <div className="space-y-4">
              {Object.entries(slotsByDate).sort(([a], [b]) => a.localeCompare(b)).map(([date, daySlots]) => (
                <div key={date}>
                  <h3 className="text-sm font-semibold text-navy mb-2">
                    {new Date(date + 'T00:00:00').toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long' })}
                  </h3>
                  <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-2">
                    {daySlots.map((slot, i) => {
                      const isSelected = selectedSlot?.startTimeUtc === slot.startTimeUtc && selectedSlot?.tutorProfileId === slot.tutorProfileId;
                      return (
                        <button key={`${slot.tutorProfileId}-${slot.startTimeUtc}-${i}`}
                          onClick={() => setSelectedSlot(isSelected ? null : slot)}
                          className={`p-3 rounded-lg border text-left transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 text-sm ${
                            isSelected
                              ? 'border-primary bg-primary/10 ring-2 ring-primary/30'
                              : 'border-border bg-surface hover:border-primary/30'
                          }`}>
                          <div className="font-medium text-navy">{slot.startTimeLocal}</div>
                          <div className="flex items-start gap-1 text-[11px] text-muted mt-0.5">
                            <Globe className="w-3 h-3 mt-0.5 shrink-0 text-muted/60" aria-hidden />
                            <span>{formatLocalAndUkTime(slot.startTimeUtc)}</span>
                          </div>
                          <div className="text-xs text-muted mt-0.5">{slot.tutorDisplayName}</div>
                          <div className="text-xs text-primary mt-1">
                            {formatPrice(slot.priceMinorUnits, slot.currency)} · {slot.durationMinutes}m
                          </div>
                        </button>
                      );
                    })}
                  </div>
                </div>
              ))}
            </div>
          )}

          {/* Booking confirmation panel */}
          {selectedSlot && (
            <MotionSection className="fixed bottom-[calc(var(--bottom-nav-height)+0.5rem)] lg:bottom-6 left-4 right-4 sm:left-auto sm:right-6 sm:max-w-md bg-surface rounded-xl border border-primary/30 shadow-2xl p-5 z-40">
              <div className="flex items-center justify-between mb-3">
                <h3 className="font-semibold text-navy">{rescheduleTarget ? 'Confirm Reschedule' : 'Confirm Booking'}</h3>
                <button onClick={() => { setSelectedSlot(null); setRescheduleTarget(null); }} aria-label="Close booking panel" className="text-muted/60 hover:text-muted"><X className="w-5 h-5" aria-hidden /></button>
              </div>
              <div className="space-y-2 text-sm text-muted mb-4">
                <div className="flex items-center gap-2"><User className="w-4 h-4 text-muted/60" /> {selectedSlot.tutorDisplayName}</div>
                <div className="flex items-center gap-2"><Calendar className="w-4 h-4 text-muted/60" /> {formatDate(selectedSlot.startTimeUtc)}</div>
                <div className="flex items-start gap-2"><Globe className="w-4 h-4 mt-0.5 text-muted/60" /> {formatLocalAndUkTime(selectedSlot.startTimeUtc)}</div>
                <div className="flex items-center gap-2"><Clock className="w-4 h-4 text-muted/60" /> {selectedSlot.durationMinutes} minutes</div>
                <div className="flex items-center gap-2"><CreditCard className="w-4 h-4 text-muted/60" /> Uses 1 session credit</div>
              </div>
              {/* Profession track (booking only — reschedule keeps the original track). */}
              {!rescheduleTarget && (
                <div className="mb-3">
                  <label htmlFor="profession-track" className="mb-1 block text-xs font-medium text-navy">Profession track</label>
                  <select
                    id="profession-track"
                    value={professionTrack}
                    onChange={e => setProfessionTrack(e.target.value as ProfessionTrack)}
                    className="w-full px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy focus:outline-none focus:ring-2 focus:ring-primary/40"
                  >
                    {PROFESSION_TRACKS.map(track => (
                      <option key={track} value={track}>{track}</option>
                    ))}
                  </select>
                </div>
              )}
              <textarea
                placeholder="Notes for the tutor (optional)"
                value={bookingNotes}
                onChange={e => setBookingNotes(e.target.value)}
                rows={2}
                className="w-full px-3 py-2 border border-border rounded-lg text-sm bg-surface text-navy resize-none focus:outline-none focus:ring-2 focus:ring-primary/40 mb-3"
              />
              <button onClick={rescheduleTarget ? () => setRescheduleConfirmOpen(true) : handleBook} disabled={bookingInProgress || (!rescheduleTarget && (entitlementRemaining ?? 0) <= 0)}
                className="w-full px-5 py-2.5 bg-primary hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600 text-white rounded-lg text-sm font-medium disabled:opacity-50 transition-[color,background-color,transform] duration-200">
                {bookingInProgress ? 'Processing...' : rescheduleTarget ? 'Confirm Reschedule' : 'Use Session Credit & Book'}
              </button>
              <p className="text-xs text-muted/60 text-center mt-2">
                {rescheduleTarget ? 'Your original booking will close and this slot will replace it.' : 'No checkout is needed when a session credit is available.'}
              </p>
            </MotionSection>
          )}
        </>
      )}

      {/* ── My Bookings ───────────────────────────────── */}
      {viewMode === 'bookings' && (
        <>
          {bookings.length === 0 ? (
            <>
              <LearnerSurfaceSectionHeader title="Your Bookings" />
              <div className="text-center py-12 text-muted/60">
                <Mic className="w-10 h-10 mx-auto mb-3 opacity-30" aria-hidden />
                <p>No bookings yet. Browse available slots to get started.</p>
              </div>
            </>
          ) : (
            <div className="space-y-8">
              {/* Upcoming Speaking Sessions (PDF §11) */}
              <section>
                <LearnerSurfaceSectionHeader title="Upcoming Speaking Sessions" />
                {upcomingBookings.length === 0 ? (
                  <p className="text-sm text-muted/60 py-2">No upcoming sessions. Browse slots to book one.</p>
                ) : (
                  <div className="space-y-3">
                    {upcomingBookings.map((booking, i) => renderBookingCard(booking, i))}
                  </div>
                )}
              </section>

              {/* Past Speaking Sessions (PDF §11) */}
              {pastBookings.length > 0 && (
                <section>
                  <LearnerSurfaceSectionHeader title="Past Speaking Sessions" />
                  <div className="space-y-3">
                    {pastBookings.map((booking, i) => renderBookingCard(booking, i))}
                  </div>
                </section>
              )}
            </div>
          )}
        </>
      )}

      {/* Cancellation policy confirmation modal (PDF §12) */}
      <Modal open={cancelConfirm !== null} onClose={() => { if (!cancelInProgress) setCancelConfirm(null); }} title="Cancel Speaking session" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">{CANCELLATION_POLICY_TEXT}</p>
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <button type="button" onClick={() => setCancelConfirm(null)} disabled={cancelInProgress}
              className="rounded-lg border border-border px-4 py-2 text-sm font-medium text-muted hover:text-navy disabled:opacity-50">
              Keep booking
            </button>
            <button type="button" onClick={handleCancelConfirmed} disabled={cancelInProgress}
              className="rounded-lg bg-danger px-4 py-2 text-sm font-medium text-white hover:bg-danger/90 disabled:opacity-50">
              {cancelInProgress ? 'Cancelling...' : 'Confirm cancellation'}
            </button>
          </div>
        </div>
      </Modal>

      {/* Reschedule policy confirmation modal (PDF §12) */}
      <Modal open={rescheduleConfirmOpen} onClose={() => { if (!bookingInProgress) setRescheduleConfirmOpen(false); }} title="Reschedule Speaking session" size="sm">
        <div className="space-y-4">
          <p className="text-sm text-muted">{RESCHEDULE_POLICY_TEXT}</p>
          {selectedSlot && (
            <p className="text-xs text-muted/60">
              New time: {formatLocalAndUkTime(selectedSlot.startTimeUtc)}
            </p>
          )}
          <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
            <button type="button" onClick={() => setRescheduleConfirmOpen(false)} disabled={bookingInProgress}
              className="rounded-lg border border-border px-4 py-2 text-sm font-medium text-muted hover:text-navy disabled:opacity-50">
              Back
            </button>
            <button type="button" onClick={handleReschedule} disabled={bookingInProgress}
              className="rounded-lg bg-primary px-4 py-2 text-sm font-medium text-white hover:bg-primary/90 dark:bg-violet-700 dark:hover:bg-violet-600 disabled:opacity-50">
              {bookingInProgress ? 'Processing...' : 'Confirm reschedule'}
            </button>
          </div>
        </div>
      </Modal>
    </LearnerDashboardShell>
  );
}
