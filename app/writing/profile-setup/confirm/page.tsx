'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { CheckCircle2, ArrowLeft, ClipboardCheck } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { completeWritingOnboarding, saveWritingV2Profile } from '@/lib/writing/api';
import { writingProfileSchema } from '@/lib/writing/zod';
import { clearWizardState, readWizardState, type WritingProfileWizardState } from '../wizard-state';
import { StepperNav } from '../StepperNav';

export default function ProfileSetupConfirmPage() {
  const router = useRouter();
  const [state, setState] = useState<WritingProfileWizardState | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  useEffect(() => {
    setState(readWizardState());
  }, []);

  const onSubmit = async (e: FormEvent) => {
    e.preventDefault();
    if (!state) return;
    setSubmitting(true);
    setError(null);
    const examDateIso = state.examDate ?? null;
    const candidate = {
      profession: state.profession,
      subDiscipline: state.subDiscipline || null,
      yearsExperience: state.yearsExperience,
      targetBand: state.targetBand,
      examDate: examDateIso,
      daysPerWeek: state.daysPerWeek,
      minutesPerDay: state.minutesPerDay,
      targetCountry: state.targetCountry,
      letterTypeFocus: state.letterTypeFocus,
      optInCommunity: state.optInCommunity,
      optInLeaderboard: state.optInLeaderboard,
      optInDataForTraining: state.optInDataForTraining,
    };

    const parse = writingProfileSchema.safeParse(candidate);
    if (!parse.success) {
      const message = parse.error.issues[0]?.message ?? 'Some answers need fixing before we can continue.';
      setError(message);
      setSubmitting(false);
      return;
    }

    try {
      await saveWritingV2Profile({
        profession: candidate.profession,
        subDiscipline: candidate.subDiscipline,
        yearsExperience: candidate.yearsExperience,
        targetBand: candidate.targetBand,
        examDate: examDateIso ? new Date(`${examDateIso}T00:00:00Z`).toISOString() : null,
        daysPerWeek: candidate.daysPerWeek,
        minutesPerDay: candidate.minutesPerDay,
        targetCountry: candidate.targetCountry,
        letterTypeFocus: candidate.letterTypeFocus,
        optInCommunity: candidate.optInCommunity,
        optInLeaderboard: candidate.optInLeaderboard,
        optInDataForTraining: candidate.optInDataForTraining,
      });
      await completeWritingOnboarding();
      clearWizardState();
      router.push('/writing/diagnostic');
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not save your profile. Please try again.';
      setError(message);
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Writing Profile — Confirm">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Step 4 of 4"
          icon={ClipboardCheck}
          accent="amber"
          title="Review and confirm"
          description="One last check before we generate your pathway and queue your diagnostic."
          highlights={[]}
        />

        <StepperNav currentStep="confirm" />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <form
          onSubmit={onSubmit}
          aria-labelledby="confirm-step-heading"
          className="space-y-6 rounded-2xl border border-border bg-surface p-5 shadow-sm"
        >
          <h2 id="confirm-step-heading" className="text-base font-bold text-navy">
            Your answers
          </h2>

          {state ? (
            <dl className="grid gap-3 sm:grid-cols-2" aria-label="Review summary">
              <div className="rounded-xl border border-border bg-background p-3">
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">Profession</dt>
                <dd className="mt-1 text-sm font-semibold text-navy capitalize">
                  {state.profession}{state.subDiscipline ? ` · ${state.subDiscipline}` : ''}
                  {typeof state.yearsExperience === 'number' ? ` · ${state.yearsExperience}y experience` : ''}
                </dd>
              </div>
              <div className="rounded-xl border border-border bg-background p-3">
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">Target</dt>
                <dd className="mt-1 text-sm font-semibold text-navy">
                  Band {state.targetBand}{state.targetCountry ? ` · ${state.targetCountry}` : ''}
                  {state.examDate ? ` · Exam ${state.examDate}` : ''}
                </dd>
              </div>
              <div className="rounded-xl border border-border bg-background p-3">
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">Weekly budget</dt>
                <dd className="mt-1 text-sm font-semibold text-navy">
                  {state.daysPerWeek} days/week · {state.minutesPerDay} min/day
                </dd>
              </div>
              <div className="rounded-xl border border-border bg-background p-3">
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">Letter focus</dt>
                <dd className="mt-1 flex flex-wrap gap-1">
                  {state.letterTypeFocus.map((code) => (
                    <Badge key={code} variant="info" size="sm">{code}</Badge>
                  ))}
                </dd>
              </div>
              <div className="rounded-xl border border-border bg-background p-3 sm:col-span-2">
                <dt className="text-xs font-bold uppercase tracking-wider text-muted">Preferences</dt>
                <dd className="mt-1 flex flex-wrap gap-1 text-sm">
                  <Badge variant={state.optInCommunity ? 'success' : 'muted'} size="sm">
                    Community {state.optInCommunity ? 'on' : 'off'}
                  </Badge>
                  <Badge variant={state.optInLeaderboard ? 'success' : 'muted'} size="sm">
                    Leaderboard {state.optInLeaderboard ? 'on' : 'off'}
                  </Badge>
                  <Badge variant={state.optInDataForTraining ? 'success' : 'muted'} size="sm">
                    Data for training {state.optInDataForTraining ? 'on' : 'off'}
                  </Badge>
                </dd>
              </div>
            </dl>
          ) : (
            <p className="text-sm text-muted">Loading your answers…</p>
          )}

          <div className="flex flex-wrap items-center justify-between gap-2">
            <Button asChild variant="outline" size="md">
              <Link href="/writing/profile-setup/focus" aria-label="Back to focus step">
                <ArrowLeft className="h-4 w-4" aria-hidden="true" /> Back
              </Link>
            </Button>
            <Button type="submit" size="md" loading={submitting} disabled={!state}>
              <CheckCircle2 className="h-4 w-4" aria-hidden="true" />
              Save and continue
            </Button>
          </div>
        </form>
      </div>
    </LearnerDashboardShell>
  );
}
