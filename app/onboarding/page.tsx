'use client';

import { useState, useCallback, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { motion, AnimatePresence } from 'motion/react';
import { BookOpen, BarChart3, Target, ArrowRight, ArrowLeft, CheckCircle2 } from 'lucide-react';
import { Button, Stepper } from '@/components/ui';
import { LearnerDashboardShell } from '@/components/layout';
import { useAnalytics } from '@/hooks/use-analytics';
import { completeOnboarding, fetchOnboardingState, startOnboarding } from '@/lib/api';

const STEPS = [
  {
    id: 'welcome',
    title: 'Welcome to OET Prep',
    icon: BookOpen,
    heading: 'What is the OET?',
    description:
      'The Occupational English Test (OET) assesses the English proficiency of healthcare professionals. It has four sub-tests: Listening, Reading, Writing, and Speaking — each designed around real healthcare workplace scenarios.',
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
      'Our platform adapts to your strengths and weaknesses. Start with a quick diagnostic to establish your baseline, then follow your AI-generated study plan to improve where it matters most.',
    details: [
      'AI-powered practice tasks with instant feedback on all 4 sub-tests',
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
      'After onboarding, you\'ll set your goals and take a short diagnostic assessment. This helps us build a study plan tailored to your profession, target score, and available study time.',
    details: [
      'Set your profession, exam date, and target scores',
      'Take a ~2 hour diagnostic across all 4 sub-tests (can be done in stages)',
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
  }));

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Getting Started" distractionFree>
        <div className="flex flex-1 items-center justify-center p-8 text-sm text-muted">Loading onboarding...</div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle="Getting Started" distractionFree>
      <div className="flex-1 flex items-center justify-center p-4 md:p-8">
        <div className="w-full max-w-2xl space-y-8">
          {/* Stepper */}
          <Stepper steps={stepperSteps} currentStep={currentStep} />

          {/* Card */}
          <AnimatePresence mode="wait" custom={direction}>
            <motion.div
              key={step.id}
              custom={direction}
              initial={{ opacity: 0, x: direction * 60 }}
              animate={{ opacity: 1, x: 0 }}
              exit={{ opacity: 0, x: direction * -60 }}
              transition={{ duration: 0.25 }}
              className="bg-surface rounded-2xl p-6 md:p-10 shadow-clinical"
            >
              {/* Icon */}
              <div className="w-14 h-14 rounded-xl bg-lavender flex items-center justify-center mb-6">
                <Icon className="w-7 h-7 text-primary" />
              </div>

              {/* Content */}
              <h2 className="text-2xl font-bold text-navy mb-3">{step.heading}</h2>
              <p className="text-muted leading-relaxed mb-6">{step.description}</p>

              <ul className="space-y-3">
                {step.details.map((detail, i) => (
                  <li key={i} className="flex gap-3 items-start">
                    <CheckCircle2 className="w-5 h-5 text-success flex-shrink-0 mt-0.5" />
                    <span className="text-sm text-navy/80">{detail}</span>
                  </li>
                ))}
              </ul>
            </motion.div>
          </AnimatePresence>

          {/* Navigation */}
          <div className="flex items-center justify-between">
            <Button
              variant="ghost"
              onClick={goPrev}
              disabled={currentStep === 0}
              className={currentStep === 0 ? 'invisible' : ''}
            >
              <ArrowLeft className="w-4 h-4 mr-2" />
              Back
            </Button>

            <span className="text-sm text-muted">
              {currentStep + 1} of {STEPS.length}
            </span>

            <Button variant="primary" onClick={goNext}>
              {isLast ? 'Set Your Goals' : 'Continue'}
              <ArrowRight className="w-4 h-4 ml-2" />
            </Button>
          </div>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
