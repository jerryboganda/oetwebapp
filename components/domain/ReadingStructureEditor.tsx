'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { ArrowDown, ArrowUp, CheckCircle2, Download, FileText, History, Plus, RefreshCw, ShieldCheck, Trash2, AlertTriangle, Upload } from 'lucide-react';
import { AdminRoutePanel } from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { Skeleton } from '@/components/ui/skeleton';
import {
  ensureCanonicalParts,
  exportReadingStructureManifest,
  getReadingStructureAdmin,
  importReadingStructureManifest,
  removeReadingQuestion,
  removeReadingText,
  reorderReadingQuestions,
  reorderReadingTexts,
  setReadingQuestionDistractors,
  getReadingQuestionReviewHistory,
  transitionReadingQuestionReviewState,
  upsertReadingQuestion,
  upsertReadingText,
  validateReadingPaper,
  type ReadingPartAdminDto,
  type ReadingPartCode,
  type ReadingQuestionAdminDto,
  type ReadingStructureManifestDto,
  type ReadingQuestionType,
  type ReadingReviewLogEntryDto,
  type ReadingReviewState,
  type ReadingDistractorCategory,
  type ReadingStructureAdminDto,
  type ReadingTextDto,
  type ReadingValidationReport,
} from '@/lib/reading-authoring-api';

interface Props { paperId: string }

type TextDraft = Omit<ReadingTextDto, 'id'> & { id: string | null };
type QuestionDraft = Omit<ReadingQuestionAdminDto, 'id'> & { id: string | null };
type ManifestModalState = { mode: 'import' | 'export'; value: string };

const QUESTION_TYPE_LABELS: Record<ReadingQuestionType, string> = {
  MatchingTextReference: 'Matching (Part A)',
  ShortAnswer: 'Short answer (Part A)',
  SentenceCompletion: 'Sentence completion (Part A)',
  MultipleChoice3: '3-option MCQ (Part B)',
  MultipleChoice4: '4-option MCQ (Part C)',
};

const PART_EXPECTED: Record<ReadingPartCode, { items: number; texts: number; minutes: number; label: string; textLabel: string }> = {
  A: { items: 20, texts: 4, minutes: 15, label: 'Part A (matching / short-answer)', textLabel: '4 related texts' },
  B: { items: 6, texts: 6, minutes: 45, label: 'Part B (3-option MCQ)', textLabel: '6 short extracts' },
  C: { items: 16, texts: 2, minutes: 45, label: 'Part C (4-option MCQ)', textLabel: '2 long passages' },
};

const REVIEW_STATES: ReadingReviewState[] = ['Draft', 'AcademicReview', 'MedicalReview', 'LanguageReview', 'Pilot', 'Published', 'Retired'];

const DISTRACTOR_CATEGORY_OPTIONS: Array<{ value: ReadingDistractorCategory; label: string }> = [
  { value: 'Opposite', label: 'Opposite' },
  { value: 'TooBroad', label: 'Too broad' },
  { value: 'TooSpecific', label: 'Too specific' },
  { value: 'WrongSpeaker', label: 'Wrong speaker/source' },
  { value: 'NotInText', label: 'Not in text' },
  { value: 'DistortedDetail', label: 'Distorted detail' },
  { value: 'OutOfScope', label: 'Out of scope' },
];

type ReviewHistoryState = { question: ReadingQuestionAdminDto; entries: ReadingReviewLogEntryDto[] };

