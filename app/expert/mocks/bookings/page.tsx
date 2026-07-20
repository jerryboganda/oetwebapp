'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { CalendarClock, CheckCircle2, ClipboardList, ExternalLink, Sparkles, Video } from 'lucide-react';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchExpertMockBookings } from '@/lib/api';
import type { MockBooking } from '@/lib/mock-data';
import { safeZoomUrl } from '@/lib/zoom-url';

function isOpenBooking(booking: MockBooking): boolean {
  return booking.status !== 'completed' && booking.status !== 'cancelled';
}

export default function ExpertMockBookingsPage() {
  const [items, setItems] = useState<MockBooking[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState('');

  useEffect(() => {
    fetchExpertMockBookings()
      .then(setItems)
      .catch((err) => setError(err instanceof Error ? err.message : 'Failed to load mock bookings.'))
      .finally(() => setLoading(false));
  }, []);

  const { scheduledCount, completedCount, nextSessionLabel } = useMemo(() => {
    const open = items.filter(isOpenBooking);
    const completed = items.filter((booking) => booking.status === 'completed');
    const upcoming = open
      .filter((booking) => new Date(booking.scheduledStartAt).getTime() >= Date.now())
      .sort((a, b) => new Date(a.scheduledStartAt).getTime() - new Date(b.scheduledStartAt).getTime());
    return {
      scheduledCount: open.length,
      completedCount: completed.length,
      nextSessionLabel: upcoming[0]
        ? new Date(upcoming[0].scheduledStartAt).toLocaleString(undefined, {
            weekday: 'short', day: 'numeric', month: 'short', hour: '2-digit', minute: '2-digit',
          })
        : '—',
    };
  }, [items]);

  return (
    <ExpertRouteWorkspace>
      <div className="space-y-8">
        <ExpertRouteHero
          eyebrow="Mocks V2"
          title="Mock session bookings"
          description="Your scheduled mock sessions. Candidate cards are visible to learners; interlocutor cards and live-room controls are tutor-only."
          icon={Sparkles}
          accent="primary"
          highlights={[
            { icon: ClipboardList, label: 'Scheduled', value: String(scheduledCount) },
            { icon: CheckCircle2, label: 'Completed', value: String(completedCount) },
            { icon: CalendarClock, label: 'Next session', value: nextSessionLabel },
          ]}
        />

        <section className="space-y-4">
          <ExpertRouteSectionHeader
            eyebrow="Live mocks"
            title="Scheduled mock sessions"
            description="Open the in-app room to run the session state machine; the Zoom link opens the external meeting."
            action={<CalendarClock className="h-5 w-5 text-muted" aria-hidden="true" />}
          />

          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}

          {!loading ? (
            <div className="grid gap-4">
              {items.map((booking) => {
                const zoomUrl = safeZoomUrl(booking.zoomJoinUrl ?? booking.joinUrl ?? null);
                return (
                  <article key={booking.id} className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
                    <div className="flex flex-wrap items-start justify-between gap-3">
                      <div>
                        <div className="flex flex-wrap items-center gap-2">
                          <h2 className="font-black text-navy">{booking.title ?? booking.mockBundleId}</h2>
                          <Badge variant={booking.status === 'completed' ? 'success' : booking.status === 'cancelled' ? 'muted' : 'info'}>{booking.status}</Badge>
                          <Badge variant="outline">{booking.liveRoomState ?? 'waiting'}</Badge>
                        </div>
                        <p className="mt-2 text-sm text-muted">{new Date(booking.scheduledStartAt).toLocaleString()} ({booking.timezoneIana})</p>
                        <p className="mt-1 text-xs text-muted">Learner: candidate card only / Tutor: interlocutor card visible</p>
                      </div>
                      <div className="flex flex-wrap items-center gap-2">
                        <Link
                          href={`/expert/speaking-room/${encodeURIComponent(booking.id)}`}
                          className="inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary-dark"
                        >
                          <Video className="h-4 w-4" aria-hidden /> Open room
                        </Link>
                        {zoomUrl ? (
                          <a
                            href={zoomUrl}
                            target="_blank"
                            rel="noopener noreferrer"
                            className="inline-flex items-center gap-1.5 rounded-lg border border-border bg-surface px-3 py-2 text-sm font-semibold text-navy transition-colors hover:bg-primary/5"
                          >
                            <ExternalLink className="h-4 w-4" aria-hidden /> Zoom link
                          </a>
                        ) : null}
                      </div>
                    </div>
                  </article>
                );
              })}
              {items.length === 0 ? <InlineAlert variant="info">No scheduled mock sessions are assigned yet.</InlineAlert> : null}
            </div>
          ) : null}
        </section>
      </div>
    </ExpertRouteWorkspace>
  );
}
