'use client';

import { useCallback, useEffect, useMemo, useState, type KeyboardEvent } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  ArrowLeft,
  ArrowRight,
  FileText,
  FormInput,
  Headphones,
  ListChecks,
  Plus,
  Save,
  Trash2,
  X,
} from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert, Toast } from '@/components/ui/alert';
import { QuestionPaperPdfViewer, type ReadingPdfAsset } from '@/components/domain/reading-pdf-viewer';
import { getContentPaper } from '@/lib/content-upload-api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import {
  getListeningStructure,
  replaceListeningStructure,
  contentTypeToWire,
  wireToContentType,
  LISTENING_CONTENT_TYPE_LABELS,
  LISTENING_SUB_SECTION_QUESTION_TARGETS,
  type ListeningAuthoredQuestion,
  type ListeningContentType,
  type ListeningSubSectionCode,
} from '@/lib/listening-authoring-api';
import { ListeningAnswerSheetBuilder, type ListeningBuilderPart } from './ListeningAnswerSheetBuilder';
import { ListeningPartAiExtraction, type ListeningExtractionPart } from './ListeningPartAiExtraction';

type LoadState = 'loading' | 'ready' | 'error';
type ToastState = { variant: 'success' | 'error'; message: string };
type PartTab = 'A' | 'B' | 'C';

const ALLOWED_TYPES: ListeningContentType[] = ['MultipleChoice3', 'FillInBlank', 'ShortAnswer'];

/** Sub-sections per part tab. Part A is notes-based (A1/A2); B/C are MCQ. */
const SECTIONS_BY_PART: Record<PartTab, ListeningSubSectionCode[]> = {
  A: ['A1', 'A2'],
  B: ['B1', 'B2', 'B3', 'B4', 'B5', 'B6'],
  C: ['C1', 'C2'],
};

/**
 * Canonical numbering ranges per sub-section (matches the printed OET paper
 * numbering 1–42). New questions claim the first free number in their range,
 * falling back to "max + 1" globally if the range is full so authoring never
 * hard-blocks.
 */
const SUB_SECTION_NUMBER_RANGES: Record<ListeningSubSectionCode, [number, number]> = {
  A1: [1, 12], A2: [13, 24],
  B1: [25, 25], B2: [26, 26], B3: [27, 27], B4: [28, 28], B5: [29, 29], B6: [30, 30],
  C1: [31, 36], C2: [37, 42],
};

function firstParam(value: string | string[] | undefined) {
  return Array.isArray(value) ? value[0] : value;
}

function errorMessage(err: unknown, fallback: string): string {
  if (err && typeof err === 'object' && 'detail' in err) {
    const detail = (err as { detail?: unknown }).detail;
    if (detail && typeof detail === 'object') {
      const msg = (detail as { message?: unknown }).message;
      if (typeof msg === 'string' && msg.trim()) return msg;
    }
  }
  return err instanceof Error && err.message ? err.message : fallback;
}

function normalizeCode(code: string): string {
  return code.trim().toUpperCase();
}

function partOf(subSection: string): PartTab {
  const code = normalizeCode(subSection);
  if (code.startsWith('B')) return 'B';
  if (code.startsWith('C')) return 'C';
  return 'A';
}

interface QuestionFormState {
  /** Existing question id, or null for a new question. */
  id: string | null;
  number: number;
  contentType: ListeningContentType;
  stem: string;
  /** MCQ option texts (length 3 for MultipleChoice3). */
  options: string[];
  /** For MCQ this stores the correct option's text; for free text the answer. */
  correctAnswer: string;
  acceptedAnswers: string[];
  explanation: string;
  skillTag: string;
  points: number;
}

function emptyForm(number: number): QuestionFormState {
  return {
    id: null,
    number,
    contentType: 'ShortAnswer',
    stem: '',
    options: [],
    correctAnswer: '',
    acceptedAnswers: [],
    explanation: '',
    skillTag: '',
    points: 1,
  };
}

