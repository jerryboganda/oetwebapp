'use client';

import { useEffect, useState, useCallback } from 'react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Mic, Calendar, Star, Clock, CreditCard, Video, X, ChevronLeft, ChevronRight, User } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchPrivateSpeakingConfig,
  fetchPrivateSpeakingTutors,
  fetchAllPrivateSpeakingSlots,
  createPrivateSpeakingBooking,
  fetchLearnerPrivateSpeakingBookings,
  cancelPrivateSpeakingBooking,
  ratePrivateSpeakingSession,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

type Config = {
  isEnabled: boolean; defaultPriceMinorUnits: number; currency: string;
  defaultSlotDurationMinutes: number; cancellationWindowHours: number;
  reservationTimeoutMinutes: number;
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
  learnerRating: number | null; learnerFeedback: string | null;
  createdAt: string;
};

const STATUS_COLORS: Record<string, string> = {
  Reserved: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
  PendingPayment: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-300',
  Confirmed: 'bg-blue-100 text-blue-700 dark:bg-blue-900/30 dark:text-blue-300',
  ZoomCreated: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  InProgress: 'bg-indigo-100 text-indigo-700 dark:bg-indigo-900/30 dark:text-indigo-300',
  Completed: 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-300',
  Cancelled: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
  Expired: 'bg-gray-100 text-gray-500 dark:bg-gray-800 dark:text-gray-400',
  Failed: 'bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-300',
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
  const [bookingNotes, setBookingNotes] = useState('');
  const [bookingInProgress, setBookingInProgress] = useState(false);
  const [ratingSession, setRatingSession] = useState<string | null>(null);
  const [ratingValue, setRatingValue] = useState(5);
  const [ratingFeedback, setRatingFeedback] = useState('');

  // Initial data load
  useEffect(() => {
    analytics.track('private_speaking_page_viewed');
    Promise.all([
      fetchPrivateSpeakingConfig(),
      fetchPrivateSpeakingTutors(),
      fetchLearnerPrivateSpeakingBookings(),
    ]).then(([cfg, tut, bk]) => {
      setConfig(cfg as Config);
      setTutors(tut as Tutor[]);
      setBookings(bk as Booking[]);
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
        idempotencyKey: crypto.randomUUID(),
      }) as { bookingId: string; checkoutUrl: string };

      analytics.track('private_speaking_booking_created', { bookingId: result.bookingId });

      // Redirect to Stripe Checkout
      if (result.checkoutUrl) {
        window.location.href = result.checkoutUrl;
        return;
      }

      // Fallback: refresh bookings
      setSelectedSlot(null);
      setBookingNotes('');
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

  async function handleCancel(bookingId: string) {
    try {
      await cancelPrivateSpeakingBooking(bookingId);
      setBookings(prev => prev.map(b => b.id === bookingId ? { ...b, status: 'Cancelled' } : b));
    } catch {
      setError('Could not cancel booking.');
    }
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

  return (
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Private Speaking Sessions"
          description="Book 1-on-1 sessions with expert OET speaking tutors via Zoom"
          icon={Mic}
        />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}<button onClick={() => setError(null)} className="ml-2"><X className="w-4 h-4 inline" /></button></InlineAlert>}

      {/* View mode toggle */}
      <div className="flex gap-2 mb-6">
        <button onClick={() => setViewMode('browse')} className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${viewMode === 'browse' ? 'bg-indigo-600 text-white' : 'bg-gray-100 dark:bg-gray-800 text-gray-600 dark:text-gray-400 hover:bg-gray-200 dark:hover:bg-gray-700'}`}>
          Browse Slots
        </button>
        <button onClick={() => setViewMode('bookings')} className={`px-4 py-2 rounded-lg text-sm font-medium transition-colors ${viewMode === 'bookings' ? 'bg-indigo-600 text-white' : 'bg-gray-100 dark:bg-gray-800 text-gray-600 dark:text-gray-400 hover:bg-gray-200 dark:hover:bg-gray-700'}`}>
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
                className="p-1.5 rounded-lg border border-gray-200 dark:border-gray-700 hover:bg-gray-100 dark:hover:bg-gray-800 disabled:opacity-30 transition-colors">
                <ChevronLeft className="w-4 h-4" />
              </button>
              <span className="text-sm font-medium text-gray-700 dark:text-gray-300 min-w-[200px] text-center">{weekLabel}</span>
              <button onClick={() => setWeekOffset(weekOffset + 1)}
                className="p-1.5 rounded-lg border border-gray-200 dark:border-gray-700 hover:bg-gray-100 dark:hover:bg-gray-800 transition-colors">
                <ChevronRight className="w-4 h-4" />
              </button>
            </div>
            <select value={selectedTutor ?? ''} onChange={e => setSelectedTutor(e.target.value || null)}
              className="px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-700 dark:text-gray-300">
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
                  className="text-left bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 hover:border-indigo-300 dark:hover:border-indigo-600 transition-colors">
                  <div className="flex items-center gap-3 mb-2">
                    <div className="w-10 h-10 rounded-full bg-indigo-100 dark:bg-indigo-900/30 flex items-center justify-center">
                      <User className="w-5 h-5 text-indigo-600 dark:text-indigo-400" />
                    </div>
                    <div>
                      <h4 className="font-medium text-gray-900 dark:text-white text-sm">{t.displayName}</h4>
                      <div className="flex items-center gap-1 text-xs text-gray-400">
                        {t.averageRating > 0 && <><Star className="w-3 h-3 text-yellow-400 fill-yellow-400" /> {t.averageRating.toFixed(1)}</>}
                        {t.totalSessions > 0 && <span className="ml-1">· {t.totalSessions} sessions</span>}
                      </div>
                    </div>
                  </div>
                  {t.bio && <p className="text-xs text-gray-500 dark:text-gray-400 line-clamp-2">{t.bio}</p>}
                  <div className="mt-2 text-xs text-indigo-600 dark:text-indigo-400">
                    {formatPrice(t.priceOverrideMinorUnits ?? config.defaultPriceMinorUnits, config.currency)} · {t.slotDurationOverrideMinutes ?? config.defaultSlotDurationMinutes} min
                  </div>
                </button>
              ))}
            </div>
          )}

          {slotsLoading ? (
            <div className="space-y-3"><Skeleton className="h-32 rounded-xl" /><Skeleton className="h-32 rounded-xl" /></div>
          ) : Object.keys(slotsByDate).length === 0 ? (
            <div className="text-center py-12 text-gray-400">
              <Calendar className="w-10 h-10 mx-auto mb-3 opacity-30" />
              <p>No available slots this week. Try another week or tutor.</p>
            </div>
          ) : (
            <div className="space-y-4">
              {Object.entries(slotsByDate).sort(([a], [b]) => a.localeCompare(b)).map(([date, daySlots]) => (
                <div key={date}>
                  <h3 className="text-sm font-semibold text-gray-700 dark:text-gray-300 mb-2">
                    {new Date(date + 'T00:00:00').toLocaleDateString('en-AU', { weekday: 'long', day: 'numeric', month: 'long' })}
                  </h3>
                  <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-4 lg:grid-cols-5 gap-2">
                    {daySlots.map((slot, i) => {
                      const isSelected = selectedSlot?.startTimeUtc === slot.startTimeUtc && selectedSlot?.tutorProfileId === slot.tutorProfileId;
                      return (
                        <button key={`${slot.tutorProfileId}-${slot.startTimeUtc}-${i}`}
                          onClick={() => setSelectedSlot(isSelected ? null : slot)}
                          className={`p-3 rounded-lg border text-left transition-all text-sm ${
                            isSelected
                              ? 'border-indigo-500 bg-indigo-50 dark:bg-indigo-900/20 ring-2 ring-indigo-300'
                              : 'border-gray-200 dark:border-gray-700 bg-white dark:bg-gray-800 hover:border-indigo-300'
                          }`}>
                          <div className="font-medium text-gray-900 dark:text-white">{slot.startTimeLocal}</div>
                          <div className="text-xs text-gray-500 dark:text-gray-400 mt-0.5">{slot.tutorDisplayName}</div>
                          <div className="text-xs text-indigo-600 dark:text-indigo-400 mt-1">
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
            <MotionSection className="fixed bottom-20 left-4 right-4 sm:left-auto sm:right-6 sm:max-w-md bg-white dark:bg-gray-800 rounded-xl border border-indigo-200 dark:border-indigo-700 shadow-2xl p-5 z-40">
              <div className="flex items-center justify-between mb-3">
                <h3 className="font-semibold text-gray-900 dark:text-white">Confirm Booking</h3>
                <button onClick={() => setSelectedSlot(null)} className="text-gray-400 hover:text-gray-600"><X className="w-5 h-5" /></button>
              </div>
              <div className="space-y-2 text-sm text-gray-600 dark:text-gray-300 mb-4">
                <div className="flex items-center gap-2"><User className="w-4 h-4 text-gray-400" /> {selectedSlot.tutorDisplayName}</div>
                <div className="flex items-center gap-2"><Calendar className="w-4 h-4 text-gray-400" /> {formatDate(selectedSlot.startTimeUtc)}</div>
                <div className="flex items-center gap-2"><Clock className="w-4 h-4 text-gray-400" /> {selectedSlot.durationMinutes} minutes</div>
                <div className="flex items-center gap-2"><CreditCard className="w-4 h-4 text-gray-400" /> {formatPrice(selectedSlot.priceMinorUnits, selectedSlot.currency)}</div>
              </div>
              <textarea
                placeholder="Notes for the tutor (optional)"
                value={bookingNotes}
                onChange={e => setBookingNotes(e.target.value)}
                rows={2}
                className="w-full px-3 py-2 border border-gray-200 dark:border-gray-700 rounded-lg text-sm bg-white dark:bg-gray-900 text-gray-800 dark:text-gray-200 resize-none focus:outline-none focus:ring-2 focus:ring-indigo-400 mb-3"
              />
              <button onClick={handleBook} disabled={bookingInProgress}
                className="w-full px-5 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-lg text-sm font-medium disabled:opacity-50 transition-colors">
                {bookingInProgress ? 'Processing...' : `Pay ${formatPrice(selectedSlot.priceMinorUnits, selectedSlot.currency)} & Book`}
              </button>
                <p className="text-xs text-gray-400 text-center mt-2">
                  You&apos;ll be redirected to secure checkout. Slot reserved for {config.reservationTimeoutMinutes} minutes.
                </p>
            </MotionSection>
          )}
        </>
      )}

      {/* ── My Bookings ───────────────────────────────── */}
      {viewMode === 'bookings' && (
        <>
          <LearnerSurfaceSectionHeader title="Your Bookings" />
          {bookings.length === 0 ? (
            <div className="text-center py-12 text-gray-400">
              <Mic className="w-10 h-10 mx-auto mb-3 opacity-30" />
              <p>No bookings yet. Browse available slots to get started.</p>
            </div>
          ) : (
            <div className="space-y-3">
              {bookings.map((booking, i) => (
                <MotionItem key={booking.id} delayIndex={i}
                  className="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4">
                  <div className="flex flex-col sm:flex-row sm:items-center gap-3">
                    <div className="flex-1">
                      <div className="flex items-center gap-2 mb-1">
                        <span className="font-medium text-gray-900 dark:text-white text-sm">{booking.tutorName ?? 'Tutor'}</span>
                        <span className={`text-xs px-2 py-0.5 rounded-full ${STATUS_COLORS[booking.status] ?? 'bg-gray-100 text-gray-500'}`}>
                          {FRIENDLY_STATUS[booking.status] ?? booking.status}
                        </span>
                      </div>
                      <div className="flex items-center gap-3 text-xs text-gray-400">
                        <span className="flex items-center gap-1"><Calendar className="w-3.5 h-3.5" />{formatDate(booking.sessionStartUtc)}</span>
                        <span>{booking.durationMinutes} min</span>
                        <span>{formatPrice(booking.priceMinorUnits, booking.currency)}</span>
                      </div>
                    </div>

                    <div className="flex items-center gap-2">
                      {/* Zoom join button */}
                      {booking.zoomJoinUrl && (booking.status === 'ZoomCreated' || booking.status === 'Confirmed') && (
                        <a href={booking.zoomJoinUrl} target="_blank" rel="noopener noreferrer"
                          className="flex items-center gap-1.5 px-3 py-1.5 bg-blue-600 hover:bg-blue-700 text-white rounded-lg text-xs font-medium transition-colors">
                          <Video className="w-3.5 h-3.5" /> Join Zoom
                        </a>
                      )}

                      {/* Cancel button */}
                      {(booking.status === 'Confirmed' || booking.status === 'ZoomCreated') && (
                        <button onClick={() => handleCancel(booking.id)}
                          className="text-xs text-red-500 hover:text-red-600 font-medium">
                          Cancel
                        </button>
                      )}

                      {/* Rating */}
                      {booking.status === 'Completed' && booking.learnerRating === null && (
                        ratingSession === booking.id ? (
                          <div className="flex items-center gap-2">
                            {[1, 2, 3, 4, 5].map(v => (
                              <button key={v} onClick={() => setRatingValue(v)}
                                className={`w-7 h-7 rounded-full text-sm ${ratingValue >= v ? 'text-yellow-400' : 'text-gray-300'}`}>★</button>
                            ))}
                            <input type="text" placeholder="Feedback" value={ratingFeedback}
                              onChange={e => setRatingFeedback(e.target.value)}
                              className="px-2 py-1 border border-gray-200 dark:border-gray-700 rounded text-xs w-24" />
                            <button onClick={() => handleRate(booking.id)} className="text-xs px-3 py-1.5 bg-yellow-500 hover:bg-yellow-600 text-white rounded-lg">Submit</button>
                            <button onClick={() => setRatingSession(null)} className="text-xs text-gray-400">Cancel</button>
                          </div>
                        ) : (
                          <button onClick={() => setRatingSession(booking.id)} className="flex items-center gap-1.5 text-sm text-yellow-500 hover:text-yellow-600 font-medium">
                            <Star className="w-4 h-4" /> Rate
                          </button>
                        )
                      )}

                      {booking.learnerRating !== null && (
                        <div className="flex items-center gap-1 text-yellow-400 text-sm">
                          {'★'.repeat(booking.learnerRating)}{'☆'.repeat(5 - booking.learnerRating)}
                        </div>
                      )}
                    </div>
                  </div>
                </MotionItem>
              ))}
            </div>
          )}
        </>
      )}
    </LearnerDashboardShell>
  );
}
