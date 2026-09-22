'use client';

/**
 * Recording room for a Speaking session (plan C.2).
 *
 * Mounted at `/speaking/sessions/[id]`. Branches by `mode`:
 *   • `ai_self_practice` / `ai_exam` → runs the hands-free native realtime AI
 *     patient voice conversation through the selected live provider. Renders
 *     the card, a 5-minute timer, an "End early" button, captions, and a
 *     mic-level indicator.
 *   • `live_tutor` → immediately redirects to `./live-tutor` where the
 *     LiveKit room is provisioned.
 *
 * The consent banner is mounted up front for AI sessions — the existing
 * `SpeakingConsentBanner` writes a server-side consent row before mic capture
 * begins (Phase 7 contract). The conversation hook is only handed the session
 * id once consent is accepted, so no provider/mic work starts before then.
 *
 * NOTE: this page does NOT replace the 50KB native-capable recorder at
 * `app/speaking/task/[id]/page.tsx`. It targets the new SessionId-based flow.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Activity, Loader2, PhoneOff } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { SpeakingConsentBanner } from '@/components/domain/speaking/SpeakingConsentBanner';
import { ExamConversationPanel } from '@/components/domain/speaking/ExamConversationPanel';
import {
  endSpeakingSession,
  getSpeakingSession,
  getSpeakingSessionClock,
  runAiAssessment,
  submitSpeakingSessionForMarking,
  type SpeakingSessionDetail,
  type SpeakingSessionMode,
} from '@/lib/api/speaking-sessions';
import { ApiError } from '@/lib/api';
import { trackSpeaking } from '@/lib/analytics/speaking-events';
import type { LiveVoiceProvider } from '@/lib/api/speaking-live-voice';

const ROLE_PLAY_HARD_LIMIT_SECONDS = 5 * 60;
const CLOCK_SYNC_INTERVAL_MS = 10_000;

function isAiMode(mode: string | SpeakingSessionMode): boolean {
  return mode === 'ai_self_practice' || mode === 'ai_exam';
}

function formatMmSs(secondsLeft: number): string {
  const safe = Math.max(0, secondsLeft);
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

export default function SpeakingSessionRecordingPage() {
  const params = useParams<{ id: string }>();
  const sessionId = params?.id ?? '';
  const router = useRouter();

  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [consentAccepted, setConsentAccepted] = useState(false);
  const [requestedVoiceProvider, setRequestedVoiceProvider] = useState<LiveVoiceProvider | undefined>();
  const [secondsLeft, setSecondsLeft] = useState<number>(ROLE_PLAY_HARD_LIMIT_SECONDS);
  const [ending, setEnding] = useState(false);
  const [endError, setEndError] = useState<string | null>(null);

  const endedRef = useRef(false);
  const roleplayStartedAtRef = useRef<number | null>(null);
  const trackedTimeWarningRef = useRef(false);
  const voiceStopRef = useRef<(() => Promise<boolean>) | null>(null);
  const handleFinalizeRef = useRef<((reason?: 'manual' | 'timer') => Promise<void>) | null>(null);
  const handleVoiceStopReady = useCallback((stop: (() => Promise<boolean>) | null) => {
    voiceStopRef.current = stop;
  }, []);

  useEffect(() => {
    const value = new URLSearchParams(window.location.search).get('voiceProvider');
    if (value === 'openai' || value === 'gemini') setRequestedVoiceProvider(value);
  }, []);

  // ── Load session ─────────────────────────────────────────────────────────
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    setLoading(true);
    getSpeakingSession(sessionId)
      .then((s) => {
        if (cancelled) return;
        setSession(s);

        // Live-tutor mode redirects to its own page.
        if (s.mode === 'live_tutor') {
          router.replace(`/speaking/sessions/${sessionId}/live-tutor`);
          return;
        }

        // Seed countdown from the server-side rolePlayEndsAt if present.
        if (s.rolePlayEndsAt) {
          const remaining = Math.max(
            0,
            Math.floor((new Date(s.rolePlayEndsAt).getTime() - Date.now()) / 1000),
          );
          setSecondsLeft(Math.min(ROLE_PLAY_HARD_LIMIT_SECONDS, remaining || ROLE_PLAY_HARD_LIMIT_SECONDS));
        }
        if (s.rolePlayStartedAt) {
          roleplayStartedAtRef.current = new Date(s.rolePlayStartedAt).getTime();
        }

        if (isAiMode(s.mode)) {
          void getSpeakingSessionClock(s.sessionId)
            .then((clock) => {
              if (cancelled) return;
              if (clock.expired || clock.stage === 'finished' || clock.stage === 'cancelled') {
                setSecondsLeft(0);
                return;
              }
              if (typeof clock.secondsRemaining === 'number') {
                setSecondsLeft(clock.secondsRemaining);
              }
            })
            .catch(() => undefined);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        const msg =
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not load session.';
        setLoadError(msg);
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [router, sessionId]);

  useEffect(() => {
    if (!session || !isAiMode(session.mode)) return;

    let cancelled = false;
    const syncClock = async () => {
      try {
        const clock = await getSpeakingSessionClock(session.sessionId);
        if (cancelled) return;
        if (clock.expired || clock.stage === 'finished' || clock.stage === 'cancelled') {
          setSecondsLeft(0);
          return;
        }
        if (typeof clock.secondsRemaining === 'number') {
          setSecondsLeft(clock.secondsRemaining);
        }
      } catch {
        // Keep the last known countdown if the authoritative clock blips.
      }
    };

    void syncClock();
    const interval = window.setInterval(() => {
      void syncClock();
    }, CLOCK_SYNC_INTERVAL_MS);

    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [session]);

  const handleFinalize = useCallback(async (reason: 'manual' | 'timer' = 'manual') => {
    if (!session || endedRef.current) return;
    endedRef.current = true;
    setEnding(true);
    setEndError(null);
    try {
      const voiceSaved = await voiceStopRef.current?.() ?? true;
      if (!voiceSaved) {
        throw new Error('The live voice transcript could not be saved. Please retry before ending the session.');
      }
      await endSpeakingSession(session.sessionId);
      // WS4 (§14.2) — commit the finished role-play for marking. Best-effort:
      // the backend gate stamps `submittedAt` only when assessable evidence
      // (a recording or non-empty transcript) exists, so a session the
      // learner ended without speaking simply skips the stamp.
      try {
        await submitSpeakingSessionForMarking(session.sessionId);
      } catch {
        // Non-blocking: results page does not depend on the submit stamp.
      }
      if (isAiMode(session.mode)) {
        // Kick off scoring; non-blocking — server returns the in-flight job.
        try {
          await runAiAssessment(session.sessionId);
        } catch {
          // Scoring kickoff is best-effort; results page will retry.
        }
      }
      const startedAt = roleplayStartedAtRef.current;
      const durationSeconds = startedAt
        ? Math.max(0, Math.floor((Date.now() - startedAt) / 1000))
        : ROLE_PLAY_HARD_LIMIT_SECONDS;
      trackSpeaking('roleplay_ended', {
        sessionId: session.sessionId,
        durationSeconds,
        reason,
      });
      router.push(`/speaking/sessions/${session.sessionId}/results`);
    } catch (err) {
      const msg =
        err instanceof ApiError
          ? err.userMessage
          : err instanceof Error
            ? err.message
            : 'Could not end the session. Please try again.';
      setEndError(msg);
      endedRef.current = false;
      setEnding(false);
    }
  }, [router, session]);
  handleFinalizeRef.current = handleFinalize;

  useEffect(() => {
    if (!session || !consentAccepted || !isAiMode(session.mode)) return;
    if (secondsLeft <= 0) {
      if (!endedRef.current) void handleFinalize('timer');
      return;
    }

    const timer = window.setTimeout(() => {
      const nextSecondsLeft = Math.max(0, secondsLeft - 1);
      setSecondsLeft(nextSecondsLeft);
      if (nextSecondsLeft <= 30 && !trackedTimeWarningRef.current) {
        trackedTimeWarningRef.current = true;
        trackSpeaking('roleplay_time_nearly_up', {
          sessionId: session.sessionId,
          secondsLeft: nextSecondsLeft,
        });
      }
      if (nextSecondsLeft <= 0 && !endedRef.current) {
        void handleFinalize('timer');
      }
    }, 1000);

    return () => {
      window.clearTimeout(timer);
    };
  }, [consentAccepted, handleFinalize, secondsLeft, session]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <span className="inline-flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing your role-play…
        </span>
      </div>
    );
  }

  if (loadError || !session) {
    return (
      <div className="mx-auto max-w-xl rounded-2xl border border-danger/30 bg-danger/10 p-6 text-sm text-danger">
        <h2 className="text-base font-semibold">Could not load this session</h2>
        <p className="mt-1">{loadError ?? 'Session not available.'}</p>
        <Button
          type="button"
          variant="outline"
          className="mt-4"
          onClick={() => router.push('/speaking')}
        >
          Back to speaking
        </Button>
      </div>
    );
  }

  if (session.mode === 'live_tutor') {
    // Redirect is in flight — render a tiny placeholder so the page
    // never flashes the AI UI.
    return (
      <div className="flex min-h-[40vh] items-center justify-center text-sm text-muted">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Switching to live tutor room…
      </div>
    );
  }

  const { card } = session;
  const isWarning = secondsLeft > 0 && secondsLeft <= 30;
  return (
    <div className="mx-auto max-w-5xl space-y-6 p-4 sm:p-6">
      {!consentAccepted ? (
        <SpeakingConsentBanner
          sessionMode="ai"
          sessionId={session.sessionId}
          onAccepted={() => setConsentAccepted(true)}
          consentVersionOverride={session.consentVersion}
        />
      ) : null}

      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <p className="text-xs font-medium uppercase tracking-wider text-muted">
            Speaking · Role-play
          </p>
          <h1 className="text-2xl font-bold text-foreground">{card.scenarioTitle}</h1>
          <p className="text-sm text-muted">
            {card.setting} · {card.candidateRole}
          </p>
        </div>
        <div className="flex items-center gap-3">
          <Timer secondsLeft={secondsLeft} isWarning={isWarning} />
          <Button
            type="button"
            variant="destructive"
            size="md"
            onClick={() => void handleFinalize('manual')}
            disabled={ending}
            data-testid="speaking-session-end-early"
          >
            {ending ? (
              <>
                <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Ending…
              </>
            ) : (
              <>
                <PhoneOff className="mr-2 h-4 w-4" aria-hidden /> End early
              </>
            )}
          </Button>
        </div>
      </header>

      <div className="grid gap-6 lg:grid-cols-[1fr_minmax(280px,360px)]">
        {/* Card recap */}
        <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
          <h2 className="text-sm font-semibold uppercase tracking-wide text-muted">
            Candidate card
          </h2>
          <p className="mt-2 whitespace-pre-wrap text-sm leading-relaxed text-foreground">
            {card.background}
          </p>
          {card.tasks.length > 0 ? (
            <ol className="mt-3 list-decimal space-y-1 pl-5 text-sm text-foreground">
              {card.tasks.map((task, idx) => (
                <li key={idx}>{task}</li>
              ))}
            </ol>
          ) : null}
        </section>

        <aside className="flex flex-col gap-4">
          <ExamConversationPanel
            sessionId={session.sessionId}
            micAllowed={consentAccepted}
            requestedProvider={requestedVoiceProvider}
            onVoiceStopReady={handleVoiceStopReady}
          />
          {endError ? (
            <p
              role="alert"
              className="rounded-md border border-danger/30 bg-danger/10 p-2 text-xs text-danger"
            >
              {endError}
            </p>
          ) : null}
        </aside>
      </div>
    </div>
  );
}

function Timer({ secondsLeft, isWarning }: { secondsLeft: number; isWarning: boolean }) {
  return (
    <div
      role="timer"
      aria-live="polite"
      className={cn(
        'inline-flex items-center gap-2 rounded-full px-3 py-1.5 text-base font-mono tabular-nums',
        isWarning ? 'bg-danger/10 text-danger' : 'bg-muted text-foreground',
      )}
    >
      <Activity className="h-4 w-4" aria-hidden />
      {formatMmSs(secondsLeft)}
    </div>
  );
}
