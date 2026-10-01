'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { GraduationCap, Calendar, Star } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { fetchTutoringSessions, rateTutoringSession } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type TutoringSession = {
  id: string; expertUserId: string; examTypeCode: string; subtestFocus: string | null;
  scheduledAt: string; durationMinutes: number; state: string; price: number;
  learnerRating: number | null;
};

const STATE_VARIANTS: Record<string, 'info' | 'success' | 'danger'> = {
  booked: 'info',
  completed: 'success',
  cancelled: 'danger',
};

function formatDate(iso: string) {
  return new Date(iso).toLocaleString('en-AU', { day: 'numeric', month: 'short', year: 'numeric', hour: '2-digit', minute: '2-digit' });
}

export default function TutoringPage() {
  const [sessions, setSessions] = useState<TutoringSession[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [ratingSession, setRatingSession] = useState<string | null>(null);
  const [ratingValue, setRatingValue] = useState(5);

  useEffect(() => {
    analytics.track('tutoring_page_viewed');
    fetchTutoringSessions().then(data => {
      setSessions(data as TutoringSession[]);
      setLoading(false);
    }).catch(() => {
      setError('Could not load sessions.');
      setLoading(false);
    });
  }, []);

  async function handleRate(sessionId: string) {
    try {
      await rateTutoringSession(sessionId, ratingValue);
      setSessions(prev => prev.map(s => s.id === sessionId ? { ...s, learnerRating: ratingValue } : s));
      setRatingSession(null);
    } catch {
      setError('Could not submit rating.');
    }
  }

  return (
    <>
      <LearnerPageHero
        title="Tutoring Sessions"
        description="Book 1-on-1 sessions with OET expert tutors"
        icon={GraduationCap}
      />

      {error && <InlineAlert variant="warning">{error}</InlineAlert>}
      <InlineAlert variant="info" title="Booking temporarily paused" live="polite">
        Tutor booking will reopen after tutor discovery, fixed launch pricing, and expert payout rules are configured. Existing booked sessions and ratings remain available.
      </InlineAlert>

      <section className="space-y-4">
        <LearnerSurfaceSectionHeader title="Your Sessions" />
        {loading ? (
          <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">{Array.from({ length: 3 }).map((_, i) => <Skeleton aria-hidden key={i} className="h-24 rounded-2xl" />)}</div>
        ) : sessions.length === 0 ? (
          <LearnerEmptyState
            icon={GraduationCap}
            title="No tutoring sessions yet."
            description="Booking is paused for now. Keep practising and your booked sessions will appear here."
            primaryAction={{ label: 'Open Study Plan', href: '/study-plan' }}
          />
        ) : (
          <div className="space-y-3">
            {sessions.map((session, i) => (
              <MotionItem key={session.id} delayIndex={Math.min(i, 5)}>
                <Card padding="md" className="flex flex-col gap-3 sm:flex-row sm:items-center">
                  <div className="min-w-0 flex-1">
                    <div className="mb-1 flex flex-wrap items-center gap-2">
                      <span className="text-sm font-medium text-navy">{session.examTypeCode.toUpperCase()} {session.subtestFocus ? `· ${session.subtestFocus}` : ''}</span>
                      <Badge variant={STATE_VARIANTS[session.state] ?? 'muted'} className="capitalize">{session.state}</Badge>
                    </div>
                    <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted tabular-nums">
                      <span className="flex items-center gap-1"><Calendar className="h-3.5 w-3.5" aria-hidden="true" />{formatDate(session.scheduledAt)}</span>
                      <span>{session.durationMinutes} min</span>
                      <span>{session.price} credits</span>
                    </div>
                  </div>
                  {session.state === 'completed' && session.learnerRating === null && (
                    ratingSession === session.id ? (
                      <div className="flex flex-wrap items-center gap-1">
                        {[1, 2, 3, 4, 5].map(v => (
                          <button key={v} type="button" onClick={() => setRatingValue(v)}
                            aria-label={`${v} star${v === 1 ? '' : 's'}`} aria-pressed={ratingValue === v}
                            className="pressable flex h-11 w-11 items-center justify-center rounded-control hover:bg-background-light focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary">
                            <Star aria-hidden="true" className={`h-5 w-5 ${ratingValue >= v ? 'fill-warning text-warning-strong' : 'text-muted'}`} />
                          </button>
                        ))}
                        <Button size="sm" onClick={() => handleRate(session.id)}>Submit</Button>
                        <Button size="sm" variant="ghost" onClick={() => setRatingSession(null)}>Cancel</Button>
                      </div>
                    ) : (
                      <Button size="sm" variant="ghost" onClick={() => setRatingSession(session.id)} className="text-warning-strong">
                        <Star className="h-4 w-4" aria-hidden="true" /> Rate
                      </Button>
                    )
                  )}
                  {session.learnerRating !== null && (
                    <div role="img" aria-label={`${session.learnerRating}/5`} className="flex items-center gap-1 text-sm text-warning-strong">
                      {'★'.repeat(session.learnerRating)}{'☆'.repeat(5 - session.learnerRating)}
                    </div>
                  )}
                </Card>
              </MotionItem>
            ))}
          </div>
        )}
      </section>
    </>
  );
}
