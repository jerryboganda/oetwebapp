'use client';

import { useCallback, useEffect, useState } from 'react';
import { Loader2, RotateCcw, CheckCircle2, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { adminListeningBackfillPaper } from '@/lib/api';
import { getContentPaper, type ContentPaperDto } from '@/lib/content-upload-api';
import type { ListeningBackfillReport } from '@/lib/types/admin/listening-authoring';

interface Props {
  paperId: string;
  onToast: (variant: 'success' | 'error', message: string) => void;
}

const PART_CODES = ['A1', 'A2', 'B', 'C1', 'C2'] as const;

export function BackfillTab({ paperId, onToast }: Props) {
  const [paper, setPaper] = useState<ContentPaperDto | null>(null);
  const [loading, setLoading] = useState(true);
  const [running, setRunning] = useState(false);
  const [lastReport, setLastReport] = useState<ListeningBackfillReport | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const p = await getContentPaper(paperId);
      setPaper(p);
    } catch (e) {
      onToast('error', `Load paper failed: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const runBackfill = useCallback(async () => {
    setRunning(true);
    try {
      const report = await adminListeningBackfillPaper(paperId);
      setLastReport(report);
      onToast(
        report.success ? 'success' : 'error',
        report.success
          ? `Backfill OK: ${report.partsCreated} parts, ${report.extractsCreated} extracts, ${report.questionsCreated} questions, ${report.optionsCreated} options.`
          : `Backfill blocked: ${report.reason ?? 'unknown'}`,
      );
      await load();
    } catch (e) {
      onToast('error', `Backfill failed: ${(e as Error).message}`);
    } finally {
      setRunning(false);
    }
  }, [load, onToast, paperId]);

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Loading…
      </div>
    );
  }

  const audioAssets = (paper?.assets ?? []).filter((a) => a.role === 'Audio');
  const audioByPart = new Map<string, boolean>();
  for (const a of audioAssets) {
    if (a.part) audioByPart.set(a.part, true);
  }

  return (
    <div className="space-y-4">
      <AdminRoutePanel title="Per-extract audio asset status">
        <div className="grid grid-cols-2 gap-2 md:grid-cols-5">
          {PART_CODES.map((code) => {
            const ok = audioByPart.has(code);
            return (
              <div
                key={code}
                className={
                  'flex items-center gap-2 rounded-lg border p-3 text-sm ' +
                  (ok
                    ? 'border-success/40 bg-success/5 text-success'
                    : 'border-danger/40 bg-danger/5 text-danger')
                }
              >
                {ok ? <CheckCircle2 className="h-4 w-4" /> : <XCircle className="h-4 w-4" />}
                <span className="font-semibold">Part {code}</span>
                <span className="ml-auto text-xs">{ok ? 'attached' : 'missing'}</span>
              </div>
            );
          })}
        </div>
        <p className="mt-2 text-xs text-muted">
          Per-part audio regeneration is not yet exposed as a per-extract endpoint. Use the paper-level backfill
          below to (re-)project JSON → relational rows; use the system-wide backfill dashboard or the admin
          assets panel for TTS regeneration. Missing audio assets can be uploaded from the Assets tab.
        </p>
      </AdminRoutePanel>

      <AdminRoutePanel title="Paper-level backfill (JSON → relational)">
        <div className="flex items-center gap-3">
          <Button variant="primary" onClick={() => void runBackfill()} disabled={running}>
            {running ? <Loader2 className="h-4 w-4 animate-spin" /> : <RotateCcw className="h-4 w-4" />}
            Backfill this paper
          </Button>
          {lastReport && (
            <Badge variant={lastReport.success ? 'success' : 'danger'}>
              Last run: {lastReport.success ? 'success' : 'failed'}
            </Badge>
          )}
        </div>
        {lastReport && (
          <pre className="mt-3 overflow-auto rounded-lg border border-border bg-background-light p-3 text-xs">
            {JSON.stringify(lastReport, null, 2)}
          </pre>
        )}
      </AdminRoutePanel>
    </div>
  );
}
