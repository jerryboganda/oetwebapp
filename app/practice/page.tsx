'use client';

import { LearnerPageHero, LearnerSurfaceSectionHeader } from "@/components/domain/learner-surface";
import { LearnerDashboardShell } from "@/components/layout/learner-dashboard-shell";
import { Card } from '@/components/ui/card';
import { MotionItem } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';
import { BookOpen, Headphones, Mic, PenLine, Shuffle, Zap } from 'lucide-react';
import Link from 'next/link';
import { useEffect } from 'react';

const PRACTICE_MODES = [
  {
    href: '/practice/interleaved',
    icon: Shuffle,
    title: 'Interleaved Practice',
    description: 'Mixed sub-test session with AI-selected tasks that target your weak areas.',
  },
  {
    href: '/practice/quick-session',
    icon: Zap,
    title: 'Quick Session',
    description: 'A short focused session you can complete in under 15 minutes.',
  },
  {
    href: '/writing',
    icon: PenLine,
    title: 'Writing Practice',
    description: 'Practise referral letters, discharge summaries, and more.',
  },
  {
    href: '/speaking',
    icon: Mic,
    title: 'Speaking Practice',
    description: 'Role plays, handovers, and pronunciation drills.',
  },
  {
    href: '/pronunciation',
    icon: Mic,
    title: 'Pronunciation Drills',
    description: 'Record yourself, get phoneme-level feedback, and train weak sounds.',
  },
  {
    href: '/reading',
    icon: BookOpen,
    title: 'Reading Practice',
    description: 'Part A, B, and C tasks with timed conditions.',
  },
  {
    href: '/listening',
    icon: Headphones,
    title: 'Listening Practice',
    description: 'Extracts, consultations, and presentations.',
  },
];

export default function PracticePage() {
  useEffect(() => {
    analytics.track('page_viewed', { page: 'practice' });
  }, []);

  return (
    <LearnerDashboardShell pageTitle="Practice">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Practice"
          title="Choose your practice mode"
          description="Pick a practice style that fits your schedule and focus area."
          icon={Shuffle}
          highlights={[
            { icon: Shuffle, label: 'Modes', value: `${PRACTICE_MODES.length} available` },
            { icon: Zap, label: 'Quick session', value: 'Under 15 min' },
            { icon: BookOpen, label: 'Focus', value: 'All sub-tests' },
          ]}
        />

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="Practice modes"
            title="Pick a mode to get started"
            description="Choose between mixed practice, quick sessions, or individual sub-tests."
            className="mb-4"
          />
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
            {PRACTICE_MODES.map((mode, i) => (
              <MotionItem key={mode.href} delayIndex={i}>
                <Link href={mode.href}>
                  <Card className="group flex items-start gap-3 p-5 shadow-sm transition-[border-color,box-shadow] duration-200 hover:border-border-hover hover:shadow-clinical">
                    <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary transition-colors group-hover:bg-primary/15">
                      <mode.icon className="h-5 w-5" />
                    </div>
                    <div className="min-w-0">
                      <h3 className="text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">{mode.title}</h3>
                      <p className="mt-1 text-xs text-muted">{mode.description}</p>
                    </div>
                  </Card>
                </Link>
              </MotionItem>
            ))}
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
