'use client';

/**
 * OET Mocks V2 Wave 6 — Expert (tutor / interlocutor) speaking-room.
 *
 * Mirror of the learner-side page at `app/mocks/speaking-room/[bookingId]`,
 * but on the expert side — and therefore deliberately EXPOSES the
 * interlocutor card (background, cue prompts, patient profile, role-play
 * scenario) plus the Zoom host URL. The learner DTO strips all of this; see
 * `MockBookingLearnerDtoTests` for the regression guard that locks it in.
 *
 * Authorization is enforced server-side: the bound endpoint
 * `GET /v1/expert/mocks/bookings/{bookingId}` requires the `ExpertOnly`
 * policy AND that the caller be the assigned tutor / interlocutor (or
 * admin). Middleware only enforces auth-cookie presence; role checks live
 * on the API.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import Link from 'next/link';
import {
  ArrowLeft,
  CalendarClock,
  CheckCircle2,
  ClipboardList,
  Mic,
  ShieldCheck,
  UserCircle2,
} from 'lucide-react';
import { ExpertRouteHero, ExpertRouteWorkspace } from '@/components/domain/expert-route-surface';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import {
  fetchExpertMockBookingDetail,
  transitionMockBookingLiveRoom,
  type ExpertMockBookingDetail,
  type ExpertSpeakingInterlocutorCard,
  type MockLiveRoomTargetState,
} from '@/lib/api';
import { analytics } from '@/lib/analytics';

const STATE_LABEL: Record<string, string> = {
  waiting: 'Waiting for tutor',
  in_progress: 'Live role-play in progress',
  completed: 'Completed',
  tutor_no_show: 'Marked tutor no-show',
};

const STATE_VARIANT: Record<string, 'muted' | 'success' | 'warning' | 'info'> = {
  waiting: 'muted',
  in_progress: 'info',
  completed: 'success',
  tutor_no_show: 'warning',
};

function pickPromptList(card: ExpertSpeakingInterlocutorCard | undefined): string[] {
  if (!card) return [];
  // Explicit narrowed lookups instead of dynamic indexing so any future field
  // rename in `ExpertSpeakingInterlocutorCard` produces a compile error.
  const candidates: ReadonlyArray<readonly string[] | undefined> = [
    card.cuePrompts,
    card.prompts,
    card.objectives,
  ];
  for (const list of candidates) {
    if (Array.isArray(list) && list.length > 0) {
      return list.filter((entry): entry is string => typeof entry === 'string' && entry.trim().length > 0);
    }
  }
  return [];
}

export default function ExpertSpeakingLiveRoomPage() {
  const params = useParams<{ bookingId: string }>();
  const router = useRouter();
  const bookingId = params?.bookingId ?? '';

  const [booking, setBooking] = useState<ExpertMockBookingDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [transitioning, setTransitioning] = useState(false);

  useEffect(() => {
    if (!bookingId) return;
    analytics.track('content_view', { page: 'expert-mock-speaking-room', bookingId });
    fetchExpertMockBookingDetail(bookingId)
      .then(setBooking)
      .catch((err) => setError(err instanceof Error ? err.message : 'Could not load this booking.'))
      .finally(() => setLoading(false));
  }, [bookingId]);

  const liveState = booking?.liveRoomState ?? 'waiting';

  const handleTransition = useCallback(
    async (target: MockLiveRoomTargetState) => {
      if (!booking) return;
      setTransitioning(true);
      setError(null);
      setSuccess(null);
      try {
        const updated = await transitionMockBookingLiveRoom(booking.bookingId ?? booking.id, target);
        setBooking((prev) => (prev ? { ...prev, ...updated } : prev));
        setSuccess(`Live room state updated to ${STATE_LABEL[updated.liveRoomState ?? target] ?? target}.`);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not transition the live room.');
      } finally {
        setTransitioning(false);
      }
    },
    [booking],
  );

  const scheduledStart = useMemo(() => {
    if (!booking) return null;
    try {
      return new Date(booking.scheduledStartAt).toLocaleString();
    } catch {
      return booking.scheduledStartAt;
    }
  }, [booking]);

  const interlocutor = booking?.speakingContent?.interlocutorCard;
  const cuePrompts = pickPromptList(interlocutor);
  const warmUps = booking?.speakingContent?.warmUpQuestions ?? [];
  const candidateRole = (booking?.speakingContent?.role as string | undefined)
    ?? (booking?.speakingContent?.candidateCard?.['role'] as string | undefined)
    ?? (booking?.speakingContent?.candidateCard?.['candidateRole'] as string | undefined);
  const setting = (booking?.speakingContent?.setting as string | undefined)
    ?? (booking?.speakingContent?.candidateCard?.['setting'] as string | undefined);
  const patient = (booking?.speakingContent?.patient as string | undefined)
    ?? (booking?.speakingContent?.candidateCard?.['patient'] as string | undefined);
  const candidateBackground = booking?.speakingContent?.background as string | undefined;

  const startUrl = booking?.zoomStartUrl ?? booking?.zoomJoinUrl ?? booking?.joinUrl ?? null;

  return (
    <ExpertRouteWorkspace>
      <div className="space-y-6">
        <Button variant="ghost" className="gap-2" onClick={() => router.push('/expert/mocks/bookings')}>
          <ArrowLeft className="h-4 w-4" />
          Back to assigned bookings
        </Button>

        {loading ? (
          <Skeleton className="h-72 rounded-3xl" />
        ) : error ? (
          <InlineAlert variant="error">{error}</InlineAlert>
        ) : booking ? (
          <>
            <ExpertRouteHero
              eyebrow="Tutor · Speaking live room"
              icon={Mic}
              accent="navy"
              title={booking.title ?? 'OET Speaking Mock'}
              description="Tutor view — interlocutor card, patient background, and live-room controls. The learner cannot see this content."
              highlights={[
                { icon: CalendarClock, label: 'Scheduled', value: scheduledStart ?? '—' },
                {
                  icon: ShieldCheck,
                  label: 'Recording',
                  value: booking.consentToRecording ? 'Consent given' : 'No recording',
                },
                { icon: CheckCircle2, label: 'State', value: STATE_LABEL[liveState] ?? liveState },
              ]}
            />

            {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}

            <section className="rounded-3xl border border-border bg-surface p-6 shadow-sm">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={STATE_VARIANT[liveState] ?? 'muted'} size="sm">
                  {STATE_LABEL[liveState] ?? liveState}
                </Badge>
                <Badge variant="info" size="sm">
                  Tutor: interlocutor card visible
                </Badge>
                {booking.assignedTutorId ? (
                  <Badge variant="muted" size="sm">
                    Tutor ID · {booking.assignedTutorId}
                  </Badge>
                ) : null}
              </div>

              <div className="mt-5 grid gap-4 lg:grid-cols-2">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">Host the room</p>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    Open the audio-only room with your host link. The learner sees only a join URL.
                  </p>
                  {startUrl ? (
                    <Link
                      href={startUrl}
                      target="_blank"
                      rel="noreferrer"
                      className="mt-3 inline-flex w-full items-center justify-center gap-2 rounded-xl bg-primary px-4 py-3 text-sm font-black text-white shadow-sm hover:bg-primary/90"
                    >
                      <Mic className="h-4 w-4" />
                      Open host room
                    </Link>
                  ) : (
                    <p className="mt-3 text-xs text-muted">
                      The host URL appears here once the booking is provisioned in Zoom.
                    </p>
                  )}
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">Live-room controls</p>
                  <p className="mt-2 text-sm leading-6 text-muted">
                    Drive the audited state machine. Learner-side fallbacks exist but tutors are the source of truth.
                  </p>
                  <div className="mt-3 flex flex-wrap gap-2">
                    {liveState === 'waiting' ? (
                      <>
                        <Button
                          size="sm"
                          variant="primary"
                          onClick={() => handleTransition('in_progress')}
                          loading={transitioning}
                          disabled={transitioning}
                        >
                          Mark in progress
                        </Button>
                        <Button
                          size="sm"
                          variant="destructive"
                          onClick={() => handleTransition('tutor_no_show')}
                          loading={transitioning}
                          disabled={transitioning}
                        >
                          Mark tutor no-show
                        </Button>
                      </>
                    ) : null}
                    {liveState === 'in_progress' ? (
                      <Button
                        size="sm"
                        variant="primary"
                        onClick={() => handleTransition('completed')}
                        loading={transitioning}
                        disabled={transitioning}
                      >
                        Mark completed
                      </Button>
                    ) : null}
                    {liveState === 'completed' ? (
                      <Badge variant="success" size="sm">
                        Session closed
                      </Badge>
                    ) : null}
                    {liveState === 'tutor_no_show' ? (
                      <Badge variant="warning" size="sm">
                        Recorded as tutor no-show
                      </Badge>
                    ) : null}
                  </div>
                </div>
              </div>
            </section>

            <section className="rounded-3xl border border-border bg-surface p-6 shadow-sm">
              <header className="flex items-center gap-2">
                <UserCircle2 className="h-5 w-5 text-primary" />
                <h2 className="font-black text-navy">Interlocutor card (tutor only)</h2>
              </header>
              <p className="mt-1 text-xs text-muted">
                Pulled from the bound Speaking content paper. The learner&apos;s endpoint never returns this payload.
              </p>

              <div className="mt-4 grid gap-4 lg:grid-cols-2">
                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">Scenario</p>
                  <dl className="mt-2 space-y-1">
                    {candidateRole ? (
                      <div className="flex gap-2">
                        <dt className="font-black text-navy">Candidate role:</dt>
                        <dd>{candidateRole}</dd>
                      </div>
                    ) : null}
                    {setting ? (
                      <div className="flex gap-2">
                        <dt className="font-black text-navy">Setting:</dt>
                        <dd>{setting}</dd>
                      </div>
                    ) : null}
                    {patient ? (
                      <div className="flex gap-2">
                        <dt className="font-black text-navy">Patient:</dt>
                        <dd>{patient}</dd>
                      </div>
                    ) : null}
                  </dl>
                  {candidateBackground ? (
                    <div className="mt-3">
                      <p className="text-[11px] font-black uppercase tracking-widest text-muted">
                        Candidate background
                      </p>
                      <p className="mt-1 whitespace-pre-line">{candidateBackground}</p>
                    </div>
                  ) : null}
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Patient background (interlocutor)
                  </p>
                  {interlocutor?.background ? (
                    <p className="mt-2 whitespace-pre-line">{interlocutor.background}</p>
                  ) : (
                    <p className="mt-2 text-xs text-muted">
                      No interlocutor background was authored on this paper.
                    </p>
                  )}
                  {interlocutor?.patientProfile ? (
                    <div className="mt-3">
                      <p className="text-[11px] font-black uppercase tracking-widest text-muted">Patient profile</p>
                      <p className="mt-1 whitespace-pre-line">{interlocutor.patientProfile}</p>
                    </div>
                  ) : null}
                </div>
              </div>

              <div className="mt-4 grid gap-4 lg:grid-cols-2">
                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <header className="flex items-center gap-2">
                    <ClipboardList className="h-4 w-4 text-primary" />
                    <p className="text-[11px] font-black uppercase tracking-widest text-muted">Cue prompts</p>
                  </header>
                  {cuePrompts.length > 0 ? (
                    <ol className="mt-2 list-decimal space-y-1 pl-5">
                      {cuePrompts.map((prompt, i) => (
                        <li key={i}>{prompt}</li>
                      ))}
                    </ol>
                  ) : (
                    <p className="mt-2 text-xs text-muted">No cue prompts authored.</p>
                  )}
                </div>

                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">Warm-up questions</p>
                  {warmUps.length > 0 ? (
                    <ul className="mt-2 list-disc space-y-1 pl-5">
                      {warmUps.map((q, i) => (
                        <li key={i}>{q}</li>
                      ))}
                    </ul>
                  ) : (
                    <p className="mt-2 text-xs text-muted">No warm-up questions authored.</p>
                  )}
                </div>
              </div>

              {booking.learnerNotes ? (
                <div className="mt-4 rounded-2xl border border-border bg-background-light p-4 text-sm leading-6 text-muted">
                  <p className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Learner&apos;s pre-session notes
                  </p>
                  <p className="mt-2 whitespace-pre-line">{booking.learnerNotes}</p>
                </div>
              ) : null}
            </section>
          </>
        ) : null}
      </div>
    </ExpertRouteWorkspace>
  );
}
