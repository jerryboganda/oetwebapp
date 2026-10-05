'use client';

/**
 * Admin · Speaking · Grader calibration (owner spec 4 Oct 2026).
 *
 * The AI Speaking grader keeps a "Provisional" label until its scores have been compared with an OET
 * expert's own marks. This page is where finished AI role-plays are promoted into the calibration set
 * and where Dr Hesham's marking progress is tracked against the coverage a calibration report needs.
 * Nothing on it ever shows an AI score — marking is blind.
 */
import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Scale } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  adminGetGraderCalibration,
  adminListGraderCalibrationCandidates,
  adminPromoteGraderCalibrationSample,
  type GraderCalibrationCandidate,
  type GraderCalibrationCoverage,
  type GraderCalibrationOverview,
  type GraderCalibrationSampleRow,
} from '@/lib/api/speaking-grader-calibration';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Speaking', href: '/admin/speaking' },
  { label: 'Grader calibration' },
];

const GRADES = ['A', 'B', 'C+', 'C', 'D', 'E'] as const;

type Tab = 'set' | 'candidates';

function formatDate(iso: string | null): string {
  if (!iso) return '-';
  const parsed = new Date(iso);
  return Number.isNaN(parsed.getTime()) ? iso : parsed.toLocaleDateString();
}

function formatMinutes(seconds: number): string {
  return `${Math.floor(seconds / 60)}:${String(seconds % 60).padStart(2, '0')}`;
}

function StatusBadge({ status, usable = true }: { status: GraderCalibrationSampleRow['status']; usable?: boolean }) {
  if (!usable) return <Badge variant="muted">Unavailable</Badge>;
  if (status === 'labelled') return <Badge variant="success">Marked</Badge>;
  if (status === 'excluded') return <Badge variant="muted">Excluded</Badge>;
  return <Badge variant="warning">To mark</Badge>;
}

function CoveragePanel({ coverage }: { coverage: GraderCalibrationCoverage }) {
  return (
    <Card>
      <CardContent className="space-y-4 p-4" data-testid="calibration-coverage">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div>
            <p className="text-sm font-semibold text-admin-fg-strong">
              {coverage.labelled} of {coverage.requiredLabelled} performances marked
            </p>
            <p className="mt-0.5 text-xs text-admin-fg-muted">
              {coverage.pending} waiting to be marked · {coverage.excluded} excluded · marking is blind: AI scores are never shown here
            </p>
          </div>
          <Badge variant={coverage.meetsCoverage ? 'success' : 'warning'}>
            {coverage.meetsCoverage ? 'Coverage met' : 'More marking needed'}
          </Badge>
        </div>

        <dl className="grid gap-2 sm:grid-cols-3 lg:grid-cols-6">
          {GRADES.map((grade) => {
            const count = coverage.labelledByGrade[grade] ?? 0;
            return (
              <div key={grade} className="rounded-admin-md border border-admin-border px-3 py-2">
                <dt className="text-xs text-admin-fg-muted">Grade {grade}</dt>
                <dd className="text-sm font-semibold text-admin-fg-strong" data-testid={`coverage-grade-${grade}`}>
                  {count} <span className="font-normal text-admin-fg-muted">/ {coverage.requiredPerGrade}</span>
                </dd>
              </div>
            );
          })}
        </dl>

        <dl className="grid gap-2 sm:grid-cols-2 lg:grid-cols-4">
          <div className="rounded-admin-md border border-admin-border px-3 py-2">
            <dt className="text-xs text-admin-fg-muted">Near the pass line (320-380)</dt>
            <dd className="text-sm font-semibold text-admin-fg-strong" data-testid="coverage-near">
              {coverage.labelledNearPassLine} <span className="font-normal text-admin-fg-muted">/ {coverage.requiredNearPassLine}</span>
            </dd>
          </div>
          {coverage.requiredEachSideOfPassLine ? (
            <>
              <div className="rounded-admin-md border border-admin-border px-3 py-2">
                <dt className="text-xs text-admin-fg-muted">Just below 350 (320-340)</dt>
                <dd className="text-sm font-semibold text-admin-fg-strong" data-testid="coverage-below">
                  {coverage.labelledBelowPassLine ?? 0}{' '}
                  <span className="font-normal text-admin-fg-muted">/ {coverage.requiredEachSideOfPassLine}</span>
                </dd>
              </div>
              <div className="rounded-admin-md border border-admin-border px-3 py-2">
                <dt className="text-xs text-admin-fg-muted">350 or just above (350-380)</dt>
                <dd className="text-sm font-semibold text-admin-fg-strong" data-testid="coverage-above">
                  {coverage.labelledAtOrAbovePassLine ?? 0}{' '}
                  <span className="font-normal text-admin-fg-muted">/ {coverage.requiredEachSideOfPassLine}</span>
                </dd>
              </div>
            </>
          ) : null}
          <div className="rounded-admin-md border border-admin-border px-3 py-2">
            <dt className="text-xs text-admin-fg-muted">Marked performances with audio</dt>
            <dd className="text-sm font-semibold text-admin-fg-strong" data-testid="coverage-audio">
              {Math.round(coverage.audioShare * 100)}%{' '}
              <span className="font-normal text-admin-fg-muted">/ {Math.round(coverage.requiredAudioShare * 100)}% needed</span>
            </dd>
          </div>
        </dl>

        {coverage.unmet.length > 0 ? (
          <ul className="list-disc space-y-1 pl-5 text-xs text-admin-fg-muted" data-testid="coverage-unmet">
            {coverage.unmet.map((line) => (
              <li key={line}>{line}</li>
            ))}
          </ul>
        ) : null}
      </CardContent>
    </Card>
  );
}

