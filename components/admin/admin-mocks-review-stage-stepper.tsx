'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Check, ChevronRight, History, Loader2 } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import {
  advanceMockBundleReviewStage,
  fetchMockBundleReviewStage,
  type MockBundleReviewStageEntry,
  type MockBundleReviewStageSummary,
} from '@/lib/api';

// Phase 3 multi-stage content review pipeline. The backend is the source of
// truth on legal transitions; the UI only enforces the canonical happy-path
// order so the "Advance to next stage" button knows what to send.
export const MOCK_REVIEW_STAGES = [
  'academic',
  'medical',
  'language',
  'technical',
  'pilot',
  'published',
] as const;

export type MockReviewStage = (typeof MOCK_REVIEW_STAGES)[number];

const STAGE_LABELS: Record<MockReviewStage, string> = {
  academic: 'Academic',
  medical: 'Medical',
  language: 'Language',
  technical: 'Technical',
  pilot: 'Pilot',
  published: 'Published',
};

const STAGE_DESCRIPTIONS: Record<MockReviewStage, string> = {
  academic: 'Subject-matter accuracy and pedagogical fit.',
  medical: 'Clinical realism and patient-perspective review.',
  language: 'Language quality, register, and OET style.',
  technical: 'Asset wiring, audio integrity, item analysis flags.',
  pilot: 'Pilot run with a small learner cohort to surface live issues.',
  published: 'Released to learners through the catalogue.',
};

interface StageHistoryRowProps {
  entry: MockBundleReviewStageEntry;
  isCurrent: boolean;
}

function StageHistoryRow({ entry, isCurrent }: StageHistoryRowProps) {
  const stageKey = entry.stage as MockReviewStage;
  const label = STAGE_LABELS[stageKey] ?? entry.stage;
  const status = entry.resolvedAt ? 'approved' : 'pending';
  return (
    <li className="rounded-xl border border-border bg-background-light p-3">
      <div className="flex flex-wrap items-center gap-2">
        <p className="font-semibold text-navy">{label}</p>
        <Badge variant={isCurrent ? 'info' : status === 'approved' ? 'success' : 'muted'} className="text-[10px] capitalize">
          {status}
        </Badge>
        {entry.resolvedAt ? (
          <span className="text-xs text-muted">
            {new Date(entry.resolvedAt).toLocaleString()}
          </span>
        ) : null}
        {entry.resolvedByAdminId ? (
          <span className="text-xs text-muted">by {entry.resolvedByAdminId}</span>
        ) : null}
      </div>
      {entry.notes ? (
        <p className="mt-1 text-sm text-muted whitespace-pre-line">{entry.notes}</p>
      ) : null}
    </li>
  );
}

export interface AdminMocksReviewStageStepperProps {
  bundleId: string;
}

