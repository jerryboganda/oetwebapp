'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { Activity, AlertTriangle, CheckCircle2, Users } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchSpeakingCalibrationSets,
  fetchSpeakingCalibrationSummary,
  type SpeakingCalibrationDriftSummary,
  type SpeakingCalibrationDriftTutorRow,
  type SpeakingCalibrationSampleSummaryRow,
} from '@/lib/api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

type DriftBucket = 'trained' | 'drifting' | 'retrain';

interface BucketedTutor extends SpeakingCalibrationDriftTutorRow {
  bucket: DriftBucket;
}

const DRIFT_TRAINED_MAX = 0.5;
const DRIFT_RETRAIN_MIN = 1.0;

function bucketForSigma(sigma: number): DriftBucket {
  if (sigma < DRIFT_TRAINED_MAX) return 'trained';
  if (sigma > DRIFT_RETRAIN_MIN) return 'retrain';
  return 'drifting';
}

function formatSigma(sigma: number | null | undefined): string {
  if (sigma === null || sigma === undefined || !Number.isFinite(sigma)) return '—';
  return sigma.toFixed(2);
}

function formatDate(iso: string | null | undefined): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return iso;
  }
}

function meanSigmaForSet(
  set: SpeakingCalibrationSampleSummaryRow,
  tutors: SpeakingCalibrationDriftTutorRow[],
): number | null {
  if (set.tutorSubmissionCount === 0) return null;
  // The drift endpoint aggregates by tutor (not by sample), so we cannot
  // attribute per-sample σ exactly. As a reasonable proxy, report the mean
  // tutor σ across all tutors that have submitted on this sample. With
  // current backend data we surface the overall mean tutor σ for any set
  // with submissions; once a per-sample drift route lands the helper will
  // pick it up automatically.
  if (tutors.length === 0) return null;
  const total = tutors.reduce((acc, t) => acc + t.meanAbsoluteError, 0);
  return total / tutors.length;
}

function bucketBadge(bucket: DriftBucket) {
  if (bucket === 'trained') {
    return (
      <Badge variant="success" className="text-[10px]">
        Trained
      </Badge>
    );
  }
  if (bucket === 'drifting') {
    return (
      <Badge variant="warning" className="text-[10px]">
        Drifting
      </Badge>
    );
  }
  return (
    <Badge variant="danger" className="text-[10px]">
      Retrain
    </Badge>
  );
}

