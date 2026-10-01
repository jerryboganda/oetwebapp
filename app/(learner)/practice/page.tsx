'use client';

import { useEffect } from 'react';
import { Shuffle, Zap, BookOpen, Headphones, Mic, PenLine } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { CardLink } from '@/components/ui/card-link';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';

// Sub-test modes carry their skill identity colour (DESIGN.md §2); the rest use the brand violet.
const PRACTICE_MODES = [
  {
    href: '/practice/interleaved',
    icon: Shuffle,
    title: 'Interleaved Practice',
    description: 'Mixed sub-test session with AI-selected tasks that target your weak areas.',
    tone: 'bg-primary/10 text-primary',
  },
  {
    href: '/vocabulary/quiz',
    icon: Zap,
    title: 'Quick Vocabulary Quiz',
    description: 'A short medical-vocabulary quiz built from your own word bank.',
    tone: 'bg-primary/10 text-primary',
  },
  {
    href: '/writing',
    icon: PenLine,
    title: 'Writing Practice',
    description: 'Practise referral letters, discharge summaries, and more.',
    tone: 'bg-skill-writing/10 text-skill-writing',
  },
  {
    href: '/speaking',
    icon: Mic,
    title: 'Speaking Practice',
    description: 'Role plays, handovers, and speaking clarity drills.',
    tone: 'bg-skill-speaking/10 text-skill-speaking',
  },
  {
    href: '/recalls/words',
    icon: Headphones,
    title: 'Recalls Audio',
    description: 'Click recall words to hear British clinical pronunciation on paid plans.',
    tone: 'bg-primary/10 text-primary',
  },
  {
    href: '/reading',
    icon: BookOpen,
    title: 'Reading Practice',
    description: 'Part A, B, and C tasks with timed conditions.',
    tone: 'bg-skill-reading/10 text-skill-reading',
  },
  {
    href: '/listening',
    icon: Headphones,
    title: 'Listening Practice',
    description: 'Extracts, consultations, and presentations.',
    tone: 'bg-skill-listening/10 text-skill-listening',
  },
];

export default function PracticePage() {
  useEffect(() => {
    analytics.track('page_viewed', { page: 'practice' });
  }, []);

  return (
    <>
      <LearnerPageHero
        eyebrow="Practice"
        title="Choose your practice mode"
        description="Pick a practice style that fits your schedule and focus area."
        icon={Shuffle}
      />

      <MotionSection>
        <LearnerSurfaceSectionHeader
          eyebrow="Practice modes"
          title="Pick a mode to get started"
          description="Choose between mixed practice, quick sessions, or individual sub-tests."
          className="mb-4"
        />
        <div className="grid grid-cols-1 gap-4 md:grid-cols-2 lg:grid-cols-3">
          {PRACTICE_MODES.map((mode, i) => (
            <MotionItem key={mode.href} delayIndex={Math.min(i, 5)} className="h-full">
              <CardLink href={mode.href} className="group flex h-full items-start gap-3">
                <div className={`flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl ${mode.tone}`}>
                  <mode.icon className="h-5 w-5" aria-hidden="true" />
                </div>
                <div className="min-w-0">
                  <h3 className="text-sm font-bold text-navy transition-colors group-hover:text-primary-dark">{mode.title}</h3>
                  <p className="mt-1 text-xs text-muted">{mode.description}</p>
                </div>
              </CardLink>
            </MotionItem>
          ))}
        </div>
      </MotionSection>
    </>
  );
}