export function ReadingStructureEditor({ paperId }: Props) {
  const [structure, setStructure] = useState<ReadingStructureAdminDto | null>(null);
  const [report, setReport] = useState<ReadingValidationReport | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [textDraft, setTextDraft] = useState<TextDraft | null>(null);
  const [questionDraft, setQuestionDraft] = useState<QuestionDraft | null>(null);
  const [savingDraft, setSavingDraft] = useState(false);
  const [manifestModal, setManifestModal] = useState<ManifestModalState | null>(null);
  const [manifestBusy, setManifestBusy] = useState(false);
  const [reviewHistory, setReviewHistory] = useState<ReviewHistoryState | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      await ensureCanonicalParts(paperId);
      const [s, r] = await Promise.all([
        getReadingStructureAdmin(paperId),
        validateReadingPaper(paperId),
      ]);
      setStructure(s);
      setReport(r);
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => { queueMicrotask(() => { void load(); }); }, [load]);

  const saveText = async (draft: TextDraft) => {
    setSavingDraft(true);
    try {
      await upsertReadingText(paperId, {
        id: draft.id,
        readingPartId: draft.readingPartId,
        displayOrder: draft.displayOrder,
        title: draft.title,
        source: draft.source,
        bodyHtml: draft.bodyHtml,
        wordCount: draft.wordCount,
        topicTag: draft.topicTag,
      });
      setTextDraft(null);
      await load();
    } catch (e) {
      setError((e as Error).message);
    } finally { setSavingDraft(false); }
  };

  const saveQuestion = async (draft: QuestionDraft) => {
    setSavingDraft(true);
    try {
      const saved = await upsertReadingQuestion(paperId, {
        id: draft.id,
        readingPartId: draft.readingPartId,
        readingTextId: draft.readingTextId,
        displayOrder: draft.displayOrder,
        points: draft.points,
        questionType: draft.questionType,
        stem: draft.stem,
        optionsJson: draft.optionsJson,
        correctAnswerJson: draft.correctAnswerJson,
        acceptedSynonymsJson: draft.acceptedSynonymsJson,
        caseSensitive: draft.caseSensitive,
        explanationMarkdown: draft.explanationMarkdown,
        skillTag: draft.skillTag,
      });
      if (draft.questionType === 'MultipleChoice3' || draft.questionType === 'MultipleChoice4') {
        await setReadingQuestionDistractors(paperId, saved.id, parseDistractorJson(draft.optionDistractorsJson));
      }
      setQuestionDraft(null);
      await load();
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string } }).detail;
      setError(detail?.error ?? (e as Error).message);
    } finally { setSavingDraft(false); }
  };

  const openManifestExport = async () => {
    setManifestBusy(true);
    setError(null);
    try {
      const manifest = await exportReadingStructureManifest(paperId);
      setManifestModal({ mode: 'export', value: JSON.stringify(manifest, null, 2) });
    } catch (e) {
      setError((e as Error).message);
    } finally {
      setManifestBusy(false);
    }
  };

  const reorderTexts = async (part: ReadingPartAdminDto, textId: string, direction: -1 | 1) => {
    const ordered = [...part.texts].sort((a, b) => a.displayOrder - b.displayOrder);
    const index = ordered.findIndex((text) => text.id === textId);
    const nextIndex = index + direction;
    if (index < 0 || nextIndex < 0 || nextIndex >= ordered.length) return;
    [ordered[index], ordered[nextIndex]] = [ordered[nextIndex], ordered[index]];
    await reorderReadingTexts(paperId, part.id, ordered.map((text) => text.id));
    await load();
  };

  const reorderQuestions = async (part: ReadingPartAdminDto, questionId: string, direction: -1 | 1) => {
    const ordered = [...part.questions].sort((a, b) => a.displayOrder - b.displayOrder);
    const index = ordered.findIndex((question) => question.id === questionId);
    const nextIndex = index + direction;
    if (index < 0 || nextIndex < 0 || nextIndex >= ordered.length) return;
    [ordered[index], ordered[nextIndex]] = [ordered[nextIndex], ordered[index]];
    await reorderReadingQuestions(paperId, part.id, ordered.map((question) => question.id));
    await load();
  };

  const advanceQuestionToPublished = async (question: ReadingQuestionAdminDto) => {
    const current = question.reviewState ?? 'Draft';
    const currentIndex = REVIEW_STATES.indexOf(current);
    const publishedIndex = REVIEW_STATES.indexOf('Published');
    if (current === 'Retired' || currentIndex < 0 || currentIndex >= publishedIndex) return;
    for (const nextState of REVIEW_STATES.slice(currentIndex + 1, publishedIndex + 1)) {
      await transitionReadingQuestionReviewState(paperId, question.id, {
        toState: nextState,
        note: 'Advanced from the Reading admin structure editor.',
      });
    }
    await load();
  };

  const advancePartToPublished = async (part: ReadingPartAdminDto) => {
    for (const question of part.questions) {
      await advanceQuestionToPublished(question);
    }
  };

  const openReviewHistory = async (question: ReadingQuestionAdminDto) => {
    const entries = await getReadingQuestionReviewHistory(paperId, question.id);
    setReviewHistory({ question, entries });
  };

  const importManifest = async (value: string) => {
    setManifestBusy(true);
    setError(null);
    try {
      const parsed = JSON.parse(value) as unknown;
      const wrappedManifest = parsed && typeof parsed === 'object' && 'manifest' in parsed
        ? (parsed as { manifest?: unknown }).manifest
        : parsed;
      if (!wrappedManifest || typeof wrappedManifest !== 'object' || !Array.isArray((wrappedManifest as ReadingStructureManifestDto).parts)) {
        throw new Error('Reading JSON must contain a manifest with a parts array.');
      }
      const manifest = wrappedManifest as ReadingStructureManifestDto;
      const result = await importReadingStructureManifest(paperId, { replaceExisting: true, manifest });
      setStructure(result.structure);
      setReport(result.report);
      setManifestModal(null);
    } catch (e) {
      const detail = (e as Error & { detail?: { error?: string } }).detail;
      setError(detail?.error ?? (e as Error).message);
    } finally {
      setManifestBusy(false);
    }
  };

  if (loading && !structure) {
    return <AdminRoutePanel title="Reading structure"><Skeleton className="h-48" /></AdminRoutePanel>;
  }

  return (
    <AdminRoutePanel
      title="Reading structure"
      actions={<div className="flex flex-wrap items-center gap-2">
        <Button variant="ghost" size="sm" onClick={() => setManifestModal({ mode: 'import', value: '' })}>
          <Upload className="w-4 h-4" /> Import JSON
        </Button>
        <Button variant="ghost" size="sm" onClick={() => void openManifestExport()} loading={manifestBusy}>
          <Download className="w-4 h-4" /> Export JSON
        </Button>
        <Button variant="ghost" size="sm" onClick={() => void load()}>
          <RefreshCw className="w-4 h-4" /> Refresh
        </Button>
      </div>}
    >
      {error && <InlineAlert variant="error">{error}</InlineAlert>}

      {report && (
        <div className="mb-4 flex flex-wrap items-center gap-3">
          {report.isPublishReady
            ? <Badge variant="success"><CheckCircle2 className="w-3 h-3 mr-1 inline" /> Ready to publish</Badge>
            : <Badge variant="warning"><AlertTriangle className="w-3 h-3 mr-1 inline" /> {report.issues.filter(i => i.severity === 'error').length} blocker(s)</Badge>}
          <span className="text-sm text-muted">
            {report.counts.partACount}/20 · {report.counts.partBCount}/6 · {report.counts.partCCount}/16 ·
            total {report.counts.totalPoints}/42
          </span>
        </div>
      )}

      {report && report.issues.length > 0 && (
        <ul className="mb-4 space-y-1 text-sm">
          {report.issues.map((iss, i) => (
            <li key={i} className={iss.severity === 'error' ? 'text-red-700' : 'text-amber-700'}>
              <Badge variant={iss.severity === 'error' ? 'danger' : 'warning'}>{iss.code}</Badge>
              <span className="ml-2">{iss.message}</span>
            </li>
          ))}
        </ul>
      )}

      <div className="space-y-6">
        {structure?.parts.map((part) => (
          <PartEditor
            key={part.id}
            part={part}
            onAddText={() => setTextDraft({
              id: null, readingPartId: part.id, displayOrder: part.texts.length + 1,
              title: '', source: '', bodyHtml: '', wordCount: 0, topicTag: null,
            })}
            onEditText={(t) => setTextDraft({ ...t })}
            onRemoveText={async (id) => { await removeReadingText(paperId, id); await load(); }}
            onMoveText={(id, direction) => void reorderTexts(part, id, direction)}
            onAddQuestion={() => setQuestionDraft(defaultQuestionDraftFor(part))}
            onEditQuestion={(q) => setQuestionDraft({ ...q })}
            onRemoveQuestion={async (id) => { await removeReadingQuestion(paperId, id); await load(); }}
            onMoveQuestion={(id, direction) => void reorderQuestions(part, id, direction)}
            onAdvanceQuestion={(question) => void advanceQuestionToPublished(question)}
            onAdvancePart={() => void advancePartToPublished(part)}
            onOpenReviewHistory={(question) => void openReviewHistory(question)}
          />
        ))}
      </div>

      {textDraft && (
        <TextEditorModal
          draft={textDraft}
          saving={savingDraft}
          onChange={setTextDraft}
          onClose={() => setTextDraft(null)}
          onSave={() => void saveText(textDraft)}
        />
      )}

      {questionDraft && (
        <QuestionEditorModal
          draft={questionDraft}
          texts={structure?.parts.find((part) => part.id === questionDraft.readingPartId)?.texts ?? []}
          saving={savingDraft}
          onChange={setQuestionDraft}
          onClose={() => setQuestionDraft(null)}
          onSave={() => void saveQuestion(questionDraft)}
        />
      )}

      {manifestModal && (
        <ManifestJsonModal
          state={manifestModal}
          busy={manifestBusy}
          onChange={setManifestModal}
          onClose={() => setManifestModal(null)}
          onImport={() => void importManifest(manifestModal.value)}
        />
      )}

      {reviewHistory && (
        <ReviewHistoryModal
          state={reviewHistory}
          onClose={() => setReviewHistory(null)}
        />
      )}
    </AdminRoutePanel>
  );
}

