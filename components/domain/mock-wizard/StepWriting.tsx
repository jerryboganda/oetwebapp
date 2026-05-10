'use client';

import { useCallback, useEffect, useState } from 'react';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { UploadSlot } from './UploadSlot';
import { useWizard } from './WizardShell';
import { ensureBundleSection, ensurePaperWithAssets, type PendingAsset } from './step-helpers';
import { setWritingStructure, type WritingStructurePayload } from '@/lib/mock-wizard/api';
import {
  CANONICAL_LETTER_TYPES,
  LETTER_TYPE_DISPLAY_LABELS,
  type CanonicalLetterType,
} from '@/lib/writing/letter-types';

const LETTER_TYPES: ReadonlyArray<{ value: CanonicalLetterType; label: string }> =
  CANONICAL_LETTER_TYPES.map((value) => ({
    value,
    label: LETTER_TYPE_DISPLAY_LABELS[value],
  }));

const COUNTRIES = [
  { value: 'UK', label: 'United Kingdom' },
  { value: 'IE', label: 'Ireland' },
  { value: 'AU', label: 'Australia' },
  { value: 'NZ', label: 'New Zealand' },
  { value: 'CA', label: 'Canada' },
  { value: 'US', label: 'United States' },
  { value: 'QA', label: 'Qatar' },
];

export function StepWriting() {
  const { bundle, refreshBundle, setSavingState, registerCanAdvance, registerStepSubmit } =
    useWizard();
  const existingSection = bundle.sections.find((s) => s.subtestCode === 'writing');
  const existingPaperId = existingSection?.contentPaperId ?? null;

  const [pending, setPending] = useState<Record<string, string>>({});
  const [letterType, setLetterType] = useState<CanonicalLetterType>('routine_referral');
  const [country, setCountry] = useState('UK');
  const [wordCountTarget, setWordCountTarget] = useState(200);
  const [writerRole, setWriterRole] = useState('');
  const [recipient, setRecipient] = useState('');
  const [prompt, setPrompt] = useState('');
  const [caseNotes, setCaseNotes] = useState('');
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    registerCanAdvance('writing', prompt.trim().length > 0 || Boolean(existingPaperId));
  }, [existingPaperId, prompt, registerCanAdvance]);

  const submit = useCallback(async () => {
    setError(null);
    setSavingState(true);
    try {
      const pendingAssets: PendingAsset[] = Object.entries(pending).map(([role, mediaAssetId]) => ({
        role: role as PendingAsset['role'],
        mediaAssetId,
      }));
      const { paper } = await ensurePaperWithAssets({
        bundle,
        step: 'writing',
        existingPaperId,
        paperTitleSuffix: 'Writing',
        estimatedDurationMinutes: 45,
        letterType,
        pendingAssets,
      });
      // The country (which drives the 350 vs 300 pass threshold) is resolved
      // per learner at scoring time via OetScoring — NOT stored on the paper.
      // Word count target is a learner-facing target, surfaced in the prompt.
      const promptWithTarget = wordCountTarget && wordCountTarget > 0
        ? `${prompt.trim()}\n\nWrite approximately ${wordCountTarget} words.`
        : prompt.trim();
      const structure: WritingStructurePayload = {
        taskPrompt: promptWithTarget,
        letterType,
        writerRole,
        recipient,
        caseNotes,
        purpose: `${letterType} (target audience: ${country})`,
      };
      await setWritingStructure(paper.id, structure);
      await ensureBundleSection(bundle, 'writing', paper.id);
      setPending({});
      await refreshBundle();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Writing save failed.');
      throw err;
    } finally {
      setSavingState(false);
    }
  }, [
    bundle,
    caseNotes,
    country,
    existingPaperId,
    letterType,
    pending,
    prompt,
    recipient,
    refreshBundle,
    setSavingState,
    wordCountTarget,
    writerRole,
  ]);

  useEffect(() => {
    registerStepSubmit('writing', submit);
    return () => registerStepSubmit('writing', null);
  }, [registerStepSubmit, submit]);

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Step 4 — Writing</h2>
        <p className="text-sm text-muted">
          Upload the case notes (and optional model answer), then author the prompt and metadata.
          Country is informational only — the actual pass threshold (350 for UK/IE/AU/NZ/CA, 300 for US/QA)
          is resolved per learner at scoring time via OetScoring.
        </p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <section className="space-y-2">
        <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Required assets</h3>
        <div className="grid gap-3 md:grid-cols-2">
          <UploadSlot
            paperId={existingPaperId}
            role="CaseNotes"
            label="Case notes (PDF)"
            accept="application/pdf"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, CaseNotes: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="ModelAnswer"
            label="Model answer (PDF) — required by publish gate"
            accept="application/pdf"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, ModelAnswer: id }))}
          />
        </div>
      </section>

      <section className="space-y-3">
        <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Task</h3>
        <Textarea
          label="Prompt / task instructions"
          value={prompt}
          onChange={(e) => setPrompt(e.target.value)}
          rows={4}
          placeholder="Using the information in the case notes, write a letter…"
        />
        <div className="grid gap-2 md:grid-cols-2">
          <Select
            label="Letter type"
            value={letterType}
            onChange={(e) => setLetterType(e.target.value as CanonicalLetterType)}
            options={LETTER_TYPES}
          />
          <Select
            label="Country (pass threshold)"
            value={country}
            onChange={(e) => setCountry(e.target.value)}
            options={COUNTRIES}
          />
          <Input
            label="Word count target"
            type="number"
            value={String(wordCountTarget)}
            onChange={(e) => setWordCountTarget(Number(e.target.value) || 200)}
          />
          <Input
            label="Writer role"
            value={writerRole}
            onChange={(e) => setWriterRole(e.target.value)}
            placeholder="e.g. ward nurse"
          />
          <Input
            label="Recipient"
            value={recipient}
            onChange={(e) => setRecipient(e.target.value)}
            placeholder="e.g. Dr Patel, GP"
          />
        </div>
        <Textarea
          label="Case notes (free text fallback if not uploading PDF)"
          value={caseNotes}
          onChange={(e) => setCaseNotes(e.target.value)}
          rows={4}
        />
      </section>
    </div>
  );
}
