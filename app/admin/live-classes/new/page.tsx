'use client';

/* Hallmark · macrostructure: Settings / Form · tone: utilitarian · anchor hue: primary-blue */

import React, { useState } from 'react';
import { useRouter } from 'next/navigation';
import { CalendarPlus } from 'lucide-react';

import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminPageShell } from '@/components/admin/layout/admin-page-shell';
import { PageHeader } from '@/components/admin/ui/page-header';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { createAdminLiveClass, type AdminLiveClassUpsertPayload } from '@/lib/api';

/* ------------------------------------------------------------------ */
/* Constants                                                           */
/* ------------------------------------------------------------------ */

const CLASS_TYPES = ['GroupClass', 'Masterclass', 'OneToOne', 'MockReview', 'OfficeHours'] as const;
const PROFESSION_TRACKS = ['All', 'Medicine', 'Pharmacy', 'Nursing', 'Dentistry'] as const;
const LEVELS = ['All', 'Beginner', 'Intermediate', 'Advanced'] as const;

/* ------------------------------------------------------------------ */
/* Helpers                                                             */
/* ------------------------------------------------------------------ */

function toLocalInputValue(date: Date): string {
  const copy = new Date(date.getTime() - date.getTimezoneOffset() * 60_000);
  return copy.toISOString().slice(0, 16);
}

const defaultMinStart = toLocalInputValue(new Date(Date.now() + 60 * 60 * 1000)); // 1 h from now

/* ------------------------------------------------------------------ */
/* Form state                                                          */
/* ------------------------------------------------------------------ */

interface FormState {
  title: string;
  titleAr: string;
  description: string;
  descriptionAr: string;
  type: string;
  professionTrack: string;
  level: string;
  tutorProfileId: string;
  scheduledStartAt: string;
  durationMinutes: number;
  capacity: number;
  creditCost: number;
  coverImageUrl: string;
  tags: string; // comma-separated
  autoPublish: boolean;
}

const initialForm: FormState = {
  title: '',
  titleAr: '',
  description: '',
  descriptionAr: '',
  type: 'GroupClass',
  professionTrack: 'All',
  level: 'All',
  tutorProfileId: '',
  scheduledStartAt: toLocalInputValue(new Date(Date.now() + 24 * 60 * 60 * 1000)),
  durationMinutes: 60,
  capacity: 100,
  creditCost: 5,
  coverImageUrl: '',
  tags: '',
  autoPublish: false,
};

/* ------------------------------------------------------------------ */
/* Validation                                                          */
/* ------------------------------------------------------------------ */

type FieldErrors = Partial<Record<keyof FormState, string>>;

function validate(form: FormState): FieldErrors {
  const errors: FieldErrors = {};

  if (!form.title.trim()) {
    errors.title = 'Title is required.';
  }

  if (!form.description.trim()) {
    errors.description = 'Description is required.';
  }

  if (!form.scheduledStartAt) {
    errors.scheduledStartAt = 'Start time is required.';
  } else {
    const start = new Date(form.scheduledStartAt).getTime();
    if (start <= Date.now()) {
      errors.scheduledStartAt = 'Start time must be in the future.';
    }
  }

  if (form.durationMinutes < 15) {
    errors.durationMinutes = 'Duration must be at least 15 minutes.';
  }

  if (form.capacity < 1) {
    errors.capacity = 'Capacity must be at least 1.';
  }

  if (form.creditCost < 0) {
    errors.creditCost = 'Credit cost cannot be negative.';
  }

  return errors;
}

/* ------------------------------------------------------------------ */
/* Shared field components                                             */
/* ------------------------------------------------------------------ */

function FieldWrapper({
  label,
  error,
  hint,
  required,
  children,
}: {
  label: string;
  error?: string;
  hint?: string;
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1">
      <label className="block text-xs font-semibold text-admin-fg-muted">
        {label}
        {required ? (
          <span className="ml-0.5 text-[var(--admin-danger)]" aria-hidden="true"> *</span>
        ) : null}
      </label>
      {children}
      {hint && !error ? (
        <p className="text-xs text-admin-fg-muted">{hint}</p>
      ) : null}
      {error ? (
        <p className="text-xs text-[var(--admin-danger)]" role="alert">{error}</p>
      ) : null}
    </div>
  );
}