function ManifestJsonModal({ state, busy, onChange, onClose, onImport }: {
  state: ManifestModalState;
  busy: boolean;
  onChange: (state: ManifestModalState) => void;
  onClose: () => void;
  onImport: () => void;
}) {
  const isImport = state.mode === 'import';

  return (
    <Modal open={true} onClose={onClose} title={isImport ? 'Import Reading JSON' : 'Export Reading JSON'} size="lg">
      <div className="space-y-4">
        {isImport ? (
          <InlineAlert variant="warning">
            Import replaces this paper&apos;s Reading texts and questions, then runs the publish validator.
          </InlineAlert>
        ) : null}
        <textarea
          className="min-h-[360px] w-full rounded-lg border border-border bg-background-light p-3 font-mono text-xs text-navy"
          value={state.value}
          readOnly={!isImport}
          spellCheck={false}
          onChange={(event) => onChange({ ...state, value: event.target.value })}
        />
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button variant="ghost" onClick={onClose}>Close</Button>
          {isImport ? (
            <Button variant="primary" onClick={onImport} loading={busy} disabled={!state.value.trim()}>
              Replace structure
            </Button>
          ) : null}
        </div>
      </div>
    </Modal>
  );
}

// ── Part block ──────────────────────────────────────────────────────────

function PartEditor({
  part,
  onAddText, onEditText, onRemoveText,
  onMoveText,
  onAddQuestion, onEditQuestion, onRemoveQuestion,
  onMoveQuestion, onAdvanceQuestion, onAdvancePart, onOpenReviewHistory,
}: {
  part: ReadingPartAdminDto;
  onAddText: () => void;
  onEditText: (t: ReadingTextDto) => void;
  onRemoveText: (id: string) => Promise<void>;
  onMoveText: (id: string, direction: -1 | 1) => void;
  onAddQuestion: () => void;
  onEditQuestion: (q: ReadingQuestionAdminDto) => void;
  onRemoveQuestion: (id: string) => Promise<void>;
  onMoveQuestion: (id: string, direction: -1 | 1) => void;
  onAdvanceQuestion: (q: ReadingQuestionAdminDto) => void;
  onAdvancePart: () => void;
  onOpenReviewHistory: (q: ReadingQuestionAdminDto) => void;
}) {
  const expected = PART_EXPECTED[part.partCode];
  const short = Math.max(0, expected.items - part.questions.length);
  const textShort = Math.max(0, expected.texts - part.texts.length);
  const unpublishedCount = part.questions.filter((q) => (q.reviewState ?? 'Draft') !== 'Published').length;

  return (
    <div className="bg-surface border border-border rounded-2xl p-5 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="font-black text-lg text-navy">{expected.label}</h3>
          <p className="text-xs text-muted">
            {part.timeLimitMinutes} min · {part.texts.length}/{expected.texts} text units ({expected.textLabel}) · {part.questions.length}/{expected.items} items
            {textShort > 0 && <span className="text-red-600"> · {textShort} more text unit(s) needed</span>}
            {short > 0 && <span className="text-red-600"> · {short} more needed</span>}
          </p>
        </div>
        <div className="flex flex-wrap items-end gap-2">
          {unpublishedCount > 0 ? (
            <Button variant="secondary" size="sm" onClick={onAdvancePart}>
              <ShieldCheck className="w-4 h-4" /> Publish {unpublishedCount}
            </Button>
          ) : <Badge variant="success">All questions published</Badge>}
          <div className="rounded-lg border border-border bg-background px-3 py-2 text-xs text-muted">
            <span className="block font-bold text-navy">Timer</span>
            {expected.minutes} min fixed{part.partCode === 'B' || part.partCode === 'C' ? ' · shared B+C block' : ''}
          </div>
        </div>
      </div>

      <section>
        <div className="flex items-center justify-between mb-2">
          <h4 className="font-bold text-sm text-navy">Texts ({part.texts.length})</h4>
          <Button variant="ghost" size="sm" onClick={onAddText}>
            <Plus className="w-4 h-4" /> Add text
          </Button>
        </div>
        {part.texts.length === 0
          ? <p className="text-sm text-muted">No texts yet.</p>
          : (
            <ul className="space-y-1">
              {[...part.texts].sort((a, b) => a.displayOrder - b.displayOrder).map((t, index, list) => (
                <li key={t.id} className="flex items-center justify-between py-1 text-sm">
                  <span className="flex items-center gap-2">
                    <Badge variant="info">{t.displayOrder}</Badge>
                    <FileText className="w-4 h-4 text-muted" />
                    <span className="font-medium">{t.title || '(untitled)'}</span>
                    <span className="text-muted">· {t.wordCount} words</span>
                    {!t.source && <Badge variant="warning">No source</Badge>}
                  </span>
                  <span className="flex gap-1">
                    <Button variant="ghost" size="sm" onClick={() => onMoveText(t.id, -1)} disabled={index === 0} aria-label={`Move ${t.title || 'text'} up`}>
                      <ArrowUp className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => onMoveText(t.id, 1)} disabled={index === list.length - 1} aria-label={`Move ${t.title || 'text'} down`}>
                      <ArrowDown className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => onEditText(t)}>Edit</Button>
                    <Button variant="ghost" size="sm" onClick={() => void onRemoveText(t.id)}>
                      <Trash2 className="w-4 h-4" />
                    </Button>
                  </span>
                </li>
              ))}
            </ul>
          )}
      </section>

      <section>
        <div className="flex items-center justify-between mb-2">
          <h4 className="font-bold text-sm text-navy">Questions ({part.questions.length}/{expected.items})</h4>
          <Button variant="ghost" size="sm" onClick={onAddQuestion}>
            <Plus className="w-4 h-4" /> Add question
          </Button>
        </div>
        {part.questions.length === 0
          ? <p className="text-sm text-muted">No questions yet.</p>
          : (
            <ul className="space-y-1">
              {[...part.questions].sort((a, b) => a.displayOrder - b.displayOrder).map((q, index, list) => (
                <li key={q.id} className="flex items-center justify-between py-1 text-sm">
                  <span className="flex flex-wrap items-center gap-2 min-w-0">
                    <Badge variant="info">{q.displayOrder}</Badge>
                    <Badge variant="muted">{QUESTION_TYPE_LABELS[q.questionType]}</Badge>
                    <Badge variant={q.reviewState === 'Published' ? 'success' : 'warning'}>{q.reviewState ?? 'Draft'}</Badge>
                    {q.skillTag ? <Badge variant="info">{q.skillTag}</Badge> : null}
                    <span className="truncate max-w-md">{q.stem}</span>
                  </span>
                  <span className="flex flex-wrap justify-end gap-1">
                    <Button variant="ghost" size="sm" onClick={() => onMoveQuestion(q.id, -1)} disabled={index === 0} aria-label={`Move question ${q.displayOrder} up`}>
                      <ArrowUp className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => onMoveQuestion(q.id, 1)} disabled={index === list.length - 1} aria-label={`Move question ${q.displayOrder} down`}>
                      <ArrowDown className="w-4 h-4" />
                    </Button>
                    <Button variant="ghost" size="sm" onClick={() => onOpenReviewHistory(q)}>
                      <History className="w-4 h-4" /> History
                    </Button>
                    {q.reviewState !== 'Published' && q.reviewState !== 'Retired' ? (
                      <Button variant="secondary" size="sm" onClick={() => onAdvanceQuestion(q)}>
                        <ShieldCheck className="w-4 h-4" /> Publish
                      </Button>
                    ) : null}
                    <Button variant="ghost" size="sm" onClick={() => onEditQuestion(q)}>Edit</Button>
                    <Button variant="ghost" size="sm" onClick={() => void onRemoveQuestion(q.id)}>
                      <Trash2 className="w-4 h-4" />
                    </Button>
                  </span>
                </li>
              ))}
            </ul>
          )}
      </section>
    </div>
  );
}

