'use client';

/**
 * Hidden-card editor for the OET Speaking role-play builder.
 *
 * Renders the form an admin uses to author the **hidden** side of a
 * two-card role-play (the interlocutor's persona, prompts, hidden
 * information, and resistance level). This form MUST be guarded with
 * the "EYES-ONLY: TUTOR + ADMIN" warning banner because everything in
 * here is data that learners must never see.
 *
 * Used by:
 *   - app/admin/content/speaking/role-play-cards/new/page.tsx (step 2)
 *   - app/admin/content/speaking/role-play-cards/[id]/interlocutor/page.tsx
 */

import { useMemo, useState, type FormEvent, type KeyboardEvent } from 'react';
import { AlertTriangle, Save, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, RadioGroup, Textarea } from '@/components/ui/form-controls';
import {
  RESISTANCE_LEVEL_OPTIONS,
  type InterlocutorScriptDetail,
  type ResistanceLevelCode,
  type UpsertInterlocutorScriptInput,
} from '@/lib/api/speaking-role-play-cards';

export type InterlocutorScriptEditorMode = 'create' | 'edit';

export interface InterlocutorScriptEditorProps {
  cardId: string;
  /** Existing script when editing; `null` when authoring a new one. */
  value?: InterlocutorScriptDetail | null;
  submitting?: boolean;
  mode: InterlocutorScriptEditorMode;
  onSubmit: (cardId: string, value: UpsertInterlocutorScriptInput) => Promise<void> | void;
  /** Optional secondary action (e.g. "Skip for now" on the wizard). */
  secondaryAction?: React.ReactNode;
}

