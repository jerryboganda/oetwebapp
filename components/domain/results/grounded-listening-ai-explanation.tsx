'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  getListeningAttemptAiExplanation,
  type ListeningGroundedAiExplanationDto,
} from '@/lib/listening-api';

export function GroundedListeningAiExplanation({
  attemptId,
  questionId,
  unanswered,
}: {
  attemptId: string;
  questionId: string;
  unanswered: boolean;
}) {
  const [result, setResult] = useState<ListeningGroundedAiExplanationDto | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  if (unanswered) return null;

  const loadExplanation = async () => {
    setLoading(true);
    setError(null);
    try {
      setResult(await getListeningAttemptAiExplanation(attemptId, questionId));
    } catch (err) {
      setError(readErrorMessage(err, 'A grounded explanation is not available for this question yet.'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="mt-4 rounded-xl border border-indigo-200 bg-indigo-50/70 p-4 dark:border-indigo-400/30 dark:bg-indigo-950/20" data-testid="listening-grounded-ai">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <p className="text-[11px] font-black uppercase tracking-[0.14em] text-indigo-700 dark:text-indigo-300">Grounded AI explanation</p>
          <p className="mt-1 text-xs text-muted">Advisory only; it cannot change your marks.</p>
        </div>
        {!result ? (
          <Button type="button" size="sm" variant="outline" onClick={() => void loadExplanation()} disabled={loading}>
            {loading ? 'Preparing…' : 'Explain this answer'}
          </Button>
        ) : null}
      </div>
      {error ? <p className="mt-3 text-sm font-semibold text-danger" role="alert">{error}</p> : null}
      {result ? (
        <div className="mt-4 space-y-3 text-sm leading-6 text-navy dark:text-white/90">
          <p><strong>Why the correct answer fits:</strong> {result.explanation.whyCorrect}</p>
          <p><strong>Why your answer was a trap:</strong> {result.explanation.whyWrong}</p>
          <p><strong>Trap:</strong> {result.explanation.trapName}</p>
          <p><strong>Next time:</strong> {result.explanation.avoidTip}</p>
        </div>
      ) : null}
    </div>
  );
}
