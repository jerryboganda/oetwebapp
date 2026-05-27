'use client';

import { useState, type FormEvent } from 'react';

import { Input, RadioGroup, Select, Textarea } from '@/components/ui/form-controls';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import type { TutorClassCreatePayload } from '@/lib/api';

const CLASS_TYPES = [
  { value: 'GroupClass', label: 'Group Class', description: 'Standard cohort class with multiple learners.' },
  { value: 'Masterclass', label: 'Masterclass', description: 'High-value session, usually higher credit cost.' },
  { value: 'OneToOne', label: 'One-to-One', description: 'Individual coaching session.' },
  { value: 'MockReview', label: 'Mock Review', description: 'Targeted feedback walk-through.' },
  { value: 'OfficeHours', label: 'Office Hours', description: 'Open Q&A.' },
];

const PROFESSION_TRACKS = ['All', 'Medicine', 'Pharmacy', 'Nursing', 'Dentistry'];
const LEVELS = ['All', 'Beginner', 'Intermediate', 'Advanced'];

function toLocalInputValue(date: Date): string {
  const copy = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return copy.toISOString().slice(0, 16);
}

const defaultStart = toLocalInputValue(new Date(Date.now() + 24 * 60 * 60 * 1000));
const minStart = toLocalInputValue(new Date(Date.now() + 60 * 60 * 1000));

interface FormState {
  title: string;
  titleAr: string;
  description: string;
  descriptionAr: string;
  type: string;
  professionTrack: string;
  level: string;
  scheduledStartAt: string;
  durationMinutes: number;
  capacity: number;
  creditCost: number;
  coverImageUrl: string;
  tags: string;
  autoPublish: boolean;
}

const initial: FormState = {
  title: '',
  titleAr: '',
  description: '',
  descriptionAr: '',
  type: 'GroupClass',
  professionTrack: 'All',
  level: 'All',
  scheduledStartAt: defaultStart,
  durationMinutes: 60,
  capacity: 30,
  creditCost: 5,
  coverImageUrl: '',
  tags: '',
  autoPublish: false,
};

export interface ClassEditorFormProps {
  onSubmit: (payload: TutorClassCreatePayload) => Promise<void>;
  onCancel?: () => void;
  submitting?: boolean;
  apiError?: string | null;
}

