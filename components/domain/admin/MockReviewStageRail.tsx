'use client';

/**
 * Mocks Module Phase 6 — admin Quality-Control review pipeline rail.
 *
 * Vertical timeline rendering every canonical editorial stage
 * (`academic → medical → language → technical → pilot → published`) with the
 * current stage highlighted and per-transition reviewer + notes visible on
 * past rows. Includes an "Advance stage" form that POSTs the next allowed
 * transition; stages already passed are disabled in the dropdown to mirror
 * the backend's monotonic invariant in `MockBundleReviewStageService`.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, Circle, ChevronRight, Loader2, RefreshCcw } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Select, Textarea } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import {
  MOCK_REVIEW_STAGES,
  advanceMockBundleReviewStage,
  fetchMockBundleReviewStage,
  type MockBundleReviewStageEntry,
  type MockBundleReviewStageSummary,
  type MockReviewStage,
} from '@/lib/api';
import { cn } from '@/lib/utils';

interface MockReviewStageRailProps {
  bundleId: string;
}

const STAGE_LABELS: Record<MockReviewStage, string> = {
  academic: 'Academic review',
  medical: 'Medical / clinical review',
  language: 'Language & register review',
  technical: 'Technical / accessibility',
  pilot: 'Pilot cohort',
  published: 'Published',
};

const STAGE_DESCRIPTIONS: Record<MockReviewStage, string> = {
  academic: 'Subject-matter accuracy and exam alignment',
  medical: 'Clinical safety, terminology, and scenario realism',
  language: 'CEFR / OET wording, register, and tone',
  technical: 'Audio fidelity, transcripts, and accessibility checks',
  pilot: 'Limited-cohort live-fire calibration window',
  published: 'Globally available to learners',
};

function stageLabel(stage: string): string {
  if ((MOCK_REVIEW_STAGES as readonly string[]).includes(stage)) {
    return STAGE_LABELS[stage as MockReviewStage];
  }
  return stage;
}

function stageIndex(stage: string | null | undefined): number {
  if (!stage) return -1;
  return (MOCK_REVIEW_STAGES as readonly string[]).indexOf(stage);
}

function StageTimeline({
  stages,
  currentStage,
  transitionsByStage,
}: {
  stages: readonly MockReviewStage[];
  currentStage: string | null;
  transitionsByStage: Map<string, MockBundleReviewStageEntry>;
}) {
  const currentIdx = stageIndex(currentStage);

  return (
    <ol className="relative space-y-3">
      {stages.map((stage, idx) => {
        const isCurrent = idx === currentIdx;
        const isPast = idx < currentIdx;
        const transition = transitionsByStage.get(stage);

        return (
          <li
            key={stage}
            className={cn(
              'relative flex gap-3 rounded-2xl border bg-surface p-4 transition-colors',
              isCurrent
                ? 'border-primary bg-primary/5 shadow-sm'
                : isPast
                ? 'border-emerald-200/60 bg-emerald-50/40 dark:border-emerald-800/60 dark:bg-emerald-950/40'
                : 'border-border',
            )}
            data-stage={stage}
            data-current={isCurrent ? 'true' : undefined}
          >
            <div className="flex h-9 w-9 shrink-0 items-center justify-center">
              {isPast ? (
                <CheckCircle2 className="h-7 w-7 text-emerald-600 dark:text-emerald-400" aria-hidden />
              ) : isCurrent ? (
                <div className="flex h-7 w-7 items-center justify-center rounded-full bg-primary text-white">
                  <ChevronRight className="h-4 w-4" aria-hidden />
                </div>
              ) : (
                <Circle className="h-7 w-7 text-muted" aria-hidden />
              )}
            </div>
            <div className="min-w-0 flex-1">
              <div className="flex flex-wrap items-center gap-2">
                <p className="font-bold text-navy">{STAGE_LABELS[stage]}</p>
                {isCurrent ? <Badge variant="info">Current</Badge> : null}
                {isPast ? <Badge variant="success">Passed</Badge> : null}
              </div>
              <p className="mt-1 text-xs text-muted">{STAGE_DESCRIPTIONS[stage]}</p>
              {transition ? (
                <dl className="mt-3 grid gap-1 rounded-xl border border-border/70 bg-background-light/80 p-3 text-xs">
                  <div className="flex flex-wrap gap-x-3 gap-y-1">
                    <dt className="font-semibold text-muted">Transitioned at</dt>
                    <dd className="text-navy">{new Date(transition.createdAt).toLocaleString()}</dd>
                  </div>
                  {transition.resolvedByAdminId ? (
                    <div className="flex flex-wrap gap-x-3 gap-y-1">
                      <dt className="font-semibold text-muted">Reviewer</dt>
                      <dd className="text-navy">{transition.resolvedByAdminId}</dd>
                    </div>
                  ) : null}
                  {transition.notes ? (
                    <div className="flex flex-col gap-1">
                      <dt className="font-semibold text-muted">Notes</dt>
                      <dd className="whitespace-pre-wrap text-navy">{transition.notes}</dd>
                    </div>
                  ) : null}
                </dl>
              ) : null}
            </div>
          </li>
        );
      })}
    </ol>
  );
}

function AdvanceStageForm({
  bundleId,
  currentStage,
  isPublished,
  onAdvanced,
}: {
  bundleId: string;
  currentStage: string | null;
  isPublished: boolean;
  onAdvanced: (summary: MockBundleReviewStageSummary) => void;
}) {
  const currentIdx = stageIndex(currentStage);

  const stageOptions = useMemo(() => {
    return MOCK_REVIEW_STAGES.map((stage, idx) => ({
      value: stage,
      label: STAGE_LABELS[stage],
      // Past + current stages are disabled — backend enforces monotonic.
      disabled: idx <= currentIdx,
    }));
  }, [currentIdx]);

  const firstSelectable = stageOptions.find((opt) => !opt.disabled)?.value ?? '';

  const [targetStage, setTargetStage] = useState<string>(firstSelectable);
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [successAt, setSuccessAt] = useState<string | null>(null);

  useEffect(() => {
    // Reset the default-selection when the underlying current stage moves
    // forward (e.g. immediately after a successful advance).
    setTargetStage(firstSelectable);
  }, [firstSelectable]);

  const allDone = isPublished || stageOptions.every((opt) => opt.disabled);

  const submit = useCallback(async () => {
    if (!targetStage) return;
    setSubmitting(true);
    setError(null);
    setSuccessAt(null);
    try {
      const summary = await advanceMockBundleReviewStage(bundleId, {
        targetStage,
        notes: notes.trim() || undefined,
      });
      onAdvanced(summary);
      setNotes('');
      setSuccessAt(new Date().toISOString());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to advance stage.');
    } finally {
      setSubmitting(false);
    }
  }, [bundleId, notes, onAdvanced, targetStage]);

  if (allDone) {
    return (
      <div className="rounded-2xl border border-emerald-200/60 bg-emerald-50/40 p-4 text-sm text-emerald-700 dark:border-emerald-800/60 dark:bg-emerald-950/30 dark:text-emerald-300">
        Bundle is already published. No further stage transitions are possible.
      </div>
    );
  }

  return (
    <form
      className="space-y-3 rounded-2xl border border-border bg-surface p-4"
      onSubmit={(event) => {
        event.preventDefault();
        void submit();
      }}
    >
      <div className="flex flex-col gap-3 md:flex-row md:items-end">
        <div className="flex-1">
          <Select
            label="Target stage"
            options={stageOptions}
            value={targetStage}
            onChange={(event) => setTargetStage(event.target.value)}
            hint="Stages already passed are disabled. Transitions must move forward through the pipeline."
          />
        </div>
        <Button
          type="submit"
          variant="primary"
          disabled={submitting || !targetStage}
          loading={submitting}
        >
          {submitting ? 'Advancing…' : 'Advance stage'}
        </Button>
      </div>
      <Textarea
        label="Notes for this transition"
        placeholder="Captured rationale, blocking issues, or reviewer remarks…"
        value={notes}
        onChange={(event) => setNotes(event.target.value)}
        rows={3}
      />
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {successAt ? (
        <InlineAlert variant="success">
          Stage advanced at {new Date(successAt).toLocaleTimeString()}.
        </InlineAlert>
      ) : null}
    </form>
  );
}

export function MockReviewStageRail({ bundleId }: MockReviewStageRailProps) {
  const [summary, setSummary] = useState<MockBundleReviewStageSummary | null>(null);
  const [loading, setLoading] = useState(true);
  const [refreshing, setRefreshing] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (opts?: { silent?: boolean }) => {
    if (!bundleId) return;
    if (opts?.silent) {
      setRefreshing(true);
    } else {
      setLoading(true);
    }
    setError(null);
    try {
      const next = await fetchMockBundleReviewStage(bundleId);
      setSummary(next);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load review pipeline.');
    } finally {
      setLoading(false);
      setRefreshing(false);
    }
  }, [bundleId]);

  useEffect(() => {
    void load();
  }, [load]);

  const transitionsByStage = useMemo(() => {
    const map = new Map<string, MockBundleReviewStageEntry>();
    if (!summary) return map;
    // The transitions array is already createdAt-ordered (oldest first), so
    // the last entry per stage wins — which is the row we want to show.
    for (const t of summary.transitions) {
      map.set(t.stage, t);
    }
    return map;
  }, [summary]);

  if (loading && !summary) {
    return (
      <div className="space-y-3">
        {Array.from({ length: 6 }).map((_, idx) => (
          <Skeleton key={idx} className="h-20 rounded-2xl" />
        ))}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="flex flex-wrap items-center justify-between gap-2">
        <div>
          <p className="text-xs font-bold uppercase tracking-widest text-muted">Current stage</p>
          <p className="mt-0.5 text-lg font-bold text-navy">
            {summary?.currentStage ? stageLabel(summary.currentStage) : 'Not started'}
          </p>
          {summary?.publishedAt ? (
            <p className="mt-1 text-xs text-muted">
              Published {new Date(summary.publishedAt).toLocaleString()}
            </p>
          ) : null}
        </div>
        <Button
          type="button"
          variant="outline"
          size="sm"
          onClick={() => void load({ silent: true })}
          disabled={refreshing}
        >
          {refreshing ? (
            <Loader2 className="mr-1 h-4 w-4 animate-spin" />
          ) : (
            <RefreshCcw className="mr-1 h-4 w-4" />
          )}
          Refresh
        </Button>
      </div>

      <StageTimeline
        stages={MOCK_REVIEW_STAGES}
        currentStage={summary?.currentStage ?? null}
        transitionsByStage={transitionsByStage}
      />

      <AdvanceStageForm
        bundleId={bundleId}
        currentStage={summary?.currentStage ?? null}
        isPublished={summary?.isPublished ?? false}
        onAdvanced={(next) => setSummary(next)}
      />
    </div>
  );
}
