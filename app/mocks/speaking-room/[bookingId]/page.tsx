'use client';

/**
 * OET Mocks V2 — Speaking audio-only live-room (learner side).
 *
 * Privacy & integrity:
 *   - We never display interlocutor identity (name, email, photo). The booking
 *     projection from the backend already strips that for non-admins. We only
 *     show the role-play card (CandidateCard) and a Zoom join URL (audio-only
 *     join hint).
 *   - We never display the Zoom start URL or password — the backend projection
 *     hides them for learners.
 *   - The room transition (waiting -> in_progress -> completed) is forwarded
 *     to the W6 endpoint POST /v1/mock-bookings/{id}/live-room/transition so
 *     the audit trail is server-authoritative.
 */

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, CalendarClock, CheckCircle2, Mic, ShieldCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchMockBookingList,
  transitionMockBookingLiveRoom,
  type MockLiveRoomTargetState,
} from '@/lib/api';
import type { MockBooking } from '@/lib/mock-data';
import { analytics } from '@/lib/analytics';

const STATE_LABEL: Record<string, string> = {
  waiting: 'Waiting for tutor',
  in_progress: 'Live role-play in progress',
  completed: 'Completed',
  tutor_no_show: 'Tutor did not arrive',
};

const STATE_VARIANT: Record<string, 'muted' | 'success' | 'warning' | 'info'> = {
  waiting: 'muted',
  in_progress: 'info',
  completed: 'success',
  tutor_no_show: 'warning',
};

export default function SpeakingLiveRoomPage() {
  const params = useParams<{ bookingId: string }>();
  const router = useRouter();
  const bookingId = params?.bookingId ?? '';

  const [booking, setBooking] = useState<MockBooking | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [transitioning, setTransitioning] = useState(false);

  useEffect(() => {
    if (!bookingId) return;
    analytics.track('content_view', { page: 'mock-speaking-room', bookingId });
    fetchMockBookingList()
      .then((response) => {
        const matched = response.items.find((b) => b.bookingId === bookingId || b.id === bookingId);
        if (!matched) {
          setError('Booking not found or no longer accessible from this account.');
          return;
        }
        setBooking(matched);
      })
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load this booking.'))
      .finally(() => setLoading(false));
  }, [bookingId]);

  const liveState = booking?.liveRoomState ?? 'waiting';

  const handleTransition = async (target: MockLiveRoomTargetState) => {
    if (!booking) return;
    setTransitioning(true);
    setError(null);
    setSuccess(null);
    try {
      const updated = await transitionMockBookingLiveRoom(booking.bookingId ?? booking.id, target);
      setBooking(updated);
      setSuccess(`Live room state updated to ${STATE_LABEL[updated.liveRoomState ?? target] ?? target}.`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not transition the live room.');
    } finally {
      setTransitioning(false);
    }
  };

  const scheduledStart = useMemo(() => {
    if (!booking) return null;
    try {
      return new Date(booking.scheduledStartAt).toLocaleString();
    } catch {
      return booking.scheduledStartAt;
    }
  }, [booking]);

  return (
    <LearnerDashboardShell pageTitle="Speaking Live Room" subtitle="Audio-only role-play with your tutor." backHref="/mocks/bookings">
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/mocks/bookings')}>
          <ArrowLeft className="h-4 w-4" />
          Back to bookings
        </Button>

        {loading ? (
          <Skeleton className="h-72 rounded-3xl" />
        ) : error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : booking ? (
          <>
            <LearnerPageHero
              eyebrow="Speaking · Live role-play"
              icon={Mic}
              accent="navy"
              title={booking.title ?? 'OET Speaking Mock'}
              description="Audio-only delivery to keep parity with the official OET interlocutor experience. Your tutor's identity is intentionally hidden until the session begins."
              highlights={[
                { icon: CalendarClock, label: 'Scheduled', value: scheduledStart ?? '—' },
                { icon: ShieldCheck, label: 'Recording', value: booking.consentToRecording ? 'Consent given' : 'No recording' },
                { icon: CheckCircle2, label: 'State', value: STATE_LABEL[liveState] ?? liveState },
              ]}
            />

            {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}

            <section className="rounded-3xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={STATE_VARIANT[liveState] ?? 'muted'} size="sm">
                  {STATE_LABEL[liveState] ?? liveState}
                </Badge>
                {booking.candidateCardVisible ? (
                  <Badge variant="success" size="sm">Candidate role-play card available</Badge>
                ) : (
                  <Badge variant="muted" size="sm">Candidate card not yet released</Badge>
                )}
              </div>

              <div className="mt-5 grid gap-4 lg:grid-cols-2">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">How to join</p>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    Use the audio-only link below. Keep your camera off — the OET role-play is voice-only and no video is graded.
                  </p>
                  {booking.zoomJoinUrl ? (
                    <Link
                      href={booking.zoomJoinUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-primary px-4 py-3 text-sm font-black text-white shadow-sm hover:bg-primary/90"
                    >
                      <Mic className="h-4 w-4" />
                      Join audio-only room
                    </Link>
                  ) : (
                    <p className="mt-3 text-xs text-muted">
                      The join URL appears here once your tutor opens the room. Refresh this page if it does not show within a minute of your scheduled start.
                    </p>
                  )}
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">Live-room controls</p>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    The tutor normally drives state transitions. These controls are here for the audited learner-side fallback (forward-only).
                  </p>
                  <div className="mt-3 flex flex-wrap gap-2">
                    {liveState === 'waiting' ? (
                      <Button size="sm" variant="primary" onClick={() => handleTransition('in_progress')} loading={transitioning} disabled={transitioning}>
                        I&apos;m in the room — start session
                      </Button>
                    ) : null}
                    {liveState === 'in_progress' ? (
                      <Button size="sm" variant="secondary" onClick={() => handleTransition('completed')} loading={transitioning} disabled={transitioning}>
                        Mark session complete
                      </Button>
                    ) : null}
                    {liveState === 'completed' ? (
                      <Badge variant="success" size="sm">Session closed</Badge>
                    ) : null}
                    {liveState === 'tutor_no_show' ? (
                      <Badge variant="warning" size="sm">Tutor no-show recorded — admin will follow up</Badge>
                    ) : null}
                  </div>
                </div>
              </div>

              {booking.learnerNotes ? (
                <div className="mt-4 rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">Your notes</p>
                  <p className="mt-2">{booking.learnerNotes}</p>
                </div>
              ) : null}

              <div className="mt-4 rounded-2xl border border-border bg-background-light p-4 text-xs leading-5 text-muted">
                Privacy: tutor identity is intentionally hidden by the platform until the role-play begins, in line with OET interlocutor anonymity. The recording flag and audit trail are server-authoritative.
              </div>
            </section>
          </>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
