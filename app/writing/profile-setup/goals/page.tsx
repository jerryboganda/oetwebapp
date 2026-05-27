'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Target, ArrowRight, ArrowLeft, Calendar, Clock } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { readWizardState, writeWizardState, type WritingProfileWizardState } from '../wizard-state';
import { StepperNav } from '../StepperNav';

const TARGET_BANDS = ['A', 'B+', 'B', 'C+', 'C'] as const;
const COUNTRIES: Array<[string, string]> = [
  ['GB', 'United Kingdom'],
  ['IE', 'Ireland'],
  ['AU', 'Australia'],
  ['NZ', 'New Zealand'],
  ['CA', 'Canada'],
  ['US', 'United States'],
  ['QA', 'Qatar'],
  ['AE', 'United Arab Emirates'],
];

export default function ProfileSetupGoalsPage() {
  const router = useRouter();
  const [targetBand, setTargetBand] = useState<WritingProfileWizardState['targetBand']>('B');
  const [examDate, setExamDate] = useState('');
  const [daysPerWeek, setDaysPerWeek] = useState(5);
  const [minutesPerDay, setMinutesPerDay] = useState(45);
  const [targetCountry, setTargetCountry] = useState('GB');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const wizard = readWizardState();
    setTargetBand(wizard.targetBand);
    setExamDate(wizard.examDate ?? '');
    setDaysPerWeek(wizard.daysPerWeek);
    setMinutesPerDay(wizard.minutesPerDay);
    setTargetCountry(wizard.targetCountry);
  }, []);

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (daysPerWeek < 1 || daysPerWeek > 7) {
      setError('Days per week must be between 1 and 7.');
      return;
    }
    if (minutesPerDay < 15 || minutesPerDay > 240) {
      setError('Minutes per day must be between 15 and 240.');
      return;
    }
    setError(null);
    writeWizardState({
      targetBand,
      examDate: examDate || null,
      daysPerWeek,
      minutesPerDay,
      targetCountry,
    });
    router.push('/writing/profile-setup/focus');
  };

  return (
    <LearnerDashboardShell pageTitle="Writing Profile — Step 2">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Step 2 of 4"
          icon={Target}
          accent="amber"
          title="Set your target and your weekly budget"
          description="The plan generator uses these to size your daily work and lock the trajectory toward your exam date."
          highlights={[]}
        />

        <StepperNav currentStep="goals" />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <form
          onSubmit={onSubmit}
          aria-labelledby="goals-step-heading"
          className="space-y-6 rounded-2xl border border-border bg-surface p-5 shadow-sm"
        >
          <h2 id="goals-step-heading" className="sr-only">Goals and practice budget</h2>

          <fieldset>
            <legend className="text-base font-bold text-navy">Target band</legend>
            <p className="mt-1 mb-3 text-sm text-muted">The band you need for your registration.</p>
            <div className="flex flex-wrap gap-2">
              {TARGET_BANDS.map((band) => {
                const selected = targetBand === band;
                return (
                  <button
                    key={band}
                    type="button"
                    onClick={() => setTargetBand(band)}
                    aria-pressed={selected}
                    className={`min-w-12 rounded-lg border px-4 py-2 text-sm font-bold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${selected ? 'border-primary bg-primary text-white' : 'border-border bg-background text-navy hover:border-primary/40'}`}
                  >
                    {band}
                  </button>
                );
              })}
            </div>
          </fieldset>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="flex flex-col gap-1 text-sm font-semibold text-navy">
              <span className="flex items-center gap-1">
                <Calendar className="h-4 w-4 text-amber-600" aria-hidden="true" />
                Exam date (optional)
              </span>
              <input
                type="date"
                value={examDate}
                onChange={(e) => setExamDate(e.target.value)}
                className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                aria-describedby="exam-date-hint"
              />
              <span id="exam-date-hint" className="text-xs font-normal text-muted">We&apos;ll compress or relax the plan to fit.</span>
            </label>
            <label className="flex flex-col gap-1 text-sm font-semibold text-navy">
              Target country
              <select
                value={targetCountry}
                onChange={(e) => setTargetCountry(e.target.value)}
                className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                {COUNTRIES.map(([code, label]) => (
                  <option key={code} value={code}>{label}</option>
                ))}
              </select>
            </label>
          </div>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="flex flex-col gap-1 text-sm font-semibold text-navy">
              <span className="flex items-center gap-1">
                <Clock className="h-4 w-4 text-amber-600" aria-hidden="true" />
                Days per week
              </span>
              <input
                type="number"
                min={1}
                max={7}
                value={daysPerWeek}
                onChange={(e) => setDaysPerWeek(Number(e.target.value))}
                className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                aria-describedby="days-hint"
              />
              <span id="days-hint" className="text-xs font-normal text-muted">3-5 days/week is realistic for working professionals.</span>
            </label>
            <label className="flex flex-col gap-1 text-sm font-semibold text-navy">
              Minutes per day
              <input
                type="number"
                min={15}
                max={240}
                step={15}
                value={minutesPerDay}
                onChange={(e) => setMinutesPerDay(Number(e.target.value))}
                className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                aria-describedby="minutes-hint"
              />
              <span id="minutes-hint" className="text-xs font-normal text-muted">Recommended: 45-60 minutes a session.</span>
            </label>
          </div>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <Button asChild variant="outline" size="md">
              <Link href="/writing/profile-setup/profession" aria-label="Back to profession step">
                <ArrowLeft className="h-4 w-4" aria-hidden="true" /> Back
              </Link>
            </Button>
            <Button type="submit" size="md">
              Continue <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        </form>
      </div>
    </LearnerDashboardShell>
  );
}