export function InterlocutorScriptEditor({
  cardId,
  value,
  submitting,
  mode,
  onSubmit,
  secondaryAction,
}: InterlocutorScriptEditorProps) {
  const [openingResponse, setOpeningResponse] = useState(value?.openingResponse ?? '');
  const [prompt1, setPrompt1] = useState(value?.prompt1 ?? '');
  const [prompt2, setPrompt2] = useState(value?.prompt2 ?? '');
  const [prompt3, setPrompt3] = useState(value?.prompt3 ?? '');
  const [hiddenInformation, setHiddenInformation] = useState(value?.hiddenInformation ?? '');
  const [resistanceLevel, setResistanceLevel] = useState<ResistanceLevelCode>(
    value?.resistanceLevel ?? 'low',
  );
  const [closingCue, setClosingCue] = useState(value?.closingCue ?? '');
  const [emotionalState, setEmotionalState] = useState(value?.emotionalState ?? '');
  const [professionRoleNotes, setProfessionRoleNotes] = useState(value?.professionRoleNotes ?? '');
  const [layLanguageTriggers, setLayLanguageTriggers] = useState<string[]>(value?.layLanguageTriggers ?? []);
  const [chipDraft, setChipDraft] = useState('');

  const validationHints = useMemo(() => {
    const hints: string[] = [];
    if (!openingResponse.trim()) {
      hints.push('Opening response is required. The AI patient needs a first line.');
    }
    if (openingResponse.length > 500) {
      hints.push('Opening response must be under 500 characters.');
    }
    if (!hiddenInformation.trim()) {
      hints.push('Hidden information is recommended so the patient has detail to reveal on questioning.');
    }
    if (!closingCue.trim()) {
      hints.push('A closing cue helps the AI know when the role-play can wrap up.');
    }
    return hints;
  }, [openingResponse, hiddenInformation, closingCue]);

  const addChip = (raw: string) => {
    const trimmed = raw.trim();
    if (!trimmed) return;
    if (layLanguageTriggers.some((t) => t.toLowerCase() === trimmed.toLowerCase())) return;
    setLayLanguageTriggers((prev) => [...prev, trimmed]);
    setChipDraft('');
  };

  const removeChip = (chip: string) => {
    setLayLanguageTriggers((prev) => prev.filter((c) => c !== chip));
  };

  const handleChipKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === 'Enter' || e.key === ',') {
      e.preventDefault();
      addChip(chipDraft);
    } else if (e.key === 'Backspace' && chipDraft.length === 0 && layLanguageTriggers.length > 0) {
      removeChip(layLanguageTriggers[layLanguageTriggers.length - 1]);
    }
  };

  const handleSubmit = async (e: FormEvent) => {
    e.preventDefault();
    const payload: UpsertInterlocutorScriptInput = {
      openingResponse: openingResponse.trim(),
      prompt1: prompt1.trim() || null,
      prompt2: prompt2.trim() || null,
      prompt3: prompt3.trim() || null,
      hiddenInformation: hiddenInformation.trim(),
      resistanceLevel,
      closingCue: closingCue.trim(),
      emotionalState: emotionalState.trim(),
      professionRoleNotes: professionRoleNotes.trim() || null,
      layLanguageTriggers,
    };
    await onSubmit(cardId, payload);
  };

  return (
    <form onSubmit={(e) => void handleSubmit(e)} className="space-y-6">
      {/* Eyes-only banner */}
      <div
        role="alert"
        aria-live="polite"
        className="flex items-start gap-3 rounded-2xl border-2 border-red-300 bg-red-50 px-4 py-3 text-red-900 shadow-sm dark:border-red-900 dark:bg-red-950 dark:text-red-100"
      >
        <AlertTriangle className="mt-0.5 h-5 w-5 flex-shrink-0" />
        <div className="space-y-0.5 text-sm">
          <p className="font-bold uppercase tracking-[0.16em]">Eyes-only: tutor + admin</p>
          <p className="text-xs leading-5">
            Everything authored here drives the AI patient persona and the tutor cue panel. It is
            never shown to learners. The candidate card has no overlap with this content.
          </p>
        </div>
      </div>

      {/* Section 1: opening + prompts */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Opening & cue prompts</h3>
        <Textarea
          label="Opening response"
          value={openingResponse}
          onChange={(e) => setOpeningResponse(e.target.value)}
          placeholder='e.g. "I am worried these tablets are too strong for me."'
          rows={3}
          maxLength={500}
          required
          hint={`${openingResponse.length}/500. First thing the AI patient says.`}
        />
        <div className="grid gap-3">
          <Textarea
            label="Prompt 1"
            value={prompt1}
            onChange={(e) => setPrompt1(e.target.value)}
            placeholder='e.g. "Bring up nausea after the first dose."'
            rows={2}
            maxLength={500}
            hint="Cue the AI to surface this detail mid-conversation."
          />
          <Textarea
            label="Prompt 2"
            value={prompt2}
            onChange={(e) => setPrompt2(e.target.value)}
            placeholder='e.g. "Mention worry about addiction."'
            rows={2}
            maxLength={500}
          />
          <Textarea
            label="Prompt 3"
            value={prompt3}
            onChange={(e) => setPrompt3(e.target.value)}
            placeholder='e.g. "Ask whether non-opioid pain relief is possible."'
            rows={2}
            maxLength={500}
          />
        </div>
      </section>

      {/* Section 2: hidden info + closing */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Hidden detail & closing</h3>
        <Textarea
          label="Hidden information (revealed on questioning)"
          value={hiddenInformation}
          onChange={(e) => setHiddenInformation(e.target.value)}
          placeholder="Patient detail NOT printed on the candidate card. The interlocutor reveals these on direct questioning."
          rows={4}
          maxLength={2000}
          hint={`${hiddenInformation.length}/2000`}
        />
        <Textarea
          label="Closing cue"
          value={closingCue}
          onChange={(e) => setClosingCue(e.target.value)}
          placeholder='e.g. "Accept advice once reassured about addiction risk."'
          rows={2}
          maxLength={500}
          hint="How the role-play ends if the candidate satisfies the patient's concerns."
        />
      </section>

      {/* Section 3: persona */}
      <section className="space-y-4">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Persona & behaviour</h3>
        <RadioGroup
          name="resistance-level"
          label="Resistance level"
          value={resistanceLevel}
          onChange={(v) => setResistanceLevel(v as ResistanceLevelCode)}
          options={RESISTANCE_LEVEL_OPTIONS}
        />
        <Input
          label="Emotional state"
          value={emotionalState}
          onChange={(e) => setEmotionalState(e.target.value)}
          placeholder='e.g. "Worried about taking opioids"'
          maxLength={200}
          hint="Richer than the candidate card's one-word emotion."
        />
        <Input
          label="Profession role notes (optional)"
          value={professionRoleNotes ?? ''}
          onChange={(e) => setProfessionRoleNotes(e.target.value)}
          placeholder={`e.g. "Patient's daughter who lives nearby" (only when interlocutor is not the patient).`}
          maxLength={500}
        />
      </section>

      {/* Section 4: lay language triggers (chip input) */}
      <section className="space-y-2">
        <h3 className="text-sm font-bold uppercase tracking-[0.16em] text-muted">Lay-language triggers</h3>
        <p className="text-xs text-muted">
          Jargon terms the AI should prompt the candidate to explain in lay language (e.g.{' '}
          <code className="rounded bg-background-light px-1 py-0.5 text-[10px]">NSAIDs</code>,{' '}
          <code className="rounded bg-background-light px-1 py-0.5 text-[10px]">PRN</code>). Press
          Enter or comma to add.
        </p>
        <div className="flex flex-wrap items-center gap-2 rounded-2xl border border-border bg-background-light p-2">
          {layLanguageTriggers.map((chip) => (
            <span
              key={chip}
              className="inline-flex items-center gap-1 rounded-full bg-primary/10 px-3 py-1 text-xs font-semibold text-primary"
            >
              {chip}
              <button
                type="button"
                aria-label={`Remove ${chip}`}
                onClick={() => removeChip(chip)}
                className="rounded-full p-0.5 text-primary/70 hover:bg-primary/10 hover:text-primary"
              >
                <X className="h-3 w-3" />
              </button>
            </span>
          ))}
          <input
            type="text"
            value={chipDraft}
            onChange={(e) => setChipDraft(e.target.value)}
            onKeyDown={handleChipKey}
            onBlur={() => addChip(chipDraft)}
            placeholder={layLanguageTriggers.length === 0 ? 'Add a trigger term…' : ''}
            className="min-w-[120px] flex-1 bg-transparent px-2 py-1 text-sm text-navy focus:outline-none"
          />
        </div>
      </section>

      {/* Validation hint bar */}
      {validationHints.length > 0 ? (
        <div className="rounded-2xl border border-amber-300 bg-amber-50 px-4 py-3 text-xs text-amber-800 dark:border-amber-900 dark:bg-amber-950 dark:text-amber-200">
          <p className="font-bold uppercase tracking-wider">Before saving</p>
          <ul className="mt-1 list-disc pl-4 space-y-0.5">
            {validationHints.map((hint, i) => <li key={i}>{hint}</li>)}
          </ul>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-2 border-t border-border pt-4">
        <Button type="submit" variant="primary" disabled={submitting}>
          <Save className="mr-1.5 h-4 w-4" />
          {mode === 'create' ? 'Save interlocutor script' : 'Save changes'}
        </Button>
        {secondaryAction}
      </div>
    </form>
  );
}

export default InterlocutorScriptEditor;
