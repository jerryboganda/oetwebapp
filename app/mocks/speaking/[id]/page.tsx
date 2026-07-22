'use client';

import { useEffect, useState } from 'react';
import { useSearchParams } from 'next/navigation';
import { GraduationCap, Users } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerSurfaceCard } from '@/components/domain/learner-surface';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import { fetchMockSpeakingAccess } from '@/lib/api';

/**
 * Full Mock Speaking gateway (2026-07-22 owner rule). Sits between the Mock
 * Center's Speaking section and its two completion paths — the AI two-card
 * exam and the existing live-tutor booking flow. Under 7 days to the
 * candidate's target exam, only the AI option is shown; 7+ days out, both
 * are offered.
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

  const aiCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'backend_task',
    accent: 'indigo',
    eyebrow: 'AI Exam',
    eyebrowIcon: GraduationCap,
    title: 'Start AI Speaking Exam',
    description:
      access?.requiresAiOnly
        ? "Your exam is under 7 days away, so this mock's Speaking section must be completed with the AI examiner."
        : 'The AI plays the patient and marks your two-card exam instantly.',
    primaryAction: { label: 'Start AI Speaking Exam', href: aiHref },
  };

  const tutorCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'frontend_navigation',
    accent: 'emerald',
    eyebrow: 'Live Tutor',
    eyebrowIcon: Users,
    title: 'Book a Tutor',
    description: 'A human tutor plays the patient and marks your exam, based on available slots.',
    primaryAction: { label: 'Book a Tutor', href: tutorHref },
  };

  return (
    <LearnerDashboardShell pageTitle="Mock Speaking" subtitle="Choose how to complete this mock's Speaking section" backHref="/mocks">
      {loadError ? (
        <p className="text-sm text-danger">{loadError}</p>
      ) : !access ? (
        <p className="text-sm text-muted">Checking your Speaking options…</p>
      ) : (
        <div className={access.requiresAiOnly ? 'max-w-md' : 'grid gap-4 sm:grid-cols-2'}>
          <LearnerSurfaceCard card={aiCard} />
          {access.requiresAiOnly ? null : <LearnerSurfaceCard card={tutorCard} />}
        </div>
      )}
    </LearnerDashboardShell>
  );
}
