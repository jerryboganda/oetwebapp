'use client';

import { useCallback, useEffect, useState } from 'react';
import { Input, Select, Textarea, Checkbox } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { updateAdminMockBundle } from '@/lib/api';
import { useWizard } from './WizardShell';
import {
  BundleMetadataSchema,
  canAdvanceBundle,
  type BundleMetadata,
} from '@/lib/mock-wizard/state';

const MOCK_TYPE_OPTIONS = [
  { value: 'full', label: 'Full mock (L+R+W+S)' },
  { value: 'lrw', label: 'L+R+W' },
  { value: 'sub', label: 'Single subtest' },
  { value: 'part', label: 'Single part' },
  { value: 'diagnostic', label: 'Diagnostic' },
  { value: 'final_readiness', label: 'Final readiness' },
  { value: 'remedial', label: 'Remedial' },
];

const DIFFICULTY_OPTIONS = [
  { value: 'foundation', label: 'Foundation' },
  { value: 'intermediate', label: 'Intermediate' },
  { value: 'exam_ready', label: 'Exam ready' },
  { value: 'advanced', label: 'Advanced' },
];

const RELEASE_OPTIONS = [
  { value: 'instant', label: 'Instant — visible on publish' },
  { value: 'scheduled', label: 'Scheduled' },
  { value: 'gated', label: 'Gated by entitlement' },
];

export function StepBundle() {
  const { bundle, refreshBundle, setSavingState, registerCanAdvance, registerStepSubmit } =
    useWizard();

  const [form, setForm] = useState<BundleMetadata>({
    title: bundle.title,
    mockType: (bundle.mockType as BundleMetadata['mockType']) ?? 'full',
    appliesToAllProfessions: bundle.appliesToAllProfessions ?? true,
    professionId: bundle.professionId,
    sourceProvenance: bundle.sourceProvenance ?? '',
    priority: bundle.priority ?? 0,
    difficulty: bundle.difficulty ?? 'exam_ready',
    releasePolicy: bundle.releasePolicy ?? 'instant',
    topicTagsCsv: bundle.topicTagsCsv ?? '',
    skillTagsCsv: bundle.skillTagsCsv ?? '',
    watermarkEnabled: bundle.watermarkEnabled ?? true,
    randomiseQuestions: bundle.randomiseQuestions ?? false,
  });
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    registerCanAdvance('bundle', canAdvanceBundle(form));
  }, [form, registerCanAdvance]);

  const submit = useCallback(async () => {
    const parsed = BundleMetadataSchema.safeParse(form);
    if (!parsed.success) {
      setError(parsed.error.issues[0]?.message ?? 'Form invalid.');
      throw new Error('invalid');
    }
    setError(null);
    setSavingState(true);
    try {
      await updateAdminMockBundle(bundle.id, {
        title: form.title.trim(),
        mockType: form.mockType,
        appliesToAllProfessions: form.appliesToAllProfessions,
        professionId: form.appliesToAllProfessions ? null : form.professionId?.trim() || null,
        sourceProvenance: form.sourceProvenance.trim(),
        priority: form.priority,
        difficulty: form.difficulty,
        releasePolicy: form.releasePolicy,
        topicTagsCsv: form.topicTagsCsv,
        skillTagsCsv: form.skillTagsCsv,
        watermarkEnabled: form.watermarkEnabled,
        randomiseQuestions: form.randomiseQuestions,
      });
      await refreshBundle();
    } finally {
      setSavingState(false);
    }
  }, [bundle.id, form, refreshBundle, setSavingState]);

  useEffect(() => {
    registerStepSubmit('bundle', submit);
    return () => registerStepSubmit('bundle', null);
  }, [registerStepSubmit, submit]);

  return (
    <div className="space-y-4">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Step 1 — Bundle metadata</h2>
        <p className="text-sm text-muted">
          Set the headline information for this mock. Provenance and difficulty drive the
          publish-gate downstream.
        </p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-3 md:grid-cols-2">
        <Input
          label="Title *"
          value={form.title}
          onChange={(e) => setForm({ ...form, title: e.target.value })}
          placeholder="Full mock — Cardiology Route 1"
        />
        <Select
          label="Mock type"
          value={form.mockType}
          onChange={(e) => setForm({ ...form, mockType: e.target.value as BundleMetadata['mockType'] })}
          options={MOCK_TYPE_OPTIONS}
        />
        <Select
          label="Difficulty"
          value={form.difficulty}
          onChange={(e) => setForm({ ...form, difficulty: e.target.value })}
          options={DIFFICULTY_OPTIONS}
        />
        <Select
          label="Release policy"
          value={form.releasePolicy}
          onChange={(e) => setForm({ ...form, releasePolicy: e.target.value })}
          options={RELEASE_OPTIONS}
        />
        <Input
          label="Priority"
          type="number"
          value={String(form.priority)}
          onChange={(e) => setForm({ ...form, priority: Number(e.target.value) || 0 })}
        />
        <Input
          label="Profession id (when not 'all')"
          value={form.professionId ?? ''}
          onChange={(e) => setForm({ ...form, professionId: e.target.value })}
          disabled={form.appliesToAllProfessions}
          placeholder="e.g. nursing"
        />
        <Input
          label="Topic tags (CSV)"
          value={form.topicTagsCsv ?? ''}
          onChange={(e) => setForm({ ...form, topicTagsCsv: e.target.value })}
          placeholder="cardiology, discharge"
        />
        <Input
          label="Skill tags (CSV)"
          value={form.skillTagsCsv ?? ''}
          onChange={(e) => setForm({ ...form, skillTagsCsv: e.target.value })}
          placeholder="inference, fluency"
        />
      </div>

      <div className="space-y-2">
        <Textarea
          label="Source provenance *"
          value={form.sourceProvenance}
          onChange={(e) => setForm({ ...form, sourceProvenance: e.target.value })}
          placeholder="Where did this mock come from? e.g. Adapted from CBLA recall report 2024-Q3, with edits to remove copyrighted phrasing."
          rows={4}
        />
        <p className="text-xs text-muted">
          Required by the publish gate. Describe origin (recall, AI-generated, partner content),
          editing pass, and the reason this mock is safe to release. Without this, the bundle
          cannot be published.
        </p>
      </div>

      <div className="grid gap-2 md:grid-cols-3">
        <Checkbox
          label="Applies to all professions"
          checked={form.appliesToAllProfessions}
          onChange={(e) => setForm({ ...form, appliesToAllProfessions: e.target.checked })}
        />
        <Checkbox
          label="Watermark enabled"
          checked={form.watermarkEnabled}
          onChange={(e) => setForm({ ...form, watermarkEnabled: e.target.checked })}
        />
        <Checkbox
          label="Randomise questions"
          checked={form.randomiseQuestions}
          onChange={(e) => setForm({ ...form, randomiseQuestions: e.target.checked })}
        />
      </div>
    </div>
  );
}
