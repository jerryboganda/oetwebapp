'use client';

import { AiHelpTooltip } from '@/components/ui/ai-help-tooltip';

export function GroundedReadingPassageQna({
  attemptId,
  passageId,
}: {
  attemptId: string;
  passageId: string;
}) {
  return (
    <div
      className="mt-4 rounded-xl border border-skill-reading/20 bg-skill-reading/10 p-4"
      data-testid="reading-grounded-qna"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="eyebrow text-skill-reading">
            AI reading helper
          </p>
          <p className="mt-1 text-xs text-muted">
            Clarify the passage, its structure, or the evidence behind an answer. Advisory only; marks are unaffected.
          </p>
        </div>
        <AiHelpTooltip variant="reading" />
      </div>
    </div>
  );
}
