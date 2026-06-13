'use client';

/**
 * Card wizard — step 1: classification.
 *
 * Profession, difficulty, scenario title, clinical topic, hidden card type
 * (with inline create), and the optional printed card number. The standalone
 * card-types admin page is retired in favour of the inline "New type" control
 * here.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Plus, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminWizard } from '@/components/domain/wizard/useAdminWizard';
import { useStepRegistration } from '@/lib/wizard/use-step-registration';
import {
  adminCreateSpeakingCardType,
  adminListSpeakingCardTypes,
  adminPatchRolePlayCard,
  DIFFICULTY_OPTIONS,
  PROFESSION_OPTIONS,
  type RolePlayCardDetail,
  type SpeakingCardTypeDetail,
} from '@/lib/api/speaking-role-play-cards';
import { unseedCardValue } from './card-wizard-config';

export function StepClassification() {
  const wizard = useAdminWizard<RolePlayCardDetail>();
  const card = wizard.entity;

  const [professionId, setProfessionId] = useState(card.professionId ?? 'nursing');
  const [difficulty, setDifficulty] = useState<string>(card.difficulty ?? 'core');
  const [scenarioTitle, setScenarioTitle] = useState(unseedCardValue(card.scenarioTitle));
  const [clinicalTopic, setClinicalTopic] = useState(card.clinicalTopic === 'general' ? '' : card.clinicalTopic ?? '');
  const [cardTypeId, setCardTypeId] = useState<string>(card.cardTypeId ?? '');
  const [displayCardNumber, setDisplayCardNumber] = useState<string>(
    card.displayCardNumber != null ? String(card.displayCardNumber) : '',
  );

  const [cardTypes, setCardTypes] = useState<SpeakingCardTypeDetail[]>([]);
  const [showNewType, setShowNewType] = useState(false);
  const [newTypeName, setNewTypeName] = useState('');
  const [newTypeDescription, setNewTypeDescription] = useState('');
  const [creatingType, setCreatingType] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    adminListSpeakingCardTypes(true)
      .then(setCardTypes)
      .catch(() => setCardTypes([]));
  }, []);

  const canAdvance = scenarioTitle.trim().length > 0 && professionId.trim().length > 0;

  const submit = useCallback(async () => {
    if (!scenarioTitle.trim()) {
      setError('Scenario title is required.');
      throw new Error('invalid');
    }
    setError(null);
    await adminPatchRolePlayCard(card.cardId, {
      professionId,
      difficulty,
      scenarioTitle: scenarioTitle.trim(),
      clinicalTopic: clinicalTopic.trim(),
      cardTypeId,
      displayCardNumber: displayCardNumber.trim() === '' ? null : Number(displayCardNumber),
    });
    await wizard.refresh();
  }, [card.cardId, professionId, difficulty, scenarioTitle, clinicalTopic, cardTypeId, displayCardNumber, wizard]);

  useStepRegistration('classification', { canAdvance, submit });

  const cardTypeOptions = useMemo(
    () => [
      { value: '', label: '— None —' },
      ...cardTypes
        .filter((t) => t.isActive || t.id === cardTypeId)
        .map((t) => ({ value: t.id, label: t.isActive ? t.name : `${t.name} (inactive)` })),
    ],
    [cardTypes, cardTypeId],
  );

  async function handleCreateType() {
    if (!newTypeName.trim()) {
      setError('Card type name is required.');
      return;
    }
    setCreatingType(true);
    setError(null);
    try {
      const created = await adminCreateSpeakingCardType({
        name: newTypeName.trim(),
        description: newTypeDescription.trim() || null,
      });
      setCardTypes((prev) => [...prev, created]);
      setCardTypeId(created.id);
      setNewTypeName('');
      setNewTypeDescription('');
      setShowNewType(false);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not create card type.');
    } finally {
      setCreatingType(false);
    }
  }

  return (
    <div className="space-y-5">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Classification</h2>
        <p className="text-sm text-muted">How this role-play card is filed. The card type is hidden from students and aids human/AI marking.</p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-4 md:grid-cols-2">
        <Select label="Profession" value={professionId} onChange={(e) => setProfessionId(e.target.value)} options={PROFESSION_OPTIONS} required />
        <Select label="Difficulty" value={difficulty} onChange={(e) => setDifficulty(e.target.value)} options={DIFFICULTY_OPTIONS} required />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <Input
          label="Scenario title"
          value={scenarioTitle}
          onChange={(e) => setScenarioTitle(e.target.value)}
          placeholder='e.g. "Discharge advice after appendectomy"'
          maxLength={200}
          required
        />
        <Input
          label="Clinical topic"
          value={clinicalTopic}
          onChange={(e) => setClinicalTopic(e.target.value)}
          placeholder='e.g. "Post-operative pain management"'
          maxLength={96}
        />
      </div>

      <div className="grid gap-4 md:grid-cols-2">
        <div className="space-y-2">
          <Select
            label="Card type (hidden from students)"
            value={cardTypeId}
            onChange={(e) => setCardTypeId(e.target.value)}
            options={cardTypeOptions}
          />
          {showNewType ? (
            <div className="space-y-2 rounded-2xl border border-border bg-background-light p-3">
              <Input label="New type name" value={newTypeName} onChange={(e) => setNewTypeName(e.target.value)} placeholder='e.g. "Examination card"' maxLength={120} />
              <Textarea label="Description (marking guidance)" value={newTypeDescription} onChange={(e) => setNewTypeDescription(e.target.value)} rows={2} maxLength={2000} />
              <div className="flex items-center gap-2">
                <Button type="button" variant="primary" size="sm" onClick={() => void handleCreateType()} disabled={creatingType}>
                  <Plus className="mr-1 h-3.5 w-3.5" /> Add type
                </Button>
                <Button type="button" variant="ghost" size="sm" onClick={() => setShowNewType(false)} disabled={creatingType}>
                  <X className="mr-1 h-3.5 w-3.5" /> Cancel
                </Button>
              </div>
            </div>
          ) : (
            <Button type="button" variant="outline" size="sm" onClick={() => setShowNewType(true)}>
              <Plus className="mr-1 h-3.5 w-3.5" /> New type
            </Button>
          )}
        </div>
        <Input
          label="Printed card number (optional)"
          type="number"
          min={1}
          value={displayCardNumber}
          onChange={(e) => setDisplayCardNumber(e.target.value)}
          placeholder='e.g. "2" → "CANDIDATE CARD NO. 2"'
        />
      </div>
    </div>
  );
}
