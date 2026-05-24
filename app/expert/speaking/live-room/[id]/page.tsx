'use client';

/**
 * Tutor-side live room (plan C.3).
 *
 * The route param `[id]` is the LiveRoomId (NOT the speaking session
 * id). The tutor lands here from the booking screen / scheduler after
 * the learner has provisioned the room.
 *
 * Steps:
 *   1. Fetch the live room → derives `speakingSessionId` and the LiveKit URL.
 *   2. Fetch the session → derives `cardId` for the cue panel.
 *   3. Mint a tutor JWT.
 *   4. Render `LiveTutorRoomShell` with `<TutorCuePanel>` as sidebar child.
 *
 * The tutor never sees the candidate's consent banner — consent
 * applies to the learner only; the tutor's own attestation lives in
 * the booking flow.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Loader2, NotebookPen, PhoneOff } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { LiveTutorRoomShell } from '@/components/domain/speaking/LiveTutorRoomShell';
import { TutorCuePanel } from '@/components/domain/speaking/TutorCuePanel';
import {
  endLiveRoom,
  getLiveRoom,
  issueLiveRoomToken,
  type LiveRoomDetail,
  type LiveRoomTokenResponse,
} from '@/lib/api/speaking-live-rooms';
import {
  endSpeakingSession,
  getSpeakingSession,
  type SpeakingSessionDetail,
} from '@/lib/api/speaking-sessions';
import { ApiError } from '@/lib/api';

export default function ExpertSpeakingLiveRoomPage() {
  const params = useParams<{ id: string }>();
  const liveRoomId = params?.id ?? '';
  const router = useRouter();

  const [room, setRoom] = useState<LiveRoomDetail | null>(null);
  const [session, setSession] = useState<SpeakingSessionDetail | null>(null);
  const [tokenInfo, setTokenInfo] = useState<LiveRoomTokenResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [privateNotes, setPrivateNotes] = useState('');
  const [ending, setEnding] = useState(false);
  const endedRef = useRef(false);

  useEffect(() => {
    if (!liveRoomId) return;
    let cancelled = false;
    setLoading(true);
    setError(null);

    (async () => {
      try {
        const r = await getLiveRoom(liveRoomId);
        if (cancelled) return;
        setRoom(r);

        const [s, token] = await Promise.all([
          getSpeakingSession(r.speakingSessionId),
          issueLiveRoomToken(r.liveRoomId, 'tutor'),
        ]);
        if (cancelled) return;
        setSession(s);
        setTokenInfo(token);
      } catch (err) {
        if (cancelled) return;
        const msg =
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not load the live room.';
        setError(msg);
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [liveRoomId]);

  const handleEndSession = useCallback(async () => {
    if (!room || endedRef.current) return;
    endedRef.current = true;
    setEnding(true);
    try {
      // End the LiveKit room first so participants disconnect, then
      // close the underlying speaking session.
      await endLiveRoom(room.liveRoomId).catch(() => undefined);
      if (session) {
        await endSpeakingSession(session.sessionId).catch(() => undefined);
      }
    } finally {
      router.push('/expert/queue');
    }
  }, [room, router, session]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <span className="inline-flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Preparing the tutor room…
        </span>
      </div>
    );
  }

  if (error || !room || !session || !tokenInfo) {
    return (
      <div className="mx-auto max-w-xl rounded-2xl border border-rose-200 bg-rose-50 p-6 text-sm text-rose-900">
        <h2 className="text-base font-semibold">Could not open this tutor room</h2>
        <p className="mt-1">{error ?? 'Live room not available.'}</p>
        <Button
          type="button"
          variant="outline"
          className="mt-4"
          onClick={() => router.push('/expert/queue')}
        >
          Back to queue
        </Button>
      </div>
    );
  }

  const { card } = session;

  return (
    <div className="mx-auto max-w-7xl space-y-4 p-4 sm:p-6">
      <header className="flex flex-wrap items-baseline justify-between gap-2">
        <div>
          <p className="text-xs font-medium uppercase tracking-wider text-muted-foreground">
            Expert · Live tutor room
          </p>
          <h1 className="text-2xl font-bold text-foreground">{card.scenarioTitle}</h1>
          <p className="text-sm text-muted-foreground">
            {card.setting} · Candidate: {card.candidateRole}
          </p>
        </div>
        <Button
          type="button"
          variant="destructive"
          size="md"
          onClick={() => void handleEndSession()}
          disabled={ending}
          data-testid="expert-live-room-end"
        >
          {ending ? (
            <>
              <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> Ending…
            </>
          ) : (
            <>
              <PhoneOff className="mr-2 h-4 w-4" aria-hidden /> End session
            </>
          )}
        </Button>
      </header>

      <LiveTutorRoomShell
        livekitWssUrl={room.livekitWssUrl}
        token={tokenInfo.token}
        onEnd={() => void handleEndSession()}
      >
        <TutorCuePanel
          cardId={card.cardId}
          rolePlayDurationSeconds={card.rolePlayTimeSeconds || 5 * 60}
        />
      </LiveTutorRoomShell>

      <section className="rounded-2xl border border-border bg-surface p-4 shadow-sm">
        <div className="mb-2 flex items-center gap-2">
          <NotebookPen className="h-4 w-4 text-muted-foreground" aria-hidden />
          <h2 className="text-sm font-semibold text-foreground">Private notes (tutor only)</h2>
        </div>
        <textarea
          value={privateNotes}
          onChange={(e) => setPrivateNotes(e.target.value)}
          placeholder="Observations, coaching points, follow-up cues — kept on your device only."
          className="h-32 w-full resize-y rounded-md border border-border bg-surface px-3 py-2 text-sm text-foreground focus:border-primary focus:outline-none focus:ring-2 focus:ring-primary/30"
          aria-label="Tutor private notes"
        />
        <p className="mt-1 text-xs text-muted-foreground">
          Notes are local to this device. Use the review screen after the session for
          formal feedback that the learner will see.
        </p>
      </section>
    </div>
  );
}
