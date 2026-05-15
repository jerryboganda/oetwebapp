'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { GraduationCap, Calendar, Star, Plus } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchTutoringSessions, rateTutoringSession } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type TutoringSession = {
  id: string; expertUserId: string; examTypeCode: string; subtestFocus: string | null;
  scheduledAt: string; durationMinutes: number; state: string; price: number;
  learnerRating: number | null;
};

const STATE_COLORS: Record<string, string> = {
  booked: 'bg-info/10 text-info',
  completed: 'bg-success/10 text-success',
  cancelled: 'bg-danger/10 text-danger',
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
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Tutoring Sessions"
          description="Book 1-on-1 sessions with OET expert tutors"
          icon={GraduationCap}
        />
        <button
          disabled
          title="Booking reopens after tutor discovery and canonical pricing are configured."
          className="flex items-center gap-2 px-4 py-2 bg-muted text-muted-foreground rounded-xl text-sm font-medium opacity-70"
        >
          <Plus className="w-4 h-4" /> Booking paused
        </button>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}
      <InlineAlert variant="info" title="Booking temporarily paused" className="mb-4">
        Tutor booking will reopen after tutor discovery, fixed launch pricing, and expert payout rules are configured. Existing booked sessions and ratings remain available.
      </InlineAlert>

      <LearnerSurfaceSectionHeader title="Your Sessions" />
      {loading ? (
        <div className="space-y-3">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-24 rounded-xl" />)}</div>
      ) : sessions.length === 0 ? (
        <div className="text-center py-12 text-muted/60">
          <GraduationCap className="w-10 h-10 mx-auto mb-3 opacity-30" />
          <p>No tutoring sessions yet.</p>
        </div>
      ) : (
        <div className="space-y-3">
          {sessions.map((session, i) => (
            <MotionItem key={session.id} delayIndex={i}
              className="bg-surface rounded-xl border border-border p-4 flex flex-col sm:flex-row sm:items-center gap-3"
            >
              <div className="flex-1">
                <div className="flex items-center gap-2 mb-1">
                  <span className="font-medium text-navy text-sm">{session.examTypeCode.toUpperCase()} {session.subtestFocus ? `— ${session.subtestFocus}` : ''}</span>
                  <span className={`text-xs px-2 py-0.5 rounded-full capitalize ${STATE_COLORS[session.state] ?? 'bg-background-light text-muted'}`}>{session.state}</span>
                </div>
                <div className="flex items-center gap-3 text-xs text-muted/60">
                  <span className="flex items-center gap-1"><Calendar className="w-3.5 h-3.5" />{formatDate(session.scheduledAt)}</span>
                  <span>{session.durationMinutes} min</span>
                  <span>{session.price} credits</span>
                </div>
              </div>
              {session.state === 'completed' && session.learnerRating === null && (
                ratingSession === session.id ? (
                  <div className="flex flex-wrap items-center gap-2">
                    {[1, 2, 3, 4, 5].map(v => (
                      <button key={v} onClick={() => setRatingValue(v)}
                        className={`w-10 h-10 rounded-full text-sm ${ratingValue >= v ? 'text-warning' : 'text-muted/40'}`}>
                        ★
                      </button>
                    ))}
                    <button onClick={() => handleRate(session.id)} className="text-xs px-3 py-2.5 bg-warning hover:bg-warning/90 text-white rounded-lg">Submit</button>
                    <button onClick={() => setRatingSession(null)} className="text-xs text-muted/60 py-2 px-1">Cancel</button>
                  </div>
                ) : (
                  <button onClick={() => setRatingSession(session.id)} className="flex items-center gap-1.5 text-sm text-warning hover:text-warning font-medium py-2 px-1">
                    <Star className="w-4 h-4" /> Rate
                  </button>
                )
              )}
              {session.learnerRating !== null && (
                <div className="flex items-center gap-1 text-warning text-sm">
                  {'★'.repeat(session.learnerRating)}{'☆'.repeat(5 - session.learnerRating)}
                </div>
              )}
            </MotionItem>
          ))}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
