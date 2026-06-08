'use client';

import { useCallback, useEffect, useId, useMemo, useState, type KeyboardEvent } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Plus, Save, X } from 'lucide-react';
import { AdminSettingsLayout } from '@/components/admin/layout/admin-settings-layout';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { PartANotesBuilder } from '@/components/domain/listening/admin/PartANotesBuilder';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';
import { countGaps } from '@/lib/listening-part-a-notes';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningExtracts,
  getListeningStructure,
  patchListeningExtract,
  patchListeningQuestion,
  type ListeningAuthoredExtract,
  type ListeningAuthoredQuestion,
} from '@/lib/listening-authoring-api';

type LoadState = 'loading' | 'ready' | 'error';
type SaveState = 'idle' | 'saving' | 'saved' | 'error';

function firstParam(value: string | string[] | undefined): string | undefined {
  return Array.isArray(value) ? value[0] : value;
}

// ── Per-question answer-key row state ─────────────────────────────────────────

interface AnswerRowState {
  questionId: string;
  questionNumber: number;
  correctAnswer: string;
  acceptedAnswers: string[];
  /** snapshot from the last successful load/save */
  initialCorrectAnswer: string;
  initialAcceptedAnswers: string[];
}

function initRows(questions: ListeningAuthoredQuestion[]): AnswerRowState[] {
  return questions.map((q) => ({
    questionId: q.id,
    questionNumber: q.number,
    correctAnswer: q.correctAnswer ?? '',
    acceptedAnswers: q.acceptedAnswers ?? [],
    initialCorrectAnswer: q.correctAnswer ?? '',
    initialAcceptedAnswers: q.acceptedAnswers ?? [],
  }));
}

function hasRowChanged(row: AnswerRowState): boolean {
  return (
    row.correctAnswer !== row.initialCorrectAnswer ||
    JSON.stringify(row.acceptedAnswers) !== JSON.stringify(row.initialAcceptedAnswers)
  );
}

// ── Per-sub-part section ───────────────────────────────────────────────────────

interface SubPartSectionProps {
  paperId: string;
  code: 'A1' | 'A2';
  extract: ListeningAuthoredExtract | undefined;
  questions: ListeningAuthoredQuestion[];
  disabled: boolean;
  onSaveSuccess: (code: 'A1' | 'A2') => void;
}

