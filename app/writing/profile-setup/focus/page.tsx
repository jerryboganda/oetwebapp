'use client';

import { useEffect, useState, type FormEvent } from 'react';
import { useRouter } from 'next/navigation';
import Link from 'next/link';
import { Layers, ArrowRight, ArrowLeft } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import type { WritingLetterType } from '@/lib/writing/types';
import { readWizardState, writeWizardState } from '../wizard-state';
import { StepperNav } from '../StepperNav';

const LETTER_TYPES: Array<{ code: WritingLetterType; label: string; description: string }> = [
  { code: 'LT-RR', label: 'Routine referral', description: 'GP to specialist, stable patient.' },
  { code: 'LT-UR', label: 'Urgent referral', description: 'Time-critical: same-day or next-day handover.' },
  { code: 'LT-DG', label: 'Discharge to GP', description: 'Hospital to primary care after admission.' },
  { code: 'LT-TR', label: 'Transfer', description: 'Patient moves between care settings.' },
  { code: 'LT-RP', label: 'Response', description: 'Reply to another health professional.' },
  { code: 'LT-NM', label: 'Non-medical referral', description: 'To social worker, OT, dietitian, etc.' },
];

export default function ProfileSetupFocusPage() {
  const router = useRouter();
  const [focus, setFocus] = useState<WritingLetterType[]>([]);
  const [optInCommunity, setOptInCommunity] = useState(false);
  const [optInLeaderboard, setOptInLeaderboard] = useState(false);
  const [optInDataForTraining, setOptInDataForTraining] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    const wizard = readWizardState();
    setFocus(wizard.letterTypeFocus);
    setOptInCommunity(wizard.optInCommunity);
    setOptInLeaderboard(wizard.optInLeaderboard);
    setOptInDataForTraining(wizard.optInDataForTraining);
  }, []);

  const toggle = (code: WritingLetterType) => {
    setFocus((prev) => (prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]));
  };

  const onSubmit = (e: FormEvent) => {
    e.preventDefault();
    if (focus.length < 2) {
      setError('Pick at least two letter types so we can rotate practice without overfitting.');
      return;
    }
    setError(null);
    writeWizardState({
      letterTypeFocus: focus,
      optInCommunity,
      optInLeaderboard,
      optInDataForTraining,
    });
    router.push('/writing/profile-setup/confirm');
  };

  return (
    <LearnerDashboardShell pageTitle="Writing Profile — Step 3">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Step 3 of 4"
          icon={Layers}
          accent="amber"
          title="What letter types do you need most?"
          description="We rotate practice across the types you pick, with weighting toward your weakest."
          highlights={[{ icon: Layers, label: 'Picked', value: `${focus.length}` }]}
        />

        <StepperNav currentStep="focus" />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <form
          onSubmit={onSubmit}
          aria-labelledby="focus-step-heading"
          className="space-y-6 rounded-2xl border border-border bg-surface p-5 shadow-sm"
        >
          <fieldset>
            <legend id="focus-step-heading" className="text-base font-bold text-navy">
              Letter type focus
            </legend>
            <p className="mt-1 mb-3 text-sm text-muted">Pick all that apply (minimum 2).</p>
            <div className="grid gap-2 sm:grid-cols-2">
              {LETTER_TYPES.map((type) => {
                const selected = focus.includes(type.code);
                return (
                  <button
                    key={type.code}
                    type="button"
                    onClick={() => toggle(type.code)}
                    aria-pressed={selected}
                    className={`flex items-start gap-3 rounded-xl border p-3 text-left transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary ${selected ? 'border-primary bg-primary/10' : 'border-border bg-background hover:border-primary/40'}`}
                  >
                    <span className="flex-1">
                      <span className="flex items-center gap-2">
                        <Badge variant={selected ? 'success' : 'muted'} size="sm">{type.code}</Badge>
                        <span className="text-sm font-bold text-navy">{type.label}</span>
                      </span>
                      <span className="mt-1 block text-xs text-muted">{type.description}</span>
                    </span>
                  </button>
                );
              })}
            </div>
          </fieldset>

          <fieldset>
            <legend className="text-base font-bold text-navy">Optional preferences</legend>
            <p className="mt-1 mb-3 text-sm text-muted">These can be changed any time from your account settings.</p>
            <ul className="space-y-2">
              <li>
                <label className="flex items-start gap-2 text-sm text-navy">
                  <input
                    type="checkbox"
                    checked={optInCommunity}
                    onChange={(e) => setOptInCommunity(e.target.checked)}
                    className="mt-0.5 accent-primary"
                  />
                  <span>
                    <span className="block font-bold">Join the community feed</span>
                    <span className="block text-xs text-muted">See showcase letters from other learners and react to peers&apos; work.</span>
                  </span>
                </label>
              </li>
              <li>
                <label className="flex items-start gap-2 text-sm text-navy">
                  <input
                    type="checkbox"
                    checked={optInLeaderboard}
                    onChange={(e) => setOptInLeaderboard(e.target.checked)}
                    className="mt-0.5 accent-primary"
                  />
                  <span>
                    <span className="block font-bold">Appear on the weekly leaderboard</span>
                    <span className="block text-xs text-muted">Top streaks and bands across the cohort. Pseudonymous.</span>
                  </span>
                </label>
              </li>
              <li>
                <label className="flex items-start gap-2 text-sm text-navy">
                  <input
                    type="checkbox"
                    checked={optInDataForTraining}
                    onChange={(e) => setOptInDataForTraining(e.target.checked)}
                    className="mt-0.5 accent-primary"
                  />
                  <span>
                    <span className="block font-bold">Help improve the grader</span>
                    <span className="block text-xs text-muted">Allow anonymised letters to refine the AI rubric. Off by default.</span>
                  </span>
                </label>
              </li>
            </ul>
          </fieldset>

          <div className="flex flex-wrap items-center justify-between gap-2">
            <Button asChild variant="outline" size="md">
              <Link href="/writing/profile-setup/goals" aria-label="Back to goals step">
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
