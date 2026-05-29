'use client';

import { useCallback, useEffect, useState } from 'react';
import { History, Send } from 'lucide-react';

import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Textarea, Select } from '@/components/ui/form-controls';
import {
  getReadingQuestionReviewHistory,
  transitionReadingQuestionReviewState,
  type ReadingReviewState,
  type ReadingReviewLogEntryDto,
} from '@/lib/reading-authoring-api';
import { REVIEW_STATES, REVIEW_STATE_LABELS, reviewStateTone } from './review-state';

interface ReadingReviewPanelProps {
  paperId: string;
  questionId: string;
  currentState: ReadingReviewState;
  onTransitioned: (toState: ReadingReviewState) => void;
  onNotify: (variant: 'success' | 'error', message: string) => void;
}

function formatTimestamp(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : date.toLocaleString();
}

export function ReadingReviewPanel({
  paperId,
  questionId,
  currentState,
  onTransitioned,
  onNotify,
}: ReadingReviewPanelProps) {
  const [toState, setToState] = useState<ReadingReviewState>(currentState);
  const [note, setNote] = useState('');
  const [override, setOverride] = useState(false);
  const [transitioning, setTransitioning] = useState(false);
  const [history, setHistory] = useState<ReadingReviewLogEntryDto[] | null>(null);
  const [historyLoading, setHistoryLoading] = useState(false);
  const [historyError, setHistoryError] = useState<string | null>(null);

  const loadHistory = useCallback(async () => {
    setHistoryLoading(true);
    setHistoryError(null);
    try {
      const log = await getReadingQuestionReviewHistory(paperId, questionId);
      setHistory(log);
    } catch (err) {
      setHistoryError(err instanceof Error ? err.message : 'Failed to load history');
    } finally {
      setHistoryLoading(false);
    }
  }, [paperId, questionId]);

  useEffect(() => {
    void loadHistory();
  }, [loadHistory]);

  async function handleTransition() {
    if (toState === currentState) {
      onNotify('error', 'Pick a different review state to transition to.');
      return;
    }
    setTransitioning(true);
    try {
      await transitionReadingQuestionReviewState(paperId, questionId, {
        toState,
        note: note.trim() || null,
        isAdminOverride: override,
      });
      onNotify('success', `Moved to ${REVIEW_STATE_LABELS[toState]}`);
      setNote('');
      setOverride(false);
      onTransitioned(toState);
      await loadHistory();
    } catch (err) {
      onNotify('error', err instanceof Error ? err.message : 'Transition failed');
    } finally {
      setTransitioning(false);
    }
  }

  return (
    <div className="space-y-4">
      <div className="flex items-center gap-2">
        <span className="text-xs uppercase tracking-wider text-admin-fg-muted">Current state</span>
        <Badge variant={reviewStateTone(currentState)}>{REVIEW_STATE_LABELS[currentState]}</Badge>
      </div>

      <div className="grid gap-3 sm:grid-cols-2">
        <Select
          label="Transition to"
          value={toState}
          onChange={(e) => setToState(e.target.value as ReadingReviewState)}
          options={REVIEW_STATES.map((s) => ({ value: s, label: REVIEW_STATE_LABELS[s] }))}
        />
        <label className="flex items-center gap-2 self-end pb-3 text-sm text-navy">
          <input
            type="checkbox"
            className="h-4 w-4 rounded border-border text-primary"
            checked={override}
            onChange={(e) => setOverride(e.target.checked)}
          />
          Admin override (skip ordered gates)
        </label>
      </div>

      <Textarea
        label="Reason / note"
        value={note}
        onChange={(e) => setNote(e.target.value)}
        rows={2}
        placeholder="Why is this question changing review state?"
      />

      <Button
        variant="primary"
        size="sm"
        onClick={handleTransition}
        disabled={transitioning || toState === currentState}
        startIcon={<Send className="h-4 w-4" />}
      >
        {transitioning ? 'Transitioning…' : 'Apply transition'}
      </Button>

      <div className="border-t border-admin-border pt-3">
        <div className="mb-2 flex items-center gap-2 text-sm font-semibold text-admin-fg-strong">
          <History className="h-4 w-4" />
          Review history
        </div>
        {historyLoading && <Skeleton variant="text" className="h-12 w-full" />}
        {historyError && <p className="text-sm text-[var(--admin-danger)]">{historyError}</p>}
        {!historyLoading && !historyError && history && history.length === 0 && (
          <p className="text-sm text-admin-fg-muted">No transitions recorded yet.</p>
        )}
        {!historyLoading && !historyError && history && history.length > 0 && (
          <ol className="space-y-2">
            {history.map((entry) => (
              <li
                key={entry.id}
                className="rounded-lg border border-admin-border bg-admin-bg-subtle px-3 py-2 text-sm"
              >
                <div className="flex flex-wrap items-center gap-1.5">
                  <Badge variant={reviewStateTone(entry.fromState)} size="sm">
                    {REVIEW_STATE_LABELS[entry.fromState]}
                  </Badge>
                  <span aria-hidden className="text-admin-fg-muted">→</span>
                  <Badge variant={reviewStateTone(entry.toState)} size="sm">
                    {REVIEW_STATE_LABELS[entry.toState]}
                  </Badge>
                  <span className="ml-auto text-xs text-admin-fg-muted">
                    {formatTimestamp(entry.transitionedAt)}
                  </span>
                </div>
                <p className="mt-1 text-xs text-admin-fg-muted">
                  {entry.reviewerDisplayName ?? entry.reviewerUserId}
                  {entry.note ? ` — ${entry.note}` : ''}
                </p>
              </li>
            ))}
          </ol>
        )}
      </div>
    </div>
  );
}
