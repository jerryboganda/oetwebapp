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
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Loader2, FileText, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { OfficialCandidateCard } from '@/components/domain/speaking/OfficialCandidateCard';
import { ExamConversationPanel } from '@/components/domain/speaking/ExamConversationPanel';
import {
  getSpeakingExam,
  finishSpeakingExamIntro,
  startSpeakingExamCard,
  type SpeakingExamDetail,
} from '@/lib/api/speaking-exams';
import { ApiError } from '@/lib/api';

const POLL_INTERVAL_MS = 3_000;

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

  const examRef = useRef<SpeakingExamDetail | null>(null);
  examRef.current = exam;

  const refresh = useCallback(async () => {
    if (!examId) return;
    try {
      const detail = await getSpeakingExam(examId);
      setExam(detail);
      setFetchedAt(Date.now());
      setLoadError(null);
      if (detail.state === 'completed' || detail.state === 'expired' || detail.state === 'cancelled') {
        router.replace(`/speaking/exam/${examId}/results`);
      }
    } catch (err) {
      setLoadError(
        err instanceof ApiError ? err.userMessage : err instanceof Error ? err.message : 'Could not load the exam.',
      );
    } finally {
      setLoading(false);
    }
  }, [examId, router]);

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
  const partLabel = exam.currentCardNumber === 2 ? 'Card B' : 'Card A';

  return (
    <div className="mx-auto max-w-2xl px-4 py-8">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Speaking exam</h1>
          <p className="text-sm text-muted">
            {state === 'intro' ? 'Part 1 — Introduction' : `Part 2 — ${partLabel}`}
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
            <div className="text-[11px] uppercase tracking-wide text-muted">
              {isPrep ? 'Preparation' : 'Discussion'}
            </div>
          </div>
        ) : null}
      </header>

      {loadError ? (
        <p className="mb-4 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
          {loadError}
        </p>
      ) : null}

      {/* ── Intro (unscored) ─────────────────────────────────────────────── */}
      {state === 'intro' && (
        <section className="rounded-2xl border border-border bg-surface p-6">
          <span className="inline-flex items-center rounded-full bg-sky-100 px-3 py-1 text-xs font-semibold uppercase tracking-wide text-sky-700">
            Not scored
          </span>
          <h2 className="mt-3 text-lg font-semibold text-foreground">Introduction</h2>
          <p className="mt-2 text-sm leading-relaxed text-muted">
            The examiner will start with a few friendly warm-up questions about you and your work.
            This part is <strong>not scored</strong> — it just helps you settle in. When you&apos;re
            ready, begin Part 2.
          </p>
          <div className="mt-4 flex items-start gap-2 rounded-lg bg-amber-50 p-3 text-sm text-amber-800">
            <FileText className="mt-0.5 h-4 w-4 flex-shrink-0" />
            <span>
              Before you begin, have a <strong>blank sheet of paper and a pen</strong> ready for
              rough notes during preparation.
            </span>
          </div>
          <Button className="mt-5 w-full" onClick={handleFinishIntro} disabled={busy}>
            {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Begin Part 2 (Card A)
          </Button>
        </section>
      )}

      {/* ── Prep (3 minutes) ─────────────────────────────────────────────── */}
      {isPrep && exam.currentCard && (
        <section className="space-y-4">
          <div className="flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
            <FileText className="mt-0.5 h-4 w-4 flex-shrink-0" />
            <span>
              Use your <strong>blank paper and pen</strong> to make rough notes. Read the card
              carefully — the discussion begins automatically when preparation ends.
            </span>
          </div>
          <OfficialCandidateCard
            card={{
              professionId: exam.currentCard.professionId,
              setting: exam.currentCard.setting,
              candidateRole: exam.currentCard.candidateRole,
              background: exam.currentCard.background,
              tasks: exam.currentCard.tasks,
              patientName: exam.currentCard.patientName,
              patientAge: exam.currentCard.patientAge,
              displayCardNumber: exam.currentCard.displayCardNumber,
              disclaimer: exam.currentCard.disclaimer,
            }}
            cardNumber={exam.currentCardNumber}
          />
          <Button className="w-full" onClick={handleStartCard} disabled={busy} variant="outline">
            {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            I&apos;m ready — start the discussion now
          </Button>
        </section>
      )}

      {/* ── Active discussion (5 minutes) ────────────────────────────────── */}
      {isActive && exam.currentCard && exam.currentSessionId && (
        <section className="space-y-4">
          <OfficialCandidateCard
            card={{
              professionId: exam.currentCard.professionId,
              setting: exam.currentCard.setting,
              candidateRole: exam.currentCard.candidateRole,
              background: exam.currentCard.background,
              tasks: exam.currentCard.tasks,
              patientName: exam.currentCard.patientName,
              patientAge: exam.currentCard.patientAge,
              displayCardNumber: exam.currentCard.displayCardNumber,
              disclaimer: exam.currentCard.disclaimer,
            }}
            cardNumber={exam.currentCardNumber}
          />
          {exam.mode === 'live_tutor' ? (
            <div className="rounded-xl border border-border bg-surface p-4 text-sm text-muted">
              <p className="font-medium text-foreground">Speak with your tutor now</p>
              <p className="mt-1">
                Your tutor is playing the patient on your booked video call. Use this card to lead the
                conversation — the timer and cards advance automatically.
              </p>
            </div>
          ) : (
            <ExamConversationPanel sessionId={exam.currentSessionId} />
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
