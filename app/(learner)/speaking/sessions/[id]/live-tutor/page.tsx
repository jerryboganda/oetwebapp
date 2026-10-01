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
import { SpeakingRoleCard, roleCardPropsFrom } from '@/components/domain/speaking-role-card';
import {
  endSpeakingSession,
  getSpeakingSession,
  submitSpeakingSessionForMarking,
  type SpeakingSessionDetail,
} from '@/lib/api/speaking-sessions';
import {
  createLiveRoom,
  endLiveRoom,
  issueLiveRoomToken,
  startRecording,
  type CreateLiveRoomResponse,
  type LiveRoomTokenResponse,
} from '@/lib/api/speaking-live-rooms';
import { ApiError } from '@/lib/api';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

export default function SpeakingSessionLiveTutorPage() {
  const params = useParams<{ id: string }>();
  const sessionId = params?.id ?? '';
  const router = useRouter();

  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [room, setRoom] = useState<CreateLiveRoomResponse | null>(null);
  const [tokenInfo, setTokenInfo] = useState<LiveRoomTokenResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [consentAccepted, setConsentAccepted] = useState(false);
  const [recordingReady, setRecordingReady] = useState(false);
  const [ending, setEnding] = useState(false);
  const endedRef = useRef(false);
  const joinedAtRef = useRef<number | null>(null);
  const trackedJoinRef = useRef(false);

  // -- Load session + provision room + token --------------------------------
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
          // Defensive - should only land here in live-tutor mode.
          router.replace(`/speaking/sessions/${sessionId}`);
          return;
        }

        const r = await createLiveRoom({ speakingSessionId: sessionId });
        if (cancelled) return;
        setRoom(r);
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

  useEffect(() => {
    if (!consentAccepted || !room || tokenInfo) return;
    let cancelled = false;

    (async () => {
      try {
        const token = await issueLiveRoomToken(room.liveRoomId, 'learner');
        if (cancelled) return;
        setTokenInfo(token);
      } catch (err) {
        if (cancelled) return;
        setError(
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not join the live tutor room.',
        );
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [consentAccepted, room, tokenInfo]);

  useEffect(() => {
    if (!consentAccepted || !room || recordingReady) return;
    let cancelled = false;
    (async () => {
      try {
        await startRecording(room.liveRoomId);
        if (!cancelled) setRecordingReady(true);
      } catch (err) {
        if (cancelled) return;
        setError(
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not start the LiveKit recording.',
        );
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [consentAccepted, recordingReady, room]);

  useEffect(() => {
    if (!session || !room || !tokenInfo || !consentAccepted || trackedJoinRef.current) return;
    trackedJoinRef.current = true;
    joinedAtRef.current = Date.now();
    trackSpeaking('live_room_joined', {
      liveRoomId: room.liveRoomId,
      role: 'learner',
    });
  }, [consentAccepted, room, session, tokenInfo]);

  const handleEnd = useCallback(async () => {
    if (!session || endedRef.current) return;
    endedRef.current = true;
    setEnding(true);
    try {
      if (room) {
        await endLiveRoom(room.liveRoomId).catch((roomError) => {
          console.warn('[live-tutor] endLiveRoom failed:', roomError);
        });
      }
      await endSpeakingSession(session.sessionId);
      // WS4 (§14.2) — commit the recorded role-play for marking. Best-effort;
      // the backend gate stamps `submittedAt` only when a recording exists.
      try {
        await submitSpeakingSessionForMarking(session.sessionId);
      } catch {
        // Non-blocking: tutor side also finalizes.
      }
      trackSpeaking('live_room_ended', {
        liveRoomId: room?.liveRoomId ?? session.sessionId,
        durationSeconds: joinedAtRef.current
          ? Math.max(0, Math.floor((Date.now() - joinedAtRef.current) / 1000))
          : 0,
        reason: 'learner_ended',
      });
    } catch (err) {
      // We don't block navigation on the end-session failure - tutor
      // side will also finalize. Log for diagnostics.

      console.warn('[live-tutor] endSpeakingSession failed:', err);
    } finally {
      router.push(`/speaking/sessions/${session.sessionId}/results`);
    }
  }, [room, router, session]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <span className="inline-flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Connecting to the tutor room...
        </span>
      </div>
    );
  }

  if (error || !session) {
    return (
      <div className="mx-auto max-w-xl rounded-2xl border border-danger/30 bg-danger/10 p-6 text-sm text-danger">
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
          sessionId={session.sessionId}
          onAccepted={() => setConsentAccepted(true)}
        />
      ) : null}

      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <p className="eyebrow text-muted">
            Speaking - Live tutor
          </p>
          <h1 className="text-2xl font-bold text-foreground">{card.scenarioTitle}</h1>
          <p className="text-sm text-muted">
            {card.setting} - {card.candidateRole}
          </p>
        </div>
        {ending ? (
          <span className="inline-flex items-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Wrapping up...
          </span>
        ) : null}
      </header>

      <div className="grid gap-4 lg:grid-cols-[1fr_minmax(260px,340px)]">
        {/* Video shell */}
        <div className="min-h-[480px]">
          {consentAccepted && room && tokenInfo && recordingReady ? (
            <LearnerLiveRoomShell
              liveRoomId={room.liveRoomId}
              livekitWssUrl={room.livekitWssUrl}
              token={tokenInfo.token}
              onEnd={() => void handleEnd()}
            />
          ) : (
            <div className="flex h-full min-h-[480px] items-center justify-center rounded-2xl border border-border bg-background-light">
              <span className="inline-flex items-center gap-2 text-sm text-muted">
                <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
                {consentAccepted ? 'Starting the LiveKit recording...' : 'Waiting for consent...'}
              </span>
            </div>
          )}
        </div>

        {/* Candidate card — the one exam-style card, always visible. */}
        <SpeakingRoleCard {...roleCardPropsFrom(card)} className="lg:max-h-[80dvh] lg:overflow-y-auto" />
      </div>
    </div>
  );
}
