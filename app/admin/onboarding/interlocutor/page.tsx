'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, ClipboardList, GraduationCap, UserMinus, UserPlus } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { AdminOperationsLayout, KpiStrip } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Button } from '@/components/admin/ui/button';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { InlineAlert } from '@/components/ui/alert';
import { MotionSection } from '@/components/ui/motion-primitives';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Onboarding', href: '/admin/onboarding' },
  { label: 'Interlocutor' },
];
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchInterlocutorPracticeQueue,
  fetchInterlocutorTraineeList,
  markInterlocutorTrained,
  startInterlocutorPracticeSession,
  type InterlocutorPracticeQueueResponse,
  type InterlocutorPracticeQueueRow,
  type InterlocutorTraineeRow,
  type InterlocutorTraineesResponse,
  type InterlocutorTrainingStatusLabel,
} from '@/lib/api';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

type RowActionState = 'idle' | 'starting' | 'marking';

function formatDate(iso: string | null): string {
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

function formatSigma(sigma: number | null): string {
  if (sigma === null || !Number.isFinite(sigma)) return '—';
  return sigma.toFixed(2);
}

function statusBadge(status: InterlocutorTrainingStatusLabel) {
  if (status === 'Trained') {
    return (
      <Badge variant="success" className="text-[10px]">
        Trained
      </Badge>
    );
  }
  if (status === 'Failed') {
    return (
      <Badge variant="danger" className="text-[10px]">
        Failed
      </Badge>
    );
  }
  return (
    <Badge variant="warning" className="text-[10px]">
      In Progress
    </Badge>
  );
}

export default function AdminInterlocutorOnboardingPage() {
  const router = useRouter();
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [data, setData] = useState<InterlocutorTraineesResponse | null>(null);
  const [practiceQueue, setPracticeQueue] = useState<InterlocutorPracticeQueueResponse | null>(
    null,
  );
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [actionState, setActionState] = useState<Record<string, RowActionState>>({});
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const loadTrainees = useCallback(async () => {
    setStatus('loading');
    setErrorMessage(null);
    try {
      const [traineeResult, queueResult] = await Promise.all([
        fetchInterlocutorTraineeList(),
        fetchInterlocutorPracticeQueue(),
      ]);
      setData(traineeResult);
      setPracticeQueue(queueResult);
      const hasData =
        traineeResult.trainees.length > 0 || queueResult.recordings.length > 0;
      setStatus(hasData ? 'success' : 'empty');
    } catch (err) {
      console.error('[admin/onboarding/interlocutor] load failed', err);
      setErrorMessage(err instanceof Error ? err.message : 'Unable to load trainee data.');
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void loadTrainees();
  }, [isAuthenticated, role, loadTrainees]);

  const handleStartPractice = useCallback(
    async (trainee: InterlocutorTraineeRow) => {
      setActionState((prev) => ({ ...prev, [trainee.traineeId]: 'starting' }));
      try {
        const result = await startInterlocutorPracticeSession(trainee.traineeId);
        if (result.sessionId) {
          router.push(`/speaking/sessions/${encodeURIComponent(result.sessionId)}/prep`);
        } else if (result.prepHref) {
          router.push(result.prepHref);
        } else {
          setToast({
            variant: 'error',
            message: 'No practice session is currently available for this trainee.',
          });
        }
      } catch (err) {
        console.error('[admin/onboarding/interlocutor] start practice failed', err);
        setToast({
          variant: 'error',
          message:
            err instanceof Error ? err.message : 'Could not start a practice session right now.',
        });
      } finally {
        setActionState((prev) => ({ ...prev, [trainee.traineeId]: 'idle' }));
      }
    },
    [router],
  );

  const handleMarkTrained = useCallback(
    async (trainee: InterlocutorTraineeRow) => {
      setActionState((prev) => ({ ...prev, [trainee.traineeId]: 'marking' }));
      try {
        await markInterlocutorTrained(trainee.traineeId);
        setData((prev) => {
          if (!prev) return prev;
          const trainees = prev.trainees.map((t) =>
            t.traineeId === trainee.traineeId ? { ...t, status: 'Trained' as const } : t,
          );
          const totalTrained = trainees.filter((t) => t.status === 'Trained').length;
          const totalDroppedOff = trainees.filter((t) => t.status === 'Failed').length;
          const totalInOnboarding = Math.max(
            0,
            trainees.length - totalTrained - totalDroppedOff,
          );
          return { trainees, totalTrained, totalDroppedOff, totalInOnboarding };
        });
        setToast({
          variant: 'success',
          message: `${trainee.traineeName} marked as Trained.`,
        });
      } catch (err) {
        console.error('[admin/onboarding/interlocutor] mark trained failed', err);
        setToast({
          variant: 'error',
          message:
            err instanceof Error ? err.message : 'Could not mark this trainee as trained.',
        });
      } finally {
        setActionState((prev) => ({ ...prev, [trainee.traineeId]: 'idle' }));
      }
    },
    [],
  );

  const summary = useMemo(() => {
    if (!data) {
      return { inOnboarding: 0, trained: 0, droppedOff: 0 };
    }
    return {
      inOnboarding: data.totalInOnboarding,
      trained: data.totalTrained,
      droppedOff: data.totalDroppedOff,
    };
  }, [data]);

  const practicePending = practiceQueue?.totalPending ?? 0;

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminOperationsLayout
      title="Interlocutor onboarding"
      description="Track tutors moving through the calibration pipeline. Start a practice role-play with any trainee or fast-track them to Trained once their drift falls within policy."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Onboarding"
      icon={<GraduationCap className="h-5 w-5" />}
      kpis={status === 'success' && data ? (
        <KpiStrip className="lg:grid-cols-4">
          <KpiTile
            label="In onboarding"
            value={summary.inOnboarding}
            tone={summary.inOnboarding > 0 ? 'primary' : 'success'}
            icon={<UserPlus className="h-4 w-4" />}
          />
          <KpiTile
            label="Trained"
            value={summary.trained}
            tone={summary.trained > 0 ? 'success' : 'default'}
            icon={<CheckCircle2 className="h-4 w-4" />}
          />
          <KpiTile
            label="Dropped off"
            value={summary.droppedOff}
            tone={summary.droppedOff > 0 ? 'danger' : 'success'}
            icon={<UserMinus className="h-4 w-4" />}
          />
          <KpiTile
            label="Recordings under review"
            value={practicePending}
            tone={practicePending > 0 ? 'warning' : 'default'}
            icon={<ClipboardList className="h-4 w-4" />}
          />
        </KpiStrip>
      ) : undefined}
      primaryGrid={(
        <div className="space-y-6">
          {status === 'loading' ? (
            <>
              <Skeleton className="h-72 rounded-admin-lg" />
              <Skeleton className="h-48 rounded-admin-lg" />
            </>
          ) : null}

          {status === 'error' ? (
            <InlineAlert variant="error" title="Onboarding data unavailable">
              {errorMessage ?? 'The training service could not be reached. Please retry shortly.'}
            </InlineAlert>
          ) : null}

          {toast ? (
            <InlineAlert variant={toast.variant} dismissible>
              {toast.message}
            </InlineAlert>
          ) : null}

          {status === 'empty' ? (
            <Card>
              <CardHeader>
                <CardTitle className="text-sm">No trainees in onboarding</CardTitle>
              </CardHeader>
              <CardContent>
                <p className="text-sm text-admin-fg-muted">
                  Onboarding metrics populate as new tutors begin submitting calibration rubrics.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {status === 'success' && data ? (
            <>
              <MotionSection delayIndex={1}>
                <Card>
                  <CardHeader>
                    <CardTitle className="text-sm">Trainees</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <p className="text-xs text-admin-fg-muted mb-3">Per-trainee calibration progress derived from the drift report. Start a practice session, or mark a trainee Trained once σ falls below the policy threshold.</p>
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-admin-border text-sm">
                  <thead>
                    <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
                      <th scope="col" className="py-3 pr-4">Trainee</th>
                      <th scope="col" className="px-4 py-3">Started</th>
                      <th scope="col" className="px-4 py-3 text-right">Role-plays</th>
                      <th scope="col" className="px-4 py-3 text-right">Calibration σ</th>
                      <th scope="col" className="px-4 py-3">Status</th>
                      <th scope="col" className="px-4 py-3 text-right">Actions</th>
                    </tr>
                  </thead>
                  <tbody className="divide-y divide-admin-border/70">
                    {data.trainees.map((trainee) => {
                      const rowState = actionState[trainee.traineeId] ?? 'idle';
                      return (
                        <tr key={trainee.traineeId}>
                          <td className="py-3 pr-4">
                            <p className="font-semibold text-admin-text">{trainee.traineeName}</p>
                            <p className="text-xs text-admin-text-muted">{trainee.traineeId}</p>
                          </td>
                          <td className="px-4 py-3 text-admin-text-muted">
                            {formatDate(trainee.startedAt)}
                          </td>
                          <td className="px-4 py-3 text-right tabular-nums text-admin-text">
                            {trainee.rolePlaysCompleted}
                          </td>
                          <td className="px-4 py-3 text-right font-semibold tabular-nums text-admin-text">
                            {formatSigma(trainee.calibrationSigma)}
                          </td>
                          <td className="px-4 py-3">{statusBadge(trainee.status)}</td>
                          <td className="px-4 py-3">
                            <div className="flex flex-wrap items-center justify-end gap-2">
                              <Button
                                variant="outline"
                                size="sm"
                                disabled={rowState !== 'idle'}
                                onClick={() => {
                                  void handleStartPractice(trainee);
                                }}
                              >
                                {rowState === 'starting' ? 'Starting…' : 'Start practice session'}
                              </Button>
                              {trainee.status !== 'Trained' ? (
                                <Button
                                  variant="primary"
                                  size="sm"
                                  disabled={rowState !== 'idle'}
                                  onClick={() => {
                                    void handleMarkTrained(trainee);
                                  }}
                                >
                                  {rowState === 'marking' ? 'Saving…' : 'Mark trained'}
                                </Button>
                              ) : null}
                            </div>
                          </td>
                        </tr>
                      );
                    })}
                  </tbody>
                </table>
              </div>
                  </CardContent>
                </Card>
              </MotionSection>

              <MotionSection delayIndex={2}>
                <Card>
                  <CardHeader>
                    <CardTitle className="text-sm">Practice queue</CardTitle>
                  </CardHeader>
                  <CardContent>
                    <p className="text-xs text-admin-fg-muted mb-3">Pending interlocutor practice recordings awaiting calibration team review. The dedicated backend endpoint lands in a follow-up; until then this panel renders any rows the API surfaces and otherwise stays empty.</p>
                    <PracticeQueueList rows={practiceQueue?.recordings ?? []} />
                  </CardContent>
                </Card>
              </MotionSection>
            </>
          ) : null}
        </div>
      )}
    />
  );
}

function PracticeQueueList({ rows }: { rows: InterlocutorPracticeQueueRow[] }) {
  if (rows.length === 0) {
    return (
      <p className="text-sm text-admin-text-muted">
        No practice recordings are pending review. Submitted role-plays appear here once the
        practice-queue endpoint ships.
      </p>
    );
  }
  return (
    <div className="overflow-x-auto">
      <table className="min-w-full divide-y divide-admin-border text-sm">
        <thead>
          <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
            <th scope="col" className="py-3 pr-4">Trainee</th>
            <th scope="col" className="px-4 py-3">Recording</th>
            <th scope="col" className="px-4 py-3">Submitted</th>
            <th scope="col" className="px-4 py-3 text-right">Duration</th>
            <th scope="col" className="px-4 py-3">Status</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border/70">
          {rows.map((row) => (
            <tr key={row.recordingId}>
              <td className="py-3 pr-4">
                <p className="font-semibold text-admin-text">{row.traineeName}</p>
                <p className="text-xs text-admin-text-muted">{row.traineeId}</p>
              </td>
              <td className="px-4 py-3 text-admin-text-muted">
                <code className="rounded bg-admin-surface-raised px-1.5 py-0.5 text-xs">
                  {row.recordingId}
                </code>
              </td>
              <td className="px-4 py-3 text-admin-text-muted">
                {row.submittedAt ? new Date(row.submittedAt).toLocaleDateString() : '—'}
              </td>
              <td className="px-4 py-3 text-right tabular-nums text-admin-text">
                {Math.round(row.durationSeconds)}s
              </td>
              <td className="px-4 py-3">
                <Badge
                  variant={(
                    row.status === 'UnderReview'
                      ? 'warning'
                      : row.status === 'Returned'
                        ? 'danger'
                        : 'info'
                  ) as any}
                  className="text-[10px]"
                >
                  {row.status}
                </Badge>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}
