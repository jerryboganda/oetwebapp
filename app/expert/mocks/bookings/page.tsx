'use client';

import { useEffect, useState } from 'react';
import { CalendarClock, Video } from 'lucide-react';
import { ExpertRouteHero, ExpertRouteWorkspace } from '@/components/domain/expert-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { fetchExpertMockBookings } from '@/lib/api';
import type { MockBooking } from '@/lib/mock-data';

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

  return (
    <ExpertRouteWorkspace>
      <div className="space-y-6">
        <ExpertRouteHero
          eyebrow="Mocks V2"
          title="Teacher mock-session queue"
          description="Candidate cards are visible to learners. Interlocutor cards and live-room controls are tutor-only."
          icon={CalendarClock}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {loading ? <Skeleton className="h-48 rounded-2xl" /> : null}

        {!loading ? (
          <div className="grid gap-4">
            {items.map((booking) => (
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
                  {booking.zoomJoinUrl || booking.joinUrl ? (
                    <Button variant="primary" onClick={() => window.location.assign(booking.zoomJoinUrl ?? booking.joinUrl ?? '#')}>
                      <Video className="mr-2 h-4 w-4" /> Open room
                    </Button>
                  ) : null}
                </div>
              </article>
            ))}
            {items.length === 0 ? <InlineAlert variant="info">No scheduled mock sessions are assigned yet.</InlineAlert> : null}
          </div>
        ) : null}
      </div>
    </ExpertRouteWorkspace>
  );
}
