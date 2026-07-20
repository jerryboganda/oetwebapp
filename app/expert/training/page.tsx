'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { BookOpenCheck, CheckCircle2, GraduationCap, ListChecks, ShieldCheck } from 'lucide-react';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import {
  ExpertRouteHero,
  ExpertRouteSectionHeader,
  ExpertRouteWorkspace,
} from '@/components/domain/expert-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card, CardContent } from '@/components/ui/card';
import { Input } from '@/components/ui/form-controls';
import { MarkdownContent } from '@/components/ui/markdown-content';
import { Toast } from '@/components/ui/alert';
import { isApiError } from '@/lib/api';
import {
  tutorListInterlocutorTraining,
  tutorCompleteInterlocutorModule,
  type TutorInterlocutorTrainingModule,
  type TutorInterlocutorTrainingState,
} from '@/lib/api/interlocutor-training';

type AsyncStatus = 'loading' | 'error' | 'success';
type ToastState = { variant: 'success' | 'error'; message: string } | null;

// Stage order mirrors the backend ordering (`OrderBy(m => m.Stage)`), so the
// checklist reads the same way in both surfaces.
const STAGE_SECTIONS: { stage: string; title: string; description: string }[] = [
  {
    stage: 'Onboarding',
    title: 'Onboarding modules',
    description: 'Work through these before you are added to the live calibration pool.',
  },
  {
    stage: 'Refresher',
    title: 'Refresher modules',
    description: 'Periodic top-ups that keep role-play delivery aligned with the current rubric.',
  },
];

