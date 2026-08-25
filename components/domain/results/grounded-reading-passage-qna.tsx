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
      className="mt-4 rounded-xl border border-sky-200 bg-sky-50/70 p-4 dark:border-sky-400/30 dark:bg-sky-950/20"
      data-testid="reading-grounded-qna"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-black uppercase tracking-[0.14em] text-sky-700 dark:text-sky-300">
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
