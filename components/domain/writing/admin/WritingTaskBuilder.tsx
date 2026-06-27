'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';

import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import { hasPermission, AdminPermission } from '@/lib/admin-permissions';
import { ApiError } from '@/lib/api';
import {
  getWritingTask,
  createWritingTask,
  updateWritingTask,
  validateWritingTask,
  publishWritingTask,
  archiveWritingTask,
  cloneWritingTask,
  exportWritingTask,
  importWritingTask,
} from '@/lib/writing/exam-api';
import { uploadFileChunked } from '@/lib/content-upload-api';
import {
  WRITING_PROFESSIONS,
  WRITING_PROFESSION_LABELS,
  type WritingTaskDto,
  type WritingProfession,
  type WritingLetterType,
} from '@/lib/writing/types';
import {
  emptyFormState,
  formStateFromDto,
  formStateToUpsert,
  formStateToImportJson,
  WRITING_LETTER_TYPES,
  WRITING_LETTER_TYPE_LABELS,
  type WritingTaskFormState,
} from './builder-state';

interface WritingTaskBuilderProps {
  /** Edit an existing task. Omit (or pass mode="new") to author a new one. */
  taskId?: string;
  mode?: 'edit' | 'new';
}

type ToastVariant = 'success' | 'error' | 'info' | 'warning';
type ToastState = { message: string; variant: ToastVariant } | null;

interface DisplayIssue {
  code: string;
  severity: 'error' | 'warning';
  message: string;
}

const DIFFICULTY_OPTIONS = [
  { value: '1', label: '1 — Foundation' },
  { value: '2', label: '2 — Easy' },
  { value: '3', label: '3 — Standard' },
  { value: '4', label: '4 — Challenging' },
  { value: '5', label: '5 — Hard' },
];

const PROFESSION_OPTIONS = WRITING_PROFESSIONS.map((p) => ({
  value: p,
  label: WRITING_PROFESSION_LABELS[p],
}));

/**
 * Letter types available for a given profession. Veterinary disallows the
 * non-medical referral (LT-NM) per the OET content rules, so we filter it out
 * of the dropdown when the profession is veterinary.
 */
function letterTypeOptionsFor(profession: WritingProfession) {
  const allowed = WRITING_LETTER_TYPES.filter(
    (lt) => !(profession === 'veterinary' && lt === 'LT-NM'),
  );
  return allowed.map((lt) => ({ value: lt, label: WRITING_LETTER_TYPE_LABELS[lt] }));
}

/**
 * A single PDF upload slot — attached / uploading / empty states with Replace +
 * Remove. Used for both the Case Notes PDF (shown left during writing) and the
 * Answer Sheet PDF (shown on the results page). Owns its own hidden file input.
 */
function PdfSlot({
  title,
  description,
  emptyHint,
  mediaAssetId,
  uploading,
  uploadPct,
  uploadError,
  canWrite,
  onUpload,
  onRemove,
}: {
  title: string;
  description: string;
  emptyHint: string;
  mediaAssetId: string | null;
  uploading: boolean;
  uploadPct: number;
  uploadError: string | null;
  canWrite: boolean;
  onUpload: (file: File) => void;
  onRemove: () => void;
}) {
  const inputRef = useRef<HTMLInputElement>(null);
  return (
    <SettingsSection title={title} description={description}>
      <div className="space-y-3">
        {mediaAssetId ? (
          <div className="flex flex-wrap items-center gap-3">
            <span className="inline-flex items-center gap-1.5 rounded-full bg-violet-50 px-3 py-1 text-sm font-medium text-violet-700 ring-1 ring-inset ring-violet-200">
              <svg
                viewBox="0 0 16 16"
                className="h-3.5 w-3.5 shrink-0"
                fill="none"
                stroke="currentColor"
                strokeWidth={1.75}
                strokeLinecap="round"
                strokeLinejoin="round"
                aria-hidden="true"
              >
                <path d="M3 2h7l3 3v9a1 1 0 0 1-1 1H3a1 1 0 0 1-1-1V3a1 1 0 0 1 1-1z" />
                <path d="M10 2v4h4" />
              </svg>
              PDF attached
            </span>
            <span className="font-mono text-xs text-admin-fg-muted">{mediaAssetId}</span>
            <div className="flex gap-2">
              <Button
                variant="secondary"
                size="sm"
                disabled={!canWrite || uploading}
                onClick={() => inputRef.current?.click()}
              >
                Replace
              </Button>
              <Button
                variant="ghost"
                size="sm"
                disabled={!canWrite || uploading}
                onClick={onRemove}
              >
                Remove
              </Button>
            </div>
          </div>
        ) : uploading ? (
          <div className="flex items-center gap-3 text-sm text-admin-fg-muted">
            <svg
              className="h-4 w-4 animate-spin text-violet-600"
              viewBox="0 0 24 24"
              fill="none"
              aria-hidden="true"
            >
              <circle
                className="opacity-25"
                cx="12"
                cy="12"
                r="10"
                stroke="currentColor"
                strokeWidth="4"
              />
              <path
                className="opacity-75"
                fill="currentColor"
                d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z"
              />
            </svg>
            <span>Uploading… {uploadPct}%</span>
          </div>
        ) : (
          <div className="space-y-2">
            <p className="text-sm text-admin-fg-muted">{emptyHint}</p>
            <Button
              variant="secondary"
              size="sm"
              disabled={!canWrite}
              onClick={() => inputRef.current?.click()}
            >
              Upload PDF
            </Button>
          </div>
        )}

        {uploadError && <p className="text-sm text-admin-danger">{uploadError}</p>}

        <input
          ref={inputRef}
          type="file"
          accept="application/pdf,.pdf"
          className="hidden"
          disabled={uploading || !canWrite}
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) onUpload(file);
            e.target.value = '';
          }}
        />
      </div>
    </SettingsSection>
  );
}

