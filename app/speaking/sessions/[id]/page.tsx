'use client';

/**
 * Active 5-minute role-play for a Speaking session (practice, free sample,
 * trainee). 23 Sep 2026 owner flow: the Rules + consent step and the 3-minute
 * prep already happened, so this screen is minimal —
 *
 *   • the exam-style role card, always visible and independently scrollable;
 *   • ONE microphone / voice-activity indicator (ExamConversationPanel):
 *     live AI patient when `liveVoiceAvailable`, otherwise the recorder
 *     fallback that uploads the recording on finish;
 *   • the countdown, which starts when speaking actually begins;
 *   • ONE "Finish & submit" in a sticky bottom bar (safe-area aware; this
 *     route renders no learner bottom nav, so nothing overlaps it).
 *
 * Finish sequence (idempotent server-side): fallback upload (must succeed,
 * never discarded) → /end → /submit → /ai-assess → results.
 *
 * `live_tutor` sessions redirect to `./live-tutor`.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Activity, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { cn } from '@/lib/utils';
import { SpeakingRoleCard, roleCardPropsFrom } from '@/components/domain/speaking-role-card';
import { ExamConversationPanel } from '@/components/domain/speaking/ExamConversationPanel';
import { SpeakingRulesConsent } from '@/components/domain/speaking/SpeakingRulesConsent';
import { RECORDING_UPLOAD_FAILED } from '@/hooks/useSpeakingSessionRecorder';
import {
  endSpeakingSession,
  getSpeakingSession,
  getSpeakingSessionClock,
  recordConsent,
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

  const [requestedVoiceProvider, setRequestedVoiceProvider] = useState<LiveVoiceProvider | undefined>();
  const [consentAccepted, setConsentAccepted] = useState(true);
  const [speakingStarted, setSpeakingStarted] = useState(false);
  const [secondsLeft, setSecondsLeft] = useState<number>(ROLE_PLAY_HARD_LIMIT_SECONDS);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [ending, setEnding] = useState(false);
  const [endError, setEndError] = useState<string | null>(null);

  const endedRef = useRef(false);
  const speakingStartedAtRef = useRef<number | null>(null);
  const trackedTimeWarningRef = useRef(false);
  const voiceStopRef = useRef<(() => Promise<boolean>) | null>(null);
  const handleVoiceStopReady = useCallback((stop: (() => Promise<boolean>) | null) => {
    voiceStopRef.current = stop;
  }, []);
  const handleSpeakingStarted = useCallback(() => {
    speakingStartedAtRef.current = Date.now();
    setSpeakingStarted(true);
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
        // Sessions created before the consent-first flow (or by the trainee
        // route) still get the one Rules + consent step, never a timed modal.
        setConsentAccepted(s.consentAccepted !== false);
        if (s.mode === 'live_tutor') {
          router.replace(`/speaking/sessions/${sessionId}/live-tutor`);
        }
      })
      .catch((err: unknown) => {
        if (cancelled) return;
        setLoadError(
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not load session.',
        );
      })
      .finally(() => {
        if (!cancelled) setLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [router, sessionId]);

  // Server-authoritative clock: re-sync while speaking.
  useEffect(() => {
    if (!session || !isAiMode(session.mode) || !speakingStarted) return;

    let cancelled = false;
    const syncClock = async () => {
      try {
        const clock = await getSpeakingSessionClock(session.sessionId);
        if (cancelled) return;
        if (clock.expired || clock.stage === 'finished' || clock.stage === 'cancelled') {
          setSecondsLeft(0);
          return;
        }
        if (clock.stage === 'active' && typeof clock.secondsRemaining === 'number') {
          setSecondsLeft(clock.secondsRemaining);
        }
      } catch {
        // Keep the last known countdown if the authoritative clock blips.
      }
    };

    void syncClock();
    const interval = window.setInterval(() => void syncClock(), CLOCK_SYNC_INTERVAL_MS);
    return () => {
      cancelled = true;
      window.clearInterval(interval);
    };
  }, [session, speakingStarted]);

  const handleFinalize = useCallback(async (reason: 'manual' | 'timer' = 'manual') => {
    if (!session || endedRef.current) return;
    endedRef.current = true;
    setConfirmOpen(false);
    setEnding(true);
    setEndError(null);
    try {
      // Recorder fallback: the upload MUST land before /end. On failure the
      // blob stays in memory and pressing the button again retries it.
      const saved = await voiceStopRef.current?.() ?? true;
      if (!saved) {
        throw new Error(session.liveVoiceAvailable
          ? 'The live voice transcript could not be saved. Please try again.'
          : RECORDING_UPLOAD_FAILED);
      }
      await endSpeakingSession(session.sessionId);
      // Both are idempotent server-side; the results page shows processing
      // and offers "Try grading again", so a blip here never strands the learner.
      await submitSpeakingSessionForMarking(session.sessionId).catch(() => undefined);
      if (isAiMode(session.mode)) await runAiAssessment(session.sessionId).catch(() => undefined);
      const startedAt = speakingStartedAtRef.current;
      trackSpeaking('roleplay_ended', {
        sessionId: session.sessionId,
        durationSeconds: startedAt ? Math.max(0, Math.floor((Date.now() - startedAt) / 1000)) : ROLE_PLAY_HARD_LIMIT_SECONDS,
        reason,
      });
      router.push(`/speaking/sessions/${session.sessionId}/results`);
    } catch (err) {
      setEndError(
        err instanceof ApiError
          ? err.userMessage
          : err instanceof Error
            ? err.message
            : 'Could not submit the role-play. Please try again.',
      );
      endedRef.current = false;
      setEnding(false);
    }
  }, [router, session]);

  // Local countdown — only once speaking has actually begun.
  useEffect(() => {
    if (!session || !speakingStarted || !isAiMode(session.mode) || endedRef.current || endError) return;
    if (secondsLeft <= 0) {
      void handleFinalize('timer');
      return;
    }
    const timer = window.setTimeout(() => {
      const next = Math.max(0, secondsLeft - 1);
      setSecondsLeft(next);
      if (next <= 30 && !trackedTimeWarningRef.current) {
        trackedTimeWarningRef.current = true;
        trackSpeaking('roleplay_time_nearly_up', { sessionId: session.sessionId, secondsLeft: next });
      }
    }, 1000);
    return () => window.clearTimeout(timer);
  }, [endError, handleFinalize, secondsLeft, session, speakingStarted]);

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
        <Button type="button" variant="outline" className="mt-4" onClick={() => router.push('/speaking')}>
          Back to speaking
        </Button>
      </div>
    );
  }

  if (session.mode === 'live_tutor') {
    return (
      <div className="flex min-h-[40vh] items-center justify-center text-sm text-muted">
        <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Switching to live tutor room…
      </div>
    );
  }

  if (!consentAccepted) {
    return (
      <div className="mx-auto w-full max-w-2xl p-4 sm:p-6">
        <SpeakingRulesConsent
          freeSample={session.isFreeSample}
          startLabel="Continue"
          onStart={async () => {
            await recordConsent(session.sessionId, session.consentVersion || 'recording.v1');
            setConsentAccepted(true);
          }}
        />
      </div>
    );
  }

  const isWarning = speakingStarted && secondsLeft > 0 && secondsLeft <= 30;
  const retryUpload = endError === RECORDING_UPLOAD_FAILED;

  return (
    <div className="flex min-h-[100dvh] flex-col bg-background-light">
      <header className="sticky top-0 z-10 flex items-center justify-between gap-3 border-b border-border bg-surface px-4 py-3">
        <div className="min-w-0">
          <p className="text-xs font-medium uppercase tracking-wider text-muted">Speaking · Role-play</p>
          <h1 className="truncate text-base font-bold text-foreground sm:text-lg">{session.card.scenarioTitle}</h1>
        </div>
        <div
          role="timer"
          aria-live={isWarning ? 'polite' : 'off'}
          className={cn(
            'inline-flex shrink-0 items-center gap-2 rounded-full px-3 py-1.5 font-mono text-base tabular-nums',
            isWarning ? 'bg-danger/10 text-danger' : 'bg-muted text-foreground',
          )}
        >
          <Activity className="h-4 w-4" aria-hidden />
          {formatMmSs(secondsLeft)}
        </div>
      </header>

      <main className="mx-auto flex w-full max-w-3xl flex-1 flex-col gap-4 p-4">
        <SpeakingRoleCard
          {...roleCardPropsFrom(session.card)}
          className="max-h-[50dvh] overflow-y-auto overscroll-contain"
        />
        <ExamConversationPanel
          sessionId={session.sessionId}
          liveVoiceAvailable={session.liveVoiceAvailable}
          requestedProvider={requestedVoiceProvider}
          onVoiceStopReady={handleVoiceStopReady}
          onSpeakingStarted={handleSpeakingStarted}
        />
        {isWarning ? (
          <p className="text-center text-sm font-medium text-danger" role="status">
            30 seconds left — wrap up. Your role-play submits automatically at 00:00.
          </p>
        ) : null}
      </main>

      <div
        className="sticky bottom-0 z-10 border-t border-border bg-surface px-4 pt-3 pb-[calc(0.75rem+env(safe-area-inset-bottom))]"
        data-testid="speaking-action-bar"
      >
        <div className="mx-auto w-full max-w-3xl space-y-2">
          {endError ? (
            <p role="alert" className="rounded-md border border-danger/30 bg-danger/10 px-3 py-2 text-sm text-danger">
              {endError}
            </p>
          ) : null}
          <Button
            type="button"
            fullWidth
            size="lg"
            disabled={ending}
            onClick={() => (retryUpload ? void handleFinalize('manual') : setConfirmOpen(true))}
            data-testid="speaking-finish-submit"
          >
            {ending ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> : null}
            {ending ? 'Submitting…' : retryUpload ? 'Retry upload' : 'Finish & submit'}
          </Button>
        </div>
      </div>

      <Modal open={confirmOpen} onClose={() => setConfirmOpen(false)} title="Finish and submit?" size="sm">
        <p className="text-sm text-foreground">
          Your role-play ends now and is sent for AI grading. You can&apos;t continue speaking afterwards.
        </p>
        <div className="mt-4 flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="ghost" onClick={() => setConfirmOpen(false)}>
            Keep speaking
          </Button>
          <Button type="button" onClick={() => void handleFinalize('manual')} disabled={ending}>
            Submit now
          </Button>
        </div>
      </Modal>
    </div>
  );
}
