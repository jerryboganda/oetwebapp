'use client';

/**
 * Admin · Speaking · Grader calibration (owner spec 4 Oct 2026; Full Mocks added 7 Oct 2026).
 *
 * The AI Speaking grader keeps a "Provisional" label until its scores have been compared with an OET
 * expert's own marks. This page is where finished AI role-plays (and whole two-card Full Mocks) are
 * promoted into the calibration set and where Dr Hesham's marking progress is tracked against the
 * coverage the approved validation report needs. Nothing on it ever shows an AI score — marking is blind.
 * An owner pilot needs none of the coverage: it can run as soon as the performances to compare are marked.
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
  adminGetGraderCalibrationMocks,
  adminListGraderCalibrationCandidates,
  adminListGraderCalibrationMockCandidates,
  adminPromoteGraderCalibrationMock,
  adminPromoteGraderCalibrationSample,
  type GraderCalibrationCandidate,
  type GraderCalibrationCoverage,
  type GraderCalibrationMockCandidate,
  type GraderCalibrationMockOverview,
  type GraderCalibrationOverview,
  type GraderCalibrationSampleRow,
} from '@/lib/api/speaking-grader-calibration';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Speaking', href: '/admin/speaking' },
  { label: 'Grader calibration' },
];

const GRADES = ['A', 'B', 'C+', 'C', 'D', 'E'] as const;

type Kind = 'cards' | 'mocks';
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

function CoveragePanel({ coverage, pilotNote }: { coverage: GraderCalibrationCoverage; pilotNote?: boolean }) {
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

        {pilotNote ? (
          <p className="rounded-admin-md border border-admin-border bg-admin-bg-subtle px-3 py-2 text-xs text-admin-fg-muted" data-testid="coverage-pilot-note">
            This coverage is what the approved <strong>validation</strong> run needs — an <strong>owner pilot</strong> needs none of it.
            As soon as the performances you want to compare are marked, the harness can grade them and show the comparison
            (a pilot&apos;s report is informational and its verdict cannot pass; the Speaking score stays Provisional).
          </p>
        ) : null}

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
  const [kind, setKind] = useState<Kind>('cards');
  const [tab, setTab] = useState<Tab>('set');
  const [overview, setOverview] = useState<GraderCalibrationOverview | null>(null);
  const [candidates, setCandidates] = useState<GraderCalibrationCandidate[] | null>(null);
  const [mockOverview, setMockOverview] = useState<GraderCalibrationMockOverview | null>(null);
  const [mockCandidates, setMockCandidates] = useState<GraderCalibrationMockCandidate[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [promoting, setPromoting] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError(null);
    try {
      const [nextOverview, nextCandidates, nextMockOverview, nextMockCandidates] = await Promise.all([
        adminGetGraderCalibration(),
        adminListGraderCalibrationCandidates(),
        adminGetGraderCalibrationMocks(),
        adminListGraderCalibrationMockCandidates(),
      ]);
      setOverview(nextOverview);
      setCandidates(nextCandidates);
      setMockOverview(nextMockOverview);
      setMockCandidates(nextMockCandidates);
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

  const mockSamples = useMemo(
    () => [...(mockOverview?.samples ?? [])].sort((a, b) => Number(b.status === 'pending') - Number(a.status === 'pending')),
    [mockOverview],
  );
  const nextMockToMark = mockSamples.find((sample) => sample.status === 'pending' && sample.usable !== false) ?? null;

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

  const promoteMock = async (candidate: GraderCalibrationMockCandidate) => {
    const audioNote = candidate.hasAudio
      ? " This learner's audio will be kept for 365 days so it can be used for calibration."
      : ' At least one card has no audio, so the audio judge cannot be used on it.';
    if (!window.confirm(`Add this Full Mock ("${candidate.cardATitle}" + "${candidate.cardBTitle}") to the calibration set as ONE performance?${audioNote}`)) return;
    setPromoting(candidate.examId);
    setError(null);
    setNotice(null);
    try {
      await adminPromoteGraderCalibrationMock(candidate.examId);
      setNotice('Added to the Full Mock calibration set. It is ready to be marked as one test.');
      await load();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not add that Full Mock.');
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

  const kindSwitch = (value: Kind, label: string) => (
    <button
      type="button"
      onClick={() => setKind(value)}
      aria-pressed={kind === value}
      data-testid={`kind-switch-${value}`}
      className={`rounded-admin-md px-3 py-1.5 text-sm font-semibold transition-colors ${
        kind === value ? 'bg-admin-primary text-white' : 'border border-admin-border text-admin-fg-muted hover:text-admin-fg-strong'
      }`}
    >
      {label}
    </button>
  );

  const loadingSet = kind === 'cards' ? !overview : !mockOverview;
  const loadingCandidates = kind === 'cards' ? !candidates : !mockCandidates;
  const setCount = kind === 'cards' ? overview?.coverage.total : mockOverview?.coverage.total;
  const candidateCount = kind === 'cards' ? candidates?.length : mockCandidates?.length;

  return (
    <AdminTableLayout
      title="Speaking grader calibration"
      description="Mark real role-plays yourself, without seeing the AI's score. The AI grader stays provisional until its marks have been compared with yours."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Speaking"
      icon={<Scale className="h-5 w-5" />}
      actions={(
        <>
          <Button asChild variant="outline" size="sm">
            <Link href="/admin/speaking/grader-calibration/runs">Comparison runs (shows AI results)</Link>
          </Button>
          {kind === 'cards'
            ? (nextToMark ? (
              <Button asChild variant="primary" size="sm">
                <Link href={`/admin/speaking/grader-calibration/${encodeURIComponent(nextToMark.id)}`}>Mark the next performance</Link>
              </Button>
            ) : null)
            : (nextMockToMark ? (
              <Button asChild variant="primary" size="sm">
                <Link href={`/admin/speaking/grader-calibration/mock/${encodeURIComponent(nextMockToMark.id)}`}>Mark the next Full Mock</Link>
              </Button>
            ) : null)}
        </>
      )}
      banner={(
        <div className="space-y-3">
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {notice ? <InlineAlert variant="success">{notice}</InlineAlert> : null}
          {kind === 'cards'
            ? (overview ? <CoveragePanel coverage={overview.coverage} pilotNote /> : <Skeleton className="h-40 w-full rounded-admin-lg" />)
            : (mockOverview ? <CoveragePanel coverage={mockOverview.coverage} pilotNote /> : <Skeleton className="h-40 w-full rounded-admin-lg" />)}
        </div>
      )}
    >
      <div className="flex items-center gap-2 border-b border-admin-border px-4 pt-3" data-testid="kind-switch">
        {kindSwitch('cards', 'Single cards')}
        {kindSwitch('mocks', 'Full Mocks')}
      </div>
      <div className="flex items-center gap-4 border-b border-admin-border px-4 pt-3">
        {tabButton('set', `${kind === 'cards' ? 'Calibration set' : 'Full Mock set'}${setCount != null ? ` (${setCount})` : ''}`)}
        {tabButton('candidates', `Candidates${candidateCount != null ? ` (${candidateCount})` : ''}`)}
      </div>

      {kind === 'cards' ? (
        tab === 'set' ? (
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
        )
      ) : tab === 'set' ? (
        !mockOverview ? (
          <div className="p-6"><Skeleton className="h-48 w-full rounded-admin-lg" /></div>
        ) : mockSamples.length === 0 ? (
          <div className="p-8 text-center text-sm text-admin-fg-muted">
            No Full Mocks in the calibration set yet. Open the Candidates tab and add completed two-card Full Mocks to mark
            each as ONE test.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
                <tr>
                  <th scope="col" className="p-3">Card A</th>
                  <th scope="col" className="p-3">Card B</th>
                  <th scope="col" className="p-3">Profession</th>
                  <th scope="col" className="p-3">Audio</th>
                  <th scope="col" className="p-3">Status</th>
                  <th scope="col" className="p-3">Your result</th>
                  <th scope="col" className="p-3">Added</th>
                  <th scope="col" className="p-3"><span className="sr-only">Action</span></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-admin-border">
                {mockSamples.map((sample) => (
                  <tr key={sample.id} data-testid="calibration-mock-row">
                    <td className="p-3 font-medium text-admin-fg-strong">{sample.cardATitle || '(untitled)'}</td>
                    <td className="p-3 font-medium text-admin-fg-strong">{sample.cardBTitle || '(untitled)'}</td>
                    <td className="p-3 capitalize">{sample.professionId}</td>
                    <td className="p-3">{sample.hasAudio ? 'Both cards' : 'Not both'}</td>
                    <td className="p-3"><StatusBadge status={sample.status} usable={sample.usable} /></td>
                    <td className="p-3 tabular-nums">
                      {sample.expertOverallScaled != null ? `${sample.expertOverallScaled} · ${sample.expertGrade}` : '-'}
                    </td>
                    <td className="p-3">{formatDate(sample.promotedAt)}</td>
                    <td className="p-3 text-right">
                      <Button asChild size="sm" variant={sample.status === 'pending' ? 'primary' : 'outline'}>
                        <Link href={`/admin/speaking/grader-calibration/mock/${encodeURIComponent(sample.id)}`}>
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
      ) : !mockCandidates ? (
        <div className="p-6"><Skeleton className="h-48 w-full rounded-admin-lg" /></div>
      ) : mockCandidates.length === 0 ? (
        <div className="p-8 text-center text-sm text-admin-fg-muted">
          No eligible Full Mocks yet. Only completed two-card AI Full Mocks recorded after the learner accepted the consent
          wording that covers quality assurance and grader calibration can be added. Your pilot testers just use Speaking
          normally — no special link or consent is needed.
        </div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
              <tr>
                <th scope="col" className="p-3">Card A</th>
                <th scope="col" className="p-3">Card B</th>
                <th scope="col" className="p-3">Profession</th>
                <th scope="col" className="p-3">Finished</th>
                <th scope="col" className="p-3">Audio</th>
                <th scope="col" className="p-3"><span className="sr-only">Action</span></th>
              </tr>
            </thead>
            <tbody className="divide-y divide-admin-border">
              {mockCandidates.map((candidate) => (
                <tr key={candidate.examId} data-testid="calibration-mock-candidate-row">
                  <td className="p-3 font-medium text-admin-fg-strong">{candidate.cardATitle || '(untitled)'}</td>
                  <td className="p-3 font-medium text-admin-fg-strong">{candidate.cardBTitle || '(untitled)'}</td>
                  <td className="p-3 capitalize">{candidate.professionId}</td>
                  <td className="p-3">{formatDate(candidate.finishedAt)}</td>
                  <td className="p-3">{candidate.hasAudio ? 'Both cards' : 'Not both'}</td>
                  <td className="p-3 text-right">
                    <Button
                      size="sm"
                      variant="outline"
                      onClick={() => void promoteMock(candidate)}
                      loading={promoting === candidate.examId}
                      disabled={promoting !== null}
                    >
                      Add as one Full Mock
                    </Button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {loadingSet && tab === 'set' ? <div className="sr-only">Loading</div> : null}
      {loadingCandidates && tab === 'candidates' ? <div className="sr-only">Loading</div> : null}
    </AdminTableLayout>
  );
}
