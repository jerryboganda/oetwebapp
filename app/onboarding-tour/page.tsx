'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { Sparkles, BookOpen, Target, BarChart3, MessageSquare, ChevronRight, ChevronLeft, Check } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { analytics } from '@/lib/analytics';

const TOUR_STEPS = [
  {
    id: 'welcome',
    icon: Sparkles,
    color: 'text-amber-500',
    bg: 'bg-amber-50 dark:bg-amber-950/30',
    title: 'Welcome to OET Prep!',
    description: 'Your personalised path to OET success starts here. This quick tour will show you the key features.',
    detail: 'OET Prep combines AI-powered practice, expert reviews, and adaptive learning to help healthcare professionals pass the OET exam with confidence.',
  },
  {
    id: 'practice',
    icon: BookOpen,
    color: 'text-blue-500',
    bg: 'bg-blue-50 dark:bg-blue-950/30',
    title: 'Practice All Four Sub-tests',
    description: 'Listening, Reading, Writing, and Speaking — all with real OET-style questions.',
    detail: 'Each sub-test has profession-specific content. Practice at your own pace or use Exam Simulation Mode for strict timed conditions. The Interleaved Practice mode mixes skills for better retention.',
  },
  {
    id: 'goals',
    icon: Target,
    color: 'text-emerald-500',
    bg: 'bg-emerald-50 dark:bg-emerald-950/30',
    title: 'Set Your Goals & Study Plan',
    description: 'Set your target exam date and desired scores. We\'ll build a personalised study plan.',
    detail: 'Your study plan adapts as you progress. The drift detection system alerts you if you\'re falling behind, and the adaptive difficulty engine keeps challenges at the right level.',
  },
  {
    id: 'analytics',
    icon: BarChart3,
    color: 'text-purple-500',
    bg: 'bg-purple-50 dark:bg-purple-950/30',
    title: 'Track Your Progress',
    description: 'Detailed analytics show your strengths, weaknesses, and growth over time.',
    detail: 'Compare your performance against the cohort. Fluency timelines for speaking, criterion breakdowns for writing, and percentile rankings help you focus your preparation.',
  },
  {
    id: 'reviews',
    icon: MessageSquare,
    color: 'text-rose-500',
    bg: 'bg-rose-50 dark:bg-rose-950/30',
    title: 'Expert Reviews & AI Feedback',
    description: 'Get instant AI feedback plus expert reviews from certified OET assessors.',
    detail: 'AI evaluations give immediate scores and suggestions. Expert reviews provide detailed criterion-by-criterion feedback with actionable improvement notes. Use credits from your plan to request reviews.',
  },
];

export default function OnboardingTourPage() {
  const router = useRouter();
  const [currentStep, setCurrentStep] = useState(0);
  const [completedSteps, setCompletedSteps] = useState<Set<number>>(new Set());

  useEffect(() => { analytics.track('onboarding_tour_started'); }, []);

  const step = TOUR_STEPS[currentStep];

  const markComplete = useCallback(() => {
    setCompletedSteps(prev => new Set([...prev, currentStep]));
    if (currentStep < TOUR_STEPS.length - 1) {
      setCurrentStep(currentStep + 1);
    }
  }, [currentStep]);

  const finishTour = useCallback(() => {
    analytics.track('onboarding_tour_completed');
    router.push('/dashboard');
  }, [router]);

  const isLastStep = currentStep === TOUR_STEPS.length - 1;

  return (
    <LearnerDashboardShell>
      <MotionSection className="max-w-3xl mx-auto space-y-6">
        {/* Progress indicator */}
        <div className="flex items-center justify-center gap-2 mb-4">
          {TOUR_STEPS.map((s, i) => (
            <button key={s.id} onClick={() => setCurrentStep(i)} className={`w-3 h-3 rounded-full transition-all ${i === currentStep ? 'bg-primary scale-125' : completedSteps.has(i) ? 'bg-emerald-500' : 'bg-muted'}`} aria-label={`Step ${i + 1}: ${s.title}`} />
          ))}
        </div>

        <Badge variant="outline" className="mx-auto block w-fit">Step {currentStep + 1} of {TOUR_STEPS.length}</Badge>

        {/* Main step card */}
        <MotionItem key={step.id}>
          <Card className={`p-8 ${step.bg} border-2`}>
            <div className="text-center space-y-4">
              <step.icon className={`w-16 h-16 mx-auto ${step.color}`} />
              <h1 className="text-2xl font-bold">{step.title}</h1>
              <p className="text-lg text-muted-foreground">{step.description}</p>
              <div className="bg-background/80 rounded-lg p-4 mt-4">
                <p className="text-sm">{step.detail}</p>
              </div>
            </div>
          </Card>
        </MotionItem>

        {/* Navigation */}
        <div className="flex items-center justify-between">
          <Button variant="ghost" onClick={() => setCurrentStep(Math.max(0, currentStep - 1))} disabled={currentStep === 0}>
            <ChevronLeft className="w-4 h-4 mr-1" /> Previous
          </Button>
          {isLastStep ? (
            <Button onClick={finishTour} className="gap-2">
              <Check className="w-4 h-4" /> Get Started
            </Button>
          ) : (
            <Button onClick={markComplete} className="gap-2">
              Next <ChevronRight className="w-4 h-4" />
            </Button>
          )}
        </div>

        {/* Skip option */}
        <div className="text-center">
          <Button variant="ghost" size="sm" onClick={finishTour} className="text-muted-foreground">
            Skip tour and go to dashboard
          </Button>
        </div>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
