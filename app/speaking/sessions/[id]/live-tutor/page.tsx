'use client';

/**
 * Learner-side live-tutor room (plan C.3).
 *
 * 1. Mints a LiveRoom for the session (or fetches the existing one).
 * 2. Mints a short-lived LiveKit token with `role=learner`.
 * 3. Renders `LearnerLiveRoomShell` alongside the candidate card.
 * 4. Ending the session calls `endSpeakingSession()` and navigates to results.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { SpeakingConsentBanner } from '@/components/domain/speaking/SpeakingConsentBanner';
import { LearnerLiveRoomShell } from '@/components/domain/speaking/LearnerLiveRoomShell';
import {
  endSpeakingSession,
  getSpeakingSession,
  type SpeakingSessionDetail,
} from '@/lib/api/speaking-sessions';
import {
  createLiveRoom,
  issueLiveRoomToken,
  type LiveRoomDetail,
  type LiveRoomTokenResponse,
} from '@/lib/api/speaking-live-rooms';
import { ApiError } from '@/lib/api';

export default function SpeakingSessionLiveTutorPage() {
  const params = useParams<{ id: string }>();
  const sessionId = params?.id ?? '';
  const router = useRouter();

  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [room, setRoom] = useState<LiveRoomDetail | null>(null);
  const [tokenInfo, setTokenInfo] = useState<LiveRoomTokenResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [consentAccepted, setConsentAccepted] = useState(false);
  const [ending, setEnding] = useState(false);
  const endedRef = useRef(false);

  // â”€â”€ Load session + provision room + token â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€â”€
  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    setLoading(true);
    setError(null);

    (async () => {
      try {
        const s = await getSpeakingSession(sessionId);
        if (cancelled) return;
        setSession(s);

        if (s.mode !== 'live_tutor') {
          // Defensive â€” should only land here in live-tutor mode.
          router.replace(`/speaking/sessions/${sessionId}`);
          return;
        }

        const r = await createLiveRoom({ speakingSessionId: sessionId });
        if (cancelled) return;
        setRoom(r);

        const token = await issueLiveRoomToken(r.liveRoomId, 'learner');
        if (cancelled) return;
        setTokenInfo(token);
      } catch (err) {
        if (cancelled) return;
        const msg =
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not connect to the live tutor room.';
        setError(msg);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [router, sessionId]);

  const handleEnd = useCallback(async () => {
    if (!session || endedRef.current) return;
    endedRef.current = true;
    setEnding(true);
    try {
      await endSpeakingSession(session.sessionId);
    } catch (err) {
      // We don't block navigation on the end-session failure â€” tutor
      // side will also finalize. Log for diagnostics.

      console.warn('[live-tutor] endSpeakingSession failed:', err);
    } finally {
      router.push(`/speaking/sessions/${session.sessionId}/results`);
    }
  }, [router, session]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <span className="inline-flex items-center gap-2 text-sm text-slate-500">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Connecting to the tutor roomâ€¦
        </span>
      </div>
    );
  }

  if (error || !session) {
    return (
      <div className="mx-auto max-w-xl rounded-2xl border border-rose-200 bg-rose-50 p-6 text-sm text-rose-900">
        <h2 className="text-base font-semibold">Could not start the live tutor session</h2>
        <p className="mt-1">{error ?? 'Session not available.'}</p>
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

  const { card } = session;

  return (
    <div className="mx-auto max-w-6xl space-y-4 p-4 sm:p-6">
      {!consentAccepted ? (
        <SpeakingConsentBanner
          sessionMode="live_tutor"
          onAccepted={() => setConsentAccepted(true)}
          consentVersionOverride={session.consentVersion}
        />
      ) : null}

      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <p className="text-xs font-medium uppercase tracking-wider text-slate-500">
            Speaking Â· Live tutor
          </p>
          <h1 className="text-2xl font-bold text-slate-900">{card.scenarioTitle}</h1>
          <p className="text-sm text-slate-600">
            {card.setting} Â· {card.candidateRole}
          </p>
        </div>
        {ending ? (
          <span className="inline-flex items-center gap-2 text-sm text-slate-500">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Wrapping upâ€¦
          </span>
        ) : null}
      </header>

      <div className="grid gap-4 lg:grid-cols-[1fr_minmax(260px,340px)]">
        {/* Video shell */}
        <div className="min-h-[480px]">
          {consentAccepted && room && tokenInfo ? (
            <LearnerLiveRoomShell
              livekitWssUrl={room.livekitWssUrl}
              token={tokenInfo.token}
              onEnd={() => void handleEnd()}
            />
          ) : (
            <div className="flex h-full min-h-[480px] items-center justify-center rounded-2xl border border-slate-200 bg-slate-50">
              <span className="inline-flex items-center gap-2 text-sm text-slate-500">
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
                {consentAccepted ? 'Setting up the roomâ€¦' : 'Waiting for consentâ€¦'}
              </span>
            </div>
          )}
        </div>

        {/* Candidate card recap */}
        <aside className="rounded-2xl border border-slate-200 bg-white p-5 text-sm text-slate-800 shadow-sm">
          <h2 className="text-xs font-semibold uppercase tracking-wide text-slate-500">
            Candidate card
          </h2>
          <p className="mt-2 whitespace-pre-wrap leading-relaxed text-slate-800">
            {card.background}
          </p>
          {card.tasks.length > 0 ? (
            <ol className="mt-3 list-decimal space-y-1 pl-5">
              {card.tasks.map((task, idx) => (
                <li key={idx}>{task}</li>
              ))}
            </ol>
          ) : null}
          <p className="mt-3 text-xs text-slate-500">
            Goal: <span className="text-slate-800">{card.communicationGoal}</span>
          </p>
        </aside>
      </div>
    </div>
  );
}
