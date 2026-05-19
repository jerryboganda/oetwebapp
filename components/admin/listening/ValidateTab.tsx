'use client';

import { useCallback, useEffect, useState } from 'react';
import { Loader2, RefreshCw, CheckCircle2, AlertTriangle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { adminListeningValidate } from '@/lib/api';
import type { ListeningValidationReport } from '@/lib/types/admin/listening-authoring';

interface Props {
  paperId: string;
  onToast: (variant: 'success' | 'error', message: string) => void;
}

export function ValidateTab({ paperId, onToast }: Props) {
  const [report, setReport] = useState<ListeningValidationReport | null>(null);
  const [loading, setLoading] = useState(true);

  const load = useCallback(async () => {
    setLoading(true);
    try {
      const r = await adminListeningValidate(paperId);
      setReport(r);
    } catch (e) {
      onToast('error', `Validate failed: ${(e as Error).message}`);
    } finally {
      setLoading(false);
    }
  }, [onToast, paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  if (loading) {
    return (
      <div className="flex items-center gap-2 text-sm text-muted">
        <Loader2 className="h-4 w-4 animate-spin" /> Validating…
      </div>
    );
  }
  if (!report) return null;

  const c = report.counts;
  return (
    <div className="space-y-4">
      <AdminRoutePanel title="Validation summary">
        <div className="flex flex-wrap items-center gap-2">
          <Badge variant={report.isPublishReady ? 'success' : 'warning'}>
            {report.isPublishReady ? 'Publish-ready' : 'Has warnings'}
          </Badge>
          <Badge variant="muted">source: {report.source ?? 'json'}</Badge>
          <Badge variant={c.totalItems === 42 ? 'success' : 'danger'}>
            {c.totalItems}/42 items
          </Badge>
          <Badge variant="info">A:{c.partACount} B:{c.partBCount} C:{c.partCCount}</Badge>
          <Button variant="ghost" size="sm" onClick={() => void load()}>
            <RefreshCw className="h-3 w-3" /> Re-validate
          </Button>
        </div>
      </AdminRoutePanel>

      <AdminRoutePanel title={`Issues (${report.issues.length})`}>
        {report.issues.length === 0 ? (
          <div className="flex items-center gap-2 text-sm text-success">
            <CheckCircle2 className="h-4 w-4" /> No issues detected.
          </div>
        ) : (
          <ul className="space-y-2">
            {report.issues.map((i, idx) => (
              <li
                key={`${i.code}-${idx}`}
                className={
                  'flex items-start gap-2 rounded-lg border p-3 text-sm ' +
                  (i.severity === 'error'
                    ? 'border-danger/40 bg-danger/5 text-danger'
                    : 'border-warning/40 bg-warning/10 text-warning')
                }
              >
                <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" />
                <div>
                  <div className="font-mono text-xs font-semibold">{i.code}</div>
                  <div>{i.message}</div>
                </div>
              </li>
            ))}
          </ul>
        )}
      </AdminRoutePanel>
    </div>
  );
}
