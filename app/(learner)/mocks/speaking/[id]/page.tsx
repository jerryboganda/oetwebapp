'use client';

import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { GraduationCap, Mic, Users } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceCard } from '@/components/domain/learner-surface';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { fetchMockSpeakingAccess } from '@/lib/api';
import { FreeSampleLauncher } from '@/components/domain/free-sample-launcher';
import { FREE_SPEAKING_SAMPLE_COPY } from '@/components/domain/speaking/SpeakingRulesConsent';
import { InlineAlert } from '@/components/ui/alert';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';

/**
 * Full Mock Speaking gateway (W8). AI grades the Speaking section on the
 * consumed Mock Attempt. A live tutor is an optional extra review, never a
 * result-release dependency.
 */
export default function MockSpeakingGatewayPage() {
  const searchParams = useSearchParams();

  const [access, setAccess] = useState<{ requiresAiOnly: boolean; daysUntilExam: number | null } | null>(null);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    fetchMockSpeakingAccess()
      .then((result) => {
        if (!cancelled) setAccess(result);
      })
      .catch(() => {
        if (!cancelled) setLoadError('Could not check your Speaking options. Please try again.');
      });
    return () => {
      cancelled = true;
    };
  }, []);

  const forwardedQuery = searchParams?.toString() ?? '';
  const aiHref = `/speaking/exam?${forwardedQuery}`;
  const tutorHref = `/mocks/bookings/new?${forwardedQuery}`;
  // Brand and neutral accents: these are two ways to take the section, not statuses.
  const aiCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'backend_task',
    accent: 'primary',
    eyebrow: 'AI Exam',
    eyebrowIcon: GraduationCap,
    title: 'Start AI Speaking Exam',
    description: 'The AI plays the patient and marks your two-card exam instantly. This uses your Mock Attempt — no extra Speaking credits.',
    primaryAction: { label: 'Start AI Speaking Exam', href: aiHref },
  };

  const tutorCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'frontend_navigation',
    accent: 'navy',
    eyebrow: 'Live Tutor',
    eyebrowIcon: Users,
    title: 'Book a Tutor',
    description: 'Optional extra: book a human tutor for additional review. AI grading still releases your mock result.',
    primaryAction: { label: 'Book a Tutor', href: tutorHref, variant: 'outline' },
  };

  return (
    <>
      {/* Title and subtitle this route carried as its shell header before the shell was unwrapped. */}
      <LearnerPageHero
        eyebrow="Mocks"
        icon={Mic}
        title="Mock Speaking"
        description="Choose how to complete this mock's Speaking section."
      />

      {loadError ? (
        <InlineAlert variant="error">{loadError}</InlineAlert>
      ) : !access ? (
        <div className="grid grid-cols-1 gap-4 sm:grid-cols-2" role="status" aria-busy="true" aria-label="Checking your Speaking options…">
          <Skeleton className="h-56 rounded-2xl" />
          <Skeleton className="h-56 rounded-2xl" />
        </div>
      ) : (
        <MotionSection className="grid grid-cols-1 gap-4 sm:grid-cols-2">
          {/* Renders nothing until a free sample exists, so it is not a motion item (an empty cell). */}
          <FreeSampleLauncher
            subtest="speaking"
            icon={Mic}
            testId="mock-speaking-free-sample"
            title="Free Speaking Mock"
            description={FREE_SPEAKING_SAMPLE_COPY}
            className="sm:col-span-2"
          />
          <MotionItem className="h-full">
            <LearnerSurfaceCard card={aiCard} />
          </MotionItem>
          <MotionItem delayIndex={1} className="h-full">
            <LearnerSurfaceCard card={tutorCard} />
          </MotionItem>
        </MotionSection>
      )}
    </>
  );
}
