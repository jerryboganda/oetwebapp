'use client';

import { type FormEvent, useMemo, useState } from 'react';
import { AlertCircle, FileText, Save } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Checkbox, Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import type {
  StrategyGuideAdminItem,
  StrategyGuidePublishValidation,
  StrategyGuideUpsertPayload,
} from '@/lib/types/strategies';

export type StrategyGuideDraft = {
  slug: string;
  examTypeCode: string;
  subtestCode: string;
  title: string;
  summary: string;
  category: string;
  readingTimeMinutes: string;
  sortOrder: string;
  isPreviewEligible: boolean;
  contentLessonId: string;
  contentJson: string;
  contentHtml: string;
  sourceProvenance: string;
  rightsStatus: string;
  freshnessConfidence: string;
};

type DraftErrors = Partial<Record<keyof StrategyGuideDraft | 'content', string>>;

const subtestOptions = [
  { value: '', label: 'All subtests' },
  { value: 'listening', label: 'Listening' },
  { value: 'reading', label: 'Reading' },
  { value: 'writing', label: 'Writing' },
  { value: 'speaking', label: 'Speaking' },
];

const categoryOptions = [
  { value: 'overview', label: 'Overview' },
  { value: 'case_notes', label: 'Case notes' },
  { value: 'structure', label: 'Structure' },
  { value: 'timing', label: 'Timing' },
  { value: 'common_mistakes', label: 'Common mistakes' },
  { value: 'exam_day', label: 'Exam day' },
];

const starterContent = JSON.stringify({
  version: 1,
  overview: 'Add a concise learner-facing overview.',
  sections: [
    {
      heading: 'What to do',
      body: 'Write the practical guidance here.',
      bullets: ['Add one concrete action.', 'Add one scoring-aware reminder.'],
    },
  ],
  keyTakeaways: ['Add the main takeaway.'],
}, null, 2);

export function emptyStrategyGuideDraft(): StrategyGuideDraft {
  return {
    slug: '',
    examTypeCode: 'oet',
    subtestCode: '',
    title: '',
    summary: '',
    category: 'overview',
    readingTimeMinutes: '6',
    sortOrder: '0',
    isPreviewEligible: true,
    contentLessonId: '',
    contentJson: starterContent,
    contentHtml: '',
    sourceProvenance: '',
    rightsStatus: 'owned',
    freshnessConfidence: 'high',
  };
}

export function strategyGuideToDraft(guide: StrategyGuideAdminItem): StrategyGuideDraft {
  return {
    slug: guide.slug ?? '',
    examTypeCode: guide.examTypeCode || 'oet',
    subtestCode: guide.subtestCode ?? '',
    title: guide.title,
    summary: guide.summary,
    category: guide.category || 'overview',
    readingTimeMinutes: String(guide.readingTimeMinutes || 0),
    sortOrder: String(guide.sortOrder || 0),
    isPreviewEligible: guide.isPreviewEligible,
    contentLessonId: guide.contentLessonId ?? '',
    contentJson: guide.contentJson ?? '',
    contentHtml: guide.contentHtml ?? '',
    sourceProvenance: guide.sourceProvenance ?? '',
    rightsStatus: guide.rightsStatus ?? '',
    freshnessConfidence: guide.freshnessConfidence ?? '',
  };
}

export function buildStrategyGuidePayload(draft: StrategyGuideDraft): StrategyGuideUpsertPayload {
  return {
    slug: draft.slug.trim() || null,
    examTypeCode: draft.examTypeCode.trim() || 'oet',
    subtestCode: draft.subtestCode.trim() || null,
    title: draft.title.trim(),
    summary: draft.summary.trim(),
    category: draft.category.trim() || 'overview',
    readingTimeMinutes: Number(draft.readingTimeMinutes) || 0,
    sortOrder: Number(draft.sortOrder) || 0,
    isPreviewEligible: draft.isPreviewEligible,
    contentLessonId: draft.contentLessonId.trim() || null,
    contentJson: draft.contentJson.trim() || null,
    contentHtml: draft.contentHtml.trim() || null,
    sourceProvenance: draft.sourceProvenance.trim() || null,
    rightsStatus: draft.rightsStatus.trim() || null,
    freshnessConfidence: draft.freshnessConfidence.trim() || null,
  };
}

function validateDraft(draft: StrategyGuideDraft): DraftErrors {
  const errors: DraftErrors = {};
  if (!draft.title.trim()) errors.title = 'Title is required.';
  if (!draft.summary.trim()) errors.summary = 'Summary is required.';
  if (!draft.category.trim()) errors.category = 'Category is required.';
  if ((Number(draft.readingTimeMinutes) || 0) <= 0) {
    errors.readingTimeMinutes = 'Reading time must be greater than zero.';
  }
  if (!draft.sourceProvenance.trim()) {
    errors.sourceProvenance = 'Source provenance is required.';
  }
  if (!draft.contentJson.trim() && !draft.contentHtml.trim()) {
    errors.content = 'Add structured content or sanitized HTML.';
  }
  if (draft.contentJson.trim()) {
    try {
      JSON.parse(draft.contentJson);
    } catch {
      errors.contentJson = 'Structured content must be valid JSON.';
    }
  }
  return errors;
}