export function ClassEditorForm({ onSubmit, onCancel, submitting = false, apiError }: ClassEditorFormProps) {
  const [form, setForm] = useState<FormState>(initial);
  const [fieldErrors, setFieldErrors] = useState<Partial<Record<keyof FormState, string>>>({});

  function update<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
    setFieldErrors((prev) => ({ ...prev, [key]: undefined }));
  }

  function validate(): boolean {
    const errors: Partial<Record<keyof FormState, string>> = {};
    if (!form.title.trim()) errors.title = 'Title is required.';
    if (!form.description.trim()) errors.description = 'Description is required.';
    if (!form.scheduledStartAt) {
      errors.scheduledStartAt = 'Start time is required.';
    } else if (new Date(form.scheduledStartAt).getTime() <= Date.now()) {
      errors.scheduledStartAt = 'Start time must be in the future.';
    }
    if (form.durationMinutes < 15) errors.durationMinutes = 'Duration must be at least 15 minutes.';
    if (form.capacity < 1) errors.capacity = 'Capacity must be at least 1.';
    if (form.creditCost < 0) errors.creditCost = 'Credit cost cannot be negative.';
    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    const payload: TutorClassCreatePayload = {
      title: form.title.trim(),
      titleAr: form.titleAr.trim() || null,
      description: form.description.trim(),
      descriptionAr: form.descriptionAr.trim() || null,
      type: form.type,
      professionTrack: form.professionTrack,
      level: form.level,
      scheduledStartAt: new Date(form.scheduledStartAt).toISOString(),
      durationMinutes: form.durationMinutes,
      capacity: form.capacity,
      creditCost: form.creditCost,
      coverImageUrl: form.coverImageUrl.trim() || null,
      tags: form.tags.split(',').map((t) => t.trim()).filter(Boolean),
      autoPublish: form.autoPublish,
    };
    await onSubmit(payload);
  }

  return (
    <form onSubmit={(e) => void handleSubmit(e)} noValidate className="space-y-6">
      {apiError ? <InlineAlert variant="warning">{apiError}</InlineAlert> : null}

      <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <h2 className="text-base font-semibold text-navy">Class details</h2>
        <Input
          label="Title"
          value={form.title}
          onChange={(e) => update('title', e.target.value)}
          error={fieldErrors.title}
          maxLength={200}
          required
        />
        <Input
          label="Title (Arabic)"
          value={form.titleAr}
          onChange={(e) => update('titleAr', e.target.value)}
          dir="rtl"
          lang="ar"
          hint="Optional. Shown to Arabic-speaking learners."
          maxLength={200}
        />
        <Textarea
          label="Description"
          value={form.description}
          onChange={(e) => update('description', e.target.value)}
          error={fieldErrors.description}
          maxLength={2000}
          required
        />
        <Textarea
          label="Description (Arabic)"
          value={form.descriptionAr}
          onChange={(e) => update('descriptionAr', e.target.value)}
          dir="rtl"
          lang="ar"
          hint="Optional."
          maxLength={2000}
        />
      </section>

      <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <h2 className="text-base font-semibold text-navy">Type & audience</h2>
        <RadioGroup
          name="class-type"
          label="Type"
          value={form.type}
          onChange={(value) => update('type', value)}
          options={CLASS_TYPES}
        />
        <div className="grid gap-4 sm:grid-cols-2">
          <Select
            label="Profession track"
            value={form.professionTrack}
            onChange={(e) => update('professionTrack', e.target.value)}
            options={PROFESSION_TRACKS.map((t) => ({ value: t, label: t }))}
          />
          <Select
            label="Level"
            value={form.level}
            onChange={(e) => update('level', e.target.value)}
            options={LEVELS.map((l) => ({ value: l, label: l }))}
          />
        </div>
      </section>

      <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <h2 className="text-base font-semibold text-navy">Schedule</h2>
        <p className="text-xs text-muted">
          Wave B1 only supports one-off sessions; recurrence will land in a future wave.
        </p>
        <Input
          type="datetime-local"
          label="Start date & time"
          value={form.scheduledStartAt}
          min={minStart}
          onChange={(e) => update('scheduledStartAt', e.target.value)}
          error={fieldErrors.scheduledStartAt}
          required
        />
        <div className="grid gap-4 sm:grid-cols-3">
          <Input
            type="number"
            label="Duration (minutes)"
            min={15}
            max={480}
            step={15}
            value={form.durationMinutes}
            onChange={(e) => update('durationMinutes', Number(e.target.value))}
            error={fieldErrors.durationMinutes}
            required
          />
          <Input
            type="number"
            label="Capacity"
            min={1}
            max={500}
            value={form.capacity}
            onChange={(e) => update('capacity', Number(e.target.value))}
            error={fieldErrors.capacity}
            required
          />
          <Input
            type="number"
            label="Credit cost"
            min={0}
            max={999}
            value={form.creditCost}
            onChange={(e) => update('creditCost', Number(e.target.value))}
            error={fieldErrors.creditCost}
            required
          />
        </div>
      </section>

      <section className="space-y-4 rounded-2xl border border-border bg-surface p-5 shadow-sm">
        <h2 className="text-base font-semibold text-navy">Media & tags</h2>
        <Input
          type="url"
          label="Cover image URL"
          value={form.coverImageUrl}
          onChange={(e) => update('coverImageUrl', e.target.value)}
          placeholder="https://cdn.example.com/cover.jpg"
        />
        <Input
          label="Tags"
          value={form.tags}
          onChange={(e) => update('tags', e.target.value)}
          hint="Comma-separated."
          placeholder="speaking, medicine, advanced"
        />
        <label className="flex cursor-pointer items-start gap-3 rounded-2xl border border-border bg-background-light p-4 shadow-sm">
          <input
            type="checkbox"
            checked={form.autoPublish}
            onChange={(e) => update('autoPublish', e.target.checked)}
            className="mt-0.5 h-4 w-4 rounded border-border text-primary focus:ring-primary"
          />
          <div>
            <span className="text-sm font-semibold text-navy">Publish immediately</span>
            <p className="mt-0.5 text-xs text-muted">If unchecked, the class is saved as a Draft.</p>
          </div>
        </label>
      </section>

      <div className="flex items-center justify-end gap-3">
        {onCancel ? (
          <Button type="button" variant="ghost" onClick={onCancel} disabled={submitting}>
            Cancel
          </Button>
        ) : null}
        <Button type="submit" variant="primary" loading={submitting} disabled={submitting}>
          {form.autoPublish ? 'Create & publish' : 'Create class'}
        </Button>
      </div>
    </form>
  );
}