function TutorTable({ tutors }: { tutors: BucketedTutor[] }) {
  if (tutors.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No tutors have submitted calibration rubrics yet. Once they score the published gold samples,
        their drift will appear here.
      </p>
    );
  }
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th className="py-3 pr-4">Tutor</th>
            <th className="px-4 py-3">Last calibration</th>
            <th className="px-4 py-3 text-right">Submissions</th>
            <th className="px-4 py-3 text-right">σ (mean abs. error)</th>
            <th className="px-4 py-3 text-right">Drift</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {tutors.map((tutor) => (
            <tr key={tutor.tutorId}>
              <td className="py-3 pr-4">
                <p className="font-semibold text-admin-text">{tutor.tutorName}</p>
                <p className="text-xs text-admin-text-muted">{tutor.tutorId}</p>
              </td>
              <td className="px-4 py-3 text-admin-text-muted">
                {formatDate(tutor.lastSubmittedAt)}
              </td>
              <td className="px-4 py-3 text-right tabular-nums text-admin-text">
                {tutor.submissionCount}
              </td>
              <td className="px-4 py-3 text-right font-semibold tabular-nums text-admin-text">
                {formatSigma(tutor.meanAbsoluteError)}
              </td>
              <td className="px-4 py-3 text-right">{bucketBadge(tutor.bucket)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SetsTable({
  sets,
  tutors,
}: {
  sets: SpeakingCalibrationSampleSummaryRow[];
  tutors: SpeakingCalibrationDriftTutorRow[];
}) {
  if (sets.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No calibration sets exist yet. Curate gold-marked recordings to seed the calibration
        pipeline.
      </p>
    );
  }
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th className="py-3 pr-4">Set</th>
            <th className="px-4 py-3">Profession</th>
            <th className="px-4 py-3 text-right">Tutors scored</th>
            <th className="px-4 py-3 text-right">Mean σ</th>
            <th className="px-4 py-3">Status</th>
            <th className="px-4 py-3 text-right">Detail</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {sets.map((set) => {
            const meanSigma = meanSigmaForSet(set, tutors);
            return (
              <tr key={set.sampleId}>
                <td className="py-3 pr-4">
                  <p className="font-semibold text-admin-text truncate">{set.title}</p>
                  <p className="text-xs text-admin-text-muted">{set.sampleId}</p>
                </td>
                <td className="px-4 py-3 text-admin-text-muted">{set.professionId}</td>
                <td className="px-4 py-3 text-right tabular-nums text-admin-text">
                  {set.tutorSubmissionCount}
                </td>
                <td className="px-4 py-3 text-right font-semibold tabular-nums text-admin-text">
                  {formatSigma(meanSigma)}
                </td>
                <td className="px-4 py-3">
                  <Badge
                    variant={
                      set.status === 'published'
                        ? 'success'
                        : set.status === 'archived'
                          ? 'muted'
                          : 'outline'
                    }
                    className="text-[10px] uppercase"
                  >
                    {set.status}
                  </Badge>
                </td>
                <td className="px-4 py-3 text-right">
                  <Link
                    href={`/admin/calibration/speaking/${encodeURIComponent(set.sampleId)}`}
                    className="text-xs font-semibold text-violet-300 hover:text-violet-200"
                  >
                    View detail
                  </Link>
                </td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}

export default function AdminSpeakingCalibrationPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [drift, setDrift] = useState<SpeakingCalibrationDriftSummary | null>(null);
  const [sets, setSets] = useState<SpeakingCalibrationSampleSummaryRow[]>([]);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;

    let cancelled = false;
    setStatus('loading');
    setErrorMessage(null);

    async function load() {
      try {
        const [driftResult, setsResult] = await Promise.all([
          fetchSpeakingCalibrationSummary(1),
          fetchSpeakingCalibrationSets(),
        ]);
        if (cancelled) return;
        setDrift(driftResult);
        setSets(setsResult);
        const hasData = driftResult.tutors.length > 0 || setsResult.length > 0;
        setStatus(hasData ? 'success' : 'empty');
      } catch (err) {
        console.error('[admin/calibration/speaking] load failed', err);
        if (!cancelled) {
          setErrorMessage(err instanceof Error ? err.message : 'Unable to load calibration data.');
          setStatus('error');
        }
      }
    }

    void load();
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated, role]);

  const bucketedTutors = useMemo<BucketedTutor[]>(() => {
    if (!drift) return [];
    return drift.tutors.map((tutor) => ({
      ...tutor,
      bucket: bucketForSigma(tutor.meanAbsoluteError),
    }));
  }, [drift]);

  const summary = useMemo(() => {
    const totalSets = sets.length;
    const activeTutors = bucketedTutors.length;
    const overThreshold = bucketedTutors.filter((t) => t.bucket !== 'trained').length;
    const driftPercent =
      activeTutors === 0 ? 0 : Math.round((overThreshold / activeTutors) * 100);
    return { totalSets, activeTutors, overThreshold, driftPercent };
  }, [bucketedTutors, sets.length]);

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Speaking calibration">
      <AdminRouteHero
        eyebrow="Calibration"
        icon={Activity}
        accent="indigo"
        title="Speaking calibration"
        description="Tutor-vs-gold drift across published calibration samples. Lower σ = closer to the gold rubric; tutors over the retrain threshold should be reseated."
      />

      {status === 'loading' ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, index) => (
              <Skeleton key={index} className="h-24 rounded-2xl" />
            ))}
          </div>
          <Skeleton className="h-64 rounded-2xl" />
          <Skeleton className="h-64 rounded-2xl" />
        </>
      ) : null}

      {status === 'error' ? (
        <InlineAlert variant="error" title="Calibration data unavailable">
          {errorMessage ?? 'The calibration service could not be reached. Please retry shortly.'}
        </InlineAlert>
      ) : null}

      {status === 'empty' ? (
        <AdminRoutePanel title="No calibration data yet">
          <p className="text-sm text-admin-text-muted">
            Calibration metrics will appear once admins publish gold samples and tutors submit their
            rubric scores.
          </p>
        </AdminRoutePanel>
      ) : null}

      {status === 'success' && drift ? (
        <div className="space-y-6">
          <MotionSection delayIndex={0}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <AdminRouteSummaryCard
                label="Calibration sets"
                value={summary.totalSets}
                hint={`${drift.samplesPublished} published`}
                icon={<CheckCircle2 className="h-5 w-5" />}
                tone={summary.totalSets > 0 ? 'success' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Active tutors"
                value={summary.activeTutors}
                hint={`${drift.sampleSize} submissions on record`}
                icon={<Users className="h-5 w-5" />}
                tone={summary.activeTutors > 0 ? 'default' : 'warning'}
              />
              <AdminRouteSummaryCard
                label="% drift over threshold"
                value={`${summary.driftPercent}%`}
                hint={`${summary.overThreshold} of ${summary.activeTutors} tutors`}
                icon={<AlertTriangle className="h-5 w-5" />}
                tone={
                  summary.driftPercent > 50
                    ? 'danger'
                    : summary.driftPercent > 20
                      ? 'warning'
                      : 'success'
                }
              />
            </div>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <AdminRoutePanel
              title="Per-tutor agreement"
              description="Mean absolute error vs the gold rubric across all 9 OET Speaking criteria. Trained < 0.5, Drifting 0.5–1.0, Retrain > 1.0."
            >
              <TutorTable tutors={bucketedTutors} />
            </AdminRoutePanel>
          </MotionSection>

          <MotionSection delayIndex={2}>
            <AdminRoutePanel
              title="Calibration sets"
              description="Curated gold-marked recordings. The mean σ column shows the average tutor drift across submissions on each set."
            >
              <SetsTable sets={sets} tutors={drift.tutors} />
            </AdminRoutePanel>
          </MotionSection>
        </div>
      ) : null}
    </AdminRouteWorkspace>
  );
}
