'use client';

/**
 * Card wizard — step 2: candidate-facing context.
 * Setting, candidate role, interlocutor role, optional patient name/age, and
 * the background written exactly as the candidate reads it.
 */

import { useCallback, useState } from 'react';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import { adminPatchRolePlayCard, type RolePlayCardDetail } from '@/lib/api/speaking-role-play-cards';
import { unseedCardValue } from './card-wizard-config';

export function StepCandidate() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const card = wizard.entity;

  const [setting, setSetting] = useState(unseedCardValue(card.setting));
  const [candidateRole, setCandidateRole] = useState(unseedCardValue(card.candidateRole));
  const [interlocutorRole, setInterlocutorRole] = useState(card.interlocutorRole || 'Patient');
  const [patientName, setPatientName] = useState(card.patientName ?? '');
  const [patientAge, setPatientAge] = useState(card.patientAge ?? '');
  const [background, setBackground] = useState(card.background ?? '');
  const [error, setError] = useState<string | null>(null);

  const canAdvance = setting.trim().length > 0 && candidateRole.trim().length > 0 && background.trim().length > 0;

  const submit = useCallback(async () => {
    if (!setting.trim() || !candidateRole.trim()) {
      setError('Setting and candidate role are required.');
      throw new Error('invalid');
    }
    setError(null);
    await adminPatchRolePlayCard(card.cardId, {
      setting: setting.trim(),
      candidateRole: candidateRole.trim(),
      interlocutorRole: interlocutorRole.trim() || 'Patient',
      patientName: patientName.trim() || null,
      patientAge: patientAge.trim() || null,
      background: background.trim(),
    });
    await wizard.refresh();
  }, [card.cardId, setting, candidateRole, interlocutorRole, patientName, patientAge, background, wizard]);

  useStepRegistration('candidate', { canAdvance, submit });

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Candidate card content</h2>
        <p className="text-sm text-muted">The context the candidate reads during the 3-minute preparation.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-4 md:grid-cols-2">
        <Input label="Setting" value={setting} onChange={(e) => setSetting(e.target.value)} placeholder='e.g. "Surgical ward"' maxLength={160} required />
        <Input label="Candidate role" value={candidateRole} onChange={(e) => setCandidateRole(e.target.value)} placeholder='e.g. "Nurse"' maxLength={64} required />
      </div>

      <div className="grid gap-4 md:grid-cols-3">
        <Input label="Interlocutor role" value={interlocutorRole} onChange={(e) => setInterlocutorRole(e.target.value)} placeholder='e.g. "Patient", "Parent"' maxLength={64} />
        <Input label="Patient name (optional)" value={patientName} onChange={(e) => setPatientName(e.target.value)} placeholder='e.g. "Mrs. Lee"' maxLength={80} />
        <Input label="Patient age (optional)" value={patientAge} onChange={(e) => setPatientAge(e.target.value)} placeholder='e.g. "48", "early 30s"' maxLength={32} />
      </div>

      <Textarea
        label="Background (case detail)"
        value={background}
        onChange={(e) => setBackground(e.target.value)}
        placeholder="Multi-line case background, written exactly as the candidate will read it…"
        rows={5}
        maxLength={4000}
        required
        hint={`${background.length}/4000 characters`}
      />
    </div>
  );
}
