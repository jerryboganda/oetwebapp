'use client';

import { useCallback, useEffect, useState } from 'react';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { UploadSlot } from './UploadSlot';
import { useWizard } from './WizardShell';
import { ensureBundleSection, ensurePaperWithAssets, type PendingAsset } from './step-helpers';
import { setSpeakingStructure, type SpeakingStructurePayload } from '@/lib/mock-wizard/api';

function multilineToList(text: string): string[] {
  return text.split('\n').map((s) => s.trim()).filter(Boolean);
}

export function StepSpeaking() {
  const { bundle, refreshBundle, setSavingState, registerCanAdvance, registerStepSubmit } =
    useWizard();
  const existingSection = bundle.sections.find((s) => s.subtestCode === 'speaking');
  const existingPaperId = existingSection?.contentPaperId ?? null;

  const [pending, setPending] = useState<Record<string, string>>({});
  const [candidateCard, setCandidateCard] = useState('');
  const [interlocutorCard, setInterlocutorCard] = useState('');
  const [warmUp, setWarmUp] = useState('');
  const [role, setRole] = useState('');
  const [scenario, setScenario] = useState('');
  const [objectives, setObjectives] = useState('');
  const [prepTime, setPrepTime] = useState(180);
  const [roleplayTime, setRoleplayTime] = useState(300);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    registerCanAdvance(
      'speaking',
      candidateCard.trim().length > 0 || Boolean(existingPaperId),
    );
  }, [candidateCard, existingPaperId, registerCanAdvance]);

  const submit = useCallback(async () => {
    setError(null);
    setSavingState(true);
    try {
      const pendingAssets: PendingAsset[] = Object.entries(pending).map(([r, mediaAssetId]) => ({
        role: r as PendingAsset['role'],
        mediaAssetId,
      }));
      const { paper } = await ensurePaperWithAssets({
        bundle,
        step: 'speaking',
        existingPaperId,
        paperTitleSuffix: 'Speaking',
        estimatedDurationMinutes: 20,
        cardType: 'roleplay',
        pendingAssets,
      });
      // Split the interlocutor textarea on the first blank line:
      //   Paragraph 1                 -> background (situational context)
      //   Remaining lines (split by NL) -> cuePrompts (specific things to elicit)
      // This matches the help text in the form and avoids duplicating identical
      // content into both fields (which the roleplay engine reads separately).
      const interlocutorBlocks = interlocutorCard.split(/\n\s*\n/);
      const interlocutorBackground = (interlocutorBlocks[0] ?? '').trim();
      const interlocutorCues = interlocutorBlocks.slice(1).join('\n');
      const structure: SpeakingStructurePayload = {
        candidateCard: {
          candidateRole: role,
          setting: scenario,
          task: candidateCard,
          tasks: multilineToList(objectives),
        },
        interlocutorCard: {
          background: interlocutorBackground || interlocutorCard.trim(),
          cuePrompts: multilineToList(interlocutorCues),
        },
        warmUpQuestions: multilineToList(warmUp),
        prepTimeSeconds: prepTime,
        roleplayTimeSeconds: roleplayTime,
      };
      await setSpeakingStructure(paper.id, structure);
      await ensureBundleSection(bundle, 'speaking', paper.id);
      setPending({});
      await refreshBundle();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Speaking save failed.');
      throw err;
    } finally {
      setSavingState(false);
    }
  }, [
    bundle,
    candidateCard,
    existingPaperId,
    interlocutorCard,
    objectives,
    pending,
    prepTime,
    refreshBundle,
    role,
    roleplayTime,
    scenario,
    setSavingState,
    warmUp,
  ]);

  useEffect(() => {
    registerStepSubmit('speaking', submit);
    return () => registerStepSubmit('speaking', null);
  }, [registerStepSubmit, submit]);

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Step 5 — Speaking</h2>
        <p className="text-sm text-muted">
          Upload the role card, assessment criteria and warm-up question sheet, then author the
          interlocutor + candidate cards.
        </p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <section className="space-y-2">
        <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Required assets</h3>
        <div className="grid gap-3 md:grid-cols-3">
          <UploadSlot
            paperId={existingPaperId}
            role="RoleCard"
            label="Role card (PDF)"
            accept="application/pdf"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, RoleCard: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="AssessmentCriteria"
            label="Assessment criteria (PDF)"
            accept="application/pdf"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, AssessmentCriteria: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="WarmUpQuestions"
            label="Warm-up questions (PDF or text)"
            accept="application/pdf,text/plain"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, WarmUpQuestions: id }))}
          />
        </div>
      </section>

      <section className="grid gap-3 md:grid-cols-2">
        <Input label="Role" value={role} onChange={(e) => setRole(e.target.value)} placeholder="e.g. Nurse" />
        <Input
          label="Scenario / setting"
          value={scenario}
          onChange={(e) => setScenario(e.target.value)}
          placeholder="e.g. Outpatient clinic"
        />
        <Input
          label="Prep time (seconds)"
          type="number"
          value={String(prepTime)}
          onChange={(e) => setPrepTime(Number(e.target.value) || 180)}
        />
        <Input
          label="Roleplay time (seconds)"
          type="number"
          value={String(roleplayTime)}
          onChange={(e) => setRoleplayTime(Number(e.target.value) || 300)}
        />
      </section>

      <section className="space-y-3">
        <Textarea
          label="Candidate card"
          value={candidateCard}
          onChange={(e) => setCandidateCard(e.target.value)}
          rows={4}
        />
        <Textarea
          label="Interlocutor card"
          value={interlocutorCard}
          onChange={(e) => setInterlocutorCard(e.target.value)}
          rows={4}
          hint="One cue per line. The first paragraph is treated as background; each subsequent line as a cue prompt."
        />
        <Textarea
          label="Warm-up questions (one per line)"
          value={warmUp}
          onChange={(e) => setWarmUp(e.target.value)}
          rows={3}
        />
        <Textarea
          label="Objectives (one per line)"
          value={objectives}
          onChange={(e) => setObjectives(e.target.value)}
          rows={3}
        />
      </section>
    </div>
  );
}
