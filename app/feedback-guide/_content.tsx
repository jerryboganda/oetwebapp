'use client';

import { useEffect } from 'react';
import { HelpCircle, CheckCircle2, TrendingUp, Target } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { analytics } from '@/lib/analytics';

export interface RubricCriterion {
  code: string;
  label: string;
  bands: string;
  description: string;
  improve: string;
}

interface FeedbackGuideContentProps {
  writingCriteria: RubricCriterion[];
  speakingCriteria: RubricCriterion[];
}

export default function FeedbackGuideContent({ writingCriteria, speakingCriteria }: FeedbackGuideContentProps) {
  useEffect(() => { analytics.track('feedback_guide_viewed'); }, []);

  const heroHighlights = [
    { icon: CheckCircle2, label: 'Writing criteria', value: `${writingCriteria.length}` },
    { icon: Target, label: 'Speaking criteria', value: `${speakingCriteria.length}` },
    { icon: TrendingUp, label: 'Score bands', value: '0–7 / 0–6 / 0–3' },
  ];

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="Feedback Interpretation Guide"
        description="Understand what each criterion score means and how to improve."
        icon={HelpCircle}
        highlights={heroHighlights}
      />

      <MotionSection className="space-y-6">
        <LearnerSurfaceSectionHeader
          eyebrow="Writing criteria"
          title="How writing feedback is scored"
          description="The writing rubric should feel like an extension of the dashboard: clear, calm, and easy to scan."
        />
        <div className="space-y-3">
          {writingCriteria.map(c => (
            <MotionItem key={c.code}>
              <Card className="p-5 shadow-sm">
                <div className="flex items-center justify-between mb-2"><h3 className="font-semibold">{c.label}</h3><Badge variant="outline">Bands {c.bands}</Badge></div>
                <p className="text-sm text-muted-foreground">{c.description}</p>
                <div className="mt-3 bg-success/10 rounded-lg p-3">
                  <p className="text-sm"><TrendingUp className="w-4 h-4 inline mr-1 text-success" /><strong>How to improve:</strong> {c.improve}</p>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader
          eyebrow="Speaking criteria"
          title="How speaking feedback is scored"
          description="Speaking feedback should feel like a guided review, not a separate design language."
        />
        <div className="space-y-3">
          {speakingCriteria.map(c => (
            <MotionItem key={c.code}>
              <Card className="p-5 shadow-sm">
                <div className="flex items-center justify-between mb-2"><h3 className="font-semibold">{c.label}</h3><Badge variant="outline">Bands {c.bands}</Badge></div>
                <p className="text-sm text-muted-foreground">{c.description}</p>
                <div className="mt-3 bg-success/10 rounded-lg p-3">
                  <p className="text-sm"><TrendingUp className="w-4 h-4 inline mr-1 text-success" /><strong>How to improve:</strong> {c.improve}</p>
                </div>
              </Card>
            </MotionItem>
          ))}
        </div>

        <LearnerSurfaceSectionHeader
          eyebrow="Score guide"
          title="How to read the bands"
          description="Keep the interpretation simple so learners can act on the score immediately."
        />
        <Card className="p-5 space-y-3 shadow-sm">
          <div className="flex items-start gap-2"><CheckCircle2 className="w-5 h-5 text-success flex-shrink-0 mt-0.5" /><p className="text-sm"><strong>Score 5-7 (Writing) / 5-6 (Speaking):</strong> Strong performance. Focus on consistency and refinement.</p></div>
          <div className="flex items-start gap-2"><Target className="w-5 h-5 text-warning flex-shrink-0 mt-0.5" /><p className="text-sm"><strong>Score 3-4:</strong> Adequate but needs improvement. Target specific criteria with focused practice.</p></div>
          <div className="flex items-start gap-2"><HelpCircle className="w-5 h-5 text-danger flex-shrink-0 mt-0.5" /><p className="text-sm"><strong>Score 0-2:</strong> Significant gaps. Start with foundation resources and work with an expert reviewer.</p></div>
        </Card>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
