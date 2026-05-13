'use client';

// Listening V2 — pre-audio preview banner + post-audio review banner.
// Both follow the same visual shape (timer chip + label + CTA), so they
// are co-located here. Extracted from the monolithic player so each
// phase can be Storybook'd and unit-tested without booting the FSM.

import { ChevronRight, Play, Timer } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  LISTENING_PREVIEW_LABEL,
  LISTENING_SECTION_LABEL,
  formatReviewSeconds,
  type ListeningSectionCode,
} from '@/lib/listening-sections';

export interface ListeningPreviewBannerProps {
  section: ListeningSectionCode;
  secondsRemaining: number;
  canSkip: boolean;
  onSkip: () => void;
}

export function ListeningPreviewBanner({
  section,
  secondsRemaining,
  canSkip,
  onSkip,
}: ListeningPreviewBannerProps) {
  return (
    <div
      data-testid="listening-preview-banner"
      className="flex flex-col gap-3 rounded-2xl border-2 border-info/30 bg-info/10 p-5 sm:flex-row sm:items-center sm:justify-between"
    >
      <div className="flex items-start gap-3">
        <Timer className="mt-0.5 h-5 w-5 shrink-0 text-info" />
        <div>
          <p className="text-sm font-black text-info">
            {LISTENING_PREVIEW_LABEL} — {secondsRemaining} seconds left
          </p>
          <p className="mt-0.5 text-xs text-info">
            {LISTENING_SECTION_LABEL[section]} — read the questions and mark answers in advance.
            Audio will start automatically when the timer hits zero.
          </p>
        </div>
      </div>
      <div className="flex items-center gap-3">
        <span className="rounded-xl bg-info/20 px-3 py-2 font-mono text-lg font-black text-info">
          {formatReviewSeconds(secondsRemaining)}
        </span>
        {canSkip ? (
          <Button size="sm" variant="outline" onClick={onSkip} className="gap-1">
            <Play className="h-4 w-4" /> Start audio
          </Button>
        ) : null}
      </div>
    </div>
  );
}

export interface ListeningReviewBannerProps {
  section: ListeningSectionCode;
  secondsRemaining: number;
  isLastSection: boolean;
  onNext: () => void;
}

export function ListeningReviewBanner({
  section,
  secondsRemaining,
  isLastSection,
  onNext,
}: ListeningReviewBannerProps) {
  return (
    <div
      data-testid="listening-review-banner"
      className="flex flex-col gap-3 rounded-2xl border-2 border-warning/30 bg-warning/10 p-5 sm:flex-row sm:items-center sm:justify-between"
    >
      <div className="flex items-start gap-3">
        <Timer className="mt-0.5 h-5 w-5 shrink-0 text-warning" />
        <div>
          <p className="text-sm font-black text-warning">
            {LISTENING_SECTION_LABEL[section]} — review window
          </p>
          <p className="mt-0.5 text-xs text-warning">
            You can finish completing any words you abbreviated. Answers for this section remain
            fully editable until the timer hits zero or you press Next.
          </p>
        </div>
      </div>
      <div className="flex items-center gap-3">
        <span className="rounded-xl bg-warning/20 px-3 py-2 font-mono text-lg font-black text-warning">
          {formatReviewSeconds(secondsRemaining)}
        </span>
        <Button size="sm" onClick={onNext} className="gap-1">
          {isLastSection ? 'Finish & Submit' : 'Next'} <ChevronRight className="h-4 w-4" />
        </Button>
      </div>
    </div>
  );
}
