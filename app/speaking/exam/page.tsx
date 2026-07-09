'use client';

/**
 * Speaking module rebuild (2026-06-11 spec).
 *
 * Launcher for the two-card Speaking exam. Starts an AI exam (the AI plays the
 * patient and marks the result) and routes into the exam runner. Booking a
 * human tutor as the patient is a separate, pay-per-session flow under
 * `/speaking` (private speaking booking).
 */
import { useCallback, useState } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Loader2, GraduationCap, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { createSpeakingExam } from '@/lib/api/speaking-exams';
import { ApiError } from '@/lib/api';
import {
  InsufficientCreditsModal,
  isInsufficientCreditsError,
  readInsufficientCreditsMessage,
  creditPurchaseHrefForError,
} from '@/components/domain/InsufficientCreditsModal';

export default function SpeakingExamLauncherPage() {
  const router = useRouter();
  const [starting, setStarting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [creditMessage, setCreditMessage] = useState<string | null>(null);
  const [creditHref, setCreditHref] = useState('/ai-packages');

  const startAiExam = useCallback(async () => {
    if (starting) return;
    setStarting(true);
    setError(null);
    setCreditMessage(null);
    try {
      const exam = await createSpeakingExam({ mode: 'ai' });
      router.push(`/speaking/exam/${exam.examId}`);
    } catch (err) {
      // No AI credits: the wallet can't fund the exam (backend pre-flight throws
      // 402 `speaking_exam_insufficient_credits` BEFORE the exam is created, so
      // the candidate is never stranded mid-exam). Surface the shared blocking
      // modal with a direct path to the AI Credits storefront — matching Writing
      // — instead of a generic inline error line.
      if (isInsufficientCreditsError(err)) {
        setCreditMessage(readInsufficientCreditsMessage(err));
        setCreditHref(creditPurchaseHrefForError(err));
      } else {
        setError(
          err instanceof ApiError
            ? err.userMessage
            : err instanceof Error
              ? err.message
              : 'Could not start the exam.',
        );
      }
      setStarting(false);
    }
  }, [router, starting]);

  return (
    <div className="mx-auto max-w-xl px-4 py-10">
      <h1 className="text-2xl font-semibold text-foreground">Speaking exam</h1>
      <p className="mt-2 text-sm leading-relaxed text-muted">
        A full OET-style Speaking exam: a short unscored introduction, then two role-play cards
        (Card A and Card B). Each card gives you 3 minutes to prepare and 5 minutes to speak. The
        second card appears automatically when the first finishes.
      </p>

      <div className="mt-5 flex items-start gap-2 rounded-lg border border-amber-200 bg-amber-50 p-3 text-sm text-amber-800">
        <FileText className="mt-0.5 h-4 w-4 flex-shrink-0" />
        <span>
          Have a <strong>blank sheet of paper and a pen</strong> ready for rough notes during
          preparation.
        </span>
      </div>

      <div className="mt-6 rounded-2xl border border-border bg-surface p-5">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 items-center justify-center rounded-full bg-primary/10 text-primary">
            <GraduationCap className="h-5 w-5" />
          </span>
          <div>
            <h2 className="font-semibold text-foreground">AI examiner</h2>
            <p className="text-sm text-muted">
              The AI plays the patient and marks your result. Uses 2 AI credits (1 per card).
            </p>
          </div>
        </div>
        {error ? (
          <p className="mt-3 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
            {error}
          </p>
        ) : null}
        <Button className="mt-4 w-full" onClick={startAiExam} disabled={starting}>
          {starting ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
          Start AI exam
        </Button>
      </div>

      <p className="mt-4 text-center text-sm text-muted">
        Prefer a human examiner?{' '}
        <Link href="/speaking" className="font-medium text-primary hover:underline">
          Book a tutor session
        </Link>
        .
      </p>

      <InsufficientCreditsModal
        open={creditMessage !== null}
        message={creditMessage ?? ''}
        onClose={() => setCreditMessage(null)}
        ctaHref={creditHref}
        ctaLabel="Buy AI Credits"
      />
    </div>
  );
}