function formatDate(iso: string | null): string {
  if (!iso) return '-';
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

function ModuleCard({
  module,
  busy,
  onComplete,
}: {
  module: TutorInterlocutorTrainingModule;
  busy: boolean;
  onComplete: (moduleId: string, quizScore: number | null) => void;
}) {
  const [quizScore, setQuizScore] = useState('');
  const [scoreError, setScoreError] = useState<string | null>(null);

  function handleComplete() {
    const raw = quizScore.trim();
    if (!raw) {
      // `quizScore` is optional on the backend — an empty field records the
      // completion without a score rather than defaulting to zero.
      onComplete(module.id, null);
      return;
    }
    const parsed = Number.parseInt(raw, 10);
    if (!Number.isFinite(parsed) || parsed < 0 || parsed > 100) {
      setScoreError('Enter a whole number between 0 and 100, or leave blank.');
      return;
    }
    setScoreError(null);
    onComplete(module.id, parsed);
  }

  return (
    <Card>
      <CardContent className="space-y-4 p-5">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0 flex-1 space-y-1">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-sm font-bold text-navy">{module.title}</p>
              {module.requiredForCalibration ? (
                <Badge variant="warning">Required</Badge>
              ) : (
                <Badge variant="muted">Optional</Badge>
              )}
              {module.isCompleted ? <Badge variant="success">Completed</Badge> : null}
            </div>
            {module.isCompleted ? (
              <p className="text-xs text-muted">
                Completed {formatDate(module.completedAt)}
                {module.quizScore !== null ? ` · quiz score ${module.quizScore}` : ''}
              </p>
            ) : (
              <p className="text-xs text-muted">Not completed yet.</p>
            )}
          </div>
          {module.isCompleted ? (
            <CheckCircle2 className="h-5 w-5 shrink-0 text-emerald-500" aria-hidden="true" />
          ) : null}
        </div>

        {module.contentMarkdown ? (
          <div className="rounded-2xl border border-border bg-background-light px-4 py-3 text-sm text-navy">
            <MarkdownContent markdown={module.contentMarkdown} />
          </div>
        ) : null}

        {!module.isCompleted ? (
          <div className="flex flex-wrap items-end gap-3">
            <div className="w-44">
              <Input
                type="number"
                min={0}
                max={100}
                label="Quiz score (optional)"
                value={quizScore}
                onChange={(event) => {
                  setQuizScore(event.target.value);
                  setScoreError(null);
                }}
                placeholder="0-100"
                error={scoreError ?? undefined}
                disabled={busy}
              />
            </div>
            <Button size="sm" onClick={handleComplete} disabled={busy}>
              {busy ? 'Saving…' : 'Mark complete'}
            </Button>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

export default function ExpertInterlocutorTrainingPage() {
  const [state, setState] = useState<TutorInterlocutorTrainingState | null>(null);
  const [pageStatus, setPageStatus] = useState<AsyncStatus>('loading');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [retryKey, setRetryKey] = useState(0);
  const [busyId, setBusyId] = useState<string | null>(null);
  const [toast, setToast] = useState<ToastState>(null);

  useEffect(() => {
    let cancelled = false;
    async function load() {
      try {
        setPageStatus('loading');
        setErrorMessage(null);
        const data = await tutorListInterlocutorTraining();
        if (cancelled) return;
        setState(data);
        setPageStatus('success');
      } catch (error) {
        if (cancelled) return;
        setErrorMessage(
          isApiError(error) ? error.userMessage : 'Unable to load your training modules right now.',
        );
        setPageStatus('error');
      }
    }
    void load();
    return () => { cancelled = true; };
  }, [retryKey]);

  // Post-completion refetch: `isEligibleForLiveRooms` is derived server-side
  // from required-module completions, so it cannot be patched optimistically.
  // Failures here are non-fatal — the optimistic patch already landed.
  const refreshSilently = useCallback(async () => {
    try {
      setState(await tutorListInterlocutorTraining());
    } catch {
      // Keep the optimistic state; the next load will reconcile.
    }
  }, []);

  const handleComplete = useCallback(
    async (moduleId: string, quizScore: number | null) => {
      setBusyId(moduleId);
      try {
        const result = await tutorCompleteInterlocutorModule(
          moduleId,
          quizScore === null ? undefined : { quizScore },
        );
        // Optimistic patch from the server response, then a silent refetch so
        // `isEligibleForLiveRooms` reflects the new completion state.
        setState((prev) => {
          if (!prev) return prev;
          return {
            ...prev,
            modules: prev.modules.map((m) =>
              m.id === moduleId
                ? {
                    ...m,
                    completedAt: result.completedAt,
                    quizScore: result.quizScore,
                    isCompleted: result.completedAt !== null,
                  }
                : m,
            ),
          };
        });
        setToast({ variant: 'success', message: 'Module marked complete.' });
        await refreshSilently();
      } catch (error) {
        setToast({
          variant: 'error',
          message: isApiError(error)
            ? error.userMessage
            : 'Could not record this completion. Please try again.',
        });
      } finally {
        setBusyId(null);
      }
    },
    [refreshSilently],
  );

  const modules = useMemo(() => state?.modules ?? [], [state]);

  const summary = useMemo(() => {
    const completed = modules.filter((m) => m.isCompleted).length;
    const requiredOutstanding = modules.filter(
      (m) => m.requiredForCalibration && !m.isCompleted,
    ).length;
    return { completed, total: modules.length, requiredOutstanding };
  }, [modules]);

  // Anything the backend returns with an unrecognised stage still needs a home,
  // so trailing stages are collected into a final "Other" section.
  const knownStages = STAGE_SECTIONS.map((section) => section.stage);
  const otherModules = modules.filter((m) => !knownStages.includes(String(m.stage)));

  return (
    <ExpertRouteWorkspace role="main" aria-label="Interlocutor Training">
      <AsyncStateWrapper
        status={pageStatus}
        onRetry={() => setRetryKey((k) => k + 1)}
        errorMessage={errorMessage ?? undefined}
      >
        <div className="space-y-6">
          <ExpertRouteHero
            eyebrow="Interlocutor Training"
            icon={GraduationCap}
            accent="primary"
            title="Your interlocutor training checklist"
            description="Published training modules prepare you to run OET Speaking role-plays as the interlocutor. Modules marked required must be completed before you can be added to a live calibration room."
            highlights={[
              { icon: ListChecks, label: 'Completed', value: `${summary.completed}/${summary.total}` },
              { icon: BookOpenCheck, label: 'Required outstanding', value: String(summary.requiredOutstanding) },
              {
                icon: ShieldCheck,
                label: 'Live rooms',
                value: state?.isEligibleForLiveRooms ? 'Eligible' : 'Not yet eligible',
              },
            ]}
          />

          {modules.length === 0 ? (
            <Card>
              <CardContent className="space-y-2 p-6 text-center">
                <p className="text-sm font-bold text-navy">No training modules published yet</p>
                <p className="text-sm text-muted">
                  Your training checklist appears here as soon as the calibration team publishes the
                  first interlocutor module. Nothing is outstanding on your side right now.
                </p>
              </CardContent>
            </Card>
          ) : null}

          {STAGE_SECTIONS.map((section) => {
            const stageModules = modules.filter((m) => String(m.stage) === section.stage);
            if (stageModules.length === 0) return null;
            const stageCompleted = stageModules.filter((m) => m.isCompleted).length;
            return (
              <section key={section.stage} className="space-y-4">
                <ExpertRouteSectionHeader
                  eyebrow={`${stageCompleted}/${stageModules.length} complete`}
                  title={section.title}
                  description={section.description}
                />
                <div className="space-y-4">
                  {stageModules.map((module) => (
                    <ModuleCard
                      key={module.id}
                      module={module}
                      busy={busyId === module.id}
                      onComplete={(id, score) => void handleComplete(id, score)}
                    />
                  ))}
                </div>
              </section>
            );
          })}

          {otherModules.length > 0 ? (
            <section className="space-y-4">
              <ExpertRouteSectionHeader
                eyebrow="Additional"
                title="Other modules"
                description="Training modules published under a stage this console does not group explicitly."
              />
              <div className="space-y-4">
                {otherModules.map((module) => (
                  <ModuleCard
                    key={module.id}
                    module={module}
                    busy={busyId === module.id}
                    onComplete={(id, score) => void handleComplete(id, score)}
                  />
                ))}
              </div>
            </section>
          ) : null}
        </div>
      </AsyncStateWrapper>

      {toast ? (
        <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
      ) : null}
    </ExpertRouteWorkspace>
  );
}
