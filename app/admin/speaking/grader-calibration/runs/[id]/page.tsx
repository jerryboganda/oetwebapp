'use client';

/**
 * Admin · Speaking · Grader calibration · one run (owner request 7 Oct 2026).
 *
 * The AI-vs-expert comparison, read-only: for every grade of every marked performance, the ten things needed to tell WHERE a
 * difference from the expert comes from: the grader's own judgement, missing audio evidence, the secondary reviewer, or the
 * provisional raw-to-/500 mapping. Numbers and codes only (no transcript, no learner identity). This page shows AI scores, so it
 * is kept apart from the blind marking pages and is reached only from the runs list.
 */
import { useCallback, useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { Scale } from 'lucide-react';
import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  adminGetGraderCalibrationRun,
  type GraderCalibrationAudioCard,
  type GraderCalibrationGradeDetail,
  type GraderCalibrationPerformance,
  type GraderCalibrationRunView,
} from '@/lib/api/speaking-grader-calibration';

const CRITERIA: Array<{ code: string; label: string; max: number }> = [
  { code: 'intelligibility', label: 'Intelligibility', max: 6 },
  { code: 'fluency', label: 'Fluency', max: 6 },
  { code: 'appropriateness', label: 'Appropriateness of language', max: 6 },
  { code: 'grammarExpression', label: 'Resources of grammar and expression', max: 6 },
  { code: 'relationshipBuilding', label: 'Relationship building', max: 3 },
  { code: 'patientPerspective', label: "Understanding and incorporating the patient's perspective", max: 3 },
  { code: 'structure', label: 'Providing structure', max: 3 },
  { code: 'informationGathering', label: 'Information gathering', max: 3 },
  { code: 'informationGiving', label: 'Information giving', max: 3 },
];

const seconds = (ms: number) => `${Math.round(ms / 1000)} s`;

/** One plain line for an audio verdict: heard from the sound or estimated from the transcript, and how much audio there was. */
function audioText(card: GraderCalibrationAudioCard | null | undefined): string {
  if (!card) return 'The audio stage did not run.';
  const amount = `${card.clips} clip${card.clips === 1 ? '' : 's'}, ${seconds(card.audioMs)} of audio for ${seconds(card.speechMs)} of speech`
    + `${card.coverage != null ? ` (${Math.round(card.coverage * 100)}%)` : ''}, ${card.turnsWithClip} of ${card.turns} turns with a clip`;
  return card.source === 'audio'
    ? `Judged from the audio (${card.model ?? 'model not recorded'}; confidence ${card.confidence}; ${amount}).`
    : `Transcript only: ${card.reason ?? 'no reason recorded'} (${amount}).`;
}

const signed = (value: number) => (value > 0 ? `+${value}` : String(value));