function questionToForm(q: ListeningAuthoredQuestion): QuestionFormState {
  const contentType = wireToContentType(q.type);
  const isMcq = contentType === 'MultipleChoice3';
  return {
    id: q.id,
    number: q.number,
    contentType,
    stem: q.stem ?? '',
    options: isMcq ? (q.options ?? ['', '', '']).slice(0, 3) : [],
    correctAnswer: q.correctAnswer ?? '',
    acceptedAnswers: q.acceptedAnswers ?? [],
    explanation: q.explanation ?? '',
    skillTag: q.skillTag ?? '',
    points: q.points ?? 1,
  };
}

/** Build the wire question for a sub-section from the edited form. */
function formToQuestion(
  form: QuestionFormState,
  code: ListeningSubSectionCode,
  previous: ListeningAuthoredQuestion | null,
): ListeningAuthoredQuestion {
  const type = contentTypeToWire(form.contentType);
  const isMcq = type === 'multiple_choice_3';
  return {
    ...(previous ?? {}),
    id: form.id ?? `lq-${form.number}`,
    number: form.number,
    partCode: code,
    type,
    stem: form.stem.trim(),
    options: isMcq ? form.options.map((o) => o.trim()) : [],
    correctAnswer: form.correctAnswer.trim(),
    acceptedAnswers: isMcq ? [] : form.acceptedAnswers,
    explanation: form.explanation.trim() ? form.explanation.trim() : null,
    skillTag: form.skillTag.trim() ? form.skillTag.trim() : null,
    points: Math.max(1, form.points),
  } as ListeningAuthoredQuestion;
}

