'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { GraduationCap, Calendar, Star } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { fetchTutoringSessions, rateTutoringSession } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type TutoringSession = {
  id: string; expertUserId: string; examTypeCode: string; subtestFocus: string | null;
  scheduledAt: string; durationMinutes: number; state: string; price: number;
  learnerRating: number | null;
};

const STATE_COLORS: Record<string, string> = {
  booked: 'bg-info/10 text-info',
  completed: 'bg-success/10 text-success-strong',
  cancelled: 'bg-danger/10 text-danger-strong',
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

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}
      <InlineAlert variant="info" title="Booking temporarily paused" className="mb-4">
        Tutor booking will reopen after tutor discovery, fixed launch pricing, and expert payout rules are configured. Existing booked sessions and ratings remain available.
      </InlineAlert>

      <LearnerSurfaceSectionHeader title="Your Sessions" />
      {loading ? (
        <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
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
            <MotionItem key={session.id} delayIndex={i}
              className="bg-surface rounded-xl border border-border p-4 flex flex-col sm:flex-row sm:items-center gap-3"
            >
              <div className="flex-1">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium text-navy text-sm">{session.examTypeCode.toUpperCase()} {session.subtestFocus ? `· ${session.subtestFocus}` : ''}</span>
                  <span className={`text-xs px-2 py-0.5 rounded-full capitalize ${STATE_COLORS[session.state] ?? 'bg-background-light text-muted'}`}>{session.state}</span>
                </div>
                <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted">
                  <span className="flex items-center gap-1"><Calendar className="w-3.5 h-3.5" aria-hidden="true" />{formatDate(session.scheduledAt)}</span>
                  <span>{session.durationMinutes} min</span>
                  <span>{session.price} credits</span>
                </div>
              </div>
              {session.state === 'completed' && session.learnerRating === null && (
                ratingSession === session.id ? (
                  <div className="flex flex-wrap items-center gap-2">
                    {[1, 2, 3, 4, 5].map(v => (
                      <button key={v} type="button" onClick={() => setRatingValue(v)}
                        aria-label={`${v} star${v === 1 ? '' : 's'}`} aria-pressed={ratingValue === v}
                        className={`w-11 h-11 rounded-full text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${ratingValue >= v ? 'text-warning-strong' : 'text-muted/40'}`}>
                        ★
                      </button>
                    ))}
                    <Button size="sm" onClick={() => handleRate(session.id)}>Submit</Button>
                    <Button size="sm" variant="ghost" onClick={() => setRatingSession(null)}>Cancel</Button>
                  </div>
                ) : (
                  <Button size="sm" variant="ghost" onClick={() => setRatingSession(session.id)} className="text-warning-strong">
                    <Star className="w-4 h-4" aria-hidden="true" /> Rate
                  </Button>
                )
              )}
              {session.learnerRating !== null && (
                <div className="flex items-center gap-1 text-warning-strong text-sm">
                  {'★'.repeat(session.learnerRating)}{'☆'.repeat(5 - session.learnerRating)}
                </div>
              )}
            </MotionItem>
          ))}
        </div>
      )}
    </>
  );
}
