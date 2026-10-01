'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Student-facing two-card Speaking exam at `/speaking/exam/[id]`.
 *
 * Phases (server-authoritative — the page renders from the server clock, never
 * its own timers; a 20s sweeper + per-read lazy advance guarantee auto-close):
 *
 *   intro    → unscored warm-up banner + "Begin Part 2"
 *   prep_a/b → official candidate card + 3-min prep + "bring paper & pen" notice
 *   active_a/b → AI patient conversation + 5-min discussion countdown
 *   completed → redirect to results
 *
 * Card A auto-closes after its 8-minute window and Card B auto-reveals — there
 * is no bridge step and no manual advance between cards.
 *
 * 23 Sep 2026 owner flow: the ONE Rules + consent step sits at the intro
 * (POST /exams/{id}/consent, before prep_a; child sessions inherit it), so
 * no consent is asked inside a timed screen. Each active card shows the
 * exam-style card plus ONE mic indicator; without live voice the card is
 * recorded and uploaded to its child session before the next card opens.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Loader2, FileText, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { SpeakingRoleCard, roleCardPropsFrom } from '@/components/domain/speaking-role-card';
import { ExamConversationPanel } from '@/components/domain/speaking/ExamConversationPanel';
import { SpeakingConsentBanner } from '@/components/domain/speaking/SpeakingConsentBanner';
import { SpeakingRulesConsent } from '@/components/domain/speaking/SpeakingRulesConsent';
import { LearnerLiveRoomShell } from '@/components/domain/speaking/LearnerLiveRoomShell';
import { RECORDING_UPLOAD_FAILED } from '@/hooks/useSpeakingSessionRecorder';
import { SPEAKING_INTRO_QUESTIONS } from '@/lib/speaking/intro-questions';
import {
  getSpeakingExam,
  finishSpeakingExamIntro,
  recordSpeakingExamConsent,
  startSpeakingExamCard,
  type SpeakingExamDetail,
} from '@/lib/api/speaking-exams';
import { ApiError, completeMockSection } from '@/lib/api';
import {
  createLiveRoom,
  endLiveRoom,
  issueLiveRoomToken,
  startRecording,
  type CreateLiveRoomResponse,
  type LiveRoomTokenResponse,
} from '@/lib/api/speaking-live-rooms';
import type { LiveVoiceProvider } from '@/lib/api/speaking-live-voice';

const POLL_INTERVAL_MS = 3_000;
// A live transcript that still will not save after this many tries stops holding the exam back.
const MAX_FAILED_TRANSCRIPT_FLUSHES = 3;

