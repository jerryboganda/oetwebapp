'use client';

/**
 * Dual-scoring layout: AI + Tutor columns side-by-side, with a top divergence
 * banner (when both are present) and a bottom advisory disclaimer card.
 *
 * Responsive: columns stack on mobile, align top on desktop.
 */

import { ArrowDownRight, ArrowUpRight, Equal, ShieldAlert } from 'lucide-react';
import { type ReactNode } from 'react';

import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import {
  CRITERION_LABEL,
  agreementBandTone,
  type DualAssessmentResponse,
  type SpeakingCriterionCode,
} from '@/lib/api/speaking-assessments';
import { cn } from '@/lib/utils';

import { DualAssessmentColumn } from './DualAssessmentColumn';

export interface DualAssessmentLayoutProps {
  data: DualAssessmentResponse;
  /** Optional CTAs for null states (e.g. "Request tutor review"). */
  tutorPlaceholderCta?: ReactNode;
  aiPlaceholderCta?: ReactNode;
}

function pickLargestDelta(
  perCriterion: Record<string, number>,
): { code: SpeakingCriterionCode; delta: number } | null {
  let best: { code: SpeakingCriterionCode; delta: number } | null = null;
  for (const [code, delta] of Object.entries(perCriterion)) {
    if (!best || Math.abs(delta) > Math.abs(best.delta)) {
      best = { code: code as SpeakingCriterionCode, delta };
    }
  }
  return best && best.delta !== 0 ? best : null;
}

function DivergenceBanner({
  divergence,
}: {
  divergence: NonNullable<DualAssessmentResponse['divergence']>;
}) {
  const tone = agreementBandTone(divergence.agreementBand);
  const topDelta = pickLargestDelta(divergence.perCriterion);
  const toneClasses = {
    success: 'border-emerald-200/60 bg-emerald-50/60 text-emerald-900 dark:border-emerald-800/40 dark:bg-emerald-950/30 dark:text-emerald-200',
    warning: 'border-amber-200/60 bg-amber-50/60 text-amber-900 dark:border-amber-800/40 dark:bg-amber-950/30 dark:text-amber-200',
    danger: 'border-red-200/60 bg-red-50/60 text-red-900 dark:border-red-800/40 dark:bg-red-950/30 dark:text-red-200',
  }[tone];

  let directionIcon: ReactNode = <Equal className="h-4 w-4" aria-hidden />;
  if (topDelta) {
    directionIcon =
      topDelta.delta > 0 ? (
        <ArrowUpRight className="h-4 w-4" aria-hidden />
      ) : (
        <ArrowDownRight className="h-4 w-4" aria-hidden />
      );
  }

  const bandLabel = {
    close: 'close agreement',
    moderate: 'moderate agreement',
    wide: 'wide divergence',
  }[divergence.agreementBand];

  const headline = topDelta
    ? `Tutor scored ${topDelta.delta > 0 ? 'higher' : 'lower'} on ${CRITERION_LABEL[topDelta.code]} (${topDelta.delta > 0 ? '+' : ''}${topDelta.delta})`
    : 'AI and tutor agreed across all criteria';

  const scaledDeltaPart =
    divergence.scaledDelta !== 0
      ? ` · Scaled-score delta: ${divergence.scaledDelta > 0 ? '+' : ''}${Math.round(divergence.scaledDelta)}`
      : '';

  return (
    <Card
      padding="md"
      className={cn('flex flex-wrap items-center gap-3 border', toneClasses)}
      aria-label="AI vs tutor divergence summary"
      data-testid="divergence-banner"
    >
      <span className="inline-flex h-8 w-8 items-center justify-center rounded-full bg-white/60 dark:bg-black/20">
        {directionIcon}
      </span>
      <div className="flex flex-1 flex-col gap-0.5">
        <p className="text-sm font-bold">{headline}</p>
        <p className="text-xs">Agreement band: {bandLabel}{scaledDeltaPart}</p>
      </div>
      <Badge variant={tone === 'success' ? 'success' : tone === 'warning' ? 'warning' : 'danger'}>
        {divergence.agreementBand}
      </Badge>
    </Card>
  );
}

export function DualAssessmentLayout({
  data,
  tutorPlaceholderCta,
  aiPlaceholderCta,
}: DualAssessmentLayoutProps) {
  const { ai, tutor, divergence } = data;
  const bothPresent = !!ai && !!tutor;

  return (
    <div className="flex flex-col gap-4" data-testid="dual-assessment-layout">
      {bothPresent && divergence && <DivergenceBanner divergence={divergence} />}

      <div className="grid gap-4 md:grid-cols-2">
        <DualAssessmentColumn
          kind="ai"
          title="AI Assessment"
          assessment={ai}
          attribution={
            ai
              ? {
                  provider: ai.provider,
                  modelId: ai.modelId,
                  submittedAt: ai.generatedAt,
                }
              : undefined
          }
          placeholderCta={aiPlaceholderCta}
        />
        <DualAssessmentColumn
          kind="tutor"
          title="Tutor Assessment"
          assessment={tutor}
          attribution={
            tutor
              ? {
                  name: tutor.tutorName,
                  photoUrl: tutor.tutorPhotoUrl,
                  submittedAt: tutor.submittedAt,
                }
              : undefined
          }
          placeholderCta={tutorPlaceholderCta}
        />
      </div>

      {/* Universal advisory disclaimer */}
      <Card
        padding="md"
        className="flex items-start gap-3 border border-warning/30 bg-warning/10 text-sm text-navy"
        role="note"
        aria-label="Speaking assessment advisory"
      >
        <ShieldAlert className="mt-0.5 h-5 w-5 shrink-0 text-warning" aria-hidden />
        <div>
          <p className="font-bold">Both estimates are advisory — not an official OET score.</p>
          <p className="mt-0.5 text-xs leading-relaxed text-muted">
            Use this dual view to triangulate your readiness. Official OET results can only be obtained from an OET test session.
          </p>
        </div>
      </Card>
    </div>
  );
}

export default DualAssessmentLayout;
