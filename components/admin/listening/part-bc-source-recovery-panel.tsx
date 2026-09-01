'use client';

import { useCallback, useEffect, useState } from 'react';
import { AlertTriangle, CheckCircle2, FileSearch, RefreshCw } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/admin/ui/skeleton';
import {
  auditListeningPartBCSource,
  recoverListeningPartBCSource,
  type ListeningPartBCRecoveryReport,
} from '@/lib/listening-authoring-api';
import { readErrorMessage } from '@/lib/read-error-message';

/**
 * Part B/C printed-question recovery.
 *
 * A November 2026 data migration replaced every sentinel Part B/C stem with one
 * generic heading and a follow-up migration then blanked it, so affected papers
 * show a candidate three options with no question above them. The ordinary
 * authoring routes refuse to write a paper that already has learner attempts —
 * which is every affected paper — so this panel drives the dedicated recovery
 * route instead.
 *
 * Recovery re-reads the paper's OWN question-paper text. It only ever fills a
 * stem or option that is currently unreadable, never edits an answer key, and
 * reports every item the source cannot support so it can be typed in by hand
 * from the printed paper.
 */
export function PartBCSourceRecoveryPanel({
  paperId,
  onRecovered,
  onNotify,
}: {
  paperId: string;
  onRecovered?: () => void;
  onNotify?: (variant: 'success' | 'error', message: string) => void;
}) {
  const [report, setReport] = useState<ListeningPartBCRecoveryReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setReport(await auditListeningPartBCSource(paperId));
    } catch (caught) {
      setError(readErrorMessage(caught, 'Could not read the Part B/C source audit.'));
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleRecover() {
    setRunning(true);
    setError(null);
    try {
      const result = await recoverListeningPartBCSource(paperId, false);
      setReport(result.recovery);
      onNotify?.(
        'success',
        `Restored ${result.recovery.recovered} printed question${result.recovery.recovered === 1 ? '' : 's'} from the source paper.`,
      );
      onRecovered?.();
    } catch (caught) {
      const message = readErrorMessage(caught, 'Source recovery failed.');
      setError(message);
      onNotify?.('error', message);
    } finally {
      setRunning(false);
    }
  }

  if (loading) return <Skeleton variant="card" />;
  if (error && !report) return <InlineAlert variant="error">{error}</InlineAlert>;
  if (!report) return null;

  // Nothing to do: every Part B/C item already shows its printed question.
  if (report.partBCQuestionCount > 0 && report.alreadyUsable === report.partBCQuestionCount) {
    return (
      <div className="flex items-center gap-2 rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-muted">
        <CheckCircle2 className="h-4 w-4 text-success" aria-hidden />
        All {report.partBCQuestionCount} Part B/C items show their printed question.
      </div>
    );
  }

  const missing = report.items.filter((item) => item.status !== 'already-usable');
  const unrecoverable = report.items.filter((item) => item.status === 'unrecoverable');

  return (
    <section
      data-testid="part-bc-source-recovery"
      className="space-y-3 rounded-admin border border-warning/50 bg-warning/5 p-4"
    >
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="space-y-1">
          <h3 className="flex items-center gap-2 text-sm font-bold text-admin-fg-strong">
            <AlertTriangle className="h-4 w-4 text-warning" aria-hidden />
            {missing.length} Part B/C item{missing.length === 1 ? '' : 's'} have no printed question
          </h3>
          <p className="max-w-2xl text-xs text-admin-fg-muted">
            Candidates currently see three options with no question above them.
            Recovery re-reads this paper&apos;s own question paper and fills only
            the unreadable items. Answer keys, option letters and numbering are
            never changed.
          </p>
        </div>
        <div className="flex shrink-0 gap-2">
          <Button
            variant="outline"
            size="sm"
            onClick={() => void load()}
            startIcon={<FileSearch className="h-4 w-4" />}
          >
            Re-check
          </Button>
          <Button
            variant="primary"
            size="sm"
            onClick={() => void handleRecover()}
            loading={running}
            loadingText="Restoring…"
            disabled={report.recovered === 0 || !report.sourceTextAvailable}
            startIcon={<RefreshCw className="h-4 w-4" />}
          >
            Restore {report.recovered} from source
          </Button>
        </div>
      </div>

      {!report.sourceTextAvailable ? (
        <InlineAlert variant="warning">
          This paper has no extracted question-paper text, so nothing can be
          recovered automatically. Upload the question paper on the PDFs tab (or
          re-run text extraction), then re-check. Until then the missing items
          must be typed in from the printed paper.
        </InlineAlert>
      ) : null}

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      {unrecoverable.length > 0 ? (
        <div className="space-y-1">
          <p className="text-xs font-semibold text-admin-fg-strong">
            Type these in by hand — the source text cannot attribute them safely:
          </p>
          <ul className="space-y-1 text-xs text-admin-fg-muted">
            {unrecoverable.map((item) => (
              <li key={item.number} className="flex gap-2">
                <span className="shrink-0 font-mono font-semibold">Q{item.number}</span>
                <span>{item.detail}</span>
              </li>
            ))}
          </ul>
        </div>
      ) : null}
    </section>
  );
}
