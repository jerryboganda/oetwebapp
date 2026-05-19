'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import {
  ArrowDown,
  ArrowLeft,
  ArrowUp,
  BookOpen,
  Plus,
  RotateCcw,
  Save,
  Trash2,
} from 'lucide-react';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { Toast } from '@/components/ui/alert';
import { AdminPermission, hasPermission } from '@/lib/admin-permissions';
import { useCurrentUser } from '@/lib/hooks/use-current-user';
import {
  adminReadingDeleteQuestion,
  adminReadingDeleteText,
  adminReadingEnsureCanonical,
  adminReadingGetStructure,
  adminReadingReorderQuestions,
  adminReadingReorderTexts,
  adminReadingSetDistractors,
  adminReadingUpsertPart,
  adminReadingUpsertQuestion,
  adminReadingUpsertText,
} from '@/lib/api';
import type {
  ReadingDistractorCategory,
  ReadingPartCode,
  ReadingPartView,
  ReadingQuestionDto,
  ReadingQuestionType,
  ReadingStructure,
  ReadingTextDto,
} from '@/lib/types/admin/reading-authoring';

type ToastState = { variant: 'success' | 'error'; message: string } | null;

function resolveParam(raw: string | string[] | undefined): string | null {
  if (!raw) return null;
  if (Array.isArray(raw)) return raw[0] ?? null;
  return raw;
}

const PART_META: Record<ReadingPartCode, {
  label: string;
  description: string;
  expected: number;
  questionType: ReadingQuestionType;
  optionLetters: string[];
  textsHint: string;
}> = {
  A: {
    label: 'Part A — 20 short-answer (blank fill)',
    description: 'Four short texts on the same topic. Learners scan-read and complete 20 blanks.',
    expected: 20,
    questionType: 'ShortAnswer',
    optionLetters: [],
    textsHint: 'Typically 4 texts (~150 words each).',
  },
  B: {
    label: 'Part B — 6 MCQ (3 options)',
    description: 'Six short workplace texts (notes, emails, policies) each with a single 3-option MCQ.',
    expected: 6,
    questionType: 'MultipleChoice3',
    optionLetters: ['A', 'B', 'C'],
    textsHint: 'One short text per question (6 texts).',
  },
  C: {
    label: 'Part C — 16 MCQ (4 options)',
    description: 'Two longer texts, 8 four-option MCQ questions each.',
    expected: 16,
    questionType: 'MultipleChoice4',
    optionLetters: ['A', 'B', 'C', 'D'],
    textsHint: 'Two long texts (~700–800 words), 8 questions per text.',
  },
};

const DISTRACTOR_CATEGORIES: ReadingDistractorCategory[] = [
  'Opposite',
  'TooBroad',
  'TooSpecific',
  'WrongSpeaker',
  'NotInText',
  'DistortedDetail',
  'OutOfScope',
];

function parseStringArray(json: string | null | undefined): string[] {
  if (!json) return [];
  try {
    const v = JSON.parse(json);
    return Array.isArray(v) ? v.map((x) => String(x)) : [];
  } catch { return []; }
}

function parseCorrectAnswer(q: ReadingQuestionDto): string {
  if (!q.correctAnswerJson) return '';
  try {
    const v = JSON.parse(q.correctAnswerJson);
    if (typeof v === 'string') return v;
    if (Array.isArray(v) && v.length > 0) return String(v[0]);
    return String(v ?? '');
  } catch { return q.correctAnswerJson; }
}

function parseDistractors(json: string | null | undefined): Record<string, ReadingDistractorCategory> {
  if (!json) return {};
  try {
    const v = JSON.parse(json);
    if (v && typeof v === 'object') {
      const out: Record<string, ReadingDistractorCategory> = {};
      for (const [k, val] of Object.entries(v)) {
        if (typeof val === 'string' && (DISTRACTOR_CATEGORIES as string[]).includes(val)) {
          out[k] = val as ReadingDistractorCategory;
        }
      }
      return out;
    }
    return {};
  } catch { return {}; }
}