function formatMmSs(secondsLeft: number): string {
  const safe = Math.max(0, secondsLeft);
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

/** Seconds remaining derived from the server clock + local elapsed since fetch. */
function deriveSecondsLeft(detail: SpeakingExamDetail | null, fetchedAtMs: number): number | null {
  if (!detail?.clock?.stageEndsAt) return null;
  const ends = new Date(detail.clock.stageEndsAt).getTime();
  const serverNow = new Date(detail.clock.serverNow).getTime();
  const elapsedSinceFetch = Date.now() - fetchedAtMs;
  const remainingMs = ends - serverNow - elapsedSinceFetch;
  return Math.max(0, Math.floor(remainingMs / 1000));
}

export default function SpeakingExamPage() {
  const params = useParams<{ id: string }>();
  const examId = params?.id ?? '';
  const router = useRouter();

  const [exam, setExam] = useState<SpeakingExamDetail | null>(null);
  const [fetchedAt, setFetchedAt] = useState(0);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);
  const [secondsLeft, setSecondsLeft] = useState<number | null>(null);
  const [busy, setBusy] = useState(false);
  const [introConsentAccepted, setIntroConsentAccepted] = useState(false);
  const [requestedVoiceProvider, setRequestedVoiceProvider] = useState<LiveVoiceProvider | undefined>();
  const [liveTutorConsentAccepted, setLiveTutorConsentAccepted] = useState(false);
  const [liveRoom, setLiveRoom] = useState<CreateLiveRoomResponse | null>(null);
  const [liveRoomToken, setLiveRoomToken] = useState<LiveRoomTokenResponse | null>(null);
  const [liveRoomError, setLiveRoomError] = useState<string | null>(null);
  const [recordingReady, setRecordingReady] = useState(false);

  const examRef = useRef<SpeakingExamDetail | null>(null);
  examRef.current = exam;
  const mockSectionCompletedRef = useRef(false);
  const refreshingRef = useRef(false);
  const failedFlushesRef = useRef(0);
  const liveRoomSessionRef = useRef<string | null>(null);
  const liveRoomRef = useRef<CreateLiveRoomResponse | null>(null);
  const voiceStopRef = useRef<(() => Promise<boolean>) | null>(null);
  // The mode ExamConversationPanel latched when this card mounted. The panel keeps it for the whole card although
  // every poll re-reads liveVoiceAvailable, so a failed save is judged by this, never by the latest poll.
  const cardModeRef = useRef<{ sessionId: string | null; live: boolean } | null>(null);
  const handleVoiceStopReady = useCallback((stop: (() => Promise<boolean>) | null) => {
    voiceStopRef.current = stop;
    // The panel registers in the commit that mounts it, so examRef still holds the DTO it latched from.
    // A re-registration inside the same card (a new stop identity) keeps the first value.
    const current = examRef.current;
    const sessionId = current?.currentSessionId ?? null;
    if (stop && cardModeRef.current?.sessionId !== sessionId) {
      cardModeRef.current = { sessionId, live: Boolean(current?.liveVoiceAvailable) };
    }
  }, []);

  // QA only: this REQUESTS a provider pin. The server honours it for a flagged QA account and ignores it for everyone else.
  useEffect(() => {
    const value = new URLSearchParams(window.location.search).get('voiceProvider');
    if (value === 'openai' || value === 'gemini') setRequestedVoiceProvider(value);
  }, []);

  const refresh = useCallback(async () => {
    // One poll at a time: an overlapping poll would run the previous card's save again (or mount the next
    // card) while the first is still saving.
    if (!examId || refreshingRef.current) return;
    refreshingRef.current = true;
    try {
      const detail = await getSpeakingExam(examId);
      const previous = examRef.current;
      const previousAiSessionActive = previous?.mode !== 'live_tutor'
        && (previous?.state === 'active_a' || previous?.state === 'active_b')
        && Boolean(previous.currentSessionId);
      const nextAiSessionActive = detail.mode !== 'live_tutor'
        && (detail.state === 'active_a' || detail.state === 'active_b')
        && Boolean(detail.currentSessionId);
      if (previousAiSessionActive
        && (!nextAiSessionActive || previous?.currentSessionId !== detail.currentSessionId)) {
        // Live: save the transcript. Fallback: upload this card's recording.
        // Never move on (and never drop the audio) until it lands.
        const saved = await voiceStopRef.current?.() ?? true;
        if (!saved) {
          failedFlushesRef.current += 1;
          // The mode this card's panel latched at mount, not the latest poll's flag: provider health flips at
          // runtime, and a recording must never be dropped because a later poll said live voice was back.
          const mode = cardModeRef.current;
          const live = mode && mode.sessionId === (previous?.currentSessionId ?? null)
            ? mode.live
            : Boolean(previous?.liveVoiceAvailable);
          // A transcript that will not save must not strand the learner on a card the exam clock has
          // already closed: after a few tries the exam moves on. A recording is never dropped.
          if (!live || failedFlushesRef.current < MAX_FAILED_TRANSCRIPT_FLUSHES) {
            setLoadError(live
              ? 'The live voice transcript could not be saved. Retrying before moving to the next card.'
              : RECORDING_UPLOAD_FAILED);
            setLoading(false);
            return;
          }
        }
        failedFlushesRef.current = 0;
      }
      setExam(detail);
      setFetchedAt(Date.now());
      setLoadError(null);
      if (detail.state === 'completed' && detail.mockAttemptId && detail.mockSectionId && !mockSectionCompletedRef.current) {
        mockSectionCompletedRef.current = true;
        try {
          await completeMockSection(detail.mockAttemptId, detail.mockSectionId, {
            contentAttemptId: detail.examId,
            rawScore: null,
            rawScoreMax: null,
            scaledScore: null,
            grade: null,
            evidence: { source: 'ai_speaking_exam', examId: detail.examId },
          });
        } catch (mockErr) {
          console.warn('Could not mark mock speaking section complete', mockErr);
        }
      }
      if (detail.state === 'completed' || detail.state === 'expired' || detail.state === 'cancelled') {
        router.replace(`/speaking/exam/${examId}/results`);
      }
    } catch (err) {
      setLoadError(
        err instanceof ApiError ? err.userMessage : err instanceof Error ? err.message : 'Could not load the exam.',
      );
    } finally {
      refreshingRef.current = false;
      setLoading(false);
    }
  }, [examId, router]);

  useEffect(() => {
    setLiveTutorConsentAccepted(false);
  }, [exam?.currentSessionId]);

  useEffect(() => {
    const sessionId = exam?.currentSessionId ?? null;
    const active = exam?.state === 'active_a' || exam?.state === 'active_b';
    if (exam?.mode !== 'live_tutor' || !sessionId || !active) {
      const roomToClose = liveRoomRef.current;
      liveRoomRef.current = null;
      liveRoomSessionRef.current = null;
      setLiveRoom(null);
      setLiveRoomToken(null);
      setRecordingReady(false);
      if (roomToClose) {
        void endLiveRoom(roomToClose.liveRoomId).catch(() => undefined);
      }
      return;
    }

    if (liveRoomSessionRef.current && liveRoomSessionRef.current !== sessionId) {
      const roomToClose = liveRoomRef.current;
      liveRoomRef.current = null;
      liveRoomSessionRef.current = null;
      setLiveRoom(null);
      setLiveRoomToken(null);
      setRecordingReady(false);
      if (roomToClose) {
        void endLiveRoom(roomToClose.liveRoomId).catch(() => undefined);
      }
    }
  }, [exam?.currentSessionId, exam?.mode, exam?.state]);

  useEffect(() => () => {
    const roomToClose = liveRoomRef.current;
    liveRoomRef.current = null;
    liveRoomSessionRef.current = null;
    if (roomToClose) {
      void endLiveRoom(roomToClose.liveRoomId).catch(() => undefined);
    }
  }, []);

  // Initial load + poll for server-authoritative phase changes.
  useEffect(() => {
    void refresh();
    const interval = window.setInterval(() => void refresh(), POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [refresh]);

  // Local countdown ticking between polls (display only).
  useEffect(() => {
    setSecondsLeft(deriveSecondsLeft(exam, fetchedAt));
    if (!exam?.clock?.stageEndsAt) return;
    const timer = window.setInterval(() => {
      setSecondsLeft((prev) => (prev == null ? prev : Math.max(0, prev - 1)));
    }, 1_000);
    return () => window.clearInterval(timer);
  }, [exam, fetchedAt]);

  useEffect(() => {
    const sessionId = exam?.currentSessionId ?? null;
    const active = exam?.state === 'active_a' || exam?.state === 'active_b';
    if (exam?.mode !== 'live_tutor' || !sessionId || !active
      || !liveTutorConsentAccepted || liveRoomSessionRef.current) {
      return;
    }

    let cancelled = false;
    liveRoomSessionRef.current = sessionId;
    setLiveRoomError(null);
    setRecordingReady(false);

    (async () => {
      let createdRoom: CreateLiveRoomResponse | null = null;
      try {
        createdRoom = await createLiveRoom({ speakingSessionId: sessionId });
        const token = await issueLiveRoomToken(createdRoom.liveRoomId, 'learner');
        await startRecording(createdRoom.liveRoomId);
        if (cancelled) {
          await endLiveRoom(createdRoom.liveRoomId).catch(() => undefined);
          return;
        }
        liveRoomRef.current = createdRoom;
        setLiveRoom(createdRoom);
        setLiveRoomToken(token);
        setRecordingReady(true);
      } catch (err) {
        if (cancelled) {
          if (createdRoom) await endLiveRoom(createdRoom.liveRoomId).catch(() => undefined);
          return;
        }
        liveRoomSessionRef.current = null;
        setLiveRoomError(
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not start the LiveKit room.',
        );
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [exam?.currentSessionId, exam?.mode, exam?.state, liveTutorConsentAccepted]);

  const handleFinishIntro = useCallback(async () => {
    if (busy) return;
    setBusy(true);
    try {
      const detail = await finishSpeakingExamIntro(examId);
      setExam(detail);
      setFetchedAt(Date.now());
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.userMessage : 'Could not start Part 2.');
    } finally {
      setBusy(false);
    }
  }, [busy, examId]);

  // Rules + consent at the intro, then straight into Card A prep.
  const handleConsentAndBegin = useCallback(async () => {
    try {
      await recordSpeakingExamConsent(examId);
    } catch (err) {
      throw new Error(err instanceof ApiError ? err.userMessage : 'Could not record consent. Please try again.');
    }
    setIntroConsentAccepted(true);
    await handleFinishIntro();
  }, [examId, handleFinishIntro]);

  const handleStartCard = useCallback(async () => {
    if (busy) return;
    setBusy(true);
    try {
      const detail = await startSpeakingExamCard(examId);
      setExam(detail);
      setFetchedAt(Date.now());
    } catch (err) {
      setLoadError(err instanceof ApiError ? err.userMessage : 'Could not start the discussion.');
    } finally {
      setBusy(false);
    }
  }, [busy, examId]);

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-muted" />
      </div>
    );
  }

  if (loadError && !exam) {
    return (
      <div className="mx-auto max-w-lg px-4 py-12 text-center">
        <p className="text-sm text-rose-700">{loadError}</p>
        <Button className="mt-4" variant="outline" onClick={() => void refresh()}>
          Retry
        </Button>
      </div>
    );
  }

  if (!exam) return null;

  const state = exam.state;
  const isPrep = state === 'prep_a' || state === 'prep_b';
  const isActive = state === 'active_a' || state === 'active_b';
  // The card is named by its slot (the server's 1|2 ordinal), never by the number printed on the source card:
  // the two cards are drawn at random and can print the same number. Unknown slot = no letter rather than a guess.
  const slotLetter = exam.currentCardNumber === 1 ? 'A' : exam.currentCardNumber === 2 ? 'B' : null;
  const cardProps = exam.currentCard ? roleCardPropsFrom(exam.currentCard) : null;
  const candidateCard = cardProps ? { ...cardProps, cardNumber: undefined, slotLabel: slotLetter ?? undefined } : null;

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Speaking exam</h1>
          <p className="text-sm text-muted">
            {state === 'intro' ? 'Part 1 — Introduction' : slotLetter ? `Part 2 — Card ${slotLetter}` : 'Part 2'}
          </p>
        </div>
        {(isPrep || isActive) && secondsLeft != null ? (
          <div
            className={cn(
              'rounded-lg border px-4 py-2 text-center',
              secondsLeft <= 30 ? 'border-rose-300 bg-rose-50' : 'border-border bg-surface',
            )}
            role="timer"
            aria-live={secondsLeft <= 30 ? 'polite' : 'off'}
          >
            <div
              className={cn(
                'text-2xl font-bold tabular-nums',
                secondsLeft <= 30 ? 'text-rose-600' : 'text-foreground',
              )}
            >
              {formatMmSs(secondsLeft)}
            </div>
            <div className="eyebrow text-muted">
              {isPrep ? 'Preparation' : 'Discussion'}
            </div>
          </div>
        ) : null}
      </header>

      {loadError ? (
        <div className="mb-4 space-y-2 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
          <p>{loadError}</p>
          {loadError === RECORDING_UPLOAD_FAILED ? (
            <Button size="sm" variant="outline" onClick={() => void refresh()}>
              Retry upload
            </Button>
          ) : null}
        </div>
      ) : null}

      {/* ── Intro (unscored) ─────────────────────────────────────────────── */}
      {state === 'intro' && (
        <section className="rounded-2xl border border-border bg-surface p-6">
          <span className="inline-flex items-center rounded-full bg-sky-100 px-3 py-1 eyebrow text-sky-700">
            Not scored
          </span>
          <h2 className="mt-3 text-lg font-semibold text-foreground">Introduction</h2>
          <p className="mt-2 text-sm leading-relaxed text-muted">
            The examiner will start with a few friendly warm-up questions about you and your work.
            This part is <strong>not scored</strong> — it just helps you settle in. When you&apos;re
            ready, begin Part 2.
          </p>
          <div className="mt-4 rounded-lg border border-border bg-background-light p-3">
            <p className="eyebrow text-muted">
              You may be asked questions like these
            </p>
            <ul className="mt-2 list-disc space-y-1 pl-5 text-sm text-foreground">
              {SPEAKING_INTRO_QUESTIONS.map((q) => (
                <li key={q}>{q}</li>
              ))}
            </ul>
          </div>
          {exam.consentAccepted || introConsentAccepted ? (
            <Button className="mt-5 w-full" onClick={handleFinishIntro} disabled={busy}>
              {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
              Begin Part 2 (Card A)
            </Button>
          ) : (
            <SpeakingRulesConsent
              exam
              className="mt-5 space-y-4"
              startLabel="Begin Part 2 (Card A)"
              onStart={handleConsentAndBegin}
            />
          )}
        </section>
      )}

      {/* ── Prep (3 minutes) ─────────────────────────────────────────────── */}
      {isPrep && candidateCard && (
        <section className="space-y-4">
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <FileText className="mt-0.5 h-4 w-4 flex-shrink-0" />
            <span>
              Use your <strong>blank paper and pen</strong> to make rough notes — you{' '}
              <strong>cannot highlight the card on screen</strong>. Read the card carefully; the
              discussion begins automatically when preparation ends. Destroy your notes after the exam.
            </span>
          </div>
          <SpeakingRoleCard {...candidateCard} />
          <Button className="w-full" onClick={handleStartCard} disabled={busy} variant="outline">
            {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            I&apos;m ready — start the discussion now
          </Button>
        </section>
      )}

      {/* ── Active discussion (5 minutes) ────────────────────────────────── */}
      {isActive && candidateCard && exam.currentSessionId && (
        <section className="space-y-4">
          <SpeakingRoleCard {...candidateCard} className="max-h-[50dvh] overflow-y-auto overscroll-contain" />
          {exam.mode === 'live_tutor' ? (
            <>
              {!liveTutorConsentAccepted ? (
                <SpeakingConsentBanner
                  sessionMode="live_tutor"
                  sessionId={exam.currentSessionId}
                  onAccepted={() => setLiveTutorConsentAccepted(true)}
                />
              ) : null}
              <div className="rounded-xl border border-border bg-surface p-4 text-sm text-muted">
                <p className="font-medium text-foreground">LiveKit tutor room</p>
                <p className="mt-1">
                  Your microphone and camera connect directly to the assigned tutor through LiveKit.
                  The room is recorded for tutor review and the timer advances from the server.
                </p>
              </div>
              {liveRoomError ? (
                <p className="rounded-lg border border-rose-300 bg-rose-50 p-3 text-sm text-rose-700" role="alert">
                  {liveRoomError}
                </p>
              ) : liveRoom && liveRoomToken && recordingReady ? (
                <LearnerLiveRoomShell
                  liveRoomId={liveRoom.liveRoomId}
                  livekitWssUrl={liveRoom.livekitWssUrl}
                  token={liveRoomToken.token}
                  onEnd={() => {
                    const roomId = liveRoom.liveRoomId;
                    liveRoomRef.current = null;
                    liveRoomSessionRef.current = null;
                    setLiveRoom(null);
                    setLiveRoomToken(null);
                    setRecordingReady(false);
                    void endLiveRoom(roomId).catch(() => undefined);
                  }}
                />
              ) : (
                <div className="flex min-h-[480px] items-center justify-center rounded-2xl border border-border bg-background-light text-sm text-muted">
                  {liveTutorConsentAccepted ? 'Preparing the LiveKit room...' : 'Waiting for live-room consent...'}
                </div>
              )}
            </>
          ) : (
            <ExamConversationPanel
              key={exam.currentSessionId}
              sessionId={exam.currentSessionId}
              liveVoiceAvailable={exam.liveVoiceAvailable}
              requestedProvider={requestedVoiceProvider}
              onVoiceStopReady={handleVoiceStopReady}
            />
          )}
          {secondsLeft != null && secondsLeft <= 30 ? (
            <p className="flex items-center justify-center gap-2 text-sm font-medium text-rose-600">
              <AlertTriangle className="h-4 w-4" /> Wrap up — the next card starts automatically.
            </p>
          ) : null}
        </section>
      )}
    </div>
  );
}
