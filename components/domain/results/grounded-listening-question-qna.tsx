'use client';

import { AiHelpTooltip } from '@/components/ui/ai-help-tooltip';

export function GroundedListeningQuestionQna({
  attemptId,
  questionId,
}: {
  attemptId: string;
  questionId: string;
}) {
  return (
    <div
      className="mt-4 rounded-xl border border-sky-200 bg-sky-50/70 p-4 dark:border-sky-400/30 dark:bg-sky-950/20"
      data-testid="listening-grounded-qna"
    >
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-[11px] font-black uppercase tracking-[0.14em] text-sky-700 dark:text-sky-300">
            AI listening helper
          </p>
          <p className="mt-1 text-xs text-muted">
            Understand why the transcript supports an answer. Advisory only; marks are unaffected.
          </p>
        </div>
        <AiHelpTooltip variant="listening" />
      </div>
    </div>
  );
}