export default function ReadingPartEditorPage() {
  const params = useParams();
  const paperId = resolveParam(params?.paperId as string | string[] | undefined);
  const partCodeRaw = resolveParam(params?.partCode as string | string[] | undefined);
  const partCode = (partCodeRaw?.toUpperCase() as ReadingPartCode | null);

  const { user } = useCurrentUser();
  const canWrite = hasPermission(user?.adminPermissions, AdminPermission.ContentWrite);

  const [structure, setStructure] = useState<ReadingStructure | null>(null);
  const [loading, setLoading] = useState(true);
  const [busy, setBusy] = useState(false);
  const [toast, setToast] = useState<ToastState>(null);

  const part: ReadingPartView | null = useMemo(() => {
    if (!structure || !partCode) return null;
    return structure.parts.find((p) => p.partCode === partCode) ?? null;
  }, [structure, partCode]);

  const isValidPart = partCode === 'A' || partCode === 'B' || partCode === 'C';
  const meta = isValidPart ? PART_META[partCode] : null;

  const load = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    try {
      const s = await adminReadingGetStructure(paperId);
      setStructure(s);
    } catch (err) {
      // If no canonical parts yet, ensure them.
      if (canWrite) {
        try {
          await adminReadingEnsureCanonical(paperId);
          const s = await adminReadingGetStructure(paperId);
          setStructure(s);
        } catch (e) {
          setToast({ variant: 'error', message: e instanceof Error ? e.message : 'Failed to load structure.' });
        }
      } else {
        setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Failed to load structure.' });
      }
    } finally {
      setLoading(false);
    }
  }, [paperId, canWrite]);

  useEffect(() => {
    queueMicrotask(() => void load());
  }, [load]);

  // ── Part-level edit (time limit + instructions) ────────────────────────
  const [timeLimit, setTimeLimit] = useState<string>('');
  const [instructions, setInstructions] = useState<string>('');
  useEffect(() => {
    if (part) {
      setTimeLimit(String(part.timeLimitMinutes ?? ''));
      setInstructions(part.instructions ?? '');
    }
  }, [part]);

  const handleSavePart = async () => {
    if (!paperId || !partCode || !canWrite) return;
    setBusy(true);
    try {
      const tl = timeLimit.trim() ? Number(timeLimit) : null;
      await adminReadingUpsertPart(paperId, partCode, {
        timeLimitMinutes: tl,
        instructions: instructions.trim() || null,
      });
      setToast({ variant: 'success', message: 'Part settings saved.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed.' });
    } finally {
      setBusy(false);
    }
  };

  // ── Text CRUD ──────────────────────────────────────────────────────────
  const handleAddText = async () => {
    if (!paperId || !part || !canWrite) return;
    setBusy(true);
    try {
      const nextOrder = (part.texts.length ?? 0) + 1;
      await adminReadingUpsertText(paperId, {
        readingPartId: part.id,
        displayOrder: nextOrder,
        title: `New text ${nextOrder}`,
        source: null,
        bodyHtml: '',
        wordCount: 0,
        topicTag: null,
      });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Add text failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleSaveText = async (t: ReadingTextDto, patch: Partial<ReadingTextDto>) => {
    if (!paperId || !canWrite) return;
    setBusy(true);
    try {
      await adminReadingUpsertText(paperId, {
        id: t.id,
        readingPartId: t.readingPartId,
        displayOrder: patch.displayOrder ?? t.displayOrder,
        title: patch.title ?? t.title,
        source: patch.source !== undefined ? patch.source : t.source,
        bodyHtml: patch.bodyHtml ?? t.bodyHtml,
        wordCount: patch.wordCount ?? t.wordCount,
        topicTag: patch.topicTag !== undefined ? patch.topicTag : t.topicTag,
      });
      setToast({ variant: 'success', message: 'Text saved.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save text failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleDeleteText = async (textId: string) => {
    if (!paperId || !canWrite) return;
    if (!confirm('Delete this text? Any questions linked to it will be unlinked.')) return;
    setBusy(true);
    try {
      await adminReadingDeleteText(paperId, textId);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Delete text failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleReorderTexts = async (direction: 'up' | 'down', textId: string) => {
    if (!paperId || !part || !canWrite) return;
    const ids = part.texts.slice().sort((a, b) => a.displayOrder - b.displayOrder).map((t) => t.id);
    const idx = ids.indexOf(textId);
    if (idx < 0) return;
    const target = direction === 'up' ? idx - 1 : idx + 1;
    if (target < 0 || target >= ids.length) return;
    [ids[idx], ids[target]] = [ids[target], ids[idx]];
    setBusy(true);
    try {
      await adminReadingReorderTexts(paperId, part.id, ids);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Reorder failed.' });
    } finally {
      setBusy(false);
    }
  };

  // ── Question CRUD ──────────────────────────────────────────────────────
  const handleAddQuestion = async () => {
    if (!paperId || !part || !meta || !canWrite) return;
    setBusy(true);
    try {
      const nextOrder = (part.questions.length ?? 0) + 1;
      const defaultText = part.texts[0]?.id ?? null;
      const options = meta.optionLetters.length > 0
        ? meta.optionLetters.map((l) => `Option ${l}`)
        : [];
      const correct = meta.optionLetters[0] ?? '';
      await adminReadingUpsertQuestion(paperId, {
        readingPartId: part.id,
        readingTextId: defaultText,
        displayOrder: nextOrder,
        points: 1,
        questionType: meta.questionType,
        stem: 'New question',
        optionsJson: JSON.stringify(options),
        correctAnswerJson: JSON.stringify(correct),
        acceptedSynonymsJson: null,
        caseSensitive: false,
        explanationMarkdown: null,
        skillTag: null,
      });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Add question failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleSaveQuestion = async (
    q: ReadingQuestionDto,
    patch: {
      stem?: string;
      options?: string[];
      correct?: string;
      acceptedSynonyms?: string[] | null;
      caseSensitive?: boolean;
      explanation?: string | null;
      skillTag?: string | null;
      readingTextId?: string | null;
      points?: number;
    },
  ) => {
    if (!paperId || !canWrite) return;
    setBusy(true);
    try {
      const optionsJson = patch.options ? JSON.stringify(patch.options) : q.optionsJson;
      const correctJson = patch.correct !== undefined
        ? JSON.stringify(patch.correct)
        : q.correctAnswerJson;
      const synJson = patch.acceptedSynonyms === undefined
        ? q.acceptedSynonymsJson
        : (patch.acceptedSynonyms === null ? null : JSON.stringify(patch.acceptedSynonyms));
      await adminReadingUpsertQuestion(paperId, {
        id: q.id,
        readingPartId: q.readingPartId,
        readingTextId: patch.readingTextId !== undefined ? patch.readingTextId : q.readingTextId,
        displayOrder: q.displayOrder,
        points: patch.points ?? q.points,
        questionType: q.questionType,
        stem: patch.stem ?? q.stem,
        optionsJson,
        correctAnswerJson: correctJson,
        acceptedSynonymsJson: synJson,
        caseSensitive: patch.caseSensitive ?? q.caseSensitive,
        explanationMarkdown: patch.explanation !== undefined ? patch.explanation : q.explanationMarkdown,
        skillTag: patch.skillTag !== undefined ? patch.skillTag : q.skillTag,
      });
      setToast({ variant: 'success', message: 'Question saved.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save question failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleDeleteQuestion = async (questionId: string) => {
    if (!paperId || !canWrite) return;
    if (!confirm('Delete this question?')) return;
    setBusy(true);
    try {
      await adminReadingDeleteQuestion(paperId, questionId);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Delete question failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleReorderQuestions = async (direction: 'up' | 'down', questionId: string) => {
    if (!paperId || !part || !canWrite) return;
    const ids = part.questions.slice().sort((a, b) => a.displayOrder - b.displayOrder).map((q) => q.id);
    const idx = ids.indexOf(questionId);
    if (idx < 0) return;
    const target = direction === 'up' ? idx - 1 : idx + 1;
    if (target < 0 || target >= ids.length) return;
    [ids[idx], ids[target]] = [ids[target], ids[idx]];
    setBusy(true);
    try {
      await adminReadingReorderQuestions(paperId, part.id, ids);
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Reorder failed.' });
    } finally {
      setBusy(false);
    }
  };

  const handleSaveDistractors = async (
    q: ReadingQuestionDto,
    distractors: Record<string, ReadingDistractorCategory>,
  ) => {
    if (!paperId || !canWrite) return;
    setBusy(true);
    try {
      await adminReadingSetDistractors(paperId, q.id, { distractors });
      setToast({ variant: 'success', message: 'Distractor analysis saved.' });
      await load();
    } catch (err) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save distractors failed.' });
    } finally {
      setBusy(false);
    }
  };

  if (!paperId || !isValidPart) {
    return (
      <AdminRouteWorkspace role="main">
        <Card className="p-8 text-center text-sm text-muted">Invalid paper or part code.</Card>
      </AdminRouteWorkspace>
    );
  }

  const sortedTexts = part ? [...part.texts].sort((a, b) => a.displayOrder - b.displayOrder) : [];
  const sortedQuestions = part ? [...part.questions].sort((a, b) => a.displayOrder - b.displayOrder) : [];

  return (
    <AdminRouteWorkspace role="main" aria-label={`Reading Part ${partCode} editor`}>
      <AdminRouteHero
        eyebrow={`Part ${partCode}`}
        icon={BookOpen}
        accent="navy"
        title={meta?.label ?? `Part ${partCode}`}
        description={meta?.description ?? ''}
        aside={(
          <Button variant="ghost" asChild>
            <Link href={`/admin/content/reading/${paperId}`}>
              <ArrowLeft className="mr-1 h-4 w-4" /> Back to workspace
            </Link>
          </Button>
        )}
      />

      {loading ? (
        <div className="space-y-2">
          {Array.from({ length: 4 }).map((_, i) => (<Skeleton key={i} className="h-24 rounded-xl" />))}
        </div>
      ) : !part ? (
        <Card className="p-6 text-sm">
          <div className="mb-2">Part {partCode} does not exist yet for this paper.</div>
          {canWrite && (
            <Button size="sm" onClick={() => void adminReadingEnsureCanonical(paperId).then(load)}>
              Ensure canonical Part A / B / C
            </Button>
          )}
        </Card>
      ) : (
        <>
          {/* Part settings */}
          <AdminRoutePanel>
            <div className="flex flex-wrap items-end gap-3">
              <div className="w-40">
                <Input
                  label="Time limit (minutes)"
                  type="number"
                  min={0}
                  value={timeLimit}
                  onChange={(e) => setTimeLimit(e.target.value)}
                />
              </div>
              <div className="flex-1 min-w-[280px]">
                <Input
                  label="Instructions (shown to learner)"
                  value={instructions}
                  onChange={(e) => setInstructions(e.target.value)}
                />
              </div>
              <Button size="sm" onClick={() => void handleSavePart()} disabled={!canWrite || busy}>
                <Save className="mr-1 h-4 w-4" /> Save part
              </Button>
              <Button variant="outline" size="sm" onClick={() => void load()} disabled={busy}>
                <RotateCcw className="mr-1 h-4 w-4" />
              </Button>
            </div>
            <div className="mt-2 text-xs text-muted">
              {meta?.textsHint} Target: {meta?.expected} questions. Currently:{' '}
              <Badge variant={part.questions.length === (meta?.expected ?? 0) ? 'success' : 'warning'}>
                {part.questions.length}/{meta?.expected}
              </Badge>
            </div>
          </AdminRoutePanel>

          {/* Texts */}
          <Card className="p-4">
            <div className="mb-3 flex items-center justify-between">
              <div className="text-sm font-medium">Texts ({sortedTexts.length})</div>
              {canWrite && (
                <Button size="sm" variant="outline" onClick={() => void handleAddText()} disabled={busy}>
                  <Plus className="mr-1 h-4 w-4" /> Add text
                </Button>
              )}
            </div>
            <div className="space-y-3">
              {sortedTexts.length === 0 && <div className="text-xs text-muted">No texts yet.</div>}
              {sortedTexts.map((t, i) => (
                <TextEditor
                  key={t.id}
                  text={t}
                  canWrite={canWrite}
                  busy={busy}
                  index={i}
                  total={sortedTexts.length}
                  onSave={(patch) => void handleSaveText(t, patch)}
                  onDelete={() => void handleDeleteText(t.id)}
                  onMoveUp={() => void handleReorderTexts('up', t.id)}
                  onMoveDown={() => void handleReorderTexts('down', t.id)}
                />
              ))}
            </div>
          </Card>

          {/* Questions */}
          <Card className="p-4">
            <div className="mb-3 flex items-center justify-between">
              <div className="text-sm font-medium">Questions ({sortedQuestions.length} / {meta?.expected})</div>
              {canWrite && (
                <Button size="sm" onClick={() => void handleAddQuestion()} disabled={busy}>
                  <Plus className="mr-1 h-4 w-4" /> Add question
                </Button>
              )}
            </div>
            <div className="space-y-3">
              {sortedQuestions.length === 0 && <div className="text-xs text-muted">No questions yet.</div>}
              {sortedQuestions.map((q, i) => (
                <QuestionEditor
                  key={q.id}
                  question={q}
                  texts={sortedTexts}
                  optionLetters={meta?.optionLetters ?? []}
                  partCode={partCode!}
                  canWrite={canWrite}
                  busy={busy}
                  index={i}
                  total={sortedQuestions.length}
                  onSave={(patch) => void handleSaveQuestion(q, patch)}
                  onDelete={() => void handleDeleteQuestion(q.id)}
                  onMoveUp={() => void handleReorderQuestions('up', q.id)}
                  onMoveDown={() => void handleReorderQuestions('down', q.id)}
                  onSaveDistractors={(d) => void handleSaveDistractors(q, d)}
                />
              ))}
            </div>
          </Card>
        </>
      )}

      {toast && (
        <Toast
          variant={toast.variant === 'error' ? 'error' : 'success'}
          message={toast.message}
          onClose={() => setToast(null)}
        />
      )}
    </AdminRouteWorkspace>
  );
}

// ── Sub-components ────────────────────────────────────────────────────────

interface TextEditorProps {
  text: ReadingTextDto;
  canWrite: boolean;
  busy: boolean;
  index: number;
  total: number;
  onSave: (patch: Partial<ReadingTextDto>) => void;
  onDelete: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
}

function TextEditor({ text, canWrite, busy, index, total, onSave, onDelete, onMoveUp, onMoveDown }: TextEditorProps) {
  const [title, setTitle] = useState(text.title);
  const [source, setSource] = useState(text.source ?? '');
  const [body, setBody] = useState(text.bodyHtml);
  const [wordCount, setWordCount] = useState(String(text.wordCount));
  const [topicTag, setTopicTag] = useState(text.topicTag ?? '');

  // Reset form state when the underlying text changes (parent reuses this editor across rows).
  /* eslint-disable react-hooks/set-state-in-effect -- intentional prop->state sync; cheaper than parent re-keying every row. */
  useEffect(() => {
    setTitle(text.title);
    setSource(text.source ?? '');
    setBody(text.bodyHtml);
    setWordCount(String(text.wordCount));
    setTopicTag(text.topicTag ?? '');
  }, [text]);
  /* eslint-enable react-hooks/set-state-in-effect */

  return (
    <div className="rounded-xl border border-border bg-background-light p-3">
      <div className="mb-2 flex items-center gap-2 text-xs text-muted">
        <span className="font-mono">#{text.displayOrder}</span>
        <span className="font-mono text-[10px]">{text.id}</span>
        <div className="ml-auto flex gap-1">
          {canWrite && (
            <>
              <Button variant="ghost" size="sm" onClick={onMoveUp} disabled={busy || index === 0} aria-label="Move up">
                <ArrowUp className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="sm" onClick={onMoveDown} disabled={busy || index === total - 1} aria-label="Move down">
                <ArrowDown className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="sm" onClick={onDelete} disabled={busy} aria-label="Delete text">
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            </>
          )}
        </div>
      </div>
      <div className="grid gap-2 sm:grid-cols-2">
        <Input label="Title" value={title} onChange={(e) => setTitle(e.target.value)} />
        <Input label="Source" value={source} onChange={(e) => setSource(e.target.value)} />
        <Input
          label="Word count"
          type="number"
          min={0}
          value={wordCount}
          onChange={(e) => setWordCount(e.target.value)}
        />
        <Input label="Topic tag" value={topicTag} onChange={(e) => setTopicTag(e.target.value)} />
      </div>
      <div className="mt-2">
        <Textarea
          label="Body (HTML)"
          value={body}
          onChange={(e) => setBody(e.target.value)}
          rows={6}
          spellCheck={false}
          className="font-mono text-xs"
        />
      </div>
      {canWrite && (
        <div className="mt-2 flex justify-end">
          <Button size="sm" onClick={() => onSave({
            title,
            source: source || null,
            bodyHtml: body,
            wordCount: Number(wordCount) || 0,
            topicTag: topicTag || null,
          })} disabled={busy}>
            <Save className="mr-1 h-4 w-4" /> Save text
          </Button>
        </div>
      )}
    </div>
  );
}

interface QuestionEditorProps {
  question: ReadingQuestionDto;
  texts: ReadingTextDto[];
  optionLetters: string[];
  partCode: ReadingPartCode;
  canWrite: boolean;
  busy: boolean;
  index: number;
  total: number;
  onSave: (patch: {
    stem?: string;
    options?: string[];
    correct?: string;
    acceptedSynonyms?: string[] | null;
    caseSensitive?: boolean;
    explanation?: string | null;
    skillTag?: string | null;
    readingTextId?: string | null;
    points?: number;
  }) => void;
  onDelete: () => void;
  onMoveUp: () => void;
  onMoveDown: () => void;
  onSaveDistractors: (d: Record<string, ReadingDistractorCategory>) => void;
}

function QuestionEditor({
  question, texts, optionLetters, canWrite, busy, index, total,
  onSave, onDelete, onMoveUp, onMoveDown, onSaveDistractors,
}: QuestionEditorProps) {
  const isMcq = optionLetters.length > 0;
  const [stem, setStem] = useState(question.stem);
  const [options, setOptions] = useState<string[]>(() => {
    const parsed = parseStringArray(question.optionsJson);
    if (isMcq) {
      const padded = [...parsed];
      while (padded.length < optionLetters.length) padded.push('');
      return padded.slice(0, optionLetters.length);
    }
    return parsed;
  });
  const [correct, setCorrect] = useState<string>(() => parseCorrectAnswer(question));
  const [synonyms, setSynonyms] = useState<string>(() => parseStringArray(question.acceptedSynonymsJson).join(', '));
  const [caseSensitive, setCaseSensitive] = useState(question.caseSensitive);
  const [explanation, setExplanation] = useState<string>(question.explanationMarkdown ?? '');
  const [skillTag, setSkillTag] = useState<string>(question.skillTag ?? '');
  const [readingTextId, setReadingTextId] = useState<string>(question.readingTextId ?? '');
  const [points, setPoints] = useState<string>(String(question.points));
  const [showDistractors, setShowDistractors] = useState(false);
  const [distractors, setDistractors] = useState<Record<string, ReadingDistractorCategory>>(
    () => parseDistractors(question.optionDistractorsJson),
  );

  // Reset question editor state when the question prop changes (parent reuses editor across rows).
  /* eslint-disable react-hooks/set-state-in-effect -- intentional prop->state sync; cheaper than parent re-keying every row. */
  useEffect(() => {
    setStem(question.stem);
    const parsed = parseStringArray(question.optionsJson);
    if (isMcq) {
      const padded = [...parsed];
      while (padded.length < optionLetters.length) padded.push('');
      setOptions(padded.slice(0, optionLetters.length));
    } else {
      setOptions(parsed);
    }
    setCorrect(parseCorrectAnswer(question));
    setSynonyms(parseStringArray(question.acceptedSynonymsJson).join(', '));
    setCaseSensitive(question.caseSensitive);
    setExplanation(question.explanationMarkdown ?? '');
    setSkillTag(question.skillTag ?? '');
    setReadingTextId(question.readingTextId ?? '');
    setPoints(String(question.points));
    setDistractors(parseDistractors(question.optionDistractorsJson));
  }, [question, isMcq, optionLetters.length]);
  /* eslint-enable react-hooks/set-state-in-effect */

  const updateOption = (idx: number, value: string) => {
    setOptions((prev) => {
      const next = [...prev];
      next[idx] = value;
      return next;
    });
  };

  const correctOptions = isMcq
    ? optionLetters.map((l) => ({ value: l, label: `${l} — ${options[optionLetters.indexOf(l)] ?? ''}`.slice(0, 60) }))
    : [];

  const wrongLetters = isMcq ? optionLetters.filter((l) => l !== correct) : [];

  return (
    <div className="rounded-xl border border-border bg-background-light p-3">
      <div className="mb-2 flex items-center gap-2 text-xs text-muted">
        <Badge variant="default">#{question.displayOrder}</Badge>
        <Badge variant="muted">{question.questionType}</Badge>
        <Badge variant="muted">{question.reviewState}</Badge>
        <span className="font-mono text-[10px]">{question.id}</span>
        <div className="ml-auto flex gap-1">
          {canWrite && (
            <>
              <Button variant="ghost" size="sm" onClick={onMoveUp} disabled={busy || index === 0} aria-label="Move up">
                <ArrowUp className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="sm" onClick={onMoveDown} disabled={busy || index === total - 1} aria-label="Move down">
                <ArrowDown className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="sm" onClick={onDelete} disabled={busy} aria-label="Delete question">
                <Trash2 className="h-3.5 w-3.5" />
              </Button>
            </>
          )}
        </div>
      </div>

      <Textarea label="Stem" value={stem} onChange={(e) => setStem(e.target.value)} rows={2} />

      <div className="mt-2 grid gap-2 sm:grid-cols-2">
        {texts.length > 0 && (
          <Select
            label="Linked text"
            value={readingTextId}
            onChange={(e) => setReadingTextId(e.target.value)}
            options={[{ value: '', label: '— none —' }, ...texts.map((t) => ({ value: t.id, label: `#${t.displayOrder} ${t.title}` }))]}
          />
        )}
        <Input label="Points" type="number" min={0} value={points} onChange={(e) => setPoints(e.target.value)} />
        <Input label="Skill tag" value={skillTag} onChange={(e) => setSkillTag(e.target.value)} />
      </div>

      {isMcq ? (
        <div className="mt-3 space-y-2">
          {optionLetters.map((letter, i) => (
            <div key={letter} className="flex items-center gap-2">
              <span className="w-6 text-center font-mono text-xs">{letter}</span>
              <Input
                value={options[i] ?? ''}
                onChange={(e) => updateOption(i, e.target.value)}
                placeholder={`Option ${letter}`}
                className="flex-1"
              />
            </div>
          ))}
          <Select
            label="Correct option"
            value={correct}
            onChange={(e) => setCorrect(e.target.value)}
            options={correctOptions.length > 0 ? correctOptions : [{ value: '', label: '— select —' }]}
          />
        </div>
      ) : (
        <div className="mt-3 grid gap-2 sm:grid-cols-2">
          <Input
            label="Correct answer"
            value={correct}
            onChange={(e) => setCorrect(e.target.value)}
            placeholder="Exact answer text"
          />
          <Input
            label="Accepted synonyms (comma-separated)"
            value={synonyms}
            onChange={(e) => setSynonyms(e.target.value)}
          />
          <label className="flex items-center gap-2 text-xs">
            <input
              type="checkbox"
              checked={caseSensitive}
              onChange={(e) => setCaseSensitive(e.target.checked)}
            />
            Case sensitive
          </label>
        </div>
      )}

      <div className="mt-2">
        <Textarea
          label="Explanation (markdown, hidden until learner submits)"
          value={explanation}
          onChange={(e) => setExplanation(e.target.value)}
          rows={3}
        />
      </div>

      {canWrite && (
        <div className="mt-2 flex flex-wrap justify-end gap-2">
          {isMcq && (
            <Button
              variant="outline"
              size="sm"
              onClick={() => setShowDistractors((s) => !s)}
              disabled={busy}
            >
              {showDistractors ? 'Hide' : 'Show'} distractor analysis
            </Button>
          )}
          <Button
            size="sm"
            onClick={() => onSave({
              stem,
              options: isMcq ? options : undefined,
              correct,
              acceptedSynonyms: isMcq
                ? null
                : (synonyms.trim() ? synonyms.split(',').map((s) => s.trim()).filter(Boolean) : null),
              caseSensitive: !isMcq ? caseSensitive : false,
              explanation: explanation || null,
              skillTag: skillTag || null,
              readingTextId: readingTextId || null,
              points: Number(points) || 1,
            })}
            disabled={busy}
          >
            <Save className="mr-1 h-4 w-4" /> Save question
          </Button>
        </div>
      )}

      {isMcq && showDistractors && (
        <div className="mt-3 rounded-lg border border-border bg-background p-3">
          <div className="mb-2 text-xs font-medium">Distractor analysis (wrong options only)</div>
          <div className="space-y-2">
            {wrongLetters.length === 0 ? (
              <div className="text-xs text-muted">Set the correct option first.</div>
            ) : wrongLetters.map((letter) => (
              <div key={letter} className="flex items-center gap-2">
                <span className="w-6 text-center font-mono text-xs">{letter}</span>
                <Select
                  value={distractors[letter] ?? ''}
                  onChange={(e) => setDistractors((prev) => {
                    const next = { ...prev };
                    if (!e.target.value) delete next[letter];
                    else next[letter] = e.target.value as ReadingDistractorCategory;
                    return next;
                  })}
                  options={[
                    { value: '', label: '— none —' },
                    ...DISTRACTOR_CATEGORIES.map((c) => ({ value: c, label: c })),
                  ]}
                  className="flex-1"
                />
              </div>
            ))}
          </div>
          {canWrite && wrongLetters.length > 0 && (
            <div className="mt-2 flex justify-end">
              <Button size="sm" onClick={() => onSaveDistractors(distractors)} disabled={busy}>
                <Save className="mr-1 h-4 w-4" /> Save distractors
              </Button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