export default function AdminListeningQuestionsPage() {
  const params = useParams<{ paperId?: string | string[] }>();
  const paperId = firstParam(params?.paperId) ?? '';
  const { isAuthenticated, role } = useAdminAuth();

  const [state, setState] = useState<LoadState>('loading');
  const [questions, setQuestions] = useState<ListeningAuthoredQuestion[]>([]);
  const [assets, setAssets] = useState<ReadingPdfAsset[]>([]);
  const [error, setError] = useState<string | null>(null);
  const [activePart, setActivePart] = useState<PartTab>('A');
  const [activeSection, setActiveSection] = useState<ListeningSubSectionCode>('A1');
  const [toast, setToast] = useState<ToastState | null>(null);

  const [editing, setEditing] = useState(false);
  const [form, setForm] = useState<QuestionFormState | null>(null);
  const [variantDraft, setVariantDraft] = useState('');
  const [saving, setSaving] = useState(false);

  const load = useCallback(async () => {
    if (!paperId) return;
    setState('loading');
    setError(null);
    try {
      const result = await getListeningStructure(paperId);
      setQuestions(result.questions);
      setState('ready');
    } catch (e) {
      setError(errorMessage(e, 'Failed to load listening structure.'));
      setState('error');
    }
    // Question-paper PDFs power the left-hand viewer. A failure here must not
    // break authoring, so it's fetched separately and swallowed.
    try {
      const paper = await getContentPaper(paperId);
      const qp = (paper.assets ?? [])
        .filter((a) => a.role === 'QuestionPaper')
        .map((a) => ({
          id: a.id,
          part: a.part,
          title: a.title ?? 'Question paper',
          downloadPath: `/v1/media/${a.mediaAssetId}/content`,
        }));
      setAssets(qp);
    } catch {
      setAssets([]);
    }
  }, [paperId]);

  useEffect(() => {
    if (!isAuthenticated || role !== 'admin') return;
    void load();
  }, [isAuthenticated, role, load]);

  // Keep the active section consistent with the active part.
  useEffect(() => {
    if (partOf(activeSection) !== activePart) {
      setActiveSection(SECTIONS_BY_PART[activePart][0]);
      setEditing(false);
      setForm(null);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activePart]);

  const countsByPart = useMemo(() => {
    const counts: Record<PartTab, number> = { A: 0, B: 0, C: 0 };
    for (const q of questions) counts[partOf(q.partCode)] += 1;
    return counts;
  }, [questions]);

  const activeQuestions = useMemo(
    () => questions
      .filter((q) => normalizeCode(q.partCode) === activeSection)
      .sort((a, b) => a.number - b.number),
    [questions, activeSection],
  );

  const totalAuthored = questions.length;
  const pdfSlotKey = activePart === 'A' ? 'A' : activeSection;

  // ── Numbering ──────────────────────────────────────────────────────────

  function nextNumberFor(code: ListeningSubSectionCode): number {
    const used = new Set(questions.map((q) => q.number));
    const [lo, hi] = SUB_SECTION_NUMBER_RANGES[code];
    for (let n = lo; n <= hi; n++) {
      if (!used.has(n)) return n;
    }
    const max = questions.reduce((m, q) => Math.max(m, q.number), 0);
    return max + 1;
  }

  // ── Editor handlers ──────────────────────────────────────────────────────

  function startAdd() {
    setForm(emptyForm(nextNumberFor(activeSection)));
    setVariantDraft('');
    setEditing(true);
  }

  function startEdit(q: ListeningAuthoredQuestion) {
    setActiveSection(normalizeCode(q.partCode) as ListeningSubSectionCode);
    setForm(questionToForm(q));
    setVariantDraft('');
    setEditing(true);
  }

  function cancelEdit() {
    setEditing(false);
    setForm(null);
    setVariantDraft('');
  }

  function handleTypeChange(next: ListeningContentType) {
    if (!form) return;
    if (next === 'MultipleChoice3') {
      setForm({
        ...form,
        contentType: next,
        options: (form.options.length ? form.options : ['', '', '']).slice(0, 3).concat(['', '', '']).slice(0, 3),
        correctAnswer: '',
        acceptedAnswers: [],
      });
    } else {
      setForm({ ...form, contentType: next, options: [] });
    }
  }

  function setOption(index: number, value: string) {
    if (!form) return;
    const options = [...form.options];
    const prevText = options[index];
    options[index] = value;
    const correctAnswer = form.correctAnswer === prevText ? value : form.correctAnswer;
    setForm({ ...form, options, correctAnswer });
  }

  function addVariant() {
    const trimmed = variantDraft.trim();
    if (!trimmed || !form) return;
    if (!form.acceptedAnswers.includes(trimmed)) {
      setForm({ ...form, acceptedAnswers: [...form.acceptedAnswers, trimmed] });
    }
    setVariantDraft('');
  }

  function removeVariant(value: string) {
    if (!form) return;
    setForm({ ...form, acceptedAnswers: form.acceptedAnswers.filter((v) => v !== value) });
  }

  function onVariantKey(event: KeyboardEvent<HTMLInputElement>) {
    if (event.key === 'Enter') {
      event.preventDefault();
      addVariant();
    }
  }

  function validateForm(f: QuestionFormState): string | null {
    if (!f.stem.trim()) return 'Question stem is required.';
    if (f.contentType === 'MultipleChoice3') {
      if (f.options.length !== 3 || f.options.some((o) => !o.trim())) {
        return 'Multiple choice questions need exactly 3 non-empty options.';
      }
      if (!f.correctAnswer.trim()) return 'Select the correct option.';
      if (!f.options.map((o) => o.trim()).includes(f.correctAnswer.trim())) {
        return 'The correct answer must match one of the options.';
      }
    } else if (!f.correctAnswer.trim()) {
      return 'A correct answer is required.';
    }
    return null;
  }

  async function persist(nextQuestions: ListeningAuthoredQuestion[], successMessage: string) {
    setSaving(true);
    try {
      const ordered = [...nextQuestions].sort((a, b) => a.number - b.number);
      const result = await replaceListeningStructure(paperId, ordered);
      setQuestions(result.questions);
      setToast({ variant: 'success', message: successMessage });
      cancelEdit();
    } catch (e) {
      setToast({ variant: 'error', message: errorMessage(e, 'Save failed.') });
    } finally {
      setSaving(false);
    }
  }

  async function handleSave() {
    if (!form) return;
    const validationError = validateForm(form);
    if (validationError) {
      setToast({ variant: 'error', message: validationError });
      return;
    }
    const previous = form.id ? questions.find((q) => q.id === form.id) ?? null : null;
    const built = formToQuestion(form, activeSection, previous);
    const next = form.id
      ? questions.map((q) => (q.id === form.id ? built : q))
      : [...questions, built];
    await persist(next, form.id ? 'Question updated.' : 'Question created.');
  }

  async function handleDelete(q: ListeningAuthoredQuestion) {
    if (!window.confirm(`Delete question ${q.number} from ${activeSection}? This cannot be undone.`)) return;
    const next = questions.filter((item) => item.id !== q.id);
    await persist(next, 'Question deleted.');
  }

  // ── Render ───────────────────────────────────────────────────────────────

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Content', href: '/admin/content' },
    { label: 'Listening', href: '/admin/content/listening' },
    { label: 'Paper', href: `/admin/content/listening/${paperId}/structure` },
    { label: 'Questions' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout title="Listening Questions" breadcrumbs={breadcrumbs}>
        <div className="p-6">
          <p className="text-sm text-admin-fg-muted">Admin access required.</p>
        </div>
      </AdminTableLayout>
    );
  }

  return (
    <AdminTableLayout
      eyebrow="Listening authoring"
      title="Listening Questions"
      description="Author per part. Part A uses note-completion gaps; Part B and Part C are PDF-backed answer sheets — record only the correct option for each item."
      breadcrumbs={breadcrumbs}
      actions={
        <div className="flex flex-wrap items-center gap-2">
          <Button variant="ghost" size="sm" asChild>
            <Link href="/admin/content/listening">
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Back to papers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/pdfs`}>
              <FileText className="h-4 w-4 mr-1.5" />
              Question papers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/audio`}>
              <Headphones className="h-4 w-4 mr-1.5" />
              Audio & timers
            </Link>
          </Button>
          <Button variant="outline" size="sm" asChild>
            <Link href={`/admin/content/listening/${paperId}/structure`}>
              <ListChecks className="h-4 w-4 mr-1.5" />
              Structure
            </Link>
          </Button>
        </div>
      }
      footer={
        <div className="flex items-center justify-between pt-2">
          <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
            <Link href={`/admin/content/listening/${paperId}/audio`}>Back to Audio & timers</Link>
          </Button>
          <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
            <Link href={`/admin/content/listening/${paperId}/sequence`}>Next: Sequence</Link>
          </Button>
        </div>
      }
    >
      <div className="p-4 sm:p-5 space-y-4">
        <div className="flex flex-wrap items-center gap-2">
          <h2 className="text-base font-semibold text-admin-fg-strong">Questions</h2>
          <Badge variant={totalAuthored === 42 ? 'success' : 'muted'} size="sm">{totalAuthored} / 42 authored</Badge>
        </div>

        {state === 'loading' && (
          <div className="space-y-3">
            <Skeleton variant="text" className="h-8 w-1/2" />
            <Skeleton variant="card" />
            <Skeleton variant="card" />
          </div>
        )}

        {state === 'error' && error && <InlineAlert variant="error">{error}</InlineAlert>}

        {state === 'ready' && (
          <div className="space-y-4">
            {/* Part tabs A / B / C */}
            <div role="tablist" aria-label="Listening parts" className="flex flex-wrap gap-1.5">
              {(['A', 'B', 'C'] as PartTab[]).map((part) => {
                const isActive = part === activePart;
                return (
                  <button
                    key={part}
                    type="button"
                    role="tab"
                    aria-selected={isActive}
                    onClick={() => setActivePart(part)}
                    className={
                      'inline-flex items-center gap-1.5 rounded-admin border px-3 py-1.5 text-sm font-semibold transition-colors ' +
                      (isActive
                        ? 'border-[var(--admin-primary)] bg-[var(--admin-primary)] text-[var(--admin-primary-fg)]'
                        : 'border-admin-border bg-admin-bg-surface text-admin-fg-muted hover:text-admin-fg-strong')
                    }
                  >
                    Part {part}
                    <span className="rounded-full px-1.5 text-xs tabular-nums opacity-70">{countsByPart[part]}</span>
                  </button>
                );
              })}
            </div>

            {/* Section sub-tabs */}
            <div className="flex flex-wrap gap-1.5">
              {SECTIONS_BY_PART[activePart].map((code) => {
                const isActive = code === activeSection;
                const count = questions.filter((q) => normalizeCode(q.partCode) === code).length;
                const target = LISTENING_SUB_SECTION_QUESTION_TARGETS[code];
                return (
                  <button
                    key={code}
                    type="button"
                    onClick={() => { setActiveSection(code); cancelEdit(); }}
                    className={
                      'inline-flex items-center gap-1.5 rounded-md border px-3 py-1.5 text-xs font-semibold transition-colors ' +
                      (isActive
                        ? 'border-primary bg-primary text-white'
                        : 'border-admin-border bg-admin-bg-surface text-admin-fg-muted hover:text-admin-fg-strong')
                    }
                  >
                    {code}
                    <span className="opacity-70">{count}/{target}</span>
                  </button>
                );
              })}
            </div>

            {activePart === 'A' ? (
              <PartANotice paperId={paperId} />
            ) : null}

            {/* B/C: one-click AI extraction (OCR) for the whole part — upload the
                question paper(s) + answer key, the AI fills A/B/C + rationale for
                Q25–30 (B) / Q31–42 (C), the admin proofreads and saves once. */}
            {activePart !== 'A' ? (
              <ListeningPartAiExtraction
                paperId={paperId}
                part={activePart as ListeningExtractionPart}
                allQuestions={questions}
                onSaved={(next) => { setQuestions(next); }}
                onNotify={(variant, message) => setToast({ variant, message })}
              />
            ) : null}

            {/* B/C: two-column PDF + answer sheet. A: single-column editor. */}
            {activePart !== 'A' ? (
              <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.05fr)_minmax(420px,0.95fr)]">
                <div className="xl:sticky xl:top-4 xl:self-start">
                  <QuestionPaperPdfViewer
                    paperId={paperId}
                    partCode={pdfSlotKey}
                    assets={assets}
                    annotations={[]}
                    readOnly
                    documentNoun="Listening paper"
                  />
                </div>
                <ListeningAnswerSheetBuilder
                  paperId={paperId}
                  partCode={activePart as ListeningBuilderPart}
                  activeSection={activeSection}
                  allQuestions={questions}
                  onSaved={(next) => { setQuestions(next); }}
                  onNotify={(variant, message) => setToast({ variant, message })}
                  onEditDetails={startEdit}
                />
              </div>
            ) : null}

            {/* Question list for the active sub-section. */}
            <Card>
              <CardHeader>
                <div className="min-w-0">
                  <CardTitle className="text-sm">Sub-section {activeSection}</CardTitle>
                  <CardDescription>
                    {activeQuestions.length} question{activeQuestions.length === 1 ? '' : 's'} authored
                    {' · target '}{LISTENING_SUB_SECTION_QUESTION_TARGETS[activeSection]}
                  </CardDescription>
                </div>
              </CardHeader>
              <CardContent className="space-y-3">
                {activeQuestions.length === 0 ? (
                  <div className="rounded-admin border border-dashed border-admin-border bg-admin-bg-subtle px-4 py-6 text-center">
                    <p className="text-sm text-admin-fg-muted">No questions yet for {activeSection}.</p>
                  </div>
                ) : (
                  <div className="space-y-2">
                    {activeQuestions.map((q) => (
                      <div
                        key={q.id}
                        className="flex items-center gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle px-3 py-2"
                      >
                        <span className="w-8 shrink-0 text-center font-mono text-xs text-admin-fg-muted">{q.number}</span>
                        <div className="min-w-0 flex-1">
                          <p className="truncate text-sm text-admin-fg-strong">
                            {q.stem || <em className="text-admin-fg-muted">empty stem</em>}
                          </p>
                        </div>
                        <Badge variant="primary" size="sm" className="shrink-0">
                          {LISTENING_CONTENT_TYPE_LABELS[wireToContentType(q.type)]}
                        </Badge>
                        <span className="shrink-0 text-xs text-admin-fg-muted">{q.points}pt</span>
                        <div className="flex shrink-0 gap-1">
                          <Button variant="ghost" size="sm" onClick={() => startEdit(q)} aria-label={`Edit question ${q.number}`}>
                            <FormInput className="h-3.5 w-3.5" />
                          </Button>
                          <Button
                            variant="ghost"
                            size="sm"
                            onClick={() => void handleDelete(q)}
                            aria-label={`Delete question ${q.number}`}
                            className="text-[var(--admin-danger)]"
                          >
                            <Trash2 className="h-3.5 w-3.5" />
                          </Button>
                        </div>
                      </div>
                    ))}
                  </div>
                )}

                {!editing && activePart === 'A' && (
                  <Button variant="primary" size="sm" onClick={startAdd} startIcon={<Plus className="h-4 w-4" />}>
                    Add question to {activeSection}
                  </Button>
                )}
              </CardContent>
            </Card>

            {editing && form && (
              <Card surface="tinted-primary">
                <CardHeader>
                  <div className="min-w-0">
                    <CardTitle className="text-sm">
                      {form.id ? `Edit question ${form.number}` : `New question ${form.number}`} · {activeSection}
                    </CardTitle>
                    <CardDescription>Advanced editor — choose a content type and fill in the answer details.</CardDescription>
                  </div>
                </CardHeader>
                <CardContent>
                  <QuestionEditor
                    form={form}
                    variantDraft={variantDraft}
                    saving={saving}
                    onChange={setForm}
                    onTypeChange={handleTypeChange}
                    onOptionChange={setOption}
                    onVariantDraftChange={setVariantDraft}
                    onAddVariant={addVariant}
                    onRemoveVariant={removeVariant}
                    onVariantKey={onVariantKey}
                    onSave={handleSave}
                    onCancel={cancelEdit}
                  />
                </CardContent>
              </Card>
            )}
          </div>
        )}
      </div>

      {toast && (
        <div className="fixed top-4 right-4 z-50">
          <Toast variant={toast.variant} message={toast.message} onClose={() => setToast(null)} />
        </div>
      )}
    </AdminTableLayout>
  );
}

