'use client';

/**
 * Learner AI Speaking result layout: the AI assessment column and the practice-estimate disclaimer.
 *
 * Owner spec 4 Oct 2026: once the AI assessment exists it stands on its own. There is no Tutor Assessment
 * panel, no divergence banner and no tutor-review prompt here — human review is a separate Book a Tutor
 * flow, not a second panel beside the AI score. (The file keeps its historical name; the tutor console
 * renders `DualAssessmentColumn` directly.)
 */

import { ShieldAlert } from 'lucide-react';
import { type ReactNode } from 'react';

import { Card } from '@/components/ui/card';
import type { DualAssessmentResponse } from '@/lib/api/speaking-assessments';

import { DualAssessmentColumn } from './DualAssessmentColumn';

export interface DualAssessmentLayoutProps {
  data: DualAssessmentResponse;
  /** Shown in the AI column while the assessment is still being produced. */
  aiPlaceholderCta?: ReactNode;
  showFullCriteria?: boolean;
  showReadinessBand?: boolean;
}

export function DualAssessmentLayout({
  data,
  aiPlaceholderCta,
  showFullCriteria = true,
  showReadinessBand = true,
}: DualAssessmentLayoutProps) {
  const { ai } = data;

  return (
    <div className="flex flex-col gap-4" data-testid="dual-assessment-layout">
      <DualAssessmentColumn
        kind="ai"
        title="AI Assessment"
        assessment={ai}
        showFullCriteria={showFullCriteria}
        showReadinessBand={showReadinessBand}
        attribution={ai ? { provider: ai.provider, modelId: ai.modelId, submittedAt: ai.generatedAt } : undefined}
        placeholderCta={aiPlaceholderCta}
      />

      {/* Universal advisory disclaimer */}
      <Card
        padding="md"
        className="flex items-start gap-3 border border-warning/30 bg-warning/10 text-sm text-navy"
        role="note"
        aria-label="Speaking assessment advisory"
      >
        <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-warning-strong" aria-hidden />
        <p className="font-bold">This AI practice estimate is not an official OET score.</p>
      </Card>
    </div>
  );
}

export default DualAssessmentLayout;