function SubPartSection({
  paperId,
  code,
  extract,
  questions,
  disabled,
  onSaveSuccess,
}: SubPartSectionProps) {
  const [notesBody, setNotesBody] = useState(extract?.notesBody ?? '');
  const [initialNotesBody, setInitialNotesBody] = useState(extract?.notesBody ?? '');
  const [rows, setRows] = useState<AnswerRowState[]>(() => initRows(questions));
  const [variantDrafts, setVariantDrafts] = useState<Record<string, string>>({});
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  // Sync when parent reloads data
  useEffect(() => {
    setNotesBody(extract?.notesBody ?? '');
    setInitialNotesBody(extract?.notesBody ?? '');
    setRows(initRows(questions));
  }, [extract, questions]);

  // Reset save status to 'idle' whenever the user makes a change so the status
  // text correctly shows unsaved changes instead of staying 'saved' forever.
  // Applied directly in each change handler to avoid a setState-in-effect lint
  // warning that matches the pre-existing pattern in this file.
  const markDirty = useCallback(() => {
    setSaveState((prev) => (prev === 'saved' ? 'idle' : prev));
  }, []);

  const gapCount = countGaps(notesBody);
  const questionCount = questions.length;
  const hasGapMismatch = notesBody.length > 0 && gapCount !== questionCount;

  const notesBodyChanged = notesBody !== initialNotesBody;
  const changedRows = rows.filter(hasRowChanged);
  const hasDirtyChanges = notesBodyChanged || changedRows.length > 0;

  const updateRow = useCallback(
    (questionId: string, field: 'correctAnswer', value: string) => {
      markDirty();
      setRows((prev) =>
        prev.map((r) => (r.questionId === questionId ? { ...r, [field]: value } : r)),
      );
    },
    [markDirty],
  );

  const addVariant = useCallback(
    (questionId: string) => {
      const draft = (variantDrafts[questionId] ?? '').trim();
      if (!draft) return;
      markDirty();
      setRows((prev) =>
        prev.map((r) => {
          if (r.questionId !== questionId) return r;
          if (r.acceptedAnswers.includes(draft)) return r;
          return { ...r, acceptedAnswers: [...r.acceptedAnswers, draft] };
        }),
      );
      setVariantDrafts((prev) => ({ ...prev, [questionId]: '' }));
    },
    [variantDrafts, markDirty],
  );

  const removeVariant = useCallback((questionId: string, variant: string) => {
    markDirty();
    setRows((prev) =>
      prev.map((r) =>
        r.questionId === questionId
          ? { ...r, acceptedAnswers: r.acceptedAnswers.filter((v) => v !== variant) }
          : r,
      ),
    );
  }, [markDirty]);

  const onVariantKey = useCallback(
    (questionId: string, event: KeyboardEvent<HTMLInputElement>) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        addVariant(questionId);
      }
    },
    [addVariant],
  );

  const onSave = useCallback(async () => {
    setSaveState('saving');
    try {
      // PATCH notesBody if changed
      if (notesBodyChanged) {
        await patchListeningExtract(paperId, code, { notesBody });
      }

      // PATCH each changed question row
      for (const row of changedRows) {
        await patchListeningQuestion(paperId, row.questionId, {
          correctAnswer: row.correctAnswer,
          acceptedAnswers: row.acceptedAnswers,
        });
      }

      // Commit initial snapshots
      setInitialNotesBody(notesBody);
      setRows((prev) =>
        prev.map((r) => ({
          ...r,
          initialCorrectAnswer: r.correctAnswer,
          initialAcceptedAnswers: r.acceptedAnswers,
        })),
      );

      setSaveState('saved');
      setToast({ variant: 'success', message: `Part ${code} saved.` });
      onSaveSuccess(code);
    } catch (e) {
      setSaveState('error');
      const msg = e instanceof Error ? e.message : `Could not save Part ${code}.`;
      setToast({ variant: 'error', message: msg });
    }
  }, [paperId, code, notesBody, notesBodyChanged, changedRows, onSaveSuccess]);

  const previewQuestions = questions.map((q) => ({ id: q.id, number: q.number }));
  const partLabel = `Part ${code}`;
  const sectionId = `part-${code.toLowerCase()}`;

  return (
    <Card id={sectionId}>
      <CardHeader>
        <CardTitle>{partLabel} — note-completion</CardTitle>
      </CardHeader>
      <CardContent>
        <div className="space-y-6">
          {/* Notes editor */}
          <PartANotesBuilder
            value={notesBody}
            onChange={(v) => { markDirty(); setNotesBody(v); }}
            partLabel={partLabel}
            disabled={disabled || saveState === 'saving'}
            renderPreview={(body) => (
              <PartANotesDocument
                partLabel={partLabel}
                notesBody={body}
                questions={previewQuestions}
                answers={{}}
                onAnswerChange={() => {}}
                locked
              />
            )}
          />

          {/* Gap-count mismatch banner */}
          {hasGapMismatch && (
            <InlineAlert variant="warning">
              {gapCount} gap{gapCount !== 1 ? 's' : ''} detected but {questionCount} answer row{questionCount !== 1 ? 's' : ''} — gaps and questions must match before publishing.
            </InlineAlert>
          )}

          {notesBody.length > 0 && !hasGapMismatch && gapCount > 0 && (
            <p className="text-xs text-admin-fg-muted">
              {gapCount} gap{gapCount !== 1 ? 's' : ''} detected — matches {questionCount} question{questionCount !== 1 ? 's' : ''}.
            </p>
          )}

          {/* Answer-key list */}
          <div>
            <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted mb-3">
              Answer key
            </p>
            <div className="space-y-4">
              {rows.map((row, gapIndex) => (
                <AnswerKeyRow
                  key={row.questionId}
                  row={row}
                  gapIndex={gapIndex}
                  variantDraft={variantDrafts[row.questionId] ?? ''}
                  onVariantDraftChange={(v) =>
                    setVariantDrafts((prev) => ({ ...prev, [row.questionId]: v }))
                  }
                  onCorrectAnswerChange={(v) => updateRow(row.questionId, 'correctAnswer', v)}
                  onAddVariant={() => addVariant(row.questionId)}
                  onRemoveVariant={(v) => removeVariant(row.questionId, v)}
                  onVariantKey={(e) => onVariantKey(row.questionId, e)}
                  disabled={disabled || saveState === 'saving'}
                />
              ))}
            </div>
          </div>

          {/* Save bar */}
          <div className="flex items-center justify-between gap-3 rounded-admin border border-admin-border bg-admin-bg-surface p-4">
            <div className="text-sm text-admin-fg-muted">
              {hasDirtyChanges
                ? `${(notesBodyChanged ? 1 : 0) + changedRows.length} unsaved change(s).`
                : 'No unsaved changes.'}
              {saveState === 'saving' && ' Saving…'}
            </div>
            <Button
              variant="primary"
              onClick={onSave}
              disabled={!hasDirtyChanges || disabled}
              loading={saveState === 'saving'}
              loadingText="Saving…"
            >
              <Save className="h-4 w-4 mr-1.5" />
              Save Part {code}
            </Button>
          </div>
        </div>
      </CardContent>

      {toast && (
        <Toast
          variant={toast.variant}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </Card>
  );
}

// ── Answer-key row ────────────────────────────────────────────────────────────

interface AnswerKeyRowProps {
  row: AnswerRowState;
  gapIndex: number;
  variantDraft: string;
  onVariantDraftChange: (v: string) => void;
  onCorrectAnswerChange: (v: string) => void;
  onAddVariant: () => void;
  onRemoveVariant: (v: string) => void;
  onVariantKey: (e: KeyboardEvent<HTMLInputElement>) => void;
  disabled: boolean;
}

function AnswerKeyRow({
  row,
  gapIndex,
  variantDraft,
  onVariantDraftChange,
  onCorrectAnswerChange,
  onAddVariant,
  onRemoveVariant,
  onVariantKey,
  disabled,
}: AnswerKeyRowProps) {
  const id = useId();

  return (
    <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-4 space-y-3">
      <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">
        Gap ({gapIndex + 1}) → Q{row.questionNumber}
      </p>

      <Input
        id={`${id}-canonical`}
        label="Canonical answer"
        placeholder="e.g. cholesterol"
        value={row.correctAnswer}
        onChange={(e) => onCorrectAnswerChange(e.target.value)}
        disabled={disabled}
      />

      {/* Accepted variants chip manager (mirrors questions/[qid]/page.tsx) */}
      <div>
        <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted mb-1.5">
          Accepted variants
        </p>
        <div className="flex flex-wrap gap-2">
          {row.acceptedAnswers.map((v) => (
            <span
              key={v}
              className="inline-flex items-center gap-1 rounded-full bg-[var(--admin-primary)]/10 px-3 py-1 text-sm text-[var(--admin-primary)]"
            >
              {v}
              <button
                type="button"
                aria-label={`Remove variant ${v}`}
                onClick={() => onRemoveVariant(v)}
                disabled={disabled}
                className="rounded-full p-0.5 hover:bg-[var(--admin-primary)]/20"
              >
                <X className="h-3 w-3" aria-hidden="true" />
              </button>
            </span>
          ))}
        </div>
        <div className="mt-2 flex items-center gap-2">
          <Input
            placeholder="Add variant…"
            aria-label={`Add accepted variant for Q${row.questionNumber}`}
            value={variantDraft}
            onChange={(e) => onVariantDraftChange(e.target.value)}
            onKeyDown={onVariantKey}
            disabled={disabled}
          />
          <Button
            type="button"
            variant="outline"
            onClick={onAddVariant}
            disabled={disabled || !variantDraft.trim()}
          >
            <Plus className="h-4 w-4 mr-1.5" />
            Add
          </Button>
        </div>
      </div>
    </div>
  );
}

// ── Page ──────────────────────────────────────────────────────────────────────

export default function AdminListeningPartAPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId);
  const { isAuthenticated, role } = useAdminAuth();

  const [loadState, setLoadState] = useState<LoadState>('loading');
  const [extracts, setExtracts] = useState<ListeningAuthoredExtract[]>([]);
  const [questions, setQuestions] = useState<ListeningAuthoredQuestion[]>([]);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoadState('loading');
    try {
      const [extractsResult, structureResult] = await Promise.all([
        getListeningExtracts(paperId),
        getListeningStructure(paperId),
      ]);
      setExtracts(extractsResult.extracts);
      setQuestions(structureResult.questions);
      setLoadState('ready');
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load Part A data.');
      setLoadState('error');
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load();
  }, [isAuthenticated, role, load]);

  // Resolve per-sub-part data — memoised so identity is stable across
  // parent re-renders that don't change the underlying fetched data.  This
  // prevents the SubPartSection useEffect (which depends on [extract,
  // questions]) from firing on every render and wiping unsaved edits.
  // NOTE: hooks must come before any early return.
  const a1Extract = useMemo(
    () => extracts.find((e) => String(e.partCode).toUpperCase() === 'A1'),
    [extracts],
  );
  const a2Extract = useMemo(
    () => extracts.find((e) => String(e.partCode).toUpperCase() === 'A2'),
    [extracts],
  );
  const a1Questions = useMemo(
    () =>
      questions
        .filter((q) => String(q.partCode).toUpperCase() === 'A1')
        .sort((a, b) => a.number - b.number),
    [questions],
  );
  const a2Questions = useMemo(
    () =>
      questions
        .filter((q) => String(q.partCode).toUpperCase() === 'A2')
        .sort((a, b) => a.number - b.number),
    [questions],
  );

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: `Paper ${paperId ?? ''}`, href: `/admin/content/listening/${paperId}/structure` },
    { label: 'Part A notes' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminSettingsLayout title="Listening: Part A note-completion" breadcrumbs={breadcrumbs}>
        <Card>
          <CardContent className="p-6">
            <p className="text-sm text-admin-fg-muted">Admin access required.</p>
          </CardContent>
        </Card>
      </AdminSettingsLayout>
    );
  }

  return (
    <AdminSettingsLayout
      eyebrow="Authoring"
      icon={<FileText className="w-5 h-5" />}
      title="Listening: Part A note-completion"
      description={`Paper ${paperId ?? ''}. Author the note-completion body for consultations A1 and A2, then fill in the answer key.`}
      breadcrumbs={breadcrumbs}
      actions={
        <Button variant="ghost" size="sm" asChild>
          <Link href={`/admin/content/listening/${paperId}/structure`}>
            <ArrowLeft className="h-4 w-4 mr-1.5" />
            Back to structure
          </Link>
        </Button>
      }
    >
      {loadState === 'loading' && <Skeleton className="h-96 rounded-admin" />}
      {loadState === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

      {loadState === 'ready' && (
        <div className="space-y-8">
          <SubPartSection
            paperId={paperId ?? ''}
            code="A1"
            extract={a1Extract}
            questions={a1Questions}
            disabled={false}
            onSaveSuccess={() => {}}
          />
          <SubPartSection
            paperId={paperId ?? ''}
            code="A2"
            extract={a2Extract}
            questions={a2Questions}
            disabled={false}
            onSaveSuccess={() => {}}
          />
        </div>
      )}
    </AdminSettingsLayout>
  );
}
