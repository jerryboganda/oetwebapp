'use client';

/** Mock-set wizard — step 1: title & metadata. */

import { useCallback, useState } from 'react';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import { updateAdminSpeakingMockSet, type AdminSpeakingMockSetRow } from '@/lib/api';
import { MOCK_SET_DIFFICULTY_OPTIONS, MOCK_SET_PROFESSION_OPTIONS } from './mock-set-wizard-config';

export function StepMockDetails() {
  const wizard = useAdminWizard<AdminSpeakingMockSetRow>();
  const row = wizard.entity;

  const [title, setTitle] = useState(row.title ?? '');
  const [description, setDescription] = useState(row.description ?? '');
  const [professionId, setProfessionId] = useState(row.professionId ?? 'nursing');
  const [difficulty, setDifficulty] = useState(row.difficulty ?? 'core');
  const [criteriaFocus, setCriteriaFocus] = useState((row.criteriaFocus ?? []).join(', '));
  const [tags, setTags] = useState((row.tags ?? []).join(', '));
  const [sortOrder, setSortOrder] = useState<number>(row.sortOrder ?? 0);
  const [error, setError] = useState<string | null>(null);

  const canAdvance = title.trim().length > 0;

  const submit = useCallback(async () => {
    if (!title.trim()) {
      setError('Title is required.');
      throw new Error('invalid');
    }
    setError(null);
    await updateAdminSpeakingMockSet(row.mockSetId, {
      title: title.trim(),
      description: description.trim(),
      professionId,
      difficulty,
      criteriaFocus,
      tags,
      sortOrder,
    });
    await wizard.refresh();
  }, [row.mockSetId, title, description, professionId, difficulty, criteriaFocus, tags, sortOrder, wizard]);

  useStepRegistration('details', { canAdvance, submit });

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Mock set details</h2>
        <p className="text-sm text-muted">A mock set bundles two speaking role-plays into one OET-shape sub-test.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <Input label="Title" value={title} onChange={(e) => setTitle(e.target.value)} placeholder="e.g. Nursing Mock Set 3 — Discharge planning" maxLength={200} required />
      <Input label="Description (optional)" value={description} onChange={(e) => setDescription(e.target.value)} maxLength={500} />

      <div className="grid gap-4 md:grid-cols-2">
        <Select label="Profession" value={professionId} onChange={(e) => setProfessionId(e.target.value)} options={MOCK_SET_PROFESSION_OPTIONS} />
        <Select label="Difficulty" value={difficulty} onChange={(e) => setDifficulty(e.target.value)} options={MOCK_SET_DIFFICULTY_OPTIONS} />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Input label="Criteria focus (CSV, optional)" value={criteriaFocus} onChange={(e) => setCriteriaFocus(e.target.value)} placeholder="informationGiving, relationshipBuilding" />
        <Input label="Tags (CSV, optional)" value={tags} onChange={(e) => setTags(e.target.value)} placeholder="nursing, week-1" />
      </div>

      <Input label="Sort order" type="number" value={String(sortOrder)} onChange={(e) => setSortOrder(Number(e.target.value) || 0)} />
    </div>
  );
}
