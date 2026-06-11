'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Tutor-only view of a live-tutor Speaking exam. Shows BOTH roleplayer (patient)
 * cards plus the live, server-authoritative phase so the human tutor can play
 * the patient and follow the two-card timed structure. The student talks to the
 * tutor on the booked video call; this page just gives the tutor their script.
 *
 * After the exam completes, the two child sessions appear in the normal tutor
 * marking queue (each is a LiveTutor SpeakingSession) and the tutor marks them
 * there — those marks become the official exam result.
 */
import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { Loader2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { RoleplayerCard } from '@/components/domain/speaking/RoleplayerCard';
import {
  getSpeakingExamTutorView,
  type SpeakingExamTutorView,
} from '@/lib/api/speaking-exams';

const POLL_INTERVAL_MS = 3_000;

const PHASE_LABEL: Record<string, string> = {
  intro: 'Introduction (unscored)',
  prep_a: 'Card A — preparation',
  active_a: 'Card A — discussion',
  prep_b: 'Card B — preparation',
  active_b: 'Card B — discussion',
  completed: 'Completed',
  cancelled: 'Cancelled',
  expired: 'Expired',
};

function formatMmSs(s: number): string {
  const safe = Math.max(0, s);
  return `${Math.floor(safe / 60).toString().padStart(2, '0')}:${(safe % 60).toString().padStart(2, '0')}`;
}

export default function ExpertSpeakingExamPage() {
  const params = useParams<{ examId: string }>();
  const examId = params?.examId ?? '';
  const [view, setView] = useState<SpeakingExamTutorView | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [secondsLeft, setSecondsLeft] = useState<number | null>(null);

  const refresh = useCallback(async () => {
    if (!examId) return;
    try {
      const v = await getSpeakingExamTutorView(examId);
      setView(v);
      setError(null);
      if (v.clock?.stageEndsAt) {
        const ends = new Date(v.clock.stageEndsAt).getTime();
        const now = new Date(v.clock.serverNow).getTime();
        setSecondsLeft(Math.max(0, Math.floor((ends - now) / 1000)));
      } else {
        setSecondsLeft(null);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load the exam.');
    } finally {
      setLoading(false);
    }
  }, [examId]);

  useEffect(() => {
    void refresh();
    const interval = window.setInterval(() => void refresh(), POLL_INTERVAL_MS);
    return () => window.clearInterval(interval);
  }, [refresh]);

  useEffect(() => {
    if (secondsLeft == null) return;
    const t = window.setInterval(() => {
      setSecondsLeft((p) => (p == null ? p : Math.max(0, p - 1)));
    }, 1_000);
    return () => window.clearInterval(t);
  }, [secondsLeft != null]); // eslint-disable-line react-hooks/exhaustive-deps

  if (loading) {
    return (
      <div className="flex min-h-[60vh] items-center justify-center">
        <Loader2 className="h-6 w-6 animate-spin text-muted" />
      </div>
    );
  }
  if (error && !view) {
    return <div className="mx-auto max-w-lg px-4 py-12 text-center text-sm text-rose-700">{error}</div>;
  }
  if (!view) return null;

  const activeCard = view.currentCardNumber === 2 ? 2 : view.currentCardNumber === 1 ? 1 : 0;

  return (
    <div className="mx-auto max-w-3xl px-4 py-8">
      <header className="mb-6 flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Live-tutor Speaking exam</h1>
          <p className="text-sm text-muted">{PHASE_LABEL[view.state] ?? view.state}</p>
        </div>
        {secondsLeft != null ? (
          <div className="rounded-lg border border-border bg-surface px-4 py-2 text-center">
            <div className="text-2xl font-bold tabular-nums text-foreground">{formatMmSs(secondsLeft)}</div>
            <div className="text-[11px] uppercase tracking-wide text-muted">remaining</div>
          </div>
        ) : null}
      </header>

      <p className="mb-4 rounded-lg bg-amber-50 px-3 py-2 text-sm text-amber-800">
        You are the patient. Play the card the candidate is currently on. The phases advance
        automatically — Card B opens after Card A&apos;s 8 minutes.
      </p>

      <div className="space-y-5">
        {view.cards.map((c) => (
          <div
            key={c.cardNumber}
            className={cn(
              'rounded-xl p-1 transition',
              (c.cardNumber === activeCard) ? 'ring-2 ring-amber-400' : 'opacity-80',
            )}
          >
            <div className="mb-1 flex items-center gap-2 px-2 text-xs font-semibold uppercase tracking-wide text-muted">
              Card {c.cardNumber === 1 ? 'A' : 'B'}
              {c.cardNumber === activeCard ? (
                <span className="rounded-full bg-amber-500 px-2 py-0.5 text-[10px] text-white">Current</span>
              ) : null}
              {c.cardTypeName ? <span className="text-muted">· {c.cardTypeName}</span> : null}
            </div>
            <RoleplayerCard
              card={{
                professionId: view.professionId,
                setting: c.setting,
                interlocutorRole: c.interlocutorRole,
                patientName: c.patientName,
                patientAge: c.patientAge,
                patientBackground: c.patientBackground,
                patientTasks: c.patientTasks,
                displayCardNumber: c.displayCardNumber,
              }}
              cardNumber={c.cardNumber}
            />
          </div>
        ))}
      </div>
    </div>
  );
}
