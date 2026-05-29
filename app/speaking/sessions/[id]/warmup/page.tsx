'use client';

/**
 * OET Speaking — Phase 3 P3.4 — warm-up timer page.
 *
 * Renders a 90-second unscored warm-up phase between intake and prep.
 * The actual conversational state is owned by the backend session
 * machine; this page is purely a timer + transition shell.
 */
import { useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { ApiError } from '@/lib/api';
import {
  finishSpeakingWarmup,
  getSpeakingSession,
  startSpeakingWarmup,
  type SpeakingSessionDetail,
} from '@/lib/api/speaking-sessions';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

const WARMUP_SECONDS = 90;

export default function SpeakingWarmupPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const sessionId = useMemo(() => {
    const raw = params?.id;
    return Array.isArray(raw) ? raw[0] : raw;
  }, [params]);

  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [secondsLeft, setSecondsLeft] = useState(WARMUP_SECONDS);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const trackedWarmupStartRef = useRef(false);

  // Bootstrap: fetch session, transition to WarmUp if needed.
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;

    (async () => {
      try {
        const detail = await getSpeakingSession(sessionId);
        if (cancelled) return;
        if (detail.state !== 'WarmUp') {
          // Idempotent — backend rejects illegal transitions.
          try {
            const started = await startSpeakingWarmup(sessionId);
            if (cancelled) return;
            setSession(started);
          } catch (err) {
            if (!cancelled) setSession(detail);
            // Don't surface as an error if we are already past warm-up.
            if (err instanceof ApiError && err.status === 409) {
              router.replace(`/speaking/sessions/${encodeURIComponent(sessionId)}/prep`);
              return;
            }
            throw err;
          }
        } else {
          setSession(detail);
        }
      } catch (err) {
        if (cancelled) return;
        setError(err instanceof Error ? err.message : 'Could not start warm-up.');
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [sessionId, router]);

  // Countdown.
  useEffect(() => {
    if (!session) return;
    if (!trackedWarmupStartRef.current) {
      trackedWarmupStartRef.current = true;
      trackSpeaking('warmup_started', { sessionId: session.sessionId });
    }
    if (secondsLeft <= 0) return;
    const t = window.setInterval(() => {
      setSecondsLeft((s) => Math.max(0, s - 1));
    }, 1000);
    return () => window.clearInterval(t);
  }, [session, secondsLeft]);

  async function finish() {
    if (!sessionId || busy) return;
    setBusy(true);
    setError(null);
    try {
      await finishSpeakingWarmup(sessionId);
      trackSpeaking('warmup_finished', {
        sessionId,
        durationSeconds: WARMUP_SECONDS - secondsLeft,
      });
      router.push(`/speaking/sessions/${encodeURIComponent(sessionId)}/prep`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not finish warm-up.');
      setBusy(false);
    }
  }

  if (!sessionId) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="error">Missing session id.</InlineAlert>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell>
      <div className="mx-auto max-w-2xl space-y-6 py-8">
        <header className="space-y-2">
          <p className="text-sm font-medium uppercase tracking-wide text-primary">
            Step 1 of 4 · Warm-up
          </p>
          <h1 className="text-2xl font-semibold text-foreground">
            Quick warm-up before your role-play
          </h1>
          <p className="text-muted">
            We&apos;ll have a relaxed 90-second chat to settle your voice and microphone.
            Nothing here is scored.
          </p>
        </header>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {!session ? (
          <Skeleton className="h-48 w-full rounded-xl" />
        ) : (
          <Card className="space-y-6 p-6">
            <div className="flex items-center justify-between">
              <div className="text-sm text-muted">Time remaining</div>
              <div className="text-3xl font-mono font-semibold tabular-nums text-foreground">
                {String(Math.floor(secondsLeft / 60)).padStart(2, '0')}:
                {String(secondsLeft % 60).padStart(2, '0')}
              </div>
            </div>

            <ul className="space-y-2 text-sm text-foreground">
              <li>• Tell me how your day has been so far.</li>
              <li>• What made you choose your healthcare profession?</li>
              <li>• How are you feeling about today&apos;s practice?</li>
            </ul>

            <div className="flex flex-col gap-3 sm:flex-row sm:justify-end">
              <Button variant="outline" onClick={finish} disabled={busy}>
                Skip warm-up
              </Button>
              <Button onClick={finish} disabled={busy || secondsLeft > 0}>
                {busy ? 'Continuing…' : 'Continue to prep'}
              </Button>
            </div>
          </Card>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