/**
 * The OET Writing Task Builder — simplified to a PDF-driven flow (Case Notes +
 * Answer Sheet) plus the minimal catalogue identity the learner library needs.
 *
 * In "edit" mode it loads a task by id; in "new" mode it starts from a blank
 * form and creates-then-redirects on first save. Edits every field of
 * `WritingTaskUpsertDto` and exposes Save / Validate / Publish / Clone /
 * Archive / Export / Import actions.
 */
export function WritingTaskBuilder({ taskId, mode }: WritingTaskBuilderProps) {
  const router = useRouter();
  const isNew = mode === 'new' || !taskId;

  const { isAuthenticated, role, isLoading: authLoading } = useAdminAuth();
  const { user } = useCurrentUser();
  const canWrite = useMemo(
    () => hasPermission(user?.adminPermissions, AdminPermission.ContentWrite),
    [user?.adminPermissions],
  );
  const canPublish = useMemo(
    () =>
      hasPermission(
        user?.adminPermissions,
        AdminPermission.ContentPublish,
      ),
    [user?.adminPermissions],
  );

  // In "new" mode the form starts immediately; in "edit" mode we load first.
  const [form, setForm] = useState<WritingTaskFormState | null>(
    isNew ? emptyFormState() : null,
  );
  // The live id — null until a "new" task is first persisted.
  const [currentId, setCurrentId] = useState<string | null>(taskId ?? null);
  const [status, setStatus] = useState<WritingTaskDto['status']>('draft');
  const [loading, setLoading] = useState(!isNew);
  const [loadError, setLoadError] = useState<string | null>(null);

  const [saving, setSaving] = useState(false);
  const [busyAction, setBusyAction] = useState<
    null | 'validate' | 'publish' | 'archive' | 'clone' | 'export' | 'import'
  >(null);
  const [dirty, setDirty] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);
  const [issues, setIssues] = useState<DisplayIssue[]>([]);

  const fileInputRef = useRef<HTMLInputElement>(null);
  const [pdfUploading, setPdfUploading] = useState(false);
  const [pdfUploadPct, setPdfUploadPct] = useState(0);
  const [pdfUploadError, setPdfUploadError] = useState<string | null>(null);
  const [answerSheetUploading, setAnswerSheetUploading] = useState(false);
  const [answerSheetUploadPct, setAnswerSheetUploadPct] = useState(0);
  const [answerSheetUploadError, setAnswerSheetUploadError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!taskId) return;
    setLoading(true);
    setLoadError(null);
    try {
      const dto = await getWritingTask(taskId);
      setForm(formStateFromDto(dto));
      setStatus(dto.status);
      setCurrentId(dto.id);
      setDirty(false);
    } catch (err) {
      setLoadError(err instanceof Error ? err.message : 'Failed to load task');
    } finally {
      setLoading(false);
    }
  }, [taskId]);

  useEffect(() => {
    if (!isNew) void load();
  }, [isNew, load]);

  const patch = useCallback((p: Partial<WritingTaskFormState>) => {
    setForm((prev) => (prev ? { ...prev, ...p } : prev));
    setDirty(true);
  }, []);

  // Letter-type options depend on the chosen profession (veterinary excludes
  // the non-medical referral). If the current selection becomes invalid after a
  // profession change, fall back to the first allowed type.
  const letterTypeOptions = useMemo(
    () => letterTypeOptionsFor(form?.profession ?? 'medicine'),
    [form?.profession],
  );
  useEffect(() => {
    if (!form) return;
    if (!letterTypeOptions.some((o) => o.value === form.letterType)) {
      patch({ letterType: letterTypeOptions[0]?.value as WritingLetterType });
    }
  }, [form, letterTypeOptions, patch]);

  /**
   * Persist the form. Creates on first save in "new" mode (then redirects into
   * the canonical edit route), updates thereafter. Returns the saved DTO.
   */
  const persist = useCallback(
    async (opts: { redirectOnCreate?: boolean } = {}): Promise<WritingTaskDto | null> => {
      if (!form) return null;
      setSaving(true);
      setToast(null);
      try {
        const payload = formStateToUpsert(form);
        let dto: WritingTaskDto;
        if (currentId) {
          dto = await updateWritingTask(currentId, payload);
        } else {
          dto = await createWritingTask(payload);
          setCurrentId(dto.id);
          if (opts.redirectOnCreate !== false) {
            router.replace(`/admin/writing/tasks/${dto.id}/edit`);
          }
        }
        setStatus(dto.status);
        setDirty(false);
        return dto;
      } catch (err) {
        setToast({
          message: err instanceof Error ? err.message : 'Failed to save',
          variant: 'error',
        });
        return null;
      } finally {
        setSaving(false);
      }
    },
    [form, currentId, router],
  );

  const handleSave = useCallback(async () => {
    const dto = await persist();
    if (dto) setToast({ message: 'Draft saved', variant: 'success' });
  }, [persist]);

  const handleValidate = useCallback(async () => {
    setBusyAction('validate');
    setToast(null);
    try {
      // Save first so validation runs against persisted state. Keep the user on
      // this screen even if a create just happened.
      const saved = await persist({ redirectOnCreate: false });
      if (!saved) {
        setBusyAction(null);
        return;
      }
      const result = await validateWritingTask(saved.id);
      const mapped: DisplayIssue[] = result.issues.map((i) => ({
        code: i.code ?? '',
        severity: i.severity === 'warning' ? 'warning' : 'error',
        message: i.message ?? 'Invalid',
      }));
      setIssues(mapped);
      setToast({
        message: result.isPublishReady
          ? 'Validation passed — ready to publish'
          : `Validation found ${mapped.length} issue${mapped.length === 1 ? '' : 's'}`,
        variant: result.isPublishReady ? 'success' : 'warning',
      });
    } catch (err) {
      setToast({
        message: err instanceof Error ? err.message : 'Validation failed',
        variant: 'error',
      });
    } finally {
      setBusyAction(null);
    }
  }, [persist]);

  const handlePublish = useCallback(async () => {
    setBusyAction('publish');
    setToast(null);
    try {
      const saved = await persist({ redirectOnCreate: false });
      if (!saved) {
        setBusyAction(null);
        return;
      }
      const dto = await publishWritingTask(saved.id);
      setStatus(dto.status);
      setIssues([]);
      setToast({ message: 'Task published', variant: 'success' });
    } catch (err) {
      // Surface 400 publish-gate issues. The shared client raises ApiError with
      // a `fieldErrors` array ({ field, code, message }). When the gate returns
      // a bare validation_error, re-run validate() to recover the issue list.
      if (err instanceof ApiError && err.status === 400) {
        let mapped: DisplayIssue[] = err.fieldErrors.map((fe) => ({
          code: fe.field || fe.code || '',
          severity: 'error',
          message: fe.message || 'Invalid',
        }));
        if (mapped.length === 0 && currentId) {
          try {
            const result = await validateWritingTask(currentId);
            mapped = result.issues.map((i) => ({
              code: i.code ?? '',
              severity: i.severity === 'warning' ? 'warning' : 'error',
              message: i.message ?? 'Invalid',
            }));
          } catch {
            // ignore — fall back to the error message below
          }
        }
        setIssues(
          mapped.length > 0
            ? mapped
            : [{ code: err.code, severity: 'error', message: err.message }],
        );
        const errorCount = mapped.filter((m) => m.severity === 'error').length || 1;
        setToast({
          message: `Cannot publish — ${errorCount} issue${errorCount === 1 ? '' : 's'} to fix`,
          variant: 'error',
        });
      } else {
        setToast({
          message: err instanceof Error ? err.message : 'Publish failed',
          variant: 'error',
        });
      }
    } finally {
      setBusyAction(null);
    }
  }, [persist, currentId]);

  const handleArchive = useCallback(async () => {
    if (!currentId) return;
    setBusyAction('archive');
    setToast(null);
    try {
      const dto = await archiveWritingTask(currentId);
      setStatus(dto.status);
      setToast({ message: 'Task archived', variant: 'success' });
    } catch (err) {
      setToast({
        message: err instanceof Error ? err.message : 'Archive failed',
        variant: 'error',
      });
    } finally {
      setBusyAction(null);
    }
  }, [currentId]);

  const handleClone = useCallback(async () => {
    if (!currentId) return;
    setBusyAction('clone');
    setToast(null);
    try {
      const dto = await cloneWritingTask(currentId);
      setToast({ message: 'Cloned — opening copy', variant: 'success' });
      router.push(`/admin/writing/tasks/${dto.id}/edit`);
    } catch (err) {
      setToast({
        message: err instanceof Error ? err.message : 'Clone failed',
        variant: 'error',
      });
      setBusyAction(null);
    }
  }, [currentId, router]);

  const handleExport = useCallback(async () => {
    if (!form) return;
    setBusyAction('export');
    setToast(null);
    try {
      // Prefer the server's canonical export when the task is persisted;
      // otherwise export the in-memory form (new, unsaved task).
      const json = currentId
        ? await exportWritingTask(currentId)
        : formStateToImportJson(form);
      const blob = new Blob([JSON.stringify(json, null, 2)], {
        type: 'application/json',
      });
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      const slug =
        (form.internalCode || form.title || 'writing-task')
          .toLowerCase()
          .replace(/[^a-z0-9]+/g, '-')
          .replace(/^-+|-+$/g, '') || 'writing-task';
      a.download = `${slug}.json`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
      setToast({ message: 'Exported JSON', variant: 'success' });
    } catch (err) {
      setToast({
        message: err instanceof Error ? err.message : 'Export failed',
        variant: 'error',
      });
    } finally {
      setBusyAction(null);
    }
  }, [currentId, form]);

  const handleImportFile = useCallback(
    async (file: File) => {
      setBusyAction('import');
      setToast(null);
      try {
        const text = await file.text();
        const parsed = JSON.parse(text);
        const dto = await importWritingTask(parsed);
        setToast({ message: 'Imported — opening task', variant: 'success' });
        router.push(`/admin/writing/tasks/${dto.id}/edit`);
      } catch (err) {
        setToast({
          message:
            err instanceof Error
              ? `Import failed: ${err.message}`
              : 'Import failed — invalid JSON',
          variant: 'error',
        });
        setBusyAction(null);
      }
    },
    [router],
  );

  const handlePdfUpload = useCallback(
    async (file: File) => {
      setPdfUploading(true);
      setPdfUploadPct(0);
      setPdfUploadError(null);
      try {
        const result = await uploadFileChunked(file, 'CaseNotes', (pct) =>
          setPdfUploadPct(Math.round(pct * 100)),
        );
        patch({ stimulusPdfMediaAssetId: result.mediaAssetId });
      } catch (err) {
        setPdfUploadError(
          err instanceof Error ? err.message : 'Upload failed — please try again.',
        );
      } finally {
        setPdfUploading(false);
      }
    },
    [patch],
  );

  const handleAnswerSheetUpload = useCallback(
    async (file: File) => {
      setAnswerSheetUploading(true);
      setAnswerSheetUploadPct(0);
      setAnswerSheetUploadError(null);
      try {
        const result = await uploadFileChunked(file, 'AnswerKey', (pct) =>
          setAnswerSheetUploadPct(Math.round(pct * 100)),
        );
        patch({ answerSheetPdfMediaAssetId: result.mediaAssetId });
      } catch (err) {
        setAnswerSheetUploadError(
          err instanceof Error ? err.message : 'Upload failed — please try again.',
        );
      } finally {
        setAnswerSheetUploading(false);
      }
    },
    [patch],
  );

  // ---- Render guards ------------------------------------------------------

  if (!authLoading && (!isAuthenticated || role !== 'admin')) {
    return (
      <AdminSettingsLayout title="Writing task" description="Edit an OET Writing task.">
        <SettingsSection title="Admin access required">
          <p className="text-sm text-admin-fg-muted">
            You must be signed in as an administrator to use the writing task
            builder.
          </p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  if (!canWrite && !authLoading) {
    return (
      <AdminSettingsLayout title="Writing task" description="Edit an OET Writing task.">
        <SettingsSection title="Permission required">
          <p className="text-sm text-admin-fg-muted">
            Authoring writing tasks requires the ContentWrite permission.
          </p>
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  if (loading || !form) {
    return (
      <AdminSettingsLayout title="Writing task" description="Loading…">
        <SettingsSection title="Loading">
          {loadError ? (
            <div className="space-y-3">
              <p className="text-sm text-admin-danger">{loadError}</p>
              <div className="flex gap-2">
                <Button variant="secondary" size="sm" onClick={() => void load()}>
                  Retry
                </Button>
                <Button
                  variant="secondary"
                  size="sm"
                  onClick={() => router.push('/admin/writing/tasks')}
                >
                  Back to list
                </Button>
              </div>
            </div>
          ) : (
            <p className="text-sm text-admin-fg-muted">Loading task…</p>
          )}
        </SettingsSection>
      </AdminSettingsLayout>
    );
  }

  const actionsBusy = saving || busyAction !== null;

  const headerActions = (
    <>
      <StatusBadge status={status} />
      {dirty && <span className="text-xs text-amber-600">Unsaved changes</span>}
      <Button
        variant="secondary"
        size="sm"
        onClick={handleValidate}
        loading={busyAction === 'validate'}
        disabled={actionsBusy}
      >
        Validate
      </Button>
      <Button size="sm" onClick={handleSave} loading={saving} disabled={actionsBusy}>
        Save draft
      </Button>
      {canPublish && status !== 'published' && (
        <Button
          size="sm"
          onClick={handlePublish}
          loading={busyAction === 'publish'}
          disabled={actionsBusy}
        >
          Publish
        </Button>
      )}
    </>
  );

  return (
    <AdminSettingsLayout
      title={form.title.trim() || (isNew ? 'New writing task' : 'Untitled writing task')}
      description="Author every part of the OET Writing task."
      eyebrow="Writing"
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Writing', href: '/admin/writing' },
        { label: 'Tasks', href: '/admin/writing/tasks' },
        { label: isNew ? 'New' : 'Edit' },
      ]}
      actions={headerActions}
    >
      {/* Secondary actions row */}
      <div className="flex flex-wrap items-center gap-2">
        <Button
          variant="secondary"
          size="sm"
          onClick={() => router.push('/admin/writing/tasks')}
        >
          ← All tasks
        </Button>
        <span className="mx-1 h-4 w-px bg-admin-border" aria-hidden />
        <Button
          variant="secondary"
          size="sm"
          onClick={handleClone}
          loading={busyAction === 'clone'}
          disabled={actionsBusy || !currentId}
        >
          Clone
        </Button>
        <Button
          variant="secondary"
          size="sm"
          onClick={handleExport}
          loading={busyAction === 'export'}
          disabled={actionsBusy}
        >
          Export JSON
        </Button>
        <Button
          variant="secondary"
          size="sm"
          onClick={() => fileInputRef.current?.click()}
          loading={busyAction === 'import'}
          disabled={actionsBusy}
        >
          Import JSON
        </Button>
        {status !== 'archived' && (
          <Button
            variant="ghost"
            size="sm"
            onClick={handleArchive}
            disabled={actionsBusy || !currentId}
          >
            Archive
          </Button>
        )}
        <input
          ref={fileInputRef}
          type="file"
          accept="application/json,.json"
          className="hidden"
          onChange={(e) => {
            const file = e.target.files?.[0];
            if (file) void handleImportFile(file);
            e.target.value = '';
          }}
        />
      </div>

      {issues.length > 0 && <IssuesPanel issues={issues} onClear={() => setIssues([])} />}

      {/* Metadata */}
      <SettingsSection
        title="Task metadata"
        description="How this task is catalogued and delivered."
      >
        <div className="space-y-4">
          <Input
            label="Title"
            value={form.title}
            onChange={(e) => patch({ title: e.target.value })}
            placeholder="e.g. Discharge — elderly patient post-hip-replacement"
          />

          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              label="Profession"
              value={form.profession}
              onChange={(e) =>
                patch({ profession: e.target.value as WritingProfession })
              }
              options={PROFESSION_OPTIONS}
            />
            <Select
              label="Letter type"
              value={form.letterType}
              onChange={(e) =>
                patch({ letterType: e.target.value as WritingLetterType })
              }
              options={letterTypeOptions}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              label="Difficulty"
              value={String(form.difficulty)}
              onChange={(e) => patch({ difficulty: Number(e.target.value) })}
              options={DIFFICULTY_OPTIONS}
            />
            <div />
          </div>
        </div>
      </SettingsSection>

      {/* Case Notes PDF — shown on the LEFT while the candidate writes the letter. */}
      <PdfSlot
        title="Case Notes PDF"
        description="Shown on the left while the candidate writes the letter (the candidate cannot copy from it)."
        emptyHint="No Case Notes PDF attached yet."
        mediaAssetId={form.stimulusPdfMediaAssetId}
        uploading={pdfUploading}
        uploadPct={pdfUploadPct}
        uploadError={pdfUploadError}
        canWrite={canWrite}
        onUpload={(file) => void handlePdfUpload(file)}
        onRemove={() => {
          patch({ stimulusPdfMediaAssetId: null });
          setPdfUploadError(null);
        }}
      />

      {/* Answer Sheet PDF — shown on the results page after submission. */}
      <PdfSlot
        title="Answer Sheet PDF"
        description="Shown on the results page after the letter is submitted, to tally answers alongside the scoring."
        emptyHint="No Answer Sheet PDF attached yet."
        mediaAssetId={form.answerSheetPdfMediaAssetId}
        uploading={answerSheetUploading}
        uploadPct={answerSheetUploadPct}
        uploadError={answerSheetUploadError}
        canWrite={canWrite}
        onUpload={(file) => void handleAnswerSheetUpload(file)}
        onRemove={() => {
          patch({ answerSheetPdfMediaAssetId: null });
          setAnswerSheetUploadError(null);
        }}
      />

      {toast && (
        <Toast
          message={toast.message}
          variant={toast.variant}
          onClose={() => setToast(null)}
        />
      )}
    </AdminSettingsLayout>
  );
}

function StatusBadge({ status }: { status: WritingTaskDto['status'] }) {
  const variant =
    status === 'published' ? 'success' : status === 'archived' ? 'warning' : 'default';
  return (
    <Badge variant={variant} size="sm" className="capitalize">
      {status}
    </Badge>
  );
}

function IssuesPanel({
  issues,
  onClear,
}: {
  issues: DisplayIssue[];
  onClear: () => void;
}) {
  const errors = issues.filter((i) => i.severity === 'error');
  const warnings = issues.filter((i) => i.severity === 'warning');
  return (
    <section className="overflow-hidden rounded-admin-lg border border-admin-border bg-admin-bg-surface shadow-admin-sm">
      <div className="flex items-center justify-between border-b border-admin-border px-5 py-3">
        <h2 className="text-sm font-semibold text-admin-fg-strong">
          {errors.length} error{errors.length === 1 ? '' : 's'}
          {warnings.length > 0 &&
            `, ${warnings.length} warning${warnings.length === 1 ? '' : 's'}`}
        </h2>
        <button
          type="button"
          onClick={onClear}
          className="text-xs font-medium text-admin-fg-muted outline-none transition-colors duration-150 hover:text-admin-fg-default focus-visible:underline motion-reduce:transition-none"
        >
          Dismiss
        </button>
      </div>
      <ul className="divide-y divide-admin-border">
        {issues.map((issue, i) => (
          <li key={i} className="flex items-start gap-3 px-5 py-2.5 text-sm">
            <span
              className={`mt-0.5 inline-flex shrink-0 items-center rounded px-1.5 py-0.5 text-xs font-medium ${
                issue.severity === 'error'
                  ? 'bg-[var(--admin-danger-tint)] text-[var(--admin-danger)]'
                  : 'bg-[var(--admin-warning-tint)] text-[var(--admin-warning)]'
              }`}
            >
              {issue.severity}
            </span>
            <div>
              <p className="text-admin-fg-default">{issue.message}</p>
              {issue.code && (
                <p className="font-mono text-xs text-admin-fg-muted">{issue.code}</p>
              )}
            </div>
          </li>
        ))}
      </ul>
    </section>
  );
}
