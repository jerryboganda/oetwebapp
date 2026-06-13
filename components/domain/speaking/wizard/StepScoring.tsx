'use client';

/**
 * Card wizard — step 5: persona, scoring focus, timing & delivery.
 * Merges the original editor's persona, criteria, timing and disclaimer
 * sections into one step.
 */

import { useCallback, useMemo, useState } from 'react';
import { Checkbox, Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import {
  adminPatchRolePlayCard,
  DEFAULT_DISCLAIMER,
  SPEAKING_CRITERIA_OPTIONS,
  type RolePlayCardDetail,
} from '@/lib/api/speaking-role-play-cards';

export function StepScoring() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const card = wizard.entity;

  const [patientEmotion, setPatientEmotion] = useState(card.patientEmotion || 'worried');
  const [communicationGoal, setCommunicationGoal] = useState(card.communicationGoal || 'Reassure');
  const [criteriaFocus, setCriteriaFocus] = useState<string[]>(card.criteriaFocus ?? []);
  const [prepTimeSeconds, setPrepTimeSeconds] = useState<number>(card.prepTimeSeconds ?? 180);
  const [rolePlayTimeSeconds, setRolePlayTimeSeconds] = useState<number>(card.rolePlayTimeSeconds ?? 300);
  const [allowedNotes, setAllowedNotes] = useState<boolean>(card.allowedNotes ?? true);
  const [isLiveTutorEligible, setIsLiveTutorEligible] = useState<boolean>(card.isLiveTutorEligible ?? false);
  const [disclaimer, setDisclaimer] = useState(card.disclaimer || DEFAULT_DISCLAIMER);
  const [error, setError] = useState<string | null>(null);

  const timingOk = prepTimeSeconds > 0 && prepTimeSeconds <= 600 && rolePlayTimeSeconds > 0 && rolePlayTimeSeconds <= 1800;
  const canAdvance = criteriaFocus.length >= 1 && timingOk;

  const toggleCriterion = (code: string) =>
    setCriteriaFocus((prev) => (prev.includes(code) ? prev.filter((c) => c !== code) : [...prev, code]));

  const submit = useCallback(async () => {
    if (!timingOk) {
      setError('Prep time must be 1–600s and role-play time 1–1800s.');
      throw new Error('invalid');
    }
    setError(null);
    await adminPatchRolePlayCard(card.cardId, {
      patientEmotion: patientEmotion.trim() || 'neutral',
      communicationGoal: communicationGoal.trim() || 'Inform',
      criteriaFocus,
      prepTimeSeconds,
      rolePlayTimeSeconds,
      allowedNotes,
      isLiveTutorEligible,
      disclaimer: disclaimer.trim() || DEFAULT_DISCLAIMER,
    });
    await wizard.refresh();
  }, [card.cardId, patientEmotion, communicationGoal, criteriaFocus, prepTimeSeconds, rolePlayTimeSeconds, allowedNotes, isLiveTutorEligible, disclaimer, timingOk, wizard]);

  useStepRegistration('scoring', { canAdvance, submit });

  const grouped = useMemo(
    () => ({
      linguistic: SPEAKING_CRITERIA_OPTIONS.filter((o) => o.band === 'linguistic'),
      clinical: SPEAKING_CRITERIA_OPTIONS.filter((o) => o.band === 'clinical'),
    }),
    [],
  );

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Persona, scoring &amp; timing</h2>
        <p className="text-sm text-muted">Drives the AI scorer, drill recommender, and the on-screen timers.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-4 md:grid-cols-2">
        <Input label="Patient emotion" value={patientEmotion} onChange={(e) => setPatientEmotion(e.target.value)} placeholder='e.g. "worried", "anxious"' maxLength={64} />
        <Input label="Communication goal" value={communicationGoal} onChange={(e) => setCommunicationGoal(e.target.value)} placeholder='e.g. "Reassure", "Explain"' maxLength={64} />
      </div>

      <fieldset>
        <legend className="mb-2 text-sm font-semibold tracking-tight text-navy">Criteria this card stresses</legend>
        <p className="mb-3 text-xs text-muted">Pick the OET criteria this scenario is designed to expose. At least one is required.</p>
        <div className="grid gap-2 sm:grid-cols-2">
          {[...grouped.linguistic, ...grouped.clinical].map((opt) => {
            const checked = criteriaFocus.includes(opt.value);
            return (
              <button
                key={opt.value}
                type="button"
                aria-pressed={checked}
                onClick={() => toggleCriterion(opt.value)}
                className={`flex items-center justify-between gap-2 rounded-2xl border px-3 py-2 text-left text-sm transition ${
                  checked ? 'border-primary bg-primary/10 text-primary' : 'border-border bg-background-light text-navy hover:border-primary/40'
                }`}
              >
                <span>{opt.label}</span>
                <span className="text-[10px] font-bold uppercase tracking-widest text-muted">{opt.band}</span>
              </button>
            );
          })}
        </div>
      </fieldset>

      <div className="grid gap-4 md:grid-cols-2">
        <Input label="Prep time (seconds)" type="number" value={String(prepTimeSeconds)} onChange={(e) => setPrepTimeSeconds(Number(e.target.value || 0))} min={30} max={600} step={15} hint="OET default is 180s (3 minutes)." />
        <Input label="Role-play time (seconds)" type="number" value={String(rolePlayTimeSeconds)} onChange={(e) => setRolePlayTimeSeconds(Number(e.target.value || 0))} min={60} max={1800} step={30} hint="OET default is 300s (5 minutes)." />
      </div>

      <Checkbox label="Allow candidate to take notes during preparation" checked={allowedNotes} onChange={(e) => setAllowedNotes(e.target.checked)} />
      <Checkbox label="Eligible for live tutor (premium booking flow)" checked={isLiveTutorEligible} onChange={(e) => setIsLiveTutorEligible(e.target.checked)} />

      <Textarea label="Footer disclaimer" value={disclaimer} onChange={(e) => setDisclaimer(e.target.value)} rows={2} maxLength={400} hint="Defaults to the standard practice-estimate disclaimer." />
    </div>
  );
}
