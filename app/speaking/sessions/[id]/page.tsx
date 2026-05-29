'use client';

/**
 * Recording room for a Speaking session (plan C.2).
 *
 * Mounted at `/speaking/sessions/[id]`. Branches by `mode`:
 *   • `ai_self_practice` / `ai_exam` → connects to `ConversationHub`
 *     and starts an AI role-play. Renders the card, a 5-minute timer,
 *     an "End early" button, a captions strip and a mic-level indicator.
 *   • `live_tutor` → immediately redirects to `./live-tutor` where the
 *     LiveKit room is provisioned.
 *
 * The consent banner is mounted up front for AI sessions — the
 * existing `SpeakingConsentBanner` writes a server-side consent row
 * before mic capture begins (Phase 7 contract).
 *
 * NOTE: this page does NOT replace the 50KB native-capable recorder
 * at `app/speaking/task/[id]/page.tsx`. It targets the new
 * SessionId-based flow only.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Activity, Loader2, Mic, PhoneOff } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { SpeakingConsentBanner } from '@/components/domain/speaking/SpeakingConsentBanner';
import {
  endSpeakingSession,
  getSpeakingSession,
  runAiAssessment,
  type SpeakingSessionDetail,
  type SpeakingSessionMode,
} from '@/lib/api/speaking-sessions';
import { ApiError } from '@/lib/api';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

const ROLE_PLAY_HARD_LIMIT_SECONDS = 5 * 60;

interface ConversationHubBridge {
  start: (sessionId: string) => Promise<void>;
  stop: () => Promise<void>;
  onCaption: (cb: (text: string, speaker: 'candidate' | 'interlocutor') => void) => void;
}

async function loadConversationHub(): Promise<ConversationHubBridge | null> {
  try {
    const { HubConnectionBuilder, LogLevel } = await import('@microsoft/signalr');
    const { ensureFreshAccessToken } = await import('@/lib/auth-client');
    const connection = new HubConnectionBuilder()
      .withUrl('/api/backend/v1/conversations/hub', {
        accessTokenFactory: async () => (await ensureFreshAccessToken()) ?? '',
      })
      .configureLogging(LogLevel.None)
      .withAutomaticReconnect([0, 2_000, 5_000])
      .build();

    let captionHandler: ((text: string, speaker: 'candidate' | 'interlocutor') => void) | null = null;
    connection.on('SpeakingCaption', (text: string, speaker: 'candidate' | 'interlocutor') => {
      captionHandler?.(text, speaker);
    });

    return {
      start: async (sessionId) => {
        await connection.start();
        await connection.invoke('StartSpeakingRoleplay', sessionId);
      },
      stop: async () => {
        try {
          await connection.stop();
        } catch {
          /* ignore */
        }
      },
      onCaption: (cb) => {
        captionHandler = cb;
      },
    };
  } catch {
    return null;
  }
}

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
  const [hubReady, setHubReady] = useState(false);
  const [hubError, setHubError] = useState<string | null>(null);
  const [captions, setCaptions] = useState<Array<{ id: string; text: string; speaker: string }>>([]);
  const [secondsLeft, setSecondsLeft] = useState<number>(ROLE_PLAY_HARD_LIMIT_SECONDS);
  const [micLevel, setMicLevel] = useState(0);
  const [ending, setEnding] = useState(false);
  const [endError, setEndError] = useState<string | null>(null);

  const hubRef = useRef<ConversationHubBridge | null>(null);
  const endedRef = useRef(false);
  const audioCleanupRef = useRef<(() => void) | null>(null);
  const roleplayStartedAtRef = useRef<number | null>(null);
  const trackedRoleplayStartRef = useRef(false);
  const trackedTimeWarningRef = useRef(false);

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

  // ── Connect hub once consent is accepted ─────────────────────────────────
  useEffect(() => {
    if (!session || !consentAccepted) return;
    if (!isAiMode(session.mode)) return;
    let cancelled = false;
    loadConversationHub().then(async (hub) => {
      if (cancelled || !hub) {
        if (!hub) {
          setHubError(
            'Could not reach the AI partner. Please refresh, or end the session and try again.',
          );
        }
        return;
      }
      hubRef.current = hub;
      hub.onCaption((text, speaker) => {
        setCaptions((prev) => {
          const next = [...prev, { id: `${Date.now()}-${prev.length}`, text, speaker }];
          // Keep only the last ~10 lines so the screen doesn't drift forever.
          return next.slice(-10);
        });
      });
      try {
        await hub.start(session.sessionId);
        if (!cancelled) {
          roleplayStartedAtRef.current ??= Date.now();
          if (!trackedRoleplayStartRef.current) {
            trackedRoleplayStartRef.current = true;
            trackSpeaking('roleplay_started', {
              sessionId: session.sessionId,
              cardId: session.card.cardId,
            });
          }
          setHubReady(true);
        }
      } catch (err) {
        if (cancelled) return;
        const msg = err instanceof Error ? err.message : 'Could not start the AI partner.';
        setHubError(msg);
      }
    });
    return () => {
      cancelled = true;
      hubRef.current?.stop().catch(() => undefined);
      hubRef.current = null;
    };
  }, [consentAccepted, session]);

  // ── Local 5-min hard timer ───────────────────────────────────────────────
  const handleFinalize = useCallback(async (reason: 'manual' | 'timer' = 'manual') => {
    if (!session || endedRef.current) return;
    endedRef.current = true;
    setEnding(true);
    setEndError(null);
    try {
      await hubRef.current?.stop().catch(() => undefined);
      await endSpeakingSession(session.sessionId);
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

  useEffect(() => {
    if (!session || !consentAccepted || !isAiMode(session.mode)) return;
    const start = Date.now();
    const baseRemaining = secondsLeft > 0 ? secondsLeft : ROLE_PLAY_HARD_LIMIT_SECONDS;
    const id = window.setInterval(() => {
      const elapsed = Math.floor((Date.now() - start) / 1000);
      const remaining = Math.max(0, baseRemaining - elapsed);
      setSecondsLeft(remaining);
      if (remaining <= 30 && !trackedTimeWarningRef.current) {
        trackedTimeWarningRef.current = true;
        trackSpeaking('roleplay_time_nearly_up', {
          sessionId: session.sessionId,
          secondsLeft: remaining,
        });
      }
      if (remaining <= 0) {
        window.clearInterval(id);
        if (!endedRef.current) void handleFinalize('timer');
      }
    }, 500);
    return () => {
      window.clearInterval(id);
    };
    // We intentionally don't depend on `secondsLeft` — that would reset
    // the countdown every tick.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [consentAccepted, session, handleFinalize]);

  // ── Mic level meter (purely visual) ──────────────────────────────────────
  useEffect(() => {
    if (!consentAccepted) return;
    let stream: MediaStream | null = null;
    let ctx: AudioContext | null = null;
    let rafId = 0;
    let cancelled = false;

    async function setup() {
      try {
        stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        if (cancelled) {
          stream.getTracks().forEach((t) => t.stop());
          return;
        }
        const AudioCtxCtor =
          (window.AudioContext as typeof AudioContext | undefined) ??
          ((window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext);
        ctx = new AudioCtxCtor();
        const source = ctx.createMediaStreamSource(stream);
        const analyser = ctx.createAnalyser();
        analyser.fftSize = 256;
        source.connect(analyser);
        const data = new Uint8Array(analyser.frequencyBinCount);

        function tick() {
          if (cancelled || !ctx) return;
          analyser.getByteTimeDomainData(data);
          let peak = 0;
          for (const sample of data) {
            const delta = Math.abs(sample - 128);
            if (delta > peak) peak = delta;
          }
          setMicLevel(Math.min(1, peak / 80));
          rafId = window.requestAnimationFrame(tick);
        }
        tick();

        audioCleanupRef.current = () => {
          cancelled = true;
          if (rafId) window.cancelAnimationFrame(rafId);
          ctx?.close().catch(() => undefined);
          stream?.getTracks().forEach((t) => t.stop());
        };
      } catch {
        // Mic permission denied or unavailable — we still let the
        // session run; the recorder will surface the real error.
      }
    }
    void setup();

    return () => {
      cancelled = true;
      audioCleanupRef.current?.();
      audioCleanupRef.current = null;
    };
  }, [consentAccepted]);

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

        {/* Live HUD */}
        <aside className="flex flex-col gap-4">
          <div className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
            <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-foreground">
              <Mic className="h-4 w-4 text-muted" aria-hidden /> Microphone
            </div>
            <MicLevelMeter level={micLevel} />
            <p className="mt-2 text-xs text-muted">
              {hubReady ? 'AI partner connected.' : 'Connecting AI partner…'}
            </p>
            {hubError ? (
              <p
                role="alert"
                className="mt-2 rounded-md border border-danger/30 bg-danger/10 p-2 text-xs text-danger"
              >
                {hubError}
              </p>
            ) : null}
          </div>

          <div className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
            <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-foreground">
              <Activity className="h-4 w-4 text-muted" aria-hidden /> Live captions
            </div>
            <div
              className="h-40 overflow-y-auto rounded-md bg-muted p-2 text-sm text-foreground"
              aria-live="polite"
            >
              {captions.length === 0 ? (
                <p className="italic text-muted">
                  Captions will appear here as you speak.
                </p>
              ) : (
                <ul className="space-y-1">
                  {captions.map((c) => (
                    <li key={c.id}>
                      <span
                        className={cn(
                          'mr-1 text-xs font-semibold uppercase tracking-wide',
                          c.speaker === 'candidate' ? 'text-success' : 'text-info',
                        )}
                      >
                        {c.speaker === 'candidate' ? 'You' : 'Patient'}:
                      </span>
                      {c.text}
                    </li>
                  ))}
                </ul>
              )}
            </div>
          </div>

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

function MicLevelMeter({ level }: { level: number }) {
  const clamped = Math.max(0, Math.min(1, level));
  return (
    <div className="h-2 w-full overflow-hidden rounded-full bg-background-light">
      <div
        className={cn(
          'h-full rounded-full transition-[width,background-color] duration-100',
          clamped > 0.85 ? 'bg-danger' : clamped > 0.4 ? 'bg-success' : 'bg-muted',
        )}
        style={{ width: `${Math.round(clamped * 100)}%` }}
        aria-hidden
      />
    </div>
  );
}
