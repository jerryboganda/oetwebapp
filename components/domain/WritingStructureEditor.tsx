'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { FileText, Save } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import {
  getWritingStructure,
  updateWritingStructure,
  type WritingAuthoringStructure,
  type WritingStructureValidationReport,
} from '@/lib/content-upload-api';

const DEFAULT_STRUCTURE: WritingAuthoringStructure = {
  taskPrompt: '',
  taskDate: '',
  writerRole: '',
  recipient: '',
  purpose: '',
  caseNotes: '',
  modelAnswerText: '',
  criteriaFocus: [],
  authoringNotes: '',
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

function mergeStructure(value: WritingAuthoringStructure): WritingAuthoringStructure {
  return {
    ...DEFAULT_STRUCTURE,
    ...value,
    criteriaFocus: value.criteriaFocus ?? [],
  };
}

export function WritingStructureEditor({ paperId }: { paperId: string }) {
  const [structure, setStructure] = useState<WritingAuthoringStructure>(DEFAULT_STRUCTURE);
  const [validation, setValidation] = useState<WritingStructureValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedAt, setSavedAt] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const response = await getWritingStructure(paperId);
      setStructure(mergeStructure(response.structure));
      setValidation(response.validation);
      setSavedAt(response.updatedAt ?? null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load writing structure.');
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

  const patch = (next: Partial<WritingAuthoringStructure>) => {
    setStructure((current) => mergeStructure({ ...current, ...next }));
  };

  const save = async () => {
    setSaving(true);
    setError(null);
    try {
      const response = await updateWritingStructure(paperId, structure);
      setStructure(mergeStructure(response.structure));
      setValidation(response.validation);
      setSavedAt(response.updatedAt ?? new Date().toISOString());
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save writing structure.');
    } finally {
      setSaving(false);
    }
  };

  if (loading) {
    return (
      <AdminRoutePanel title="Writing authoring" description="Loading task prompt, case notes, and model answer fields...">
        <p className="text-sm text-muted">Loading writing structure...</p>
      </AdminRoutePanel>
    );
  }

  return (
    <AdminRoutePanel
      title="Writing authoring"
      description="Author the learner-visible OET Writing task prompt and case notes, plus the post-submit model answer used by the study flow. Model answers are never exposed in the learner player."
      actions={(
        <Button onClick={save} loading={saving}>
          <Save className="h-4 w-4" /> Save writing structure
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
            <FileText className="h-5 w-5 text-primary" />
            <h3 className="font-bold text-navy">Learner task</h3>
          </div>
          <Textarea
            label="Task prompt"
            value={structure.taskPrompt ?? ''}
            onChange={(e) => patch({ taskPrompt: e.target.value })}
            hint="The instruction learners see above the case notes."
          />
          <div className="grid gap-3 md:grid-cols-2">
            <Input label="Task date" value={structure.taskDate ?? ''} onChange={(e) => patch({ taskDate: e.target.value })} placeholder="25 Mar 2023" />
            <Input label="Writer role" value={structure.writerRole ?? ''} onChange={(e) => patch({ writerRole: e.target.value })} placeholder="Doctor / Nurse / Dentist" />
            <Input label="Recipient" value={structure.recipient ?? ''} onChange={(e) => patch({ recipient: e.target.value })} placeholder="Dr Smith, GP" />
            <Input label="Purpose" value={structure.purpose ?? ''} onChange={(e) => patch({ purpose: e.target.value })} placeholder="Referral / transfer / discharge update" />
          </div>
          <Textarea
            label="Criteria focus"
            value={arrayToMultiline(structure.criteriaFocus)}
            onChange={(e) => patch({ criteriaFocus: multilineToArray(e.target.value) })}
            hint="One focus area per line, e.g. purpose, content, conciseness."
          />
        </div>

        <div className="space-y-4 rounded-2xl border border-border bg-background-light p-4">
          <h3 className="font-bold text-navy">Case notes and reference answer</h3>
          <Textarea
            label="Case notes"
            value={structure.caseNotes ?? ''}
            onChange={(e) => patch({ caseNotes: e.target.value })}
            rows={12}
            hint="Learner-visible stimulus. Keep the note order faithful to the source PDF."
          />
          <Textarea
            label="Model answer text"
            value={structure.modelAnswerText ?? ''}
            onChange={(e) => patch({ modelAnswerText: e.target.value })}
            rows={12}
            hint="Hidden during attempts; shown only in the model-answer study flow after submission."
          />
        </div>
      </section>

      <section className="rounded-2xl border border-border p-4">
        <Textarea
          label="Private authoring notes"
          value={structure.authoringNotes ?? ''}
          onChange={(e) => patch({ authoringNotes: e.target.value })}
          hint="Admin-only notes about source quality, extraction corrections, or review decisions."
        />
      </section>
    </AdminRoutePanel>
  );
}