export default function SpeakingGraderCalibrationPage() {
  const [tab, setTab] = useState<Tab>('set');
  const [overview, setOverview] = useState<GraderCalibrationOverview | null>(null);
  const [candidates, setCandidates] = useState<GraderCalibrationCandidate[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [promoting, setPromoting] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [nextOverview, nextCandidates] = await Promise.all([
        adminGetGraderCalibration(),
        adminListGraderCalibrationCandidates(),
      ]);
      setOverview(nextOverview);
      setCandidates(nextCandidates);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load the calibration set.');
    }
  }, []);

  useEffect(() => {
    void load();
  }, [load]);

  const samples = useMemo(
    () => [...(overview?.samples ?? [])].sort((a, b) => Number(b.status === 'pending') - Number(a.status === 'pending')),
    [overview],
  );
  const nextToMark = samples.find((sample) => sample.status === 'pending' && sample.usable !== false) ?? null;

  const promote = async (candidate: GraderCalibrationCandidate) => {
    const audioNote = candidate.hasAudio
      ? " This learner's audio will be kept for 365 days so it can be used for calibration."
      : ' It has no audio, so only the transcript can be used.';
    if (!window.confirm(`Add "${candidate.cardTitle}" to the calibration set?${audioNote}`)) return;
    setPromoting(candidate.sessionId);
    setError(null);
    setNotice(null);
    try {
      await adminPromoteGraderCalibrationSample(candidate.sessionId);
      setNotice('Added to the calibration set. It is ready to be marked.');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not add that performance.');
    } finally {
      setPromoting(null);
    }
  };

  const tabButton = (value: Tab, label: string) => (
    <button
      type="button"
      onClick={() => setTab(value)}
      aria-pressed={tab === value}
      className={`border-b-2 px-2 pb-2 text-sm font-semibold transition-colors ${
        tab === value ? 'border-admin-primary text-admin-fg-strong' : 'border-transparent text-admin-fg-muted hover:text-admin-fg-strong'
      }`}
    >
      {label}
    </button>
  );

  return (
    <AdminTableLayout
      title="Speaking grader calibration"
      description="Mark real role-plays yourself, without seeing the AI's score. The AI grader stays provisional until its marks have been compared with yours."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Speaking"
      icon={<Scale className="h-5 w-5" />}
      actions={nextToMark ? (
        <Button asChild variant="primary" size="sm">
          <Link href={`/admin/speaking/grader-calibration/${encodeURIComponent(nextToMark.id)}`}>Mark the next performance</Link>
        </Button>
      ) : undefined}
      banner={(
        <div className="space-y-3">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {notice ? <InlineAlert variant="success">{notice}</InlineAlert> : null}
          {overview ? <CoveragePanel coverage={overview.coverage} /> : <Skeleton className="h-40 w-full rounded-admin-lg" />}
        </div>
      )}
    >
      <div className="flex items-center gap-4 border-b border-admin-border px-4 pt-3">
        {tabButton('set', `Calibration set${overview ? ` (${overview.coverage.total})` : ''}`)}
        {tabButton('candidates', `Candidates${candidates ? ` (${candidates.length})` : ''}`)}
      </div>

      {tab === 'set' ? (
        !overview ? (
          <div className="p-6"><Skeleton className="h-48 w-full rounded-admin-lg" /></div>
        ) : samples.length === 0 ? (
          <div className="p-8 text-center text-sm text-admin-fg-muted">
            Nothing here yet. Open the Candidates tab and add finished AI role-plays to mark.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
                <tr>
                  <th scope="col" className="p-3">Role-play</th>
                  <th scope="col" className="p-3">Profession</th>
                  <th scope="col" className="p-3">Audio</th>
                  <th scope="col" className="p-3">Status</th>
                  <th scope="col" className="p-3">Your result</th>
                  <th scope="col" className="p-3">Added</th>
                  <th scope="col" className="p-3"><span className="sr-only">Action</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-admin-border">
                {samples.map((sample) => (
                  <tr key={sample.id} data-testid="calibration-sample-row">
                    <td className="p-3 font-medium text-admin-fg-strong">{sample.cardTitle || '(untitled)'}</td>
                    <td className="p-3 capitalize">{sample.professionId}</td>
                    <td className="p-3">{sample.hasAudio ? 'Yes' : 'No'}</td>
                    <td className="p-3"><StatusBadge status={sample.status} usable={sample.usable} /></td>
                    <td className="p-3 tabular-nums">
                      {sample.expertOverallScaled != null ? `${sample.expertOverallScaled} · ${sample.expertGrade}` : '-'}
                    </td>
                    <td className="p-3">{formatDate(sample.promotedAt)}</td>
                    <td className="p-3 text-right">
                      <Button asChild size="sm" variant={sample.status === 'pending' ? 'primary' : 'outline'}>
                        <Link href={`/admin/speaking/grader-calibration/${encodeURIComponent(sample.id)}`}>
                          {sample.status === 'pending' ? 'Mark' : sample.status === 'labelled' ? 'Edit marks' : 'Open'}
                        </Link>
                      </Button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )
      ) : !candidates ? (
        <div className="p-6"><Skeleton className="h-48 w-full rounded-admin-lg" /></div>
      ) : candidates.length === 0 ? (
        <div className="p-8 text-center text-sm text-admin-fg-muted">
          No eligible performances yet. Only finished AI role-plays recorded after the learner accepted the consent wording
          that covers quality assurance and grader calibration can be added.
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
              <tr>
                <th scope="col" className="p-3">Role-play</th>
                <th scope="col" className="p-3">Profession</th>
                <th scope="col" className="p-3">Finished</th>
                <th scope="col" className="p-3">Length</th>
                <th scope="col" className="p-3">Audio</th>
                <th scope="col" className="p-3"><span className="sr-only">Action</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-admin-border">
              {candidates.map((candidate) => (
                <tr key={candidate.sessionId} data-testid="calibration-candidate-row">
                  <td className="p-3 font-medium text-admin-fg-strong">{candidate.cardTitle || '(untitled)'}</td>
                  <td className="p-3 capitalize">{candidate.professionId}</td>
                  <td className="p-3">{formatDate(candidate.finishedAt)}</td>
                  <td className="p-3 tabular-nums">{formatMinutes(candidate.elapsedSeconds)}</td>
                  <td className="p-3">{candidate.hasAudio ? 'Yes' : 'No'}</td>
                  <td className="p-3 text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => void promote(candidate)}
                      loading={promoting === candidate.sessionId}
                      disabled={promoting !== null}
                    >
                      Add to calibration set
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </AdminTableLayout>
  );
}