// ── Text modal ───────────────────────────────────────────────────────────

function TextEditorModal({ draft, saving, onChange, onClose, onSave }: {
  draft: TextDraft; saving: boolean;
  onChange: (d: TextDraft) => void; onClose: () => void; onSave: () => void;
}) {
  return (
    <Modal open={true} onClose={onClose} title={draft.id ? 'Edit text' : 'Add text'} size="lg">
      <div className="space-y-3">
        <Input label="Title" value={draft.title} onChange={(e) => onChange({ ...draft, title: e.target.value })} />
        <Input label="Source (required to publish)" value={draft.source ?? ''}
          onChange={(e) => onChange({ ...draft, source: e.target.value })} />
        <Input type="number" label="Display order" value={draft.displayOrder}
          onChange={(e) => onChange({ ...draft, displayOrder: Number(e.target.value) })} />
        <Input type="number" label="Word count" value={draft.wordCount}
          onChange={(e) => onChange({ ...draft, wordCount: Number(e.target.value) })} />
        <Input label="Topic tag (optional)" value={draft.topicTag ?? ''}
          onChange={(e) => onChange({ ...draft, topicTag: e.target.value || null })} />
        <div>
          <label className="block text-sm font-medium mb-1">Body HTML</label>
          <textarea
            className="w-full border rounded-lg p-2 font-mono text-sm min-h-[200px]"
            value={draft.bodyHtml}
            onChange={(e) => onChange({ ...draft, bodyHtml: e.target.value })}
          />
        </div>
        <div className="flex gap-2 justify-end">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" onClick={onSave} loading={saving} disabled={!draft.title.trim()}>Save</Button>
        </div>
      </div>
    </Modal>
  );
}