/** Part A is authored as note-completion gaps on a dedicated editor. */
function PartANotice({ paperId }: { paperId: string }) {
  return (
    <InlineAlert variant="info">
      Part A is note-completion. Author the notes layout and inline gap answers on the{' '}
      <Link href={`/admin/content/listening/${paperId}/part-a`} className="font-semibold underline">
        Part A notes editor
      </Link>
      . Per-gap answer keys below are kept in sync with the notes gaps.
    </InlineAlert>
  );
}

function QuestionEditor({
  form,
  variantDraft,
  saving,
  onChange,
  onTypeChange,
  onOptionChange,
  onVariantDraftChange,
  onAddVariant,
  onRemoveVariant,
  onVariantKey,
  onSave,
  onCancel,
}: {
  form: QuestionFormState;
  variantDraft: string;
  saving: boolean;
  onChange: (form: QuestionFormState) => void;
  onTypeChange: (type: ListeningContentType) => void;
  onOptionChange: (index: number, value: string) => void;
  onVariantDraftChange: (value: string) => void;
  onAddVariant: () => void;
  onRemoveVariant: (value: string) => void;
  onVariantKey: (event: KeyboardEvent<HTMLInputElement>) => void;
  onSave: () => void;
  onCancel: () => void;
}) {
  const isMcq = form.contentType === 'MultipleChoice3';

  return (
    <div className="space-y-4">
      <div className="grid gap-4 md:grid-cols-2">
        <Select
          label="Content type"
          value={form.contentType}
          onChange={(e) => onTypeChange(e.target.value as ListeningContentType)}
          options={ALLOWED_TYPES.map((t) => ({ value: t, label: LISTENING_CONTENT_TYPE_LABELS[t] }))}
        />
        <Input
          label="Points"
          type="number"
          min={1}
          value={form.points}
          onChange={(e) => onChange({ ...form, points: Math.max(1, parseInt(e.target.value, 10) || 1) })}
        />
      </div>

      <Textarea
        label="Stem (question text)"
        rows={3}
        value={form.stem}
        onChange={(e) => onChange({ ...form, stem: e.target.value })}
        placeholder={isMcq ? 'What does the speaker imply about…?' : 'Patient reports pain located in the ____'}
      />

      {isMcq ? (
        <div className="space-y-3 rounded-admin border border-admin-border bg-admin-bg-surface p-4">
          <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Options</p>
          <p className="text-xs text-admin-fg-muted">Select the radio next to the correct option.</p>
          {form.options.map((option, index) => {
            const letter = String.fromCharCode(65 + index);
            const isCorrect = form.correctAnswer === option && option !== '';
            return (
              <div key={index} className="flex items-start gap-3 rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                <label className="flex items-center gap-2 pt-2">
                  <input
                    type="radio"
                    name="listening-correct-option"
                    checked={isCorrect}
                    onChange={() => onChange({ ...form, correctAnswer: option })}
                    aria-label={`Mark option ${letter} correct`}
                  />
                  <Badge variant={isCorrect ? 'success' : 'default'}>{letter}</Badge>
                </label>
                <div className="flex-1">
                  <Input
                    aria-label={`Option ${letter} text`}
                    value={option}
                    onChange={(e) => onOptionChange(index, e.target.value)}
                    placeholder={`Option ${letter}`}
                  />
                </div>
              </div>
            );
          })}
        </div>
      ) : (
        <div className="space-y-3">
          <Input
            label="Correct answer"
            value={form.correctAnswer}
            onChange={(e) => onChange({ ...form, correctAnswer: e.target.value })}
            placeholder="cholesterol"
          />
          <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-4">
            <p className="text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">Accepted variants</p>
            <p className="mt-1 text-xs text-admin-fg-muted">UK/US spelling, abbreviations, plurals. Keep tight.</p>
            <div className="mt-3 flex flex-wrap gap-2">
              {form.acceptedAnswers.map((v) => (
                <span key={v} className="inline-flex items-center gap-1 rounded-full bg-[var(--admin-primary)]/10 px-3 py-1 text-sm text-[var(--admin-primary)]">
                  {v}
                  <button
                    type="button"
                    aria-label={`Remove variant ${v}`}
                    onClick={() => onRemoveVariant(v)}
                    className="rounded-full p-0.5 hover:bg-[var(--admin-primary)]/20"
                  >
                    <X className="h-3 w-3" aria-hidden="true" />
                  </button>
                </span>
              ))}
            </div>
            <div className="mt-3 flex items-center gap-2">
              <Input
                placeholder="Add variant…"
                value={variantDraft}
                onChange={(e) => onVariantDraftChange(e.target.value)}
                onKeyDown={onVariantKey}
              />
              <Button type="button" variant="outline" size="sm" onClick={onAddVariant} disabled={!variantDraft.trim()}>
                <Plus className="h-4 w-4 mr-1" />
                Add
              </Button>
            </div>
          </div>
        </div>
      )}

      <div className="grid gap-4 md:grid-cols-2">
        <Input
          label="Skill tag (optional)"
          value={form.skillTag}
          onChange={(e) => onChange({ ...form, skillTag: e.target.value })}
          placeholder="numbers_units"
        />
      </div>

      <Textarea
        label="Explanation (optional, shown on review)"
        rows={2}
        value={form.explanation}
        onChange={(e) => onChange({ ...form, explanation: e.target.value })}
        placeholder="Explain why this is the correct answer…"
      />

      <div className="flex gap-2 pt-1">
        <Button variant="primary" size="sm" onClick={onSave} loading={saving} loadingText="Saving…" startIcon={<Save className="h-4 w-4" />}>
          Save
        </Button>
        <Button variant="ghost" size="sm" onClick={onCancel} disabled={saving}>
          Cancel
        </Button>
      </div>
    </div>
  );
}