const inputClass =
  'mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong placeholder:text-admin-fg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1 aria-invalid:border-[var(--admin-danger)]';

const textareaClass =
  'mt-1 min-h-[100px] w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong placeholder:text-admin-fg-muted focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1 resize-y aria-invalid:border-[var(--admin-danger)]';

const selectClass =
  'mt-1 h-10 w-full rounded-admin-md border border-admin-border bg-admin-bg-surface px-3 text-sm text-admin-fg-strong focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1';

/* ------------------------------------------------------------------ */
/* Page                                                                */
/* ------------------------------------------------------------------ */

export default function AdminLiveClassNewPage() {
  useAdminAuth();
  const router = useRouter();

  const [form, setForm] = useState<FormState>(initialForm);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [apiError, setApiError] = useState<string | null>(null);
  const [saving, setSaving] = useState(false);
  const [submitted, setSubmitted] = useState(false);

  function set<K extends keyof FormState>(key: K, value: FormState[K]) {
    setForm((prev) => ({ ...prev, [key]: value }));
    if (submitted) {
      // Re-validate on change after first submit attempt
      setFieldErrors((prev) => ({ ...prev, [key]: undefined }));
    }
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    setSubmitted(true);
    setApiError(null);

    const errors = validate(form);
    if (Object.keys(errors).length > 0) {
      setFieldErrors(errors);
      return;
    }

    const payload: AdminLiveClassUpsertPayload = {
      title: form.title.trim(),
      titleAr: form.titleAr.trim() || null,
      description: form.description.trim(),
      descriptionAr: form.descriptionAr.trim() || null,
      type: form.type,
      professionTrack: form.professionTrack,
      level: form.level,
      tutorProfileId: form.tutorProfileId.trim() || null,
      scheduledStartAt: new Date(form.scheduledStartAt).toISOString(),
      durationMinutes: form.durationMinutes,
      capacity: form.capacity,
      creditCost: form.creditCost,
      coverImageUrl: form.coverImageUrl.trim() || null,
      tags: form.tags
        .split(',')
        .map((t) => t.trim())
        .filter(Boolean),
      autoPublish: form.autoPublish,
    };

    setSaving(true);
    try {
      const created = await createAdminLiveClass(payload);
      router.push(`/admin/live-classes/${created.id}`);
    } catch (err: unknown) {
      setApiError(err instanceof Error ? err.message : 'Could not create live class. Please try again.');
      setSaving(false);
    }
  }

  return (
    <AdminRouteWorkspace role="main" aria-label="Create live class">
      <AdminPageShell>
        <PageHeader
          title="New Live Class"
          description="Schedule a Zoom-backed class. Fill in the details below and optionally publish immediately."
          breadcrumbs={[
            { label: 'Admin', href: '/admin' },
            { label: 'Live Classes', href: '/admin/live-classes' },
            { label: 'New Class' },
          ]}
        />

        {apiError ? (
          <InlineAlert variant="warning" className="mb-4">{apiError}</InlineAlert>
        ) : null}

        <form onSubmit={(e) => void handleSubmit(e)} noValidate>
          <div className="grid gap-5 xl:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">

            {/* ── Left column: content ── */}
            <div className="space-y-5">

              {/* Core details */}
              <Card>
                <CardHeader>
                  <CardTitle>Class Details</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <FieldWrapper label="Title" required error={fieldErrors.title}>
                    <input
                      className={inputClass}
                      value={form.title}
                      onChange={(e) => set('title', e.target.value)}
                      placeholder="e.g. OET Speaking Masterclass: Medicine"
                      maxLength={200}
                      aria-invalid={!!fieldErrors.title}
                      aria-required="true"
                    />
                  </FieldWrapper>

                  <FieldWrapper
                    label="Title (Arabic)"
                    error={fieldErrors.titleAr}
                    hint="Optional. Will be displayed to Arabic-speaking learners."
                  >
                    <input
                      className={inputClass}
                      value={form.titleAr}
                      onChange={(e) => set('titleAr', e.target.value)}
                      placeholder="العنوان بالعربية"
                      dir="rtl"
                      lang="ar"
                      maxLength={200}
                    />
                  </FieldWrapper>

                  <FieldWrapper label="Description" required error={fieldErrors.description}>
                    <textarea
                      className={textareaClass}
                      value={form.description}
                      onChange={(e) => set('description', e.target.value)}
                      placeholder="Describe what learners will gain from this class…"
                      maxLength={2000}
                      aria-invalid={!!fieldErrors.description}
                      aria-required="true"
                    />
                  </FieldWrapper>

                  <FieldWrapper
                    label="Description (Arabic)"
                    error={fieldErrors.descriptionAr}
                    hint="Optional."
                  >
                    <textarea
                      className={textareaClass}
                      value={form.descriptionAr}
                      onChange={(e) => set('descriptionAr', e.target.value)}
                      placeholder="الوصف بالعربية"
                      dir="rtl"
                      lang="ar"
                      maxLength={2000}
                    />
                  </FieldWrapper>
                </CardContent>
              </Card>

              {/* Classification */}
              <Card>
                <CardHeader>
                  <CardTitle>Classification</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
                    <FieldWrapper label="Type" required>
                      <select
                        className={selectClass}
                        value={form.type}
                        onChange={(e) => set('type', e.target.value)}
                        aria-required="true"
                      >
                        {CLASS_TYPES.map((t) => (
                          <option key={t} value={t}>{t}</option>
                        ))}
                      </select>
                    </FieldWrapper>

                    <FieldWrapper label="Profession Track" required>
                      <select
                        className={selectClass}
                        value={form.professionTrack}
                        onChange={(e) => set('professionTrack', e.target.value)}
                        aria-required="true"
                      >
                        {PROFESSION_TRACKS.map((t) => (
                          <option key={t} value={t}>{t}</option>
                        ))}
                      </select>
                    </FieldWrapper>

                    <FieldWrapper label="Level" required>
                      <select
                        className={selectClass}
                        value={form.level}
                        onChange={(e) => set('level', e.target.value)}
                        aria-required="true"
                      >
                        {LEVELS.map((l) => (
                          <option key={l} value={l}>{l}</option>
                        ))}
                      </select>
                    </FieldWrapper>
                  </div>

                  <FieldWrapper
                    label="Tags"
                    hint="Comma-separated list, e.g. speaking, medicine, advanced"
                    error={fieldErrors.tags}
                  >
                    <input
                      className={inputClass}
                      value={form.tags}
                      onChange={(e) => set('tags', e.target.value)}
                      placeholder="speaking, medicine, advanced"
                      maxLength={500}
                    />
                  </FieldWrapper>
                </CardContent>
              </Card>

            </div>

            {/* ── Right column: scheduling + publishing ── */}
            <div className="space-y-5">

              {/* Scheduling */}
              <Card>
                <CardHeader>
                  <CardTitle>Scheduling</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <FieldWrapper
                    label="Start Date & Time"
                    required
                    error={fieldErrors.scheduledStartAt}
                    hint="Must be in the future."
                  >
                    <input
                      type="datetime-local"
                      className={inputClass}
                      value={form.scheduledStartAt}
                      min={defaultMinStart}
                      onChange={(e) => set('scheduledStartAt', e.target.value)}
                      aria-required="true"
                      aria-invalid={!!fieldErrors.scheduledStartAt}
                    />
                  </FieldWrapper>

                  <FieldWrapper
                    label="Duration (minutes)"
                    required
                    error={fieldErrors.durationMinutes}
                  >
                    <input
                      type="number"
                      min={15}
                      max={480}
                      step={15}
                      className={inputClass}
                      value={form.durationMinutes}
                      onChange={(e) => set('durationMinutes', Number(e.target.value))}
                      aria-required="true"
                      aria-invalid={!!fieldErrors.durationMinutes}
                    />
                  </FieldWrapper>

                  <FieldWrapper
                    label="Capacity"
                    required
                    error={fieldErrors.capacity}
                    hint="Maximum number of enrolled learners."
                  >
                    <input
                      type="number"
                      min={1}
                      max={5000}
                      className={inputClass}
                      value={form.capacity}
                      onChange={(e) => set('capacity', Number(e.target.value))}
                      aria-required="true"
                      aria-invalid={!!fieldErrors.capacity}
                    />
                  </FieldWrapper>
                </CardContent>
              </Card>

              {/* Pricing */}
              <Card>
                <CardHeader>
                  <CardTitle>Pricing & Tutor</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <FieldWrapper
                    label="Credit Cost"
                    required
                    error={fieldErrors.creditCost}
                    hint="Credits deducted from learner at enrollment."
                  >
                    <input
                      type="number"
                      min={0}
                      max={999}
                      className={inputClass}
                      value={form.creditCost}
                      onChange={(e) => set('creditCost', Number(e.target.value))}
                      aria-required="true"
                      aria-invalid={!!fieldErrors.creditCost}
                    />
                  </FieldWrapper>

                  <FieldWrapper
                    label="Tutor Profile ID"
                    hint="Optional. Leave blank if no specific tutor is assigned yet."
                    error={fieldErrors.tutorProfileId}
                  >
                    <input
                      className={inputClass}
                      value={form.tutorProfileId}
                      onChange={(e) => set('tutorProfileId', e.target.value)}
                      placeholder="uuid or leave blank"
                      spellCheck={false}
                    />
                  </FieldWrapper>
                </CardContent>
              </Card>

              {/* Media */}
              <Card>
                <CardHeader>
                  <CardTitle>Media</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <FieldWrapper
                    label="Cover Image URL"
                    hint="Optional. Absolute URL to a JPG or PNG image."
                    error={fieldErrors.coverImageUrl}
                  >
                    <input
                      type="url"
                      className={inputClass}
                      value={form.coverImageUrl}
                      onChange={(e) => set('coverImageUrl', e.target.value)}
                      placeholder="https://cdn.example.com/class-cover.jpg"
                    />
                  </FieldWrapper>
                </CardContent>
              </Card>

              {/* Publishing */}
              <Card>
                <CardHeader>
                  <CardTitle>Publishing</CardTitle>
                </CardHeader>
                <CardContent className="space-y-4">
                  <label className="flex cursor-pointer items-start gap-3">
                    <div className="mt-0.5 flex h-5 w-5 shrink-0 items-center justify-center">
                      <input
                        type="checkbox"
                        checked={form.autoPublish}
                        onChange={(e) => set('autoPublish', e.target.checked)}
                        className="h-4 w-4 rounded border-admin-border accent-[var(--admin-primary)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[var(--admin-primary)] focus-visible:ring-offset-1"
                      />
                    </div>
                    <div>
                      <span className="text-sm font-medium text-admin-fg-strong">
                        Publish immediately
                      </span>
                      <p className="mt-0.5 text-xs text-admin-fg-muted">
                        If unchecked, the class will be saved as a Draft and published manually.
                      </p>
                    </div>
                  </label>
                </CardContent>
              </Card>

              {/* Submit */}
              <div className="flex items-center justify-end gap-3">
                <Button
                  type="button"
                  variant="secondary"
                  onClick={() => router.push('/admin/live-classes')}
                  disabled={saving}
                >
                  Cancel
                </Button>
                <Button
                  type="submit"
                  variant="primary"
                  loading={saving}
                  disabled={saving}
                >
                  <CalendarPlus className="h-4 w-4" aria-hidden="true" />
                  {form.autoPublish ? 'Create & Publish' : 'Create Class'}
                </Button>
              </div>
            </div>
          </div>
        </form>
      </AdminPageShell>
    </AdminRouteWorkspace>
  );
}
