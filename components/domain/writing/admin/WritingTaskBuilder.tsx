'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useRouter } from 'next/navigation';

import {
  AdminSettingsLayout,
  SettingsSection,
} from '@/components/admin/layout/admin-settings-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Textarea, Select, Checkbox } from '@/components/ui/form-controls';
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
import {
  WRITING_PROFESSIONS,
  WRITING_PROFESSION_LABELS,
  type WritingTaskDto,
  type WritingProfession,
  type WritingLetterType,
  type WritingSimulationMode,
  type WritingMarkingMode,
} from '@/lib/writing/types';
import {
  emptyFormState,
  formStateFromDto,
  formStateToUpsert,
  formStateToImportJson,
  WRITING_LETTER_TYPES,
  WRITING_LETTER_TYPE_LABELS,
  WRITING_SIMULATION_MODES,
  WRITING_SIMULATION_MODE_LABELS,
  WRITING_MARKING_MODES,
  WRITING_MARKING_MODE_LABELS,
  type WritingTaskFormState,
} from './builder-state';
import { RecipientEditor } from './RecipientEditor';
import { CaseNotesSectionBuilder } from './CaseNotesSectionBuilder';
import { ContentChecklistBuilder } from './ContentChecklistBuilder';
import { ModelAnswerEditor } from './ModelAnswerEditor';
import { StringLineListEditor } from './StringLineListEditor';
import { TaskPreview } from './TaskPreview';

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
const LETTER_TYPE_OPTIONS = WRITING_LETTER_TYPES.map((lt) => ({
  value: lt,
  label: WRITING_LETTER_TYPE_LABELS[lt],
}));
const SIMULATION_OPTIONS = WRITING_SIMULATION_MODES.map((m) => ({
  value: m,
  label: WRITING_SIMULATION_MODE_LABELS[m],
}));
const MARKING_OPTIONS = WRITING_MARKING_MODES.map((m) => ({
  value: m,
  label: WRITING_MARKING_MODE_LABELS[m],
}));