// ── Question modal ───────────────────────────────────────────────────────

function QuestionEditorModal({ draft, texts, saving, onChange, onClose, onSave }: {
  draft: QuestionDraft;
  texts: ReadingTextDto[];
  saving: boolean;
  onChange: (d: QuestionDraft) => void;
  onClose: () => void;
  onSave: () => void;
}) {
  const isMcq = draft.questionType === 'MultipleChoice3' || draft.questionType === 'MultipleChoice4';
  const isShort = draft.questionType === 'ShortAnswer' || draft.questionType === 'SentenceCompletion';
  const isMatching = draft.questionType === 'MatchingTextReference';

  const optionCount = draft.questionType === 'MultipleChoice3' ? 3 : 4;
  const correctLetter = parseJsonString(draft.correctAnswerJson, 'A');
  const distractors = useMemo(() => parseDistractorJson(draft.optionDistractorsJson), [draft.optionDistractorsJson]);
  const options = useMemo(() => {
    try { return JSON.parse(draft.optionsJson || '[]'); }
    catch { return []; }
  }, [draft.optionsJson]);

  return (
    <Modal open={true} onClose={onClose} title={draft.id ? 'Edit question' : 'Add question'} size="lg">
      <div className="space-y-3">
        <Select
          label="Question type"
          value={draft.questionType}
          onChange={(e) => onChange({ ...draft, questionType: e.target.value as ReadingQuestionType })}
          options={Object.entries(QUESTION_TYPE_LABELS).map(([v, l]) => ({ value: v, label: l }))}
        />
        <Input label="Stem" value={draft.stem}
          onChange={(e) => onChange({ ...draft, stem: e.target.value })} />
        <Input type="number" label="Display order" value={draft.displayOrder}
          onChange={(e) => onChange({ ...draft, displayOrder: Number(e.target.value) })} />
        <Input type="number" label="Points" value={draft.points}
          onChange={(e) => onChange({ ...draft, points: Math.max(1, Number(e.target.value)) })} />
        <Input label="Skill tag" value={draft.skillTag ?? ''}
          onChange={(e) => onChange({ ...draft, skillTag: e.target.value || null })}
          placeholder="part-a-scan | inference | vocabulary | reference" />

        {texts.length > 0 && (
          <Select
            label="Attached text (optional)"
            value={draft.readingTextId ?? ''}
            onChange={(e) => onChange({ ...draft, readingTextId: e.target.value || null })}
            options={[{ value: '', label: '(none)' }, ...texts.map(t => ({ value: t.id, label: t.title }))]}
          />
        )}

        {isMcq && (
          <div>
            <div className="text-sm font-medium mb-1">Options (exactly {optionCount})</div>
            {Array.from({ length: optionCount }).map((_, i) => {
              const letter = String.fromCharCode(65 + i);
              const val = options[i] ?? '';
              return (
                <div key={letter} className="flex items-center gap-2 mb-2">
                  <span className="w-6 font-mono">{letter}</span>
                  <input
                    className="flex-1 border rounded-lg p-2 text-sm"
                    value={val}
                    onChange={(e) => {
                      const next = [...options];
                      while (next.length < optionCount) next.push('');
                      next[i] = e.target.value;
                      onChange({ ...draft, optionsJson: JSON.stringify(next.slice(0, optionCount)) });
                    }}
                  />
                </div>
              );
            })}
            <Select
              label="Correct answer"
              value={correctLetter}
              onChange={(e) => onChange({ ...draft, correctAnswerJson: JSON.stringify(e.target.value) })}
              options={Array.from({ length: optionCount }).map((_, i) => {
                const l = String.fromCharCode(65 + i);
                return { value: l, label: l };
              })}
            />
            <div className="mt-3 space-y-2 rounded-lg border border-border bg-background-light p-3">
              <p className="text-xs font-bold uppercase text-muted">Distractor metadata</p>
              {Array.from({ length: optionCount }).map((_, i) => {
                const letter = String.fromCharCode(65 + i);
                if (letter === correctLetter) {
                  return <p key={letter} className="text-xs text-muted">{letter} is the correct answer; no distractor tag needed.</p>;
                }
                return (
                  <Select
                    key={letter}
                    label={`Option ${letter}`}
                    value={distractors[letter] ?? ''}
                    onChange={(event) => {
                      const next = { ...distractors };
                      const value = event.target.value as ReadingDistractorCategory | '';
                      if (value) next[letter] = value;
                      else delete next[letter];
                      onChange({ ...draft, optionDistractorsJson: Object.keys(next).length ? JSON.stringify(next) : null });
                    }}
                    options={[{ value: '', label: 'Not tagged' }, ...DISTRACTOR_CATEGORY_OPTIONS]}
                  />
                );
              })}
            </div>
          </div>
        )}

        {isShort && (
          <div className="space-y-2">
            <Input label="Correct answer" value={parseJsonString(draft.correctAnswerJson, '')}
              onChange={(e) => onChange({ ...draft, correctAnswerJson: JSON.stringify(e.target.value) })} />
            <Input label="Accepted synonyms (comma separated)"
              value={draft.acceptedSynonymsJson
                ? (JSON.parse(draft.acceptedSynonymsJson) as string[]).join(', ')
                : ''}
              onChange={(e) => {
                const list = e.target.value.split(',').map(s => s.trim()).filter(Boolean);
                onChange({ ...draft, acceptedSynonymsJson: list.length ? JSON.stringify(list) : null });
              }} />
            <label className="flex items-center gap-2 text-sm">
              <input type="checkbox" checked={draft.caseSensitive}
                onChange={(e) => onChange({ ...draft, caseSensitive: e.target.checked })} />
              Case-sensitive match
            </label>
          </div>
        )}

        {isMatching && (
          <div>
            <Input label='Correct text IDs (JSON array, e.g. ["1","3"])' value={draft.correctAnswerJson}
              onChange={(e) => onChange({ ...draft, correctAnswerJson: e.target.value })} />
          </div>
        )}

        <div>
          <label className="block text-sm font-medium mb-1">Explanation (shown after submit)</label>
          <textarea
            className="w-full border rounded-lg p-2 text-sm min-h-[80px]"
            value={draft.explanationMarkdown ?? ''}
            onChange={(e) => onChange({ ...draft, explanationMarkdown: e.target.value || null })}
          />
        </div>

        <div className="flex gap-2 justify-end">
          <Button variant="ghost" onClick={onClose}>Cancel</Button>
          <Button variant="primary" onClick={onSave} loading={saving} disabled={!draft.stem.trim()}>Save</Button>
        </div>
      </div>
    </Modal>
  );
}

