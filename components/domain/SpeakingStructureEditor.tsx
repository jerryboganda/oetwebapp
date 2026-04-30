'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { BookOpenCheck, Save } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Textarea } from '@/components/ui/form-controls';
import {
  getSpeakingStructure,
  updateSpeakingStructure,
  type SpeakingAuthoringStructure,
  type SpeakingStructureValidationReport,
} from '@/lib/content-upload-api';

const DEFAULT_STRUCTURE: SpeakingAuthoringStructure = {
  candidateCard: {
    candidateRole: '',
    setting: '',
    patientRole: '',
    task: '',
    background: '',
    tasks: [],
  },
  interlocutorCard: {
    patientProfile: '',
    cuePrompts: [],
    privateNotes: '',
  },
  warmUpQuestions: [],
  prepTimeSeconds: 180,
  roleplayTimeSeconds: 300,
  patientEmotion: '',
  communicationGoal: '',
  clinicalTopic: '',
  criteriaFocus: [],
  complianceNotes: '',
};

function multilineToArray(value: string) {
  return value
    .split(/\r?\n/)
    .map((item) => item.trim())
    .filter(Boolean);
}

function arrayToMultiline(value?: string[]) {
  return (value ?? []).join('\n');
}

function mergeStructure(value: SpeakingAuthoringStructure): SpeakingAuthoringStructure {
  return {
    ...DEFAULT_STRUCTURE,
    ...value,
    candidateCard: { ...DEFAULT_STRUCTURE.candidateCard, ...value.candidateCard },
    interlocutorCard: { ...DEFAULT_STRUCTURE.interlocutorCard, ...value.interlocutorCard },
    warmUpQuestions: value.warmUpQuestions ?? [],
    criteriaFocus: value.criteriaFocus ?? [],
  };
}

