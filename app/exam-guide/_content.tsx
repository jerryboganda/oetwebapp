'use client';

import type { ReactNode } from 'react';
import { useEffect } from 'react';
import { Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { analytics } from '@/lib/analytics';

export interface ExamSection {
  icon: ReactNode;
  title: string;
  duration: string;
  parts: string;
  scoring: string;
  tips: string[];
}

export interface ScoringGuideEntry {
  grade: string;
  range: string;
  level: string;
  description: string;
}

interface ExamGuideContentProps {
  examSections: ExamSection[];
  scoringGuide: ScoringGuideEntry[];
}

export default function ExamGuideContent({ examSections, scoringGuide }: ExamGuideContentProps) {
  useEffect(() => { analytics.track('exam_guide_viewed'); }, []);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="OET Exam Guide" description="Everything you need to know about the OET exam format, timing, scoring, and strategies." />

      <MotionSection className="space-y-8 max-w-4xl mx-auto">
        <LearnerSurfaceSectionHeader title="Exam Structure" />
        <div className="space-y-4">
          {examSections.map(section => (
            <MotionItem key={section.title}>
              <Card className="p-5">
                <div className="flex items-start gap-4">
                  {section.icon}
                  <div className="flex-1">
                    <div className="flex items-center justify-between"><h3 className="text-lg font-semibold">{section.title}</h3><Badge variant="outline"><Clock className="w-3 h-3 inline mr-1" />{section.duration}</Badge></div>
                    <p className="text-sm text-muted-foreground mt-1">{section.parts}</p>
                    <p className="text-sm text-muted-foreground">Scoring: {section.scoring}</p>
                    <div className="mt-3 space-y-1">{section.tips.map((tip, i) => <p key={i} className="text-sm">• {tip}</p>)}</div>
                  </div>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader title="Scoring & Grades" />
        <div className="grid grid-cols-1 md:grid-cols-2 gap-3">
          {scoringGuide.map(s => (
            <MotionItem key={s.grade}>
              <Card className="p-4 flex items-center gap-3">
                <div className="w-12 h-12 rounded-full bg-primary/10 flex items-center justify-center font-bold text-primary">{s.grade}</div>
                <div><p className="font-semibold">{s.level} <span className="text-muted-foreground font-normal text-sm">({s.range})</span></p><p className="text-sm text-muted-foreground">{s.description}</p></div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader title="Key Facts" />
        <Card className="p-5 space-y-2">
          <p className="text-sm">• <strong>12 healthcare professions</strong> supported: Medicine, Nursing, Dentistry, Pharmacy, Physiotherapy, and more</p>
          <p className="text-sm">• <strong>Delivery modes</strong>: Paper-based, Computer-based, OET@Home</p>
          <p className="text-sm">• <strong>Results</strong>: 6 business days (computer/home), 12 days (paper)</p>
          <p className="text-sm">• <strong>Validity</strong>: Results valid for 2 years</p>
          <p className="text-sm">• <strong>Required score</strong>: Most regulatory bodies require minimum B (350+) in all subtests</p>
        </Card>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
