'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Activity, CheckCircle2, Send } from 'lucide-react';

import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  fetchTutorSpeakingCalibrationSamples,
  submitTutorSpeakingCalibrationScores,
  type SpeakingCriterionRubric,
  type TutorCalibrationSubmissionResult,
  type TutorSpeakingCalibrationSampleRow,
} from '@/lib/api';

// Wave 4 of docs/SPEAKING-MODULE-PLAN.md - tutor calibration mode.
// Each tutor sees the published sample inventory + their own latest
// drift score per sample. Submitting fresh scores returns the per-criterion
// delta vs the gold rubric so the tutor sees exactly where they diverged.

const CRITERIA: Array<{ key: keyof SpeakingCriterionRubric; label: string; max: number; band: 'linguistic' | 'clinical' }> = [
  { key: 'intelligibility', label: 'Intelligibility', max: 6, band: 'linguistic' },
  { key: 'fluency', label: 'Fluency', max: 6, band: 'linguistic' },
  { key: 'appropriateness', label: 'Appropriateness of language', max: 6, band: 'linguistic' },
  { key: 'grammarExpression', label: 'Resources of grammar & expression', max: 6, band: 'linguistic' },
  { key: 'relationshipBuilding', label: 'Relationship building', max: 3, band: 'clinical' },
  { key: 'patientPerspective', label: "Patient's perspective", max: 3, band: 'clinical' },
  { key: 'structure', label: 'Providing structure', max: 3, band: 'clinical' },
  { key: 'informationGathering', label: 'Information gathering', max: 3, band: 'clinical' },
  { key: 'informationGiving', label: 'Information giving', max: 3, band: 'clinical' },
];

const EMPTY_RUBRIC: SpeakingCriterionRubric = {
  intelligibility: 0,
  fluency: 0,
  appropriateness: 0,
  grammarExpression: 0,
  relationshipBuilding: 0,
  patientPerspective: 0,
  structure: 0,
  informationGathering: 0,
  informationGiving: 0,
};

