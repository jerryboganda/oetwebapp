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
import { useRouter, useSearchParams } from 'next/navigation';
import Link from 'next/link';
import { Loader2, GraduationCap, Mic } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
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
  const searchParams = useSearchParams();
  const mockAttemptId = searchParams?.get('mockAttemptId') ?? undefined;
  const mockSectionId = searchParams?.get('mockSectionId') ?? undefined;
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
      const exam = await createSpeakingExam({ mode: 'ai', mockAttemptId, mockSectionId });
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
  }, [router, starting, mockAttemptId, mockSectionId]);

  // No reveal motion here: the production E2E clicks "Start AI exam" right after load.
  return (
    <>
      <LearnerPageHero
        eyebrow="Speaking"
        icon={Mic}
        accent="speaking"
        title="Speaking exam"
        description="A full OET-style Speaking exam: a short unscored introduction, then two role-play cards (Card A and Card B). Each card gives you 3 minutes to prepare and 5 minutes to speak. The second card appears automatically when the first finishes."
      />

      {/* Two columns from lg, so the full-width start button stays a card's width, not the workspace's. */}
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2 lg:items-start">
        <Card padding="lg">
          <div className="flex items-center gap-3">
            <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-skill-speaking/10 text-skill-speaking">
              <GraduationCap className="h-5 w-5" aria-hidden="true" />
            </span>
            <div className="min-w-0">
              <h2 className="font-bold text-navy">AI examiner</h2>
              <p className="text-sm text-muted">
                The AI plays the patient and marks your result. Uses 4 AI credits per exam (2 per card).
              </p>
            </div>
          </div>
          {error ? (
            <InlineAlert variant="error" className="mt-3">
              {error}
            </InlineAlert>
          ) : null}
          <Button className="mt-4 w-full" onClick={startAiExam} disabled={starting}>
            {starting ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : null}
            Start AI exam
          </Button>
        </Card>

        <div className="space-y-4">
          <InlineAlert variant="warning" live="polite">
            Have a <strong>blank sheet of paper and a pen</strong> ready for rough notes during
            preparation.
          </InlineAlert>
          <p className="text-sm text-muted">
            Prefer a human examiner?{' '}
            <Link href="/speaking" className="font-medium text-primary hover:underline">
              Book a tutor session
            </Link>
            .
          </p>
        </div>
      </div>

      <InsufficientCreditsModal
        open={creditMessage !== null}
        message={creditMessage ?? ''}
        onClose={() => setCreditMessage(null)}
        ctaHref={creditHref}
        ctaLabel="Buy AI Credits"
      />
    </>
  );
}
