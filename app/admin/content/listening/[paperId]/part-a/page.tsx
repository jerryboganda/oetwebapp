'use client';

import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Plus, Save, Sparkles, X } from 'lucide-react';
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
  ensureListeningPartASlots,
  getListeningExtracts,
  getListeningStructure,
  importListeningPartAFromUpload,
  patchListeningExtract,
  patchListeningQuestion,
  type ListeningAuthoredExtract,
  type ListeningAuthoredQuestion,
} from '@/lib/listening-authoring-api';

/**
 * An AI-import payload for one sub-part (A1/A2), applied into the section's
 * editor + answer-key rows for the operator to review then Save. `token`
 * increments per import so a re-import re-applies even if content is identical.
 */
interface SectionImport {
  token: number;
  notesBody: string;
  answers: Array<{ number: number; correctAnswer: string | null; acceptedAnswers: string[] }>;
}

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
  /** Re-fetch paper data (e.g. after creating answer-key slots). */
  onReload: () => void | Promise<void>;
  /** AI-import payload to apply into this section (review-then-Save). */
  imported?: SectionImport;
}

function SubPartSection({
  paperId,
  code,
  extract,
  questions,
  disabled,
  onSaveSuccess,
  onReload,
  imported,
}: SubPartSectionProps) {
  const [notesBody, setNotesBody] = useState(extract?.notesBody ?? '');
  const [initialNotesBody, setInitialNotesBody] = useState(extract?.notesBody ?? '');
  const [rows, setRows] = useState<AnswerRowState[]>(() => initRows(questions));
  const [variantDrafts, setVariantDrafts] = useState<Record<string, string>>({});
  const [saveState, setSaveState] = useState<SaveState>('idle');
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);
  const [creatingSlots, setCreatingSlots] = useState(false);

  // Sync when parent reloads data
  useEffect(() => {
    setNotesBody(extract?.notesBody ?? '');
    setInitialNotesBody(extract?.notesBody ?? '');
    setRows(initRows(questions));
  }, [extract, questions]);

  // Apply an AI-import payload (one-click OCR). Pre-fills the editor + answer
  // rows but deliberately does NOT touch the `initial*` snapshots, so the change
  // shows as "unsaved" and the operator must review and click Save. Keyed on the
  // import token so a fresh import re-applies even with identical content.
  const lastImportToken = useRef(0);
  useEffect(() => {
    if (!imported || imported.token === 0 || imported.token === lastImportToken.current) return;
    lastImportToken.current = imported.token;
    setNotesBody(imported.notesBody);
    setRows((prev) =>
      prev.map((r) => {
        const a = imported.answers.find((x) => x.number === r.questionNumber);
        return a ? { ...r, correctAnswer: a.correctAnswer ?? '', acceptedAnswers: a.acceptedAnswers ?? [] } : r;
      }),
    );
    setSaveState((prev) => (prev === 'saved' ? 'idle' : prev));
  }, [imported]);

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

  // Create the answer-key question slots (Q1..12 / Q13..24) to match the
  // authored gap count, then reload so the answer boxes appear. Used when a note
  // was authored (or AI-imported) but no answer-key questions exist yet.
  const onCreateSlots = useCallback(async () => {
    setCreatingSlots(true);
    try {
      await ensureListeningPartASlots(paperId, code, Math.min(Math.max(gapCount, 1), 12));
      setToast({ variant: 'success', message: `Answer-key slots ready for Part ${code}.` });
      await onReload();
    } catch (e) {
      setToast({ variant: 'error', message: e instanceof Error ? e.message : `Could not create answer slots for Part ${code}.` });
    } finally {
      setCreatingSlots(false);
    }
  }, [paperId, code, gapCount, onReload]);

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
          <div className="grid gap-6 lg:grid-cols-2 lg:items-start">
            {/* Left column: note-completion editor + validation */}
            <div className="space-y-4">
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
            </div>

            {/* Right column: per-gap answer key, sticky beside the notes on wide screens */}
            <div className="lg:sticky lg:top-4">
              <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted mb-3">
                Answer key
              </p>

              {/* No question slots yet (or fewer than gaps): offer to create them
                  so the per-gap answer boxes appear. */}
              {gapCount > 0 && questionCount < gapCount && (
                <div className="mb-4 rounded-admin border border-dashed border-admin-border bg-admin-bg-subtle p-4">
                  <p className="text-sm text-admin-fg-muted mb-3">
                    {questionCount === 0
                      ? `This note has ${gapCount} gap${gapCount !== 1 ? 's' : ''} but no answer-key slots yet.`
                      : `${gapCount} gaps but only ${questionCount} answer slot${questionCount !== 1 ? 's' : ''}.`}{' '}
                    Create the answer boxes to fill in each gap’s correct answer.
                  </p>
                  <Button
                    variant="primary"
                    size="sm"
                    onClick={onCreateSlots}
                    loading={creatingSlots}
                    loadingText="Creating…"
                    disabled={disabled || creatingSlots}
                  >
                    <Plus className="h-4 w-4 mr-1.5" />
                    Create {Math.min(gapCount, 12)} answer slot{Math.min(gapCount, 12) !== 1 ? 's' : ''}
                  </Button>
                </div>
              )}

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

  // One-click AI import (OCR) state.
  const fileInputRef = useRef<HTMLInputElement>(null);
  const importTokenRef = useRef(0);
  const [importing, setImporting] = useState(false);
  const [importByCode, setImportByCode] = useState<{ A1?: SectionImport; A2?: SectionImport }>({});
  const [importToast, setImportToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  const onImportFileChosen = useCallback(
    async (file: File | undefined) => {
      if (!file || !paperId) return;
      setImporting(true);
      setImportToast(null);
      try {
        const detail = await importListeningPartAFromUpload(paperId, file);
        const a1 = detail.extracts.find((e) => e.partCode === 'A1') ?? detail.extracts[0];
        const a2 = detail.extracts.find((e) => e.partCode === 'A2') ?? detail.extracts[1];

        // Ensure answer-key slots exist for each consultation so the AI-suggested
        // answers have rows to populate, then refresh the structure before applying.
        if (a1 && a1.gapCount > 0) await ensureListeningPartASlots(paperId, 'A1', Math.min(a1.gapCount, 12));
        if (a2 && a2.gapCount > 0) await ensureListeningPartASlots(paperId, 'A2', Math.min(a2.gapCount, 12));
        try {
          const fresh = await getListeningStructure(paperId);
          setQuestions(fresh.questions);
        } catch {
          /* non-fatal: the operator can still Save; rows refresh on next load */
        }

        const token = importTokenRef.current + 1;
        importTokenRef.current = token;
        const toImport = (e: typeof a1): SectionImport | undefined =>
          e ? { token, notesBody: e.notesBody ?? '', answers: e.answers } : undefined;
        setImportByCode({ A1: toImport(a1), A2: toImport(a2) });
        setImportToast({
          variant: detail.isStub ? 'error' : 'success',
          message: detail.isStub
            ? `Imported with issues to review: ${detail.stubReason ?? detail.summary}`
            : `AI import ready — review both consultations and Save. ${detail.summary}`,
        });
      } catch (e) {
        setImportToast({ variant: 'error', message: e instanceof Error ? e.message : 'AI import failed.' });
      } finally {
        setImporting(false);
      }
    },
    [paperId],
  );

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
        <div className="flex items-center gap-2">
          <input
            ref={fileInputRef}
            type="file"
            accept="application/pdf,image/*"
            className="hidden"
            onChange={(e) => {
              const file = e.target.files?.[0];
              e.target.value = ''; // allow re-importing the same file
              void onImportFileChosen(file);
            }}
          />
          <Button
            variant="secondary"
            size="sm"
            onClick={() => fileInputRef.current?.click()}
            loading={importing}
            loadingText="Importing…"
            disabled={importing}
          >
            <Sparkles className="h-4 w-4 mr-1.5" />
            AI import (OCR)
          </Button>
          <Button variant="ghost" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/structure`}>
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to structure
            </Link>
          </Button>
        </div>
      }
    >
      {loadState === 'loading' && <Skeleton className="h-96 rounded-admin" />}
      {loadState === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

      {loadState === 'ready' && (
        <div className="space-y-8">
          <p className="text-xs text-admin-fg-muted">
            Tip: click <strong>AI import (OCR)</strong> above to upload the official Part A question paper — the AI fills both
            consultations in the same structure with fill-in-the-blank gaps. Review, edit anything, then Save each part.
          </p>
          <SubPartSection
            paperId={paperId ?? ''}
            code="A1"
            extract={a1Extract}
            questions={a1Questions}
            disabled={false}
            onSaveSuccess={() => {}}
            onReload={load}
            imported={importByCode.A1}
          />
          <SubPartSection
            paperId={paperId ?? ''}
            code="A2"
            extract={a2Extract}
            questions={a2Questions}
            disabled={false}
            onSaveSuccess={() => {}}
            onReload={load}
            imported={importByCode.A2}
          />
        </div>
      )}

      {importToast && (
        <Toast
          variant={importToast.variant}
          message={importToast.message}
          onClose={() => setImportToast(null)}
        />
      )}
    </AdminSettingsLayout>
  );
}