export default function ExpertSpeakingCalibrationPage() {
  const [samples, setSamples] = useState<TutorSpeakingCalibrationSampleRow[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [activeSampleId, setActiveSampleId] = useState<string | null>(null);
  const [rubric, setRubric] = useState<SpeakingCriterionRubric>(EMPTY_RUBRIC);
  const [notes, setNotes] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [lastResult, setLastResult] = useState<TutorCalibrationSubmissionResult | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setSamples(await fetchTutorSpeakingCalibrationSamples());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load calibration samples.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const activeSample = useMemo(
    () => samples.find((s) => s.sampleId === activeSampleId) ?? null,
    [samples, activeSampleId],
  );

  const submit = useCallback(async () => {
    if (!activeSampleId) return;
    setSubmitting(true);
    setError(null);
    try {
      const result = await submitTutorSpeakingCalibrationScores(activeSampleId, rubric, notes || undefined);
      setLastResult(result);
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Submission failed.');
    } finally {
      setSubmitting(false);
    }
  }, [activeSampleId, rubric, notes, load]);

  const sampleColumns = useMemo<Column<TutorSpeakingCalibrationSampleRow>[]>(() => [
    {
      key: 'title',
      header: 'Sample',
      render: (row) => (
        <div className="flex flex-col">
          <span className="font-bold">{row.title}</span>
          <span className="text-[10px] uppercase tracking-widest text-muted">attempt {row.sourceAttemptId}</span>
        </div>
      ),
    },
    {
      key: 'profession',
      header: 'Profession',
      render: (row) => <Badge variant="muted">{row.professionId}</Badge>,
    },
    {
      key: 'submitted',
      header: 'My drift',
      render: (row) =>
        row.submitted && row.mySubmission ? (
          <Badge variant={row.mySubmission.totalAbsoluteError <= 3 ? 'success' : 'warning'}>
            {row.mySubmission.totalAbsoluteError.toFixed(1)} total
          </Badge>
        ) : (
          <Badge variant="info">Not submitted</Badge>
        ),
    },
    {
      key: 'actions',
      header: 'Actions',
      render: (row) => (
        <Button
          size="sm"
          variant={activeSampleId === row.sampleId ? 'primary' : 'outline'}
          onClick={() => {
            setActiveSampleId(row.sampleId);
            setRubric(EMPTY_RUBRIC);
            setLastResult(null);
            setNotes('');
          }}
        >
          {activeSampleId === row.sampleId ? 'Selected' : 'Calibrate'}
        </Button>
      ),
    },
  ], [activeSampleId]);

  return (
    <ExpertRouteWorkspace>
      <ExpertRouteHero
        eyebrow="Quality control"
        title="Speaking calibration"
        description="Score the gold-marked samples and compare your rubric against the senior reviewer's. Drift trends are reviewed by admins."
        icon={Activity}
      />

      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      <ExpertRouteSectionHeader title="Available calibration samples" />
      {loading ? (
        <Skeleton className="h-32 w-full" />
      ) : samples.length === 0 ? (
        <p className="text-sm text-muted">No published calibration samples yet — check back soon.</p>
      ) : (
        <DataTable<TutorSpeakingCalibrationSampleRow>
          columns={sampleColumns}
          data={samples}
          keyExtractor={(row) => row.sampleId}
        />
      )}

      {activeSample && (
        <section className="rounded border border-border bg-surface p-4">
          <ExpertRouteSectionHeader title={`Calibrate: ${activeSample.title}`} />
          {activeSample.description && (
            <p className="mb-3 text-sm text-muted">{activeSample.description}</p>
          )}
          <p className="mb-4 text-xs text-muted">
            Listen to the linked attempt ({activeSample.sourceAttemptId}) in the expert review console, then enter your rubric below.
          </p>

          <div className="grid gap-3 sm:grid-cols-2">
            {CRITERIA.map((c) => (
              <label key={c.key} className="flex items-center justify-between gap-2 rounded border border-border-subtle px-3 py-2">
                <span className="flex flex-col">
                  <span className="text-sm font-bold">{c.label}</span>
                  <span className="text-[10px] uppercase tracking-widest text-muted">
                    {c.band} · 0–{c.max}
                  </span>
                </span>
                <Input
                  type="number"
                  min={0}
                  max={c.max}
                  value={rubric[c.key]}
                  onChange={(e: React.ChangeEvent<HTMLInputElement>) => {
                    const raw = Number(e.target.value);
                    const clamped = Math.max(0, Math.min(c.max, Number.isNaN(raw) ? 0 : raw));
                    setRubric((r) => ({ ...r, [c.key]: clamped }));
                  }}
                  className="w-20"
                />
              </label>
            ))}
          </div>

          <label className="mt-4 flex flex-col gap-1">
            <span className="text-xs uppercase tracking-widest text-muted">Notes (optional)</span>
            <textarea
              className="min-h-[80px] rounded border border-border-subtle bg-surface p-2 text-sm"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              maxLength={2000}
            />
          </label>

          <div className="mt-4 flex items-center gap-2">
            <Button onClick={() => void submit()} loading={submitting}>
              <Send className="h-3.5 w-3.5" /> Submit rubric
            </Button>
            {lastResult && (
              <Badge variant={lastResult.totalAbsoluteError === 0 ? 'success' : 'info'}>
                <CheckCircle2 className="h-3.5 w-3.5" />
                Total drift {lastResult.totalAbsoluteError.toFixed(1)}
              </Badge>
            )}
          </div>

          {lastResult && (
            <div className="mt-3 rounded border border-border-subtle bg-surface p-3">
              <p className="mb-2 text-xs uppercase tracking-widest text-muted">Per-criterion delta vs gold</p>
              <div className="grid gap-1 text-xs sm:grid-cols-3">
                {Object.entries(lastResult.perCriterionDelta).map(([k, v]) => (
                  <span key={k} className="tabular-nums">
                    <span className="text-muted">{k}: </span>
                    <span className={v === 0 ? 'text-success' : 'text-warning'}>{v! > 0 ? `+${v}` : v}</span>
                  </span>
                ))}
              </div>
            </div>
          )}
        </section>
      )}
    </ExpertRouteWorkspace>
  );
}
