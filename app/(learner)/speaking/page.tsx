'use client';

import { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { BookOpen, ClipboardList, Clock, MessageCircleQuestion, Mic, RefreshCw, Star, Users, Video } from 'lucide-react';
import { useAuth } from '@/contexts/auth-context';
import { LearnerDashboardShell } from '@/components/layout';
import { trackSpeaking } from '@/lib/analytics/speaking-events';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { fetchSpeakingHome, type SpeakingHome } from '@/lib/api';
import { useEntitlementSnapshot } from '@/lib/query/hooks';
import { CreditsGuideButton, LearnerPageHero, LearnerSurfaceCard, LearnerSurfaceSectionHeader } from '@/components/domain';
import { FreeSampleLauncher } from '@/components/domain/free-sample-launcher';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { FREE_SPEAKING_SAMPLE_COPY } from '@/components/domain/speaking/SpeakingRulesConsent';
import type { LearnerSurfaceCardModel } from '@/lib/learner-surface';
import {
  SPEAKING_ASSESSMENT_CRITERIA_HREF,
  SPEAKING_INTRO_QUESTIONS_HREF,
} from '@/lib/speaking-candidate-resources';

/**
 * 23 Sep 2026 owner copy (exact): the Practice Library is a full card in the
 * same pattern as the Writing hub's practice card (WritingLandingCardItem).
 */
function PracticeLibraryCard() {
  return (
    <Card padding="md" className="h-full" data-testid="speaking-practice-library-card">
      <CardContent className="flex h-full flex-col">
        <span className="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-background-light text-primary">
          <BookOpen className="h-5 w-5" aria-hidden="true" />
        </span>
        <h3 className="mt-3 text-base font-bold text-navy">Practice Library</h3>
        <p className="mt-1 flex-1 text-sm leading-snug text-muted">
          Practise one OET Speaking role-play card at a time - 3 minutes to prepare and 5 minutes to speak. The AI plays the patient and marks your result.
        </p>
        <p className="mt-2 text-xs font-semibold text-muted" data-testid="speaking-credit-cost-note">
          1 card = 2 AI credits | Browsing the library is free
        </p>
        <Link
          href="/speaking/selection"
          className="mt-4 inline-flex items-center gap-1.5 rounded text-sm font-bold text-primary transition-colors hover:text-primary/80 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
        >
          Open practice library <span aria-hidden="true">→</span>
        </Link>
      </CardContent>
    </Card>
  );
}

export default function SpeakingHome() {
  const router = useRouter();
  const { user, loading: authLoading } = useAuth();
  const needsProfession = !authLoading && user?.role === 'learner' && !user?.activeProfessionId;
  const [home, setHome] = useState<SpeakingHome | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  // Live Tutor is entitlement-gated (FINAL 2026-09-06: eligible main course /
  // package or the Speaking Crash Course = plan flag SpeakingAddonsEnabled).
  // Fail OPEN while the snapshot is unknown so the card never flickers locked;
  // the server still enforces the gate (403 live_tutor_not_eligible).
  const identity = user?.userId ?? '';
  const { data: entitlement } = useEntitlementSnapshot(identity, { enabled: Boolean(identity) });
  const tutorEligible = entitlement ? entitlement.speakingAddonsEnabled === true : true;

  useEffect(() => {
    if (needsProfession) {
      router.replace('/speaking/select-profession');
    }
  }, [needsProfession, router]);

  useEffect(() => {
    if (authLoading || needsProfession) return;
    trackSpeaking('module_entry', { from: 'speaking_home' });
    fetchSpeakingHome()
      .then((speakingHome) => setHome(speakingHome))
      .catch(() => setError('Failed to load speaking tasks. Please try again.'))
      .finally(() => setLoading(false));
  }, [authLoading, needsProfession]);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Speaking">
        <LearnerSkeleton variant="dashboard" />
      </LearnerDashboardShell>
    );
  }

  const credits = home?.reviewCredits?.available ?? 0;
  // Distinct cards on the platform (the recommended role play is also in the
  // featured list) — shown in the hero; the cards themselves live in the library.
  const practiceCardCount = new Set(
    [home?.recommendedRolePlay?.id, ...(home?.featuredTasks ?? []).map((task) => task?.id)].filter(Boolean),
  ).size;

  // Resume an in-progress role play — kept because it's necessary for progress
  // (backend surfaces pastAttempts[].state='in_progress'/'draft').
  const resumeAttempt = (home?.pastAttempts ?? []).find((attempt) => {
    const state = attempt?.state?.toLowerCase() ?? '';
    return state === 'in_progress' || state === 'in-progress' || state === 'draft';
  }) ?? null;

  const examCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'backend_task',
    accent: 'indigo',
    eyebrow: 'AI Assessment',
    eyebrowIcon: Mic,
    title: 'Full AI Speaking Mock',
    description: 'A short unscored intro, then Card A and Card B — 3 minutes to prepare and 5 minutes to speak on each. The AI plays the patient and marks your result.',
    metaItems: [
      { icon: Mic, label: 'Card A + Card B' },
      { icon: Star, label: '2 cards = 4 AI credits' },
    ],
    primaryAction: { label: 'Start Speaking Exam', href: '/speaking/exam' },
  };

  // Book a Tutor stays visible for everyone; ineligible learners get an
  // explanatory locked state instead of a dead-end booking page.
  const tutorCard: LearnerSurfaceCardModel = {
    kind: 'task',
    sourceType: 'frontend_navigation',
    accent: 'emerald',
    eyebrow: 'Live Tutor',
    eyebrowIcon: Video,
    title: 'Book a tutor as your patient',
    description: tutorEligible
      ? 'Prefer a human examiner? Book a 1-on-1 live speaking session with an OET tutor who plays the patient and gives you personalised feedback.'
      : 'Live 1-on-1 tutor sessions are included with eligible course packages. See which courses include a tutor to play your patient.',
    metaItems: [
      { icon: Users, label: 'Live 1-on-1' },
      { icon: Clock, label: 'Scheduled session' },
    ],
    ...(tutorEligible
      ? { primaryAction: { label: 'Book a Tutor', href: '/private-speaking' } }
      : {
          statusLabel: 'Eligible packages only',
          primaryAction: { label: 'View eligible courses', href: '/catalog' },
          secondaryAction: { label: 'My bookings', href: '/private-speaking', variant: 'secondary' as const },
        }),
  };

  return (
    <LearnerDashboardShell pageTitle="Speaking">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Speaking"
          icon={Mic}
          accent="primary"
          title="Get assessed by AI or book a live tutor"
          description="Practise any role-play card on the platform, take a full two-card Speaking exam marked by AI, or book a tutor to play your patient."
          highlights={[
            { icon: Star, label: 'AI credits', value: `${credits} available` },
            { icon: Mic, label: 'Practice cards', value: practiceCardCount > 0 ? `${practiceCardCount} ready` : 'Browse library' },
            { icon: Video, label: 'Live tutoring', value: '1-on-1 booking' },
          ]}
        />

        <CreditsGuideButton variant="banner" />

        {/* Same for every profession / package — not filtered by the practice library. */}
        <section className="grid grid-cols-1 gap-4 lg:grid-cols-2">
          <LearnerSurfaceCard
            card={{
              kind: 'navigation',
              sourceType: 'frontend_navigation',
              accent: 'primary',
              eyebrow: 'Reference · All professions',
              eyebrowIcon: ClipboardList,
              title: 'Speaking Assessment Criteria',
              description: 'The 9 criteria your role-plays are assessed against, with 4 linguistic bands and 5 clinical indicators. Same for all professions.',
              metaItems: [
                { icon: ClipboardList, label: '9 sections' },
                { icon: ClipboardList, label: 'Language + clinical' },
              ],
              primaryAction: {
                label: 'Open Assessment Criteria',
                href: SPEAKING_ASSESSMENT_CRITERIA_HREF,
              },
            }}
          />
          <LearnerSurfaceCard
            card={{
              kind: 'navigation',
              sourceType: 'frontend_navigation',
              accent: 'primary',
              eyebrow: 'Reference · All professions',
              eyebrowIcon: MessageCircleQuestion,
              title: 'Speaking Intro Questions',
              description: '12 common introductory questions with adaptable sample answers for every profession. Personalise the highlighted details.',
              metaItems: [
                { icon: MessageCircleQuestion, label: '12 questions' },
                { icon: MessageCircleQuestion, label: 'All professions' },
              ],
              primaryAction: {
                label: 'Open Intro Questions',
                href: SPEAKING_INTRO_QUESTIONS_HREF,
              },
            }}
          />
        </section>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {/* Start Speaking (22 Sep 2026 handoff, item 3): fixed order — Free
            Speaking Mock, then Practice Library (a full card, not a text
            link), then the SEPARATE full two-card AI mock, then the gated
            tutor booking. The library is not inlined here and never absorbs
            the full AI mock. */}
        <section aria-label="Start Speaking" data-tour="speaking-hub" className="space-y-4">
          <LearnerSurfaceSectionHeader
            eyebrow="AI Assessment"
            title="Start Speaking"
            description="Try a free sample, browse the library, take the full AI mock, or book a tutor."
          />

          {/* Resume in-progress role play — necessary for progress; backend
              surfaces pastAttempts[].state='in_progress'. Shown first: picking
              up saved work outranks starting something new. */}
          {resumeAttempt ? (
            <MotionSection>
              <LearnerSurfaceCard card={{
                kind: 'task',
                sourceType: 'backend_task',
                accent: 'indigo',
                eyebrow: 'Resume Attempt',
                eyebrowIcon: RefreshCw,
                title: 'Continue your in-progress role play',
                description: 'Your speaking attempt is saved. Pick up exactly where you stopped. No credits are spent until you submit for review.',
                metaItems: [
                  { icon: Clock, label: 'Paused' },
                  { icon: RefreshCw, label: 'In progress' },
                ],
                primaryAction: { label: 'Resume Role Play', href: resumeAttempt.route },
                secondaryAction: { label: 'Pick a Different Scenario', href: '/speaking/selection', variant: 'secondary' },
              }} />
            </MotionSection>
          ) : null}

          {/* Free Mocks: ONE free AI-graded role-play card per learner, for the
              learner's own profession (22 Sep 2026 handoff: no cross-profession
              picker). Renders nothing unless the server offers a sample. */}
          <FreeSampleLauncher
            subtest="speaking"
            icon={Mic}
            testId="speaking-free-mock-card"
            title="Free Speaking Mock"
            description={FREE_SPEAKING_SAMPLE_COPY}
            className=""
          />

          <section className="grid grid-cols-1 gap-6 lg:grid-cols-2">
            <MotionSection>
              <PracticeLibraryCard />
            </MotionSection>
            <MotionSection delayIndex={1}>
              <LearnerSurfaceCard card={examCard} />
            </MotionSection>
            <MotionSection delayIndex={2}>
              <LearnerSurfaceCard card={tutorCard} />
            </MotionSection>
          </section>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