export function AdminStrategyGuideEditor({
  initial,
  saving,
  submitLabel = 'Save guide',
  validation,
  onSave,
}: {
  initial: StrategyGuideDraft;
  saving?: boolean;
  submitLabel?: string;
  validation?: StrategyGuidePublishValidation | null;
  onSave: (payload: StrategyGuideUpsertPayload) => Promise<void> | void;
}) {
  const [draft, setDraft] = useState(initial);
  const [errors, setErrors] = useState<DraftErrors>({});

  const hasPublishErrors = Boolean(validation && !validation.canPublish && validation.errors.length > 0);

  const contentStatus = useMemo(() => {
    if (draft.contentJson.trim()) return 'Structured JSON';
    if (draft.contentHtml.trim()) return 'HTML fallback';
    return 'Missing content';
  }, [draft.contentHtml, draft.contentJson]);

  function update<K extends keyof StrategyGuideDraft>(key: K, value: StrategyGuideDraft[K]) {
    setDraft((current) => ({ ...current, [key]: value }));
    setErrors((current) => ({ ...current, [key]: undefined }));
  }

  async function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    const nextErrors = validateDraft(draft);
    setErrors(nextErrors);
    if (Object.values(nextErrors).some(Boolean)) return;
    await onSave(buildStrategyGuidePayload(draft));
  }

  return (
    <form className="space-y-6" onSubmit={handleSubmit}>
      {hasPublishErrors ? (
        <InlineAlert variant="warning" title="Publish checks">
          <ul className="list-disc space-y-1 pl-5">
            {validation?.errors.map((error) => (
              <li key={`${error.field}-${error.message}`}>{error.message}</li>
            ))}
          </ul>
        </InlineAlert>
      ) : null}

      {errors.content ? (
        <InlineAlert variant="error" title="Content required">
          {errors.content}
        </InlineAlert>
      ) : null}

      <section className="grid gap-4 lg:grid-cols-[1.2fr_0.8fr]">
        <div className="space-y-4">
          <Input
            label="Title"
            value={draft.title}
            onChange={(event) => update('title', event.target.value)}
            error={errors.title}
          />
          <Textarea
            label="Summary"
            value={draft.summary}
            onChange={(event) => update('summary', event.target.value)}
            error={errors.summary}
            rows={3}
          />
          <Input
            label="Slug"
            value={draft.slug}
            onChange={(event) => update('slug', event.target.value)}
            placeholder="auto-generated-from-title"
          />
        </div>

        <div className="space-y-4">
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Exam"
              value={draft.examTypeCode}
              onChange={(event) => update('examTypeCode', event.target.value)}
              options={[{ value: 'oet', label: 'OET' }]}
            />
            <Select
              label="Subtest"
              value={draft.subtestCode}
              onChange={(event) => update('subtestCode', event.target.value)}
              options={subtestOptions}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Select
              label="Category"
              value={draft.category}
              onChange={(event) => update('category', event.target.value)}
              options={categoryOptions}
              error={errors.category}
            />
            <Input
              label="Read time"
              type="number"
              min={1}
              value={draft.readingTimeMinutes}
              onChange={(event) => update('readingTimeMinutes', event.target.value)}
              error={errors.readingTimeMinutes}
            />
          </div>
          <div className="grid gap-3 sm:grid-cols-2">
            <Input
              label="Sort order"
              type="number"
              value={draft.sortOrder}
              onChange={(event) => update('sortOrder', event.target.value)}
            />
            <Input
              label="Content lesson"
              value={draft.contentLessonId}
              onChange={(event) => update('contentLessonId', event.target.value)}
              placeholder="optional lesson id"
            />
          </div>
          <Checkbox
            label="Preview eligible"
            checked={draft.isPreviewEligible}
            onChange={(event) => update('isPreviewEligible', event.target.checked)}
          />
        </div>
      </section>

      <section className="space-y-4">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div className="flex items-center gap-2 text-sm font-semibold text-navy">
            <FileText className="h-4 w-4 text-primary" />
            Content
          </div>
          <span className="rounded-full bg-background-light px-3 py-1 text-xs font-semibold text-muted">
            {contentStatus}
          </span>
        </div>
        <Textarea
          label="Structured content JSON"
          value={draft.contentJson}
          onChange={(event) => update('contentJson', event.target.value)}
          error={errors.contentJson}
          rows={14}
          className="font-mono text-xs"
        />
        <Textarea
          label="Sanitized HTML"
          value={draft.contentHtml}
          onChange={(event) => update('contentHtml', event.target.value)}
          rows={8}
          className="font-mono text-xs"
        />
      </section>

      <section className="grid gap-4 lg:grid-cols-3">
        <Textarea
          label="Source provenance"
          value={draft.sourceProvenance}
          onChange={(event) => update('sourceProvenance', event.target.value)}
          error={errors.sourceProvenance}
          rows={4}
        />
        <Input
          label="Rights status"
          value={draft.rightsStatus}
          onChange={(event) => update('rightsStatus', event.target.value)}
          placeholder="owned, licensed, migrated"
        />
        <Input
          label="Freshness"
          value={draft.freshnessConfidence}
          onChange={(event) => update('freshnessConfidence', event.target.value)}
          placeholder="high, medium, low"
        />
      </section>

      <div className="flex flex-col gap-3 border-t border-border pt-5 sm:flex-row sm:items-center sm:justify-between">
        <p className="flex items-start gap-2 text-xs leading-5 text-muted">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
          Provenance and content are required before a guide can be published.
        </p>
        <Button type="submit" loading={saving} className="gap-2">
          <Save className="h-4 w-4" />
          {submitLabel}
        </Button>
      </div>
    </form>
  );
}