export function SpeakingStructureEditor({ paperId }: { paperId: string }) {
  const [structure, setStructure] = useState<SpeakingAuthoringStructure>(DEFAULT_STRUCTURE);
  const [validation, setValidation] = useState<SpeakingStructureValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await getSpeakingStructure(paperId);
      setStructure(mergeStructure(response.structure));
      setValidation(response.validation);
      setSavedAt(response.updatedAt ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load speaking structure.');
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    void load();
  }, [load]);

  const blockers = useMemo(
    () => validation?.issues.filter((issue) => issue.severity === 'error') ?? [],
    [validation],
  );

  const patch = (next: Partial<SpeakingAuthoringStructure>) => {
    setStructure((current) => mergeStructure({ ...current, ...next }));
  };

  const patchCandidate = (next: NonNullable<SpeakingAuthoringStructure['candidateCard']>) => {
    setStructure((current) => mergeStructure({
      ...current,
      candidateCard: { ...current.candidateCard, ...next },
    }));
  };

  const patchInterlocutor = (next: NonNullable<SpeakingAuthoringStructure['interlocutorCard']>) => {
    setStructure((current) => mergeStructure({
      ...current,
      interlocutorCard: { ...current.interlocutorCard, ...next },
    }));
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      const response = await updateSpeakingStructure(paperId, structure);
      setStructure(mergeStructure(response.structure));
      setValidation(response.validation);
      setSavedAt(response.updatedAt ?? new Date().toISOString());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save speaking structure.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <AdminRoutePanel title="Speaking role-play authoring" description="Loading candidate and interlocutor card fields...">
        <p className="text-sm text-muted">Loading speaking structure...</p>
      </AdminRoutePanel>
    );
  }

  return (
    <AdminRoutePanel
      title="Speaking role-play authoring"
      description="Author the learner-safe candidate card, hidden interlocutor card, warm-up content, timing, tags, and compliance notes used by the Speaking workflow."
      actions={(
        <Button onClick={save} loading={saving}>
          <Save className="h-4 w-4" /> Save speaking structure
        </Button>
      )}
    >
      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      <div className="flex flex-wrap items-center gap-2">
        <Badge variant={validation?.isPublishReady ? 'success' : 'warning'}>
          {validation?.isPublishReady ? 'Publish-ready structure' : 'Needs authoring'}
        </Badge>
        {savedAt ? <span className="text-xs text-muted">Saved {new Date(savedAt).toLocaleString()}</span> : null}
      </div>
      {blockers.length > 0 ? (
        <InlineAlert variant="warning">
          <strong>Publish blockers:</strong> {blockers.map((issue) => issue.message).join(' ')}
        </InlineAlert>
      ) : null}

      <section className="grid gap-4 lg:grid-cols-2">
        <div className="space-y-4 rounded-2xl border border-border bg-background-light p-4">
          <div className="flex items-center gap-2">
            <BookOpenCheck className="h-5 w-5 text-primary" />
            <h3 className="font-bold text-navy">Candidate card (learner-visible)</h3>
          </div>
          <Input label="Candidate role" value={structure.candidateCard?.candidateRole ?? ''} onChange={(e) => patchCandidate({ candidateRole: e.target.value })} />
          <Input label="Setting" value={structure.candidateCard?.setting ?? ''} onChange={(e) => patchCandidate({ setting: e.target.value })} />
          <Input label="Patient/client role" value={structure.candidateCard?.patientRole ?? ''} onChange={(e) => patchCandidate({ patientRole: e.target.value })} />
          <Textarea label="Candidate task / brief" value={structure.candidateCard?.task ?? ''} onChange={(e) => patchCandidate({ task: e.target.value })} />
          <Textarea label="Background" value={structure.candidateCard?.background ?? ''} onChange={(e) => patchCandidate({ background: e.target.value })} />
          <Textarea
            label="Role objectives / task bullets"
            value={arrayToMultiline(structure.candidateCard?.tasks)}
            onChange={(e) => patchCandidate({ tasks: multilineToArray(e.target.value) })}
            hint="One objective per line."
          />
        </div>

        <div className="space-y-4 rounded-2xl border border-warning/25 bg-amber-50/70 p-4">
          <h3 className="font-bold text-navy">Hidden interlocutor card (admin/tutor only)</h3>
          <Textarea
            label="Patient profile / hidden information"
            value={structure.interlocutorCard?.patientProfile ?? ''}
            onChange={(e) => patchInterlocutor({ patientProfile: e.target.value })}
          />
          <Textarea
            label="Cue prompts"
            value={arrayToMultiline(structure.interlocutorCard?.cuePrompts ?? structure.interlocutorCard?.prompts)}
            onChange={(e) => patchInterlocutor({ cuePrompts: multilineToArray(e.target.value) })}
            hint="One prompt per line. This is never exposed to learner task endpoints."
          />
          <Textarea
            label="Private tutor notes"
            value={structure.interlocutorCard?.privateNotes ?? ''}
            onChange={(e) => patchInterlocutor({ privateNotes: e.target.value })}
          />
        </div>
      </section>

      <section className="grid gap-4 lg:grid-cols-2">
        <div className="space-y-4 rounded-2xl border border-border p-4">
          <h3 className="font-bold text-navy">Warm-up and timing</h3>
          <Textarea
            label="Warm-up questions"
            value={arrayToMultiline(structure.warmUpQuestions)}
            onChange={(e) => patch({ warmUpQuestions: multilineToArray(e.target.value) })}
            hint="One question per line."
          />
          <div className="grid grid-cols-2 gap-3">
            <Input type="number" label="Prep seconds" value={structure.prepTimeSeconds ?? 180} onChange={(e) => patch({ prepTimeSeconds: Number(e.target.value) })} />
            <Input type="number" label="Role-play seconds" value={structure.roleplayTimeSeconds ?? 300} onChange={(e) => patch({ roleplayTimeSeconds: Number(e.target.value) })} />
          </div>
        </div>

        <div className="space-y-4 rounded-2xl border border-border p-4">
          <h3 className="font-bold text-navy">Metadata and compliance</h3>
          <Input label="Patient emotion" value={structure.patientEmotion ?? ''} onChange={(e) => patch({ patientEmotion: e.target.value })} />
          <Input label="Communication goal / purpose" value={structure.communicationGoal ?? ''} onChange={(e) => patch({ communicationGoal: e.target.value })} />
          <Input label="Clinical topic" value={structure.clinicalTopic ?? ''} onChange={(e) => patch({ clinicalTopic: e.target.value })} />
          <Textarea
            label="Criteria focus tags"
            value={arrayToMultiline(structure.criteriaFocus)}
            onChange={(e) => patch({ criteriaFocus: multilineToArray(e.target.value) })}
            hint="Use stable criterion keys where possible, e.g. relationshipBuilding."
          />
          <Textarea label="Compliance notes" value={structure.complianceNotes ?? ''} onChange={(e) => patch({ complianceNotes: e.target.value })} />
        </div>
      </section>
    </AdminRoutePanel>
  );
}
