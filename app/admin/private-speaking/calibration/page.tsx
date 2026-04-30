'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Activity, RefreshCw } from 'lucide-react';

import { AdminRouteHero, AdminRoutePanel, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchAdminSpeakingCalibrationDrift,
  fetchAdminSpeakingCalibrationSamples,
  type AdminSpeakingCalibrationDriftReport,
  type AdminSpeakingCalibrationDriftRow,
  type AdminSpeakingCalibrationSampleRow,
} from '@/lib/api';

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md - admin drift dashboard.
// Shows the calibration sample inventory plus the per-tutor mean
// absolute error against the gold rubric.
export default function AdminSpeakingCalibrationPage() {
  const [report, setReport] = useState<AdminSpeakingCalibrationDriftReport | null>(null);
  const [samples, setSamples] = useState<AdminSpeakingCalibrationSampleRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [minSubmissions, setMinSubmissions] = useState(1);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const [drift, sampleList] = await Promise.all([
        fetchAdminSpeakingCalibrationDrift(minSubmissions),
        fetchAdminSpeakingCalibrationSamples(),
      ]);
      setReport(drift);
      setSamples(sampleList);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load calibration data.');
    } finally {
      setLoading(false);
    }
  }, [minSubmissions]);

  useEffect(() => {
    void load();
  }, [load]);

  const driftColumns = useMemo<Column<AdminSpeakingCalibrationDriftRow>[]>(() => [
    {
      key: 'tutor',
      header: 'Tutor',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-bold">{row.tutorName || row.tutorId}</span>
          <span className="text-[10px] uppercase tracking-widest text-muted">id {row.tutorId}</span>
        </div>
      ),
    },
    {
      key: 'submissionCount',
      header: 'Submissions',
      render: (row) => <span className="tabular-nums">{row.submissionCount}</span>,
    },
    {
      key: 'mae',
      header: 'Mean abs. error',
      render: (row) => {
        const variant = row.meanAbsoluteError >= 1 ? 'danger' : row.meanAbsoluteError >= 0.5 ? 'warning' : 'success';
        return <Badge variant={variant}>{row.meanAbsoluteError.toFixed(3)}</Badge>;
      },
    },
    {
      key: 'totalAbs',
      header: 'Total abs. error',
      render: (row) => <span className="tabular-nums text-muted">{row.totalAbsoluteError.toFixed(2)}</span>,
    },
    {
      key: 'lastSubmittedAt',
      header: 'Last submission',
      render: (row) => (
        <span className="text-xs text-muted">
          {row.lastSubmittedAt ? new Date(row.lastSubmittedAt).toLocaleString() : '—'}
        </span>
      ),
    },
  ], []);

  return (
    <AdminRouteWorkspace>
      <AdminRouteHero
        eyebrow="Quality control"
        title="Speaking calibration drift"
        description="Per-tutor mean absolute error against the gold rubric for every published calibration sample."
        icon={Activity}
      />

      <AdminRoutePanel
        title="Filters"
        description="Hide tutors with too few submissions to compute a stable MAE."
      >
        <div className="flex flex-wrap items-end gap-3">
          <div className="flex flex-col gap-1">
            <label className="text-xs uppercase tracking-widest text-muted" htmlFor="minSubmissions">
              Min submissions
            </label>
            <Input
              id="minSubmissions"
              type="number"
              min={1}
              max={50}
              value={minSubmissions}
              onChange={(e: React.ChangeEvent<HTMLInputElement>) => setMinSubmissions(Math.max(1, Number(e.target.value) || 1))}
              className="w-32"
            />
          </div>
          <Button variant="outline" onClick={() => void load()}>
            <RefreshCw className="h-3.5 w-3.5" /> Refresh
          </Button>
          {report && (
            <span className="text-xs text-muted">
              {report.tutors.length} tutors · {report.sampleSize} score rows · {report.samplesPublished} published samples
            </span>
          )}
        </div>
      </AdminRoutePanel>

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      <AdminRoutePanel title="Tutor drift table">
        {loading ? (
          <Skeleton className="h-48 w-full" />
        ) : report && report.tutors.length > 0 ? (
          <DataTable<AdminSpeakingCalibrationDriftRow>
            columns={driftColumns}
            data={report.tutors}
            keyExtractor={(row) => row.tutorId}
          />
        ) : (
          <p className="text-sm text-muted">No tutors meet the minimum submission threshold.</p>
        )}
      </AdminRoutePanel>

      <AdminRoutePanel
        title="Calibration samples"
        description="Gold-marked recordings tutors calibrate against."
      >
        {samples.length === 0 ? (
          <p className="text-sm text-muted">No samples yet — create one in the speaking content workspace.</p>
        ) : (
          <ul className="flex flex-col gap-2">
            {samples.map((s) => (
              <li
                key={s.sampleId}
                className="flex items-center justify-between rounded border border-border bg-surface px-3 py-2"
              >
                <div className="flex flex-col">
                  <span className="font-bold">{s.title}</span>
                  <span className="text-xs text-muted">
                    source attempt {s.sourceAttemptId} · {s.tutorSubmissionCount} tutor submissions
                  </span>
                </div>
                <Badge variant={s.status === 'published' ? 'success' : s.status === 'archived' ? 'muted' : 'info'}>
                  {s.status}
                </Badge>
              </li>
            ))}
          </ul>
        )}
      </AdminRoutePanel>
    </AdminRouteWorkspace>
  );
}