function GradeBlock({ performance, grade, mappingVersion, graderVersion }: {
  performance: GraderCalibrationPerformance;
  grade: GraderCalibrationGradeDetail;
  mappingVersion: string;
  graderVersion: string;
}) {
  const d = grade.diagnostics ?? null;
  const cards = d?.audio?.cards ?? [];
  const review = d?.review ?? null;
  return (
    <div className="space-y-3 border-t border-admin-border pt-4 first:border-t-0 first:pt-0" data-testid="calibration-run-grade">
      <h3 className="text-sm font-semibold text-admin-fg-strong">Repeat {grade.repeat}</h3>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
            <tr>
              <th scope="col" className="p-2">1. Criterion</th>
              <th scope="col" className="p-2">Your mark</th>
              <th scope="col" className="p-2">Claude (before the reviewer)</th>
              <th scope="col" className="p-2">Reviewer proposed</th>
              <th scope="col" className="p-2">Final AI mark</th>
              <th scope="col" className="p-2">Final minus yours</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-admin-border tabular-nums">
            {CRITERIA.map((criterion) => {
              const expert = performance.expertScores[criterion.code];
              const final = grade.scores[criterion.code];
              const primary = d?.primaryScores?.[criterion.code];
              const proposed = review?.reviewerScores?.[criterion.code];
              const moved = review?.changes.some((change) => change.criterion === criterion.code) ?? false;
              return (
                <tr key={criterion.code}>
                  <th scope="row" className="p-2 text-left font-normal text-admin-fg-default">
                    {criterion.label} <span className="text-admin-fg-muted">/{criterion.max}</span>
                  </th>
                  <td className="p-2">{expert ?? '-'}</td>
                  <td className="p-2">{primary ?? '-'}</td>
                  <td className="p-2">{proposed ?? '-'}{moved ? ' (moved the grade)' : ''}</td>
                  <td className="p-2">{final ?? '-'}</td>
                  <td className="p-2">{expert != null && final != null ? signed(final - expert) : '-'}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </div>
      <dl className="grid gap-x-6 gap-y-2 text-sm md:grid-cols-[16rem_1fr]">
        <dt className="text-admin-fg-muted">2. Overall result /500</dt>
        <dd>
          You {performance.expertOverall} ({performance.expertGrade}); the AI as a learner sees it {grade.reportedScaled ?? 'n/a'}
          {grade.reportedGrade ? ` (${grade.reportedGrade})` : ''}
          {grade.reportedError != null ? `, ${signed(grade.reportedError)} against you` : ''}; through a map fitted on your marks
          (leave-one-out) {grade.scaledLeaveOneOut} ({grade.grade}), {signed(grade.scaledError)}.
        </dd>
        <dt className="text-admin-fg-muted">3. AI raw total before mapping</dt>
        <dd>{grade.raw} / 39 (yours {performance.expertRaw} / 39)</dd>
        <dt className="text-admin-fg-muted">4. Mapping version</dt>
        <dd><code>{d?.mappingVersion ?? mappingVersion}</code> (a provisional heuristic, not an official OET conversion)</dd>
        {cards.length > 0 ? cards.map((card, index) => (
          <div key={index} className="contents">
            <dt className="text-admin-fg-muted">{5 + index}. Audio, {cards.length > 1 ? `card ${String.fromCharCode(65 + index)}` : 'the card'}</dt>
            <dd>{audioText(card)}</dd>
          </div>
        )) : (
          <>
            <dt className="text-admin-fg-muted">5-6. Audio</dt>
            <dd>{d ? audioText(d.audio?.combined) : 'Not recorded: this grade was made before diagnostics were kept.'}</dd>
          </>
        )}
        <dt className="text-admin-fg-muted">7. Combined Intelligibility</dt>
        <dd>{grade.intelligibilitySource === 'audio' ? 'Audio-based' : 'Transcript only'}</dd>
        <dt className="text-admin-fg-muted">8. Grader version</dt>
        <dd><code>{graderVersion}</code></dd>
        <dt className="text-admin-fg-muted">9. Claude before the reviewer</dt>
        <dd>{d?.primaryScores ? CRITERIA.map((c) => d.primaryScores?.[c.code] ?? '?').join(' / ') : 'Not recorded: this grade was made before diagnostics were kept.'}</dd>
        <dt className="text-admin-fg-muted">10. Reviewer</dt>
        <dd>
          {review
            ? `${review.status}${review.model ? ` (${review.model})` : ''}. ${review.changes.length > 0
              ? `Changed: ${review.changes.map((change) => `${change.criterion} ${change.from} to ${change.to}`).join(', ')}.`
              : 'No criterion was changed.'}`
            : 'Not recorded: this grade was made before diagnostics were kept.'}
        </dd>
      </dl>
    </div>
  );
}

export default function SpeakingGraderCalibrationRunPage() {
  const params = useParams<{ id: string }>();
  const runId = params?.id ?? '';
  const [run, setRun] = useState<GraderCalibrationRunView | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const load = useCallback(async () => {
    if (!runId) return;
    setLoading(true);
    setError(null);
    try {
      setRun(await adminGetGraderCalibrationRun(runId));
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load this run.');
    } finally {
      setLoading(false);
    }
  }, [runId]);

  useEffect(() => {
    void load();
  }, [load]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Speaking', href: '/admin/speaking' },
    { label: 'Grader calibration', href: '/admin/speaking/grader-calibration' },
    { label: 'Runs', href: '/admin/speaking/grader-calibration/runs' },
    { label: 'Run' },
  ];

  if (!run) {
    return (
      <AdminSettingsLayout title="Calibration run" breadcrumbs={breadcrumbs} eyebrow="Speaking" icon={<Scale className="h-5 w-5" />}>
        {error ? (
          <div className="space-y-3">
            <InlineAlert variant="error">{error}</InlineAlert>
            <Button size="sm" variant="outline" onClick={() => void load()} disabled={loading}>Try again</Button>
          </div>
        ) : <Skeleton className="h-64 w-full" />}
      </AdminSettingsLayout>
    );
  }

  const report = run.report;
  const isPilot = report?.verdict.mode === 'pilot' || run.pilot === true;
  const mappingVersion = report?.mappingVersion ?? 'not recorded';
  return (
    <AdminSettingsLayout
      title={`Calibration run ${run.scope === 'mock' ? '· Full Mock' : '· single cards'}`}
      description="The AI against your marks, grade by grade. Numbers and codes only."
      breadcrumbs={breadcrumbs}
      eyebrow="Speaking"
      icon={<Scale className="h-5 w-5" />}
    >
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <SettingsSection
        title="Run"
        description={`Started ${new Date(run.createdAt).toLocaleString()}${run.finalizedAt ? `, finished ${new Date(run.finalizedAt).toLocaleString()}` : ''}.`}
      >
        <div className="flex flex-wrap items-center gap-2 text-sm">
          <Badge variant={run.status === 'complete' ? 'success' : 'info'}>{run.status}</Badge>
          {isPilot ? <Badge variant="warning">Pilot: informational, cannot pass by design</Badge> : null}
          <span className="text-admin-fg-muted">
            graded {run.progress.done} of {run.progress.total}
            {run.progress.failed > 0 ? `, ${run.progress.failed} failed` : ''}, {run.repeats} repeats per performance,
            audio {run.useAudio ? 'on' : 'off'}
          </span>
          <Button size="sm" variant="outline" onClick={() => void load()} disabled={loading}>
            {loading ? 'Refreshing' : 'Refresh'}
          </Button>
        </div>
        <p className="mt-2 text-sm text-admin-fg-muted">
          Grader version: <code>{run.graderVersion || 'set when the run finishes'}</code>
        </p>
        {report?.graderVersions ? (
          <ul className="mt-1 text-sm text-admin-fg-muted">
            {Object.entries(report.graderVersions).map(([version, count]) => (
              <li key={version}>{count} grade(s) by <code>{version}</code></li>
            ))}
          </ul>
        ) : null}
      </SettingsSection>

      {!report || (report.detail ?? []).length === 0 ? (
        <SettingsSection title="Performances" description="No grade has finished yet.">
          <p className="text-sm text-admin-fg-muted">Refresh once the first grade completes.</p>
        </SettingsSection>
      ) : (
        (report.detail ?? []).map((performance) => (
          <SettingsSection
            key={performance.sampleId}
            title={`Performance ${performance.sampleId}`}
            description={`Your result: ${performance.expertOverall} · ${performance.expertGrade} (raw ${performance.expertRaw} / 39). ${performance.hasAudio ? 'Audio kept.' : 'No audio kept.'}`}
          >
            {performance.grades.length === 0 ? (
              <p className="text-sm text-admin-fg-muted">Not graded yet.</p>
            ) : (
              <div className="space-y-4">
                {performance.grades.map((grade) => (
                  <GradeBlock
                    key={grade.repeat}
                    performance={performance}
                    grade={grade}
                    mappingVersion={mappingVersion}
                    graderVersion={run.graderVersion || 'see the per-version counts above'}
                  />
                ))}
              </div>
            )}
          </SettingsSection>
        ))
      )}
    </AdminSettingsLayout>
  );
}