/**
 * The rich OET Writing Task Builder (spec §3/§4/§5/§6/§18/§19.2).
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
          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Title"
              value={form.title}
              onChange={(e) => patch({ title: e.target.value })}
              placeholder="e.g. Discharge — elderly patient post-hip-replacement"
            />
            <Input
              label="Internal code"
              hint="Optional — your authoring reference."
              value={form.internalCode}
              onChange={(e) => patch({ internalCode: e.target.value })}
              placeholder="e.g. MED-DIS-014"
            />
          </div>

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
              options={LETTER_TYPE_OPTIONS}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              label="Difficulty"
              value={String(form.difficulty)}
              onChange={(e) => patch({ difficulty: Number(e.target.value) })}
              options={DIFFICULTY_OPTIONS}
            />
            <Select
              label="Simulation modes"
              value={form.simulationModes}
              onChange={(e) =>
                patch({ simulationModes: e.target.value as WritingSimulationMode })
              }
              options={SIMULATION_OPTIONS}
            />
          </div>

          <div className="grid gap-4 sm:grid-cols-2">
            <Select
              label="Marking mode"
              value={form.markingMode}
              onChange={(e) =>
                patch({ markingMode: e.target.value as WritingMarkingMode })
              }
              options={MARKING_OPTIONS}
            />
            <div />
          </div>

          <Textarea
            label="Source provenance"
            hint="Where this scenario originates (textbook, past paper, original). Required before publish."
            value={form.sourceProvenance}
            onChange={(e) => patch({ sourceProvenance: e.target.value })}
            rows={2}
          />

          <Checkbox
            checked={form.integrityAcknowledged}
            onChange={(e) => patch({ integrityAcknowledged: e.target.checked })}
            label="I confirm this content is original or properly licensed, is not derived from any recalled or leaked OET exam paper, and contains no real patient data."
          />
        </div>
      </SettingsSection>

      {/* Task prompt */}
      <SettingsSection
        title="Task prompt"
        description="The instructions the candidate reads, their role, and the in-scenario date."
      >
        <div className="space-y-4">
          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Writer role"
              hint="Who the candidate is, in role."
              value={form.writerRole}
              onChange={(e) => patch({ writerRole: e.target.value })}
              placeholder="e.g. the charge nurse on the ward"
            />
            <Input
              label="Today's date"
              hint="Optional — the in-scenario date."
              value={form.todayDate}
              onChange={(e) => patch({ todayDate: e.target.value })}
              placeholder="e.g. 14 March 2026"
            />
          </div>
          <Textarea
            label="Task prompt"
            hint="Markdown supported. Describe the writing task."
            value={form.taskPromptMarkdown}
            onChange={(e) => patch({ taskPromptMarkdown: e.target.value })}
            rows={5}
            placeholder="Using the information given in the case notes, write a letter…"
          />
          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Expected purpose"
              hint="Optional — the letter's core purpose (for marking)."
              value={form.expectedPurpose}
              onChange={(e) => patch({ expectedPurpose: e.target.value })}
              placeholder="e.g. Refer for specialist cardiac assessment"
            />
            <Input
              label="Expected action"
              hint="Optional — the action requested of the recipient."
              value={form.expectedAction}
              onChange={(e) => patch({ expectedAction: e.target.value })}
              placeholder="e.g. Review and arrange follow-up within 2 weeks"
            />
          </div>
        </div>
      </SettingsSection>

      {/* Recipient */}
      <SettingsSection
        title="Recipient"
        description="Who the candidate addresses the letter to."
      >
        <RecipientEditor
          value={form.recipient}
          onChange={(recipient) => patch({ recipient })}
        />
      </SettingsSection>

      {/* Case notes — centrepiece */}
      <SettingsSection
        title="Case notes"
        description="The structured source material. Add sections and notes; reorder freely."
      >
        <CaseNotesSectionBuilder
          sections={form.caseNoteSections}
          onChange={(caseNoteSections) => patch({ caseNoteSections })}
        />
      </SettingsSection>

      {/* Word guide + instructions */}
      <SettingsSection
        title="Word guide & instructions"
        description="The word range and the fixed instruction lines shown to candidates."
      >
        <div className="space-y-5">
          <div className="grid gap-4 sm:grid-cols-2">
            <Input
              label="Minimum words"
              type="number"
              min={0}
              value={String(form.wordGuideMin)}
              onChange={(e) => patch({ wordGuideMin: Number(e.target.value) })}
            />
            <Input
              label="Maximum words"
              type="number"
              min={0}
              value={String(form.wordGuideMax)}
              onChange={(e) => patch({ wordGuideMax: Number(e.target.value) })}
            />
          </div>
          {form.wordGuideMin > form.wordGuideMax && (
            <p className="text-xs text-amber-600">
              Minimum is greater than maximum — check the word guide.
            </p>
          )}
          <div>
            <p className="mb-1.5 text-sm font-semibold tracking-tight text-navy">
              Fixed instruction lines
            </p>
            <p className="mb-2 text-xs text-muted">
              Shown verbatim under the task. Seeded with the standard four lines.
            </p>
            <StringLineListEditor
              lines={form.fixedInstructions}
              onChange={(fixedInstructions) => patch({ fixedInstructions })}
              addLabel="Add instruction line"
              itemNoun="instruction"
              placeholder="e.g. Use letter format"
              emptyHint="No instruction lines."
            />
          </div>
        </div>
      </SettingsSection>

      {/* Model answer */}
      <SettingsSection
        title="Model answer"
        description="The reference letter and optional paragraph breakdown for graders."
      >
        <ModelAnswerEditor
          modelAnswerText={form.modelAnswerText}
          onModelAnswerTextChange={(modelAnswerText) => patch({ modelAnswerText })}
          paragraphs={form.modelAnswerParagraphs}
          onParagraphsChange={(modelAnswerParagraphs) =>
            patch({ modelAnswerParagraphs })
          }
          wordGuideMin={form.wordGuideMin}
          wordGuideMax={form.wordGuideMax}
        />
      </SettingsSection>

      {/* Key content checklist */}
      <SettingsSection
        title="Key content checklist"
        description="The facts a strong letter must include, with importance and links."
      >
        <ContentChecklistBuilder
          variant="key"
          items={form.keyContentChecklist}
          onChange={(keyContentChecklist) => patch({ keyContentChecklist })}
          sections={form.caseNoteSections}
        />
      </SettingsSection>

      {/* Irrelevant content checklist */}
      <SettingsSection
        title="Irrelevant content (distractors)"
        description="Notes a candidate should deliberately leave out of the letter."
      >
        <ContentChecklistBuilder
          variant="irrelevant"
          items={form.irrelevantContentChecklist}
          onChange={(irrelevantContentChecklist) =>
            patch({ irrelevantContentChecklist })
          }
        />
      </SettingsSection>

      {/* Preview */}
      <SettingsSection
        title="Candidate preview"
        description="How the source material reads in paper and computer modes."
      >
        <TaskPreview form={form} />
      </SettingsSection>

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
