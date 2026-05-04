'use client';

import { useEffect } from 'react';
import { BookOpen, BarChart3, FileText, GraduationCap, Globe, Mic, PenLine, Headphones } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Card } from '@/components/ui/card';
import { MotionItem } from '@/components/ui/motion-primitives';
import { analytics } from '@/lib/analytics';

const IELTS_SECTIONS = [
  {
    icon: Headphones,
    title: 'Listening',
    description: 'Four sections, 40 questions, approx. 30 minutes. Practice with extracts, consultations, and academic presentations.',
    route: '/listening',
    available: true,
  },
  {
    icon: BookOpen,
    title: 'Reading',
    description: 'Three passages, 40 questions, 60 minutes. Academic texts include journals, books, and magazines.',
    route: '/reading',
    available: true,
  },
  {
    icon: PenLine,
    title: 'Writing',
    description: 'Task 1 (150 words) + Task 2 (250 words), 60 minutes. Academic Task 1 is graph/table/diagram analysis.',
    route: '/writing',
    available: true,
  },
  {
    icon: Mic,
    title: 'Speaking',
    description: 'Three parts: introduction, long turn, and two-way discussion. 11–14 minutes with a certified examiner.',
    route: '/speaking',
    available: true,
  },
];

const SCORING_NOTES = [
  {
    icon: BarChart3,
    title: 'Band Scale 0–9',
    description: 'IELTS uses a 9-band scale in 0.5 increments. Most nursing registration boards require 7.0 overall (often with 7.0 in each skill).',
  },
  {
    icon: GraduationCap,
    title: 'Academic vs General',
    description: 'Choose Academic for university and professional registration. Choose General Training for immigration and work experience. Your goals page lets you switch anytime.',
  },
  {
    icon: Globe,
    title: 'Shared OET Core',
    description: 'IELTS runs on the same practice engine as OET. Your study plan, progress tracking, and AI feedback use the same robust infrastructure.',
  },
  {
    icon: FileText,
    title: 'Writing Task Weighting',
    description: 'Task 2 carries roughly 60% of the Writing band score. Focus your preparation time accordingly.',
  },
];

export default function IeltsGuidePage() {
  useEffect(() => {
    analytics.track('page_viewed', { page: 'ielts-guide' });
  }, []);

  return (
    <LearnerDashboardShell pageTitle="IELTS Guide">
      <main className="space-y-8">
        <LearnerPageHero
          eyebrow="IELTS Preparation"
          icon={Globe}
          accent="primary"
          title="IELTS on the same powerful core"
          description="While OET remains our flagship, IELTS preparation uses the same four-skill engine, AI feedback, and expert review backbone. Select Academic or General Training in your Goals settings."
          highlights={[
            { icon: BarChart3, label: 'Band scale', value: '0–9' },
            { icon: Mic, label: 'Speaking', value: 'In-person examiner' },
            { icon: PenLine, label: 'Writing', value: 'Task 1 + Task 2' },
          ]}
        />

        <LearnerSurfaceSectionHeader
          eyebrow="The Four Skills"
          title="IELTS test structure"
          description="Tap any skill to practise. The engine adapts to your weakest areas across all four papers."
        />

        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          {IELTS_SECTIONS.map((section, index) => (
            <MotionItem key={section.title} delayIndex={index}>
              <Card className="border-border bg-surface p-5 flex items-start gap-4 hover:border-primary/30 transition-colors">
                <div className="w-10 h-10 rounded-xl bg-primary/10 flex items-center justify-center shrink-0">
                  <section.icon className="w-5 h-5 text-primary" />
                </div>
                <div>
                  <h3 className="text-base font-bold text-navy">{section.title}</h3>
                  <p className="text-sm text-muted mt-1">{section.description}</p>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader
          eyebrow="Scoring & Strategy"
          title="What makes IELTS different"
          description="Key differences from OET to keep in mind while you prepare."
        />

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
          {SCORING_NOTES.map((note, index) => (
            <MotionItem key={note.title} delayIndex={index}>
              <div className="rounded-2xl border border-border bg-background-light p-5">
                <div className="flex items-center gap-2 mb-2">
                  <note.icon className="w-4 h-4 text-primary" />
                  <h3 className="text-sm font-bold text-navy">{note.title}</h3>
                </div>
                <p className="text-sm text-muted leading-relaxed">{note.description}</p>
              </div>
            </MotionItem>
          ))}
        </div>

        <div className="rounded-2xl border border-info/30 bg-info/10 p-6">
          <h3 className="text-sm font-bold text-info mb-2">Coming soon</h3>
          <p className="text-sm text-info leading-relaxed">
            IELTS-specific mock exam format, task-aware reporting, and Academic/General Writing rubrics are actively being expanded. For now, use the shared practice engine and set your pathway in Goals to get started.
          </p>
        </div>
      </main>
    </LearnerDashboardShell>
  );
}
