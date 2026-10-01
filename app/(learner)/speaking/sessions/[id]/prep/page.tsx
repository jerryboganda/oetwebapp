'use client';

/**
 * 3-minute preparation for a Speaking session (practice, free sample and the
 * interlocutor-trainee route). 23 Sep 2026 owner flow: Rules + consent already
 * happened (roleplay page) before this timer started, so this screen is only
 * the exam-style role card plus the countdown. Notes go on blank paper — there
 * is no on-screen notes panel.
 *
 * At zero (or "Start speaking now") it calls `startRolePlay()` and opens the
 * active role-play; that tap is also the user gesture the live AI patient
 * needs to start audio.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { ArrowRight, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { PrepCountdown } from '@/components/domain/speaking/PrepCountdown';
import { SpeakingRoleCard, roleCardPropsFrom } from '@/components/domain/speaking-role-card';
import {
  getSpeakingSession,
  getSpeakingSessionClock,
  startRolePlay,
  type SpeakingSessionDetail,
} from '@/lib/api/speaking-sessions';
import { ApiError } from '@/lib/api';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

export default function SpeakingSessionPrepPage() {
  const params = useParams<{ id: string }>();
  const sessionId = params?.id ?? '';
  const router = useRouter();

  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [prepSeconds, setPrepSeconds] = useState(180);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [transitioning, setTransitioning] = useState(false);
  const [transitionError, setTransitionError] = useState<string | null>(null);
  const trackedPrepStartRef = useRef(false);

  // ── Load session + server prep clock (a refresh resumes, never restarts) ──
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    (async () => {
      try {
        const detail = await getSpeakingSession(sessionId);
        const clock = await getSpeakingSessionClock(sessionId).catch(() => null);
        if (cancelled) return;
        if (clock?.stage === 'active') {
          router.replace(`/speaking/sessions/${encodeURIComponent(sessionId)}`);
          return;
        }
        const fallback = Math.max(30, Math.floor(detail.card.prepTimeSeconds || 180));
        setPrepSeconds(clock?.stage === 'prep' && typeof clock.secondsRemaining === 'number'
          ? Math.max(0, clock.secondsRemaining)
          : fallback);
        setSession(detail);
      } catch (err) {
        if (cancelled) return;
        setLoadError(
          err instanceof ApiError ? err.userMessage : err instanceof Error ? err.message : 'Could not load session.',
        );
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [router, sessionId]);

  useEffect(() => {
    if (!session || trackedPrepStartRef.current) return;
    trackedPrepStartRef.current = true;
    trackSpeaking('prep_started', { sessionId: session.sessionId, cardId: session.card.cardId });
  }, [session]);

  const beginRolePlay = useCallback(async () => {
    if (!sessionId || transitioning) return;
    setTransitioning(true);
    setTransitionError(null);
    try {
      await startRolePlay(sessionId);
      router.push(`/speaking/sessions/${encodeURIComponent(sessionId)}`);
    } catch (err) {
      setTransitionError(
        err instanceof ApiError
          ? err.userMessage
          : err instanceof Error
            ? err.message
            : 'Could not start the role-play. Please try again.',
      );
      setTransitioning(false);
    }
  }, [router, sessionId, transitioning]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <span className="inline-flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Loading session…
        </span>
      </div>
    );
  }

  if (loadError || !session) {
    return (
      <div className="mx-auto max-w-xl rounded-2xl border border-danger/30 bg-danger/10 p-6 text-sm text-danger">
        <h2 className="text-base font-semibold">Could not load this session</h2>
        <p className="mt-1">{loadError ?? 'Session not available.'}</p>
        <Button type="button" variant="outline" className="mt-4" onClick={() => router.push('/speaking')}>
          Back to speaking
        </Button>
      </div>
    );
  }

  return (
    <div className="flex min-h-[100dvh] flex-col bg-background-light">
      <header className="border-b border-border bg-surface px-4 py-3">
        <p className="eyebrow text-muted">Speaking · Preparation</p>
        <h1 className="truncate text-base font-bold text-foreground sm:text-lg">{session.card.scenarioTitle}</h1>
      </header>

      <main className="mx-auto grid w-full max-w-5xl flex-1 gap-4 p-4 lg:grid-cols-[1fr_minmax(240px,320px)]">
        <SpeakingRoleCard {...roleCardPropsFrom(session.card)} />
        <aside className="flex flex-col gap-3">
          <PrepCountdown durationSeconds={prepSeconds} onComplete={() => void beginRolePlay()} size="md" />
          <p className="text-center text-xs text-muted">
            Make notes on your blank paper. The role-play starts automatically when preparation ends.
          </p>
        </aside>
      </main>

      <div className="sticky bottom-0 z-10 border-t border-border bg-surface px-4 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))]">
        <div className="mx-auto w-full max-w-5xl space-y-2">
          {transitionError ? (
            <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
              {transitionError}
            </p>
          ) : null}
          <Button type="button" fullWidth size="lg" onClick={() => void beginRolePlay()} disabled={transitioning}>
            {transitioning ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> : <ArrowRight className="mr-2 h-4 w-4" aria-hidden />}
            Start speaking now
          </Button>
        </div>
      </div>
    </div>
  );
}
