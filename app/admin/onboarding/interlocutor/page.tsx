'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, GraduationCap, UserMinus, UserPlus } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { MotionSection } from '@/components/ui/motion-primitives';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  fetchInterlocutorTrainees,
  markInterlocutorTrained,
  startInterlocutorPracticeSession,
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
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [actionState, setActionState] = useState<Record<string, RowActionState>>({});
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const loadTrainees = useCallback(async () => {
    setStatus('loading');
    setErrorMessage(null);
    try {
      const result = await fetchInterlocutorTrainees();
      setData(result);
      setStatus(result.trainees.length > 0 ? 'success' : 'empty');
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

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Interlocutor onboarding">
      <AdminRouteHero
        eyebrow="Onboarding"
        icon={GraduationCap}
        accent="indigo"
        title="Interlocutor onboarding"
        description="Track tutors moving through the calibration pipeline. Start a practice role-play with any trainee or fast-track them to Trained once their drift falls within policy."
      />

      {status === 'loading' ? (
        <>
          <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
            {Array.from({ length: 3 }).map((_, index) => (
              <Skeleton key={index} className="h-24 rounded-2xl" />
            ))}
          </div>
          <Skeleton className="h-72 rounded-2xl" />
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
        <AdminRoutePanel title="No trainees in onboarding">
          <p className="text-sm text-admin-text-muted">
            Onboarding metrics populate as new tutors begin submitting calibration rubrics.
          </p>
        </AdminRoutePanel>
      ) : null}

      {status === 'success' && data ? (
        <div className="space-y-6">
          <MotionSection delayIndex={0}>
            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-3">
              <AdminRouteSummaryCard
                label="In onboarding"
                value={summary.inOnboarding}
                hint="Active in the queue"
                icon={<UserPlus className="h-5 w-5" />}
                tone={summary.inOnboarding > 0 ? 'default' : 'success'}
              />
              <AdminRouteSummaryCard
                label="Trained"
                value={summary.trained}
                hint="Cleared the calibration bar"
                icon={<CheckCircle2 className="h-5 w-5" />}
                tone={summary.trained > 0 ? 'success' : 'default'}
              />
              <AdminRouteSummaryCard
                label="Dropped off"
                value={summary.droppedOff}
                hint="Failed to converge"
                icon={<UserMinus className="h-5 w-5" />}
                tone={summary.droppedOff > 0 ? 'danger' : 'success'}
              />
            </div>
          </MotionSection>

          <MotionSection delayIndex={1}>
            <AdminRoutePanel
              title="Trainees"
              description="Per-trainee calibration progress derived from the drift report. Start a practice session, or mark a trainee Trained once σ falls below the policy threshold."
            >
              <div className="overflow-x-auto">
                <table className="min-w-full divide-y divide-admin-border text-sm">
                  <thead>
                    <tr className="text-left text-xs font-bold uppercase tracking-[0.14em] text-admin-text-muted">
                      <th className="py-3 pr-4">Trainee</th>
                      <th className="px-4 py-3">Started</th>
                      <th className="px-4 py-3 text-right">Role-plays</th>
                      <th className="px-4 py-3 text-right">Calibration σ</th>
                      <th className="px-4 py-3">Status</th>
                      <th className="px-4 py-3 text-right">Actions</th>
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
            </AdminRoutePanel>
          </MotionSection>
        </div>
      ) : null}
    </AdminRouteWorkspace>
  );
}
