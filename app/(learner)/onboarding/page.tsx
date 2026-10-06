'use client';

import { ArrowLeft, ArrowRight, BarChart3, BookOpen, CheckCircle2, Target } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { useCallback, useEffect, useState } from 'react';
import { Button } from '@/components/ui/button';
import { MotionFadeSwitch } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { Stepper } from '@/components/ui/stepper';
import { useAnalytics } from '@/hooks/use-analytics';
import { completeOnboarding, fetchOnboardingState, startOnboarding } from '@/lib/api';

const STEPS = [
  {
    id: 'welcome',
    title: 'Welcome to OET Prep',
    icon: BookOpen,
    heading: 'What is the OET?',
    description:
      'The Occupational English Test (OET) assesses the English proficiency of healthcare professionals. It has four sub-tests: Listening, Reading, Writing, and Speaking, each designed around real healthcare workplace scenarios.',
    details: [
      'Recognised by regulatory bodies in Australia, New Zealand, UK, Ireland, Singapore, Dubai & more',
      'Available for 12 healthcare professions',
      'Tests real clinical communication, not general English',
    ],
  },
  {
    id: 'platform',
    title: 'How the Platform Works',
    icon: BarChart3,
    heading: 'Your personalised learning journey',
    description:
      'Our platform adapts to your strengths and weaknesses. Set your goals, then follow your AI-generated study plan to improve where it matters most.',
    details: [
      'AI-powered practice tasks with detailed feedback on all 4 sub-tests',
      'Expert human review available for Writing and Speaking',
      'Progress tracking with readiness estimates for your exam date',
    ],
  },
  {
    id: 'expect',
    title: 'What to Expect',
    icon: Target,
    heading: 'Getting started is easy',
    description:
      'After onboarding, you\'ll set your goals. This helps us build a study plan tailored to your profession, target score, and available study time.',
    details: [
      'Set your profession, exam date, and target scores',
      'Jump straight into practice across all 4 sub-tests',
      'Receive your personalised study plan within minutes',
    ],
  },
];

export default function OnboardingPage() {
  const router = useRouter();
  const { track } = useAnalytics();
  const [currentStep, setCurrentStep] = useState(0);
  const [direction, setDirection] = useState(1);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    (async () => {
      try {
        const state = await fetchOnboardingState();
        if (cancelled) return;

        if (!state.completed) {
          await startOnboarding();
          track('onboarding_started');
        }

        const stepIndex = Math.min(Math.max((state.currentStep ?? 1) - 1, 0), STEPS.length - 1);
        setCurrentStep(stepIndex);
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => { cancelled = true; };
  }, [track]);

  const goNext = useCallback(() => {
    if (currentStep < STEPS.length - 1) {
      setDirection(1);
      setCurrentStep((s) => s + 1);
    } else {
      void (async () => {
        await completeOnboarding();
        track('onboarding_completed');
        router.push('/goals');
      })();
    }
  }, [currentStep, router, track]);

  const goPrev = useCallback(() => {
    if (currentStep > 0) {
      setDirection(-1);
      setCurrentStep((s) => s - 1);
    }
  }, [currentStep]);

  const step = STEPS[currentStep];
  const Icon = step.icon;
  const isLast = currentStep === STEPS.length - 1;

  const stepperSteps = STEPS.map((s) => ({
    id: s.id,
    label: s.title,
    description: s.heading,
  }));

  // Focus route: the shell gives gutters and vertical padding, so the page is
  // just a centred wizard column (no second padding layer).
  if (loading) {
    return (
      <div className="mx-auto w-full max-w-2xl space-y-5 sm:space-y-8" role="status" aria-busy="true" aria-label="Loading onboarding">
        <Skeleton aria-hidden className="h-12 w-full rounded-2xl" />
        <Skeleton aria-hidden className="h-72 w-full rounded-2xl" />
      </div>
    );
  }

  return (
    <div className="mx-auto w-full max-w-2xl space-y-5 pb-(--safe-area-inset-bottom) sm:space-y-8">
      {/* One stepper at every width: below sm it shows numbered steps only, so
          the step card and its Continue button stay in the first screen on phones. */}
      <Stepper steps={stepperSteps} currentStep={currentStep} />

      <MotionFadeSwitch
        activeKey={step.id}
        direction={direction as 1 | -1}
        className="rounded-2xl border border-border bg-surface p-6 shadow-clinical md:p-10"
      >
        <div className="mb-6 flex h-14 w-14 items-center justify-center rounded-xl bg-lavender">
          <Icon className="h-7 w-7 text-primary" aria-hidden="true" />
        </div>

        {/* The step heading is the page's one h1 (the focus header title is not a heading). */}
        <h1 className="mb-3 text-balance text-2xl font-bold leading-tight tracking-tight text-navy">{step.heading}</h1>
        <p className="mb-6 leading-relaxed text-muted">{step.description}</p>

        <ul className="space-y-3">
          {step.details.map((detail, i) => (
            <li key={i} className="flex items-start gap-3">
              <CheckCircle2 className="mt-0.5 h-5 w-5 shrink-0 text-success-strong" aria-hidden="true" />
              <span className="text-sm text-navy/80">{detail}</span>
            </li>
          ))}
        </ul>
      </MotionFadeSwitch>

      <div className="flex items-center justify-between gap-3">
        <Button
          variant="ghost"
          onClick={goPrev}
          disabled={currentStep === 0}
          className={currentStep === 0 ? 'invisible' : ''}
        >
          <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
          Back
        </Button>

        <span className="text-sm text-muted tabular-nums">
          {currentStep + 1} of {STEPS.length}
        </span>

        <Button variant="primary" onClick={goNext}>
          {isLast ? 'Set Your Goals' : 'Continue'}
          <ArrowRight className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
        </Button>
      </div>
    </div>
  );
}