export function AdminMocksReviewStageStepper({ bundleId }: AdminMocksReviewStageStepperProps) {
  const [summary, setSummary] = useState<MockBundleReviewStageSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [advancing, setAdvancing] = useState(false);
  const [notes, setNotes] = useState('');
  const [notice, setNotice] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const next = await fetchMockBundleReviewStage(bundleId);
      setSummary(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load review stage summary.');
    } finally {
      setLoading(false);
    }
  }, [bundleId]);

  useEffect(() => {
    void load();
  }, [load]);

  const currentStage = summary?.currentStage ?? '';
  const currentIndex = useMemo(() => {
    if (!currentStage) return -1;
    return MOCK_REVIEW_STAGES.findIndex((s) => s === currentStage);
  }, [currentStage]);

  const nextStage = currentIndex >= 0 && currentIndex < MOCK_REVIEW_STAGES.length - 1
    ? MOCK_REVIEW_STAGES[currentIndex + 1]
    : null;

  // Build a lookup of the most recent history entry per stage so we can render
  // reviewed-at / reviewed-by metadata on each pill.
  const latestByStage = useMemo(() => {
    const map = new Map<string, MockBundleReviewStageEntry>();
    for (const entry of summary?.transitions ?? []) {
      map.set(entry.stage, entry);
    }
    return map;
  }, [summary]);

  const handleAdvance = useCallback(async () => {
    if (!nextStage || advancing) return;
    setAdvancing(true);
    setNotice(null);
    setError(null);
    try {
      await advanceMockBundleReviewStage(bundleId, { targetStage: nextStage, notes: notes.trim() ? notes.trim() : undefined });
      setNotes('');
      setNotice(`Advanced to ${STAGE_LABELS[nextStage]}.`);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to advance review stage.');
    } finally {
      setAdvancing(false);
    }
  }, [bundleId, nextStage, notes, advancing, load]);

  return (
    <section className="space-y-4 rounded-2xl border border-border bg-surface p-4">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Multi-stage content review</h2>
        <p className="text-sm text-muted">
          Each stage gates the next. Approvals are recorded in the bundle&apos;s review history; only the
          final &apos;Published&apos; stage releases the bundle to learners.
        </p>
      </header>

      {error ? (
        <InlineAlert variant="error" title="Could not update review stage">
          {error}
        </InlineAlert>
      ) : null}

      {notice ? (
        <InlineAlert variant="success">{notice}</InlineAlert>
      ) : null}

      {loading ? (
        <p className="inline-flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" /> Loading review stages…
        </p>
      ) : (
        <ol className="flex flex-wrap items-center gap-2" aria-label="Review stages">
          {MOCK_REVIEW_STAGES.map((stage, idx) => {
            const isCurrent = stage === currentStage;
            const isComplete = currentIndex >= 0 && idx < currentIndex;
            const entry = latestByStage.get(stage);
            return (
              <li key={stage} className="flex items-center gap-2">
                <div
                  aria-current={isCurrent ? 'step' : undefined}
                  title={STAGE_DESCRIPTIONS[stage]}
                  className={
                    'inline-flex items-center gap-2 rounded-xl px-3 py-1.5 text-xs font-bold transition-colors ' +
                    (isCurrent
                      ? 'bg-primary text-white'
                      : isComplete
                        ? 'bg-emerald-100 text-emerald-700'
                        : 'bg-background-light text-navy')
                  }
                >
                  <span
                    className={
                      'inline-flex h-5 w-5 items-center justify-center rounded-full text-[11px] ' +
                      (isCurrent ? 'bg-white/30' : isComplete ? 'bg-emerald-200' : 'bg-white/60')
                    }
                  >
                    {isComplete ? <Check className="h-3 w-3" aria-hidden /> : idx + 1}
                  </span>
                  {STAGE_LABELS[stage]}
                  {entry?.reviewedAt ? (
                    <span className="ml-1 text-[10px] opacity-80">
                      {new Date(entry.reviewedAt).toLocaleDateString()}
                    </span>
                  ) : null}
                </div>
                {idx < MOCK_REVIEW_STAGES.length - 1 ? (
                  <ChevronRight aria-hidden className="h-3 w-3 text-muted" />
                ) : null}
              </li>
            );
          })}
        </ol>
      )}

      {!loading && summary && currentStage === '' ? (
        <p className="text-sm text-muted">
          This bundle has not entered the multi-stage review yet. Once a backend review-stage record exists,
          the stepper will reflect its current state.
        </p>
      ) : null}

      {!loading && nextStage ? (
        <div className="space-y-2 rounded-xl border border-border bg-background-light p-3">
          <label htmlFor="review-stage-notes" className="text-xs font-bold uppercase tracking-[0.14em] text-muted">
            Notes for this transition (optional)
          </label>
          <textarea
            id="review-stage-notes"
            value={notes}
            onChange={(event) => setNotes(event.target.value)}
            rows={2}
            placeholder={`What did the ${STAGE_LABELS[(MOCK_REVIEW_STAGES[currentIndex] ?? 'academic') as MockReviewStage]} reviewer find?`}
            className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy placeholder:text-muted focus:border-primary focus:outline-none"
          />
          <div className="flex items-center justify-between gap-2">
            <p className="text-xs text-muted">
              Next: <span className="font-semibold text-navy">{STAGE_LABELS[nextStage]}</span> — {STAGE_DESCRIPTIONS[nextStage]}
            </p>
            <Button
              variant="primary"
              onClick={() => void handleAdvance()}
              disabled={advancing}
            >
              {advancing ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <ChevronRight className="mr-1 h-4 w-4" />}
              Advance to {STAGE_LABELS[nextStage]}
            </Button>
          </div>
        </div>
      ) : null}

      {!loading && !nextStage && currentStage ? (
        <p className="rounded-xl border border-emerald-200 bg-emerald-50 px-3 py-2 text-sm text-emerald-800">
          Final stage reached. No further review advances are required.
        </p>
      ) : null}

      <section className="space-y-2">
        <h3 className="inline-flex items-center gap-2 text-sm font-bold text-navy">
          <History className="h-4 w-4" /> Stage history
        </h3>
        {(summary?.stages ?? []).length === 0 ? (
          <p className="text-sm text-muted">No review activity recorded yet.</p>
        ) : (
          <ul className="space-y-2">
            {(summary?.stages ?? []).map((entry, idx) => (
              <StageHistoryRow
                key={`${entry.stage}-${idx}`}
                entry={entry}
                isCurrent={entry.stage === currentStage}
              />
            ))}
          </ul>
        )}
      </section>
    </section>
  );
}
