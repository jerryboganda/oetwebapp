'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { CheckCircle2, Download, FileText, Plus, RefreshCw, Trash2, AlertTriangle, Upload } from 'lucide-react';
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
  upsertReadingPart,
  upsertReadingQuestion,
  upsertReadingText,
  validateReadingPaper,
  type ReadingPartAdminDto,
  type ReadingPartCode,
  type ReadingQuestionAdminDto,
  type ReadingStructureManifestDto,
  type ReadingQuestionType,
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

const PART_EXPECTED: Record<ReadingPartCode, { items: number; minutes: number; label: string }> = {
  A: { items: 20, minutes: 15, label: 'Part A (matching / short-answer)' },
  B: { items: 6, minutes: 45, label: 'Part B (3-option MCQ)' },
  C: { items: 16, minutes: 45, label: 'Part C (4-option MCQ)' },
};

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
      await upsertReadingQuestion(paperId, {
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
            onEditPartSettings={async (mins, instr) => {
              await upsertReadingPart(paperId, part.partCode, { timeLimitMinutes: mins, instructions: instr });
              await load();
            }}
            onAddText={() => setTextDraft({
              id: null, readingPartId: part.id, displayOrder: part.texts.length + 1,
              title: '', source: '', bodyHtml: '', wordCount: 0, topicTag: null,
            })}
            onEditText={(t) => setTextDraft({ ...t })}
            onRemoveText={async (id) => { await removeReadingText(paperId, id); await load(); }}
            onAddQuestion={() => setQuestionDraft(defaultQuestionDraftFor(part))}
            onEditQuestion={(q) => setQuestionDraft({ ...q })}
            onRemoveQuestion={async (id) => { await removeReadingQuestion(paperId, id); await load(); }}
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
  part, onEditPartSettings,
  onAddText, onEditText, onRemoveText,
  onAddQuestion, onEditQuestion, onRemoveQuestion,
}: {
  part: ReadingPartAdminDto;
  onEditPartSettings: (mins: number | null, instr: string | null) => Promise<void>;
  onAddText: () => void;
  onEditText: (t: ReadingTextDto) => void;
  onRemoveText: (id: string) => Promise<void>;
  onAddQuestion: () => void;
  onEditQuestion: (q: ReadingQuestionAdminDto) => void;
  onRemoveQuestion: (id: string) => Promise<void>;
}) {
  const expected = PART_EXPECTED[part.partCode];
  const short = Math.max(0, expected.items - part.questions.length);

  return (
    <div className="bg-surface border border-border rounded-2xl p-5 space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h3 className="font-black text-lg text-navy">{expected.label}</h3>
          <p className="text-xs text-muted">
            {part.timeLimitMinutes} min · {part.questions.length}/{expected.items} items
            {short > 0 && <span className="text-red-600"> · {short} more needed</span>}
          </p>
        </div>
        <Input
          type="number"
          label="Time limit (min)"
          value={part.timeLimitMinutes}
          onChange={(e) => void onEditPartSettings(Number(e.target.value) || expected.minutes, part.instructions)}
        />
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
              {part.texts.map((t) => (
                <li key={t.id} className="flex items-center justify-between py-1 text-sm">
                  <span className="flex items-center gap-2">
                    <FileText className="w-4 h-4 text-muted" />
                    <span className="font-medium">{t.title || '(untitled)'}</span>
                    <span className="text-muted">· {t.wordCount} words</span>
                    {!t.source && <Badge variant="warning">No source</Badge>}
                  </span>
                  <span className="flex gap-1">
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
              {part.questions.map((q) => (
                <li key={q.id} className="flex items-center justify-between py-1 text-sm">
                  <span className="flex items-center gap-2 min-w-0">
                    <Badge variant="info">{q.displayOrder}</Badge>
                    <Badge variant="muted">{QUESTION_TYPE_LABELS[q.questionType]}</Badge>
                    <span className="truncate max-w-md">{q.stem}</span>
                  </span>
                  <span className="flex gap-1">
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
              value={JSON.parse(draft.correctAnswerJson || '""') as string}
              onChange={(e) => onChange({ ...draft, correctAnswerJson: JSON.stringify(e.target.value) })}
              options={Array.from({ length: optionCount }).map((_, i) => {
                const l = String.fromCharCode(65 + i);
                return { value: l, label: l };
              })}
            />
          </div>
        )}

        {isShort && (
          <div className="space-y-2">
            <Input label="Correct answer" value={JSON.parse(draft.correctAnswerJson || '""')}
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
  };
}
