'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import { ClipboardList, ArrowRight, Briefcase } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { getWritingV2Profile } from '@/lib/writing/api';
import type { WritingProfession } from '@/lib/writing/types';
import { readWizardState, writeWizardState } from '../wizard-state';
import { StepperNav } from '../StepperNav';

const PROFESSIONS: Array<{ id: WritingProfession; label: string; description: string }> = [
  { id: 'medicine', label: 'Medicine', description: 'GPs, hospital doctors, specialists.' },
  { id: 'pharmacy', label: 'Pharmacy', description: 'Community + hospital pharmacy.' },
  { id: 'nursing', label: 'Nursing', description: 'Acute, community, paediatric nursing.' },
  { id: 'other', label: 'Other allied health', description: 'Dentistry, OT, physio, dietetics, etc.' },
];

export default function ProfileSetupProfessionPage() {
  const router = useRouter();
  const [profession, setProfession] = useState<WritingProfession>('medicine');
  const [subDiscipline, setSubDiscipline] = useState('');
  const [yearsExperience, setYearsExperience] = useState<string>('');

  useEffect(() => {
    const wizard = readWizardState();
    setProfession(wizard.profession);
    setSubDiscipline(wizard.subDiscipline);
    setYearsExperience(wizard.yearsExperience === null ? '' : String(wizard.yearsExperience));

    // Hydrate from existing profile so re-entry doesn't reset answers.
    void getWritingV2Profile()
      .then((profile) => {
        if (!profile) return;
        const next = writeWizardState({
          profession: profile.profession ?? wizard.profession,
          subDiscipline: profile.subDiscipline ?? wizard.subDiscipline,
          yearsExperience: profile.yearsExperience ?? wizard.yearsExperience,
        });
        setProfession(next.profession);
        setSubDiscipline(next.subDiscipline);
        setYearsExperience(next.yearsExperience === null ? '' : String(next.yearsExperience));
      })
      .catch(() => {
        /* first-visit profile may not exist yet */
      });
  }, []);

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    const parsedYears = yearsExperience.trim() === '' ? null : Math.max(0, Math.min(60, Number(yearsExperience) || 0));
    writeWizardState({ profession, subDiscipline: subDiscipline.trim(), yearsExperience: parsedYears });
    router.push('/writing/profile-setup/goals');
  };

  return (
    <LearnerDashboardShell pageTitle="Writing Profile — Step 1">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Step 1 of 4"
          icon={ClipboardList}
          accent="amber"
          title="Tell us who you are"
          description="Your profession decides letter types we drill, scenarios we surface, and exemplar libraries we compare against."
          highlights={[]}
        />

        <StepperNav currentStep="profession" />

        <form
          onSubmit={onSubmit}
          aria-labelledby="profession-step-heading"
          className="space-y-6 rounded-2xl border border-border bg-surface p-5 shadow-sm"
        >
          <fieldset>
            <legend id="profession-step-heading" className="text-base font-bold text-navy">
              Profession
            </legend>
            <p className="mt-1 mb-4 text-sm text-muted">Choose the one that matches your day-to-day work.</p>
            <div className="grid gap-2 sm:grid-cols-2">
              {PROFESSIONS.map((option) => {
                const selected = profession === option.id;
                return (
                  <label
                    key={option.id}
                    className={`flex cursor-pointer items-start gap-3 rounded-xl border p-3 transition-colors focus-within:ring-2 focus-within:ring-primary ${selected ? 'border-primary bg-primary/10' : 'border-border bg-background hover:border-primary/40'}`}
                  >
                    <input
                      type="radio"
                      name="profession"
                      value={option.id}
                      checked={selected}
                      onChange={() => setProfession(option.id)}
                      className="mt-1 accent-primary"
                      aria-describedby={`profession-${option.id}-desc`}
                    />
                    <span className="flex-1">
                      <span className="block text-sm font-bold text-navy">
                        <Briefcase className="mr-1 inline h-4 w-4 text-amber-600" aria-hidden="true" />
                        {option.label}
                      </span>
                      <span id={`profession-${option.id}-desc`} className="block text-xs text-muted">
                        {option.description}
                      </span>
                    </span>
                  </label>
                );
              })}
            </div>
          </fieldset>

          <div className="grid gap-4 md:grid-cols-2">
            <label className="flex flex-col gap-1 text-sm font-semibold text-navy">
              Sub-discipline (optional)
              <input
                type="text"
                value={subDiscipline}
                onChange={(e) => setSubDiscipline(e.target.value)}
                placeholder="e.g. paediatrics, oncology, community pharmacy"
                maxLength={120}
                className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>
            <label className="flex flex-col gap-1 text-sm font-semibold text-navy">
              Years of experience (optional)
              <input
                type="number"
                min={0}
                max={60}
                value={yearsExperience}
                onChange={(e) => setYearsExperience(e.target.value)}
                placeholder="e.g. 5"
                className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              />
            </label>
          </div>

          <div className="flex flex-wrap items-center justify-end gap-2">
            <Button type="submit" size="md">
              Continue <ArrowRight className="h-4 w-4" aria-hidden="true" />
            </Button>
          </div>
        </form>
      </div>
    </LearnerDashboardShell>
  );
}