function defaultQuestionDraftFor(part: ReadingPartAdminDto): QuestionDraft {
  const nextOrder = part.questions.length + 1;
  const type: ReadingQuestionType =
    part.partCode === 'A' ? 'ShortAnswer'
      : part.partCode === 'B' ? 'MultipleChoice3'
      : 'MultipleChoice4';
  return {
    id: null,
    readingPartId: part.id,
    readingTextId: null,
    displayOrder: nextOrder,
    points: 1,
    questionType: type,
    stem: '',
    optionsJson: type === 'MultipleChoice3' ? '["","",""]'
      : type === 'MultipleChoice4' ? '["","","",""]' : '[]',
    correctAnswerJson: type.startsWith('MultipleChoice') ? '"A"' : '""',
    acceptedSynonymsJson: null,
    caseSensitive: false,
    explanationMarkdown: null,
    skillTag: null,
    optionDistractorsJson: null,
    reviewState: 'Draft',
    latestReviewNote: null,
  };
}

function ReviewHistoryModal({ state, onClose }: { state: ReviewHistoryState; onClose: () => void }) {
  return (
    <Modal open={true} onClose={onClose} title={`Review history: question ${state.question.displayOrder}`} size="lg">
      <div className="space-y-4">
        {state.entries.length === 0 ? (
          <p className="text-sm text-muted">No review transitions recorded yet.</p>
        ) : (
          <ul className="space-y-3">
            {state.entries.map((entry) => (
              <li key={entry.id} className="rounded-lg border border-border bg-background-light p-3 text-sm">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant="muted">{entry.fromState}</Badge>
                  <span className="text-muted">to</span>
                  <Badge variant={entry.toState === 'Published' ? 'success' : 'info'}>{entry.toState}</Badge>
                  <span className="text-xs text-muted">{new Date(entry.transitionedAt).toLocaleString()}</span>
                </div>
                <p className="mt-2 text-xs text-muted">Reviewer: {entry.reviewerDisplayName ?? entry.reviewerUserId}</p>
                {entry.note ? <p className="mt-1 text-sm text-navy">{entry.note}</p> : null}
              </li>
            ))}
          </ul>
        )}
        <div className="flex justify-end">
          <Button variant="primary" onClick={onClose}>Close</Button>
        </div>
      </div>
    </Modal>
  );
}

function parseJsonString(value: string, fallback: string): string {
  try {
    const parsed = JSON.parse(value || 'null');
    return typeof parsed === 'string' ? parsed : fallback;
  } catch {
    return fallback;
  }
}

function parseDistractorJson(value?: string | null): Partial<Record<string, ReadingDistractorCategory>> {
  if (!value) return {};
  try {
    const parsed = JSON.parse(value) as Record<string, unknown>;
    const result: Partial<Record<string, ReadingDistractorCategory>> = {};
    const allowed = new Set(DISTRACTOR_CATEGORY_OPTIONS.map((option) => option.value));
    for (const [rawKey, rawValue] of Object.entries(parsed)) {
      const key = rawKey.trim().toUpperCase();
      if (!/^[A-D]$/.test(key)) continue;
      if (typeof rawValue === 'string' && allowed.has(rawValue as ReadingDistractorCategory)) {
        result[key] = rawValue as ReadingDistractorCategory;
      }
    }
    return result;
  } catch {
    return {};
  }
}
