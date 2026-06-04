'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, Plus, Trash2, Save, Code, FormInput, ArrowUp, ArrowDown, ClipboardCheck, ShieldCheck } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { Input, Textarea, Select, Checkbox } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import { ReadingPartTabs } from '@/components/domain/admin/reading/ReadingPartTabs';
import {
  getReadingStructureAdmin,
  upsertReadingQuestion,
  removeReadingQuestion,
  reorderReadingQuestions,
  type ReadingPartCode,
  type ReadingQuestionType,
  type ReadingQuestionAdminDto,
  type ReadingPartAdminDto,
  type ReadingSectionAdminDto,
  type ReadingTextDto,
  type ReadingReviewState,
} from '@/lib/reading-authoring-api';
import { AcceptedVariantManager } from './AcceptedVariantManager';
import { LabeledBoxesManager, type LabeledBox, serializeLabeledBoxes, parseLabeledBoxes } from './LabeledBoxesManager';
import { ReadingReviewPanel } from './ReadingReviewPanel';
import { REVIEW_STATE_LABELS, reviewStateTone } from './review-state';
import {
  parseAcceptedVariants,
  serializeAcceptedVariants,
  type AcceptedVariant,
} from './accepted-variants';

// ── Constants ──────────────────────────────────────────────────────────

const ALLOWED_TYPES: Record<ReadingPartCode, ReadingQuestionType[]> = {
  A: ['MatchingTextReference', 'ShortAnswer', 'SentenceCompletion'],
  B: ['MultipleChoice3', 'MultipleChoice4', 'FillInBlank', 'ShortAnswer', 'SentenceCompletion', 'ShortAnswerLabeled', 'MultipleChoiceFlexible'],
  C: ['MultipleChoice4', 'FillInBlank', 'ShortAnswer', 'SentenceCompletion', 'ShortAnswerLabeled', 'MultipleChoiceFlexible'],
};

const TYPE_LABELS: Record<ReadingQuestionType, string> = {
  MatchingTextReference: 'Matching Text Reference',
  ShortAnswer: 'Short Answer',
  SentenceCompletion: 'Sentence Completion',
  MultipleChoice3: 'Multiple Choice (3)',
  MultipleChoice4: 'Multiple Choice (4)',
  FillInBlank: 'Fill in the Blank',
  ShortAnswerLabeled: 'Short Answer (Labeled)',
  MultipleChoiceFlexible: 'Multiple Choice (Flexible)',
};

const TEXT_REFERENCE_LABELS = ['A', 'B', 'C', 'D'];

const MCQ_LABELS = ['A', 'B', 'C', 'D', 'E', 'F'];

function normaliseMatchingTextAnswer(answer: string, questionType: ReadingQuestionType): string {
  if (questionType !== 'MatchingTextReference') return answer;
  const trimmed = answer.trim();
  if (/^[1-4]$/.test(trimmed)) {
    return TEXT_REFERENCE_LABELS[Number(trimmed) - 1] ?? trimmed;
  }
  return trimmed.toUpperCase();
}

// ── Form state interfaces ──────────────────────────────────────────────

interface QuestionFormState {
  id: string | null;
  readingPartId: string;
  readingSectionId: string | null;
  readingTextId: string | null;
  displayOrder: number;
  points: number;
  questionType: ReadingQuestionType;
  stem: string;
  caseSensitive: boolean;
  correctAnswer: string;
  acceptedVariants: AcceptedVariant[];
  options: string[];
  /** Boxes for ShortAnswerLabeled questions. */
  labeledBoxes: LabeledBox[];
  explanationMarkdown: string;
  evidenceSentence: string;
}

type EditorMode = 'form' | 'json';

function emptyFormState(partId: string, partCode: ReadingPartCode, nextOrder: number, sectionId: string | null = null): QuestionFormState {
  const defaultType = ALLOWED_TYPES[partCode][0];
  const optionCount = defaultType === 'MultipleChoice4' ? 4 : defaultType === 'MultipleChoice3' ? 3 : 0;
  return {
    id: null,
    readingPartId: partId,
    readingSectionId: sectionId,
    readingTextId: null,
    displayOrder: nextOrder,
    points: 1,
    questionType: defaultType,
    stem: '',
    caseSensitive: false,
    correctAnswer: '',
    acceptedVariants: [],
    options: Array(optionCount).fill(''),
    labeledBoxes: [],
    explanationMarkdown: '',
    evidenceSentence: '',
  };
}

function questionToFormState(q: ReadingQuestionAdminDto): QuestionFormState {
  let options: string[] = [];
  let correctAnswer = '';

  try {
    const parsed = JSON.parse(q.optionsJson);
    if (Array.isArray(parsed)) options = parsed;
  } catch { /* empty */ }

  try {
    const parsed = JSON.parse(q.correctAnswerJson);
    correctAnswer = typeof parsed === 'string' ? parsed : q.correctAnswerJson ?? '';
  } catch {
    correctAnswer = q.correctAnswerJson ?? '';
  }
  correctAnswer = normaliseMatchingTextAnswer(correctAnswer, q.questionType);

  const labeledBoxes = q.questionType === 'ShortAnswerLabeled'
    ? parseLabeledBoxes(q.optionsJson, q.correctAnswerJson, q.acceptedSynonymsJson, q.boxExplanationsJson)
    : [];

  return {
    id: q.id,
    readingPartId: q.readingPartId,
    readingSectionId: q.readingSectionId,
    readingTextId: q.readingTextId,
    displayOrder: q.displayOrder,
    points: q.points,
    questionType: q.questionType,
    stem: q.stem,
    caseSensitive: q.caseSensitive,
    correctAnswer,
    acceptedVariants: parseAcceptedVariants(q.acceptedSynonymsJson),
    options,
    labeledBoxes,
    explanationMarkdown: q.explanationMarkdown ?? '',
    evidenceSentence: q.evidenceSentence ?? '',
  };
}

function questionToJson(q: ReadingQuestionAdminDto): string {
  return JSON.stringify(
    {
      questionType: q.questionType,
      stem: q.stem,
      optionsJson: q.optionsJson,
      correctAnswerJson: q.correctAnswerJson,
      acceptedSynonymsJson: q.acceptedSynonymsJson,
      caseSensitive: q.caseSensitive,
      explanationMarkdown: q.explanationMarkdown,
    },
    null,
    2,
  );
}

// ── Page Component ─────────────────────────────────────────────────────

export default function ReadingQuestionsEditorPage() {
  const params = useParams<{ paperId: string }>();
  const paperId = params?.paperId ?? '';

  const [parts, setParts] = useState<ReadingPartAdminDto[]>([]);
  const [activeTab, setActiveTab] = useState<ReadingPartCode>('A');
  const [activeSectionId, setActiveSectionId] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  // Editor state
  const [editing, setEditing] = useState(false);
  const [editorMode, setEditorMode] = useState<EditorMode>('form');
  const [form, setForm] = useState<QuestionFormState | null>(null);
  const [jsonText, setJsonText] = useState('');
  const [saving, setSaving] = useState(false);

  // Review-state workflow
  const [reviewing, setReviewing] = useState<ReadingQuestionAdminDto | null>(null);

  const activePart = parts.find((p) => p.partCode === activeTab) ?? null;
  const activeSections: ReadingSectionAdminDto[] = activePart?.sections ?? [];
  const activeSection = activeSections.find((s) => s.id === activeSectionId) ?? activeSections[0] ?? null;

  const fetchData = useCallback(async () => {
    if (!paperId) return;
    setLoading(true);
    setError(null);
    try {
      const data = await getReadingStructureAdmin(paperId);
      setParts(data.parts);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to load structure');
    } finally {
      setLoading(false);
    }
  }, [paperId]);

  useEffect(() => {
    queueMicrotask(() => {
      void fetchData();
    });
  }, [fetchData]);

  const counts: { A: number; B: number; C: number } = {
    A: parts.find((p) => p.partCode === 'A')?.questions.length ?? 0,
    B: parts.find((p) => p.partCode === 'B')?.questions.length ?? 0,
    C: parts.find((p) => p.partCode === 'C')?.questions.length ?? 0,
  };

  // Paper-level publish readiness: every authored question must reach Published.
  const allQuestions = parts.flatMap((p) => p.questions);
  const totalQuestions = allQuestions.length;
  const publishedCount = allQuestions.filter((q) => q.reviewState === 'Published').length;
  const allPublished = totalQuestions > 0 && publishedCount === totalQuestions;

  // ── Handlers ─────────────────────────────────────────────────────────

  function startAdd() {
    if (!activePart) return;
    const currentSectionId = activeSections.length > 0 ? (activeSection?.id ?? null) : null;
    const questionsInScope = currentSectionId
      ? (activeSection?.questions.length ?? 0)
      : activePart.questions.length;
    const nextOrder = questionsInScope + 1;
    const newForm = emptyFormState(activePart.id, activeTab, nextOrder, currentSectionId);
    setForm(newForm);
    setJsonText(JSON.stringify(
      {
        questionType: newForm.questionType,
        stem: '',
        optionsJson: '[]',
        correctAnswerJson: '""',
        acceptedSynonymsJson: null,
        caseSensitive: false,
        explanationMarkdown: null,
      },
      null,
      2,
    ));
    setEditorMode('form');
    setEditing(true);
  }

  function startEdit(q: ReadingQuestionAdminDto) {
    setReviewing(null);
    setForm(questionToFormState(q));
    setJsonText(questionToJson(q));
    setEditorMode('form');
    setEditing(true);
  }

  function cancelEdit() {
    setEditing(false);
    setForm(null);
    setJsonText('');
  }

  function startReview(q: ReadingQuestionAdminDto) {
    cancelEdit();
    setReviewing(q);
  }

  function addOption() {
    if (!form || form.questionType !== 'MultipleChoiceFlexible' || form.options.length >= 6) return;
    setForm({ ...form, options: [...form.options, ''] });
  }

  function removeOption(idx: number) {
    if (!form || form.questionType !== 'MultipleChoiceFlexible' || form.options.length <= 2) return;
    const surviving = form.options.filter((_, i) => i !== idx);
    const correctIdx = MCQ_LABELS.indexOf(form.correctAnswer);
    const correctAfterRemove = correctIdx < 0 || correctIdx === idx
      ? ''
      : correctIdx < idx
        ? form.correctAnswer
        : MCQ_LABELS[correctIdx - 1];
    setForm({ ...form, options: surviving, correctAnswer: correctAfterRemove });
  }

  function handleTypeChange(newType: ReadingQuestionType) {
    if (!form) return;
    if (newType === 'ShortAnswerLabeled') {
      setForm({ ...form, questionType: newType, options: [], correctAnswer: '', labeledBoxes: [] });
      return;
    }
    const optionCount = newType === 'MultipleChoiceFlexible'
      ? 4
      : newType === 'MultipleChoice4'
        ? 4
        : newType === 'MultipleChoice3'
          ? 3
          : 0;
    setForm({
      ...form,
      questionType: newType,
      options: Array(optionCount).fill('').map((_, i) => form.options[i] ?? ''),
      correctAnswer: '',
      labeledBoxes: [],
    });
  }

  function buildPayload(f: QuestionFormState) {
    const base = {
      id: f.id,
      readingPartId: f.readingPartId,
      readingSectionId: f.readingSectionId || null,
      readingTextId: f.readingTextId || null,
      displayOrder: f.displayOrder,
      points: f.points,
      questionType: f.questionType,
      stem: f.stem,
      caseSensitive: f.caseSensitive,
      explanationMarkdown: f.explanationMarkdown || null,
      evidenceSentence: f.evidenceSentence.trim() || null,
      skillTag: null,
      difficulty: null,
      paragraphIndex: null,
    };

    if (f.questionType === 'ShortAnswerLabeled') {
      const { optionsJson, correctAnswerJson, acceptedSynonymsJson, boxExplanationsJson } = serializeLabeledBoxes(f.labeledBoxes);
      return { ...base, optionsJson, correctAnswerJson, acceptedSynonymsJson, boxExplanationsJson, distractorRationale: null };
    }

    const isMultiChoice = f.questionType === 'MultipleChoice3'
      || f.questionType === 'MultipleChoice4'
      || f.questionType === 'MultipleChoiceFlexible';
    const optionsJson = isMultiChoice ? JSON.stringify(f.options) : '[]';
    const correctAnswerJson = JSON.stringify(f.correctAnswer);
    const acceptedSynonymsJson = serializeAcceptedVariants(f.acceptedVariants);
    return { ...base, optionsJson, correctAnswerJson, acceptedSynonymsJson, distractorRationale: null, boxExplanationsJson: null };
  }

  async function handleSaveForm() {
    if (!form) return;
    // Client-side validation
    if (!form.stem.trim()) {
      setToast({ variant: 'error', message: 'Question stem is required.' });
      return;
    }
    if (form.questionType === 'ShortAnswerLabeled') {
      if (form.labeledBoxes.length === 0) {
        setToast({ variant: 'error', message: 'Add at least one answer box.' });
        return;
      }
      if (form.labeledBoxes.some((b) => !b.title.trim() || !b.answer.trim())) {
        setToast({ variant: 'error', message: 'Each box requires a title and correct answer.' });
        return;
      }
    }
    const isMulti = form.questionType === 'MultipleChoice3'
      || form.questionType === 'MultipleChoice4'
      || form.questionType === 'MultipleChoiceFlexible';
    if (isMulti && form.options.some((o) => !o.trim())) {
      setToast({ variant: 'error', message: 'All options must have text.' });
      return;
    }
    if (isMulti && !form.correctAnswer) {
      setToast({ variant: 'error', message: 'Select the correct answer.' });
      return;
    }
    const isShortAnswer = form.questionType === 'ShortAnswer'
      || form.questionType === 'SentenceCompletion'
      || form.questionType === 'FillInBlank';
    if (isShortAnswer && !form.correctAnswer.trim()) {
      setToast({ variant: 'error', message: 'Correct answer is required.' });
      return;
    }
    setSaving(true);
    try {
      const payload = buildPayload(form);
      await upsertReadingQuestion(paperId, payload);
      setToast({ variant: 'success', message: form.id ? 'Question updated' : 'Question created' });
      cancelEdit();
      await fetchData();
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Save failed' });
    } finally {
      setSaving(false);
    }
  }

  async function handleSaveJson() {
    if (!activePart) return;
    setSaving(true);
    try {
      const parsed = JSON.parse(jsonText);
      const payload = {
        id: form?.id ?? null,
        readingPartId: form?.readingPartId ?? activePart.id,
        readingSectionId: form?.readingSectionId ?? null,
        readingTextId: parsed.readingTextId ?? form?.readingTextId ?? null,
        displayOrder: form?.displayOrder ?? (activePart.questions.length + 1),
        points: parsed.points ?? form?.points ?? 1,
        questionType: parsed.questionType as ReadingQuestionType,
        stem: parsed.stem as string,
        optionsJson: typeof parsed.optionsJson === 'string' ? parsed.optionsJson : JSON.stringify(parsed.optionsJson ?? []),
        correctAnswerJson: typeof parsed.correctAnswerJson === 'string' ? parsed.correctAnswerJson : JSON.stringify(parsed.correctAnswerJson ?? ''),
        acceptedSynonymsJson: parsed.acceptedSynonymsJson ?? null,
        caseSensitive: parsed.caseSensitive ?? false,
        explanationMarkdown: parsed.explanationMarkdown ?? null,
        skillTag: null,
        difficulty: null,
        paragraphIndex: null,
        distractorRationale: null,
      };
      await upsertReadingQuestion(paperId, payload);
      setToast({ variant: 'success', message: form?.id ? 'Question updated (JSON)' : 'Question created (JSON)' });
      cancelEdit();
      await fetchData();
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Invalid JSON or save failed' });
    } finally {
      setSaving(false);
    }
  }

  async function handleDelete(questionId: string) {
    if (!confirm('Delete this question? This cannot be undone.')) return;
    try {
      await removeReadingQuestion(paperId, questionId);
      setToast({ variant: 'success', message: 'Question deleted' });
      await fetchData();
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Delete failed' });
    }
  }

  async function handleQuestionMoveUp(index: number) {
    if (!activePart || index <= 0) return;
    const questions = [...activePart.questions];
    [questions[index - 1], questions[index]] = [questions[index], questions[index - 1]];
    const orderedIds = questions.map((q) => q.id);
    try {
      await reorderReadingQuestions(paperId, activePart.id, orderedIds);
      await fetchData();
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Reorder failed' });
    }
  }

  async function handleQuestionMoveDown(index: number) {
    if (!activePart) return;
    const questions = [...activePart.questions];
    if (index >= questions.length - 1) return;
    [questions[index], questions[index + 1]] = [questions[index + 1], questions[index]];
    const orderedIds = questions.map((q) => q.id);
    try {
      await reorderReadingQuestions(paperId, activePart.id, orderedIds);
      await fetchData();
    } catch (err: unknown) {
      setToast({ variant: 'error', message: err instanceof Error ? err.message : 'Reorder failed' });
    }
  }

  // ── Render Helpers ───────────────────────────────────────────────────

  function renderQuestionList() {
    if (!activePart) return <p className="text-admin-fg-muted text-sm">No part data found.</p>;
    const questions = activeSection ? activeSection.questions : activePart.questions;

    if (questions.length === 0) {
      return (
        <div className="text-center py-8">
          <p className="text-admin-fg-muted text-sm mb-3">No questions yet for Part {activeTab}.</p>
          <Button variant="primary" size="sm" onClick={startAdd}>
            <Plus className="h-4 w-4 mr-1" />
            Add First Question
          </Button>
        </div>
      );
    }

    return (
      <div className="space-y-2">
        {questions.map((q, idx) => (
          <div
            key={q.id}
            className="flex items-center gap-3 rounded-lg border border-admin-border bg-admin-bg-subtle px-3 py-2"
          >
            <div className="flex flex-col items-center gap-0.5 text-admin-fg-muted shrink-0">
              <button
                type="button"
                onClick={() => handleQuestionMoveUp(idx)}
                disabled={idx === 0}
                className="p-0.5 hover:text-primary disabled:opacity-30 disabled:cursor-not-allowed"
                aria-label="Move up"
              >
                <ArrowUp className="h-3 w-3" />
              </button>
              <button
                type="button"
                onClick={() => handleQuestionMoveDown(idx)}
                disabled={idx === questions.length - 1}
                className="p-0.5 hover:text-primary disabled:opacity-30 disabled:cursor-not-allowed"
                aria-label="Move down"
              >
                <ArrowDown className="h-3 w-3" />
              </button>
            </div>
            <span className="text-xs font-mono text-admin-fg-muted w-6 text-center shrink-0">
              {q.displayOrder}
            </span>
            <div className="flex-1 min-w-0">
              <p className="text-sm text-admin-fg-strong truncate">{q.stem}</p>
            </div>
            <Badge variant="primary" className="shrink-0 text-xs">
              {TYPE_LABELS[q.questionType] ?? q.questionType}
            </Badge>
            <Badge variant={reviewStateTone(q.reviewState)} size="sm" className="shrink-0">
              {REVIEW_STATE_LABELS[q.reviewState ?? 'Draft']}
            </Badge>
            <span className="text-xs text-admin-fg-muted shrink-0">{q.points}pt</span>
            <div className="flex gap-1 shrink-0">
              <Button variant="ghost" size="sm" onClick={() => startReview(q)} aria-label="Review state">
                <ClipboardCheck className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="sm" onClick={() => startEdit(q)} aria-label="Edit question">
                <FormInput className="h-3.5 w-3.5" />
              </Button>
              <Button variant="ghost" size="sm" onClick={() => handleDelete(q.id)} aria-label="Delete question">
                <Trash2 className="h-3.5 w-3.5 text-red-400" />
              </Button>
            </div>
          </div>
        ))}
        <div className="pt-2">
          <Button variant="primary" size="sm" onClick={startAdd}>
            <Plus className="h-4 w-4 mr-1" />
            Add Question
          </Button>
        </div>
      </div>
    );
  }

  function renderFormEditor() {
    if (!form || !activePart) return null;
    const allowedTypes = ALLOWED_TYPES[activeTab];
    const isMultiChoice = form.questionType === 'MultipleChoice3'
      || form.questionType === 'MultipleChoice4'
      || form.questionType === 'MultipleChoiceFlexible';
    const isShortOrSentence = form.questionType === 'ShortAnswer'
      || form.questionType === 'SentenceCompletion'
      || form.questionType === 'FillInBlank';
    const isMatching = form.questionType === 'MatchingTextReference';
    const optionLabels = MCQ_LABELS.slice(0, form.options.length || (form.questionType === 'MultipleChoice3' ? 3 : 4));
    const matchingTextOptions = activePart.texts
      .slice()
      .sort((a, b) => a.displayOrder - b.displayOrder)
      .map((txt: ReadingTextDto, index) => {
        const label = TEXT_REFERENCE_LABELS[index] ?? String(index + 1);
        return {
          value: label,
          label: `Text ${label}: ${txt.title}`,
        };
      });

    return (
      <div className="space-y-4">
        {/* Question Type */}
        <div>
          <Select
            label="Question Type"
            value={form.questionType}
            onChange={(e) => handleTypeChange(e.target.value as ReadingQuestionType)}
            options={allowedTypes.map((t) => ({ value: t, label: TYPE_LABELS[t] }))}
          />
        </div>

        {/* Associated Text */}
        <div>
          <Select
            label="Associated Text"
            value={form.readingTextId ?? ''}
            onChange={(e) => setForm({ ...form, readingTextId: e.target.value || null })}
            options={[
              { value: '', label: 'None' },
              ...activePart.texts.map((txt: ReadingTextDto) => ({
                value: txt.id,
                label: `${txt.displayOrder}. ${txt.title}`,
              })),
            ]}
          />
        </div>

        {/* Stem */}
        <div>
          <Textarea
            label="Stem (Question Text)"
            value={form.stem}
            onChange={(e) => setForm({ ...form, stem: e.target.value })}
            rows={3}
            placeholder="Enter the question text..."
          />
        </div>

        {/* Points */}
        <div className="flex gap-4 items-end">
          <div className="w-24">
            <Input
              label="Points"
              type="number"
              min={1}
              value={form.points}
              onChange={(e) => setForm({ ...form, points: Math.max(1, parseInt(e.target.value) || 1) })}
            />
          </div>
          <Checkbox
            label="Case Sensitive"
            checked={form.caseSensitive}
            onChange={(e) => setForm({ ...form, caseSensitive: e.target.checked })}
          />
        </div>

        {/* Multiple Choice Options */}
        {isMultiChoice && (
          <div className="space-y-2">
            <p className="text-xs font-medium text-admin-fg-muted uppercase tracking-wide">Options</p>
            {form.options.map((opt, idx) => {
              const key = optionLabels[idx];
              return (
                <div key={idx} className="space-y-2 rounded-lg border border-admin-border bg-admin-bg-subtle/40 p-3">
                  {form.questionType === 'MultipleChoiceFlexible' && form.options.length > 2 && (
                    <div className="flex justify-end">
                      <button
                        type="button"
                        onClick={() => removeOption(idx)}
                        className="p-1 text-red-400 hover:text-red-600"
                        aria-label={`Remove option ${key}`}
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  )}
                  <Input
                    label={`Option ${key}`}
                    value={opt}
                    onChange={(e) => {
                      const updated = [...form.options];
                      updated[idx] = e.target.value;
                      setForm({ ...form, options: updated });
                    }}
                    placeholder={`Option ${key} text...`}
                  />
                </div>
              );
            })}
            <Select
              label="Correct Answer"
              value={form.correctAnswer}
              onChange={(e) => setForm({ ...form, correctAnswer: e.target.value })}
              options={[
                { value: '', label: 'Select correct answer…' },
                ...form.options.map((opt, idx) => ({
                  value: optionLabels[idx],
                  label: `${optionLabels[idx]}: ${opt || '(empty)'}`,
                })),
              ]}
            />
            {form.questionType === 'MultipleChoiceFlexible' && (
              <Button
                variant="ghost"
                size="sm"
                onClick={addOption}
                disabled={form.options.length >= 6}
              >
                <Plus className="h-4 w-4 mr-1" />
                Add option
              </Button>
            )}
          </div>
        )}

        {/* Short Answer / Sentence Completion */}
        {isShortOrSentence && (
          <div className="space-y-3">
            <Input
              label="Correct Answer"
              value={form.correctAnswer}
              onChange={(e) => setForm({ ...form, correctAnswer: e.target.value })}
              placeholder="The expected answer..."
            />
            <AcceptedVariantManager
              variants={form.acceptedVariants}
              onChange={(acceptedVariants) => setForm({ ...form, acceptedVariants })}
            />
          </div>
        )}

        {form.questionType === 'ShortAnswerLabeled' && (
          <LabeledBoxesManager
            boxes={form.labeledBoxes}
            onChange={(labeledBoxes) => setForm({ ...form, labeledBoxes })}
          />
        )}

        {/* Matching Text Reference */}
        {isMatching && (
          <div>
            <Select
              label="Correct Answer (matching text)"
              value={form.correctAnswer}
              onChange={(e) => setForm({ ...form, correctAnswer: e.target.value })}
              options={[
                { value: '', label: 'Select matching text…' },
                ...matchingTextOptions,
              ]}
            />
          </div>
        )}

        {/* Common fields */}
        <div className="space-y-3 border-t border-admin-border pt-4">
          <Textarea
            label="Evidence sentence (optional)"
            value={form.evidenceSentence}
            onChange={(e) => setForm({ ...form, evidenceSentence: e.target.value })}
            rows={2}
            placeholder="Verbatim sentence from the text that supports the correct answer..."
          />
          <Textarea
            label="Explanation (optional, shown on review)"
            value={form.explanationMarkdown}
            onChange={(e) => setForm({ ...form, explanationMarkdown: e.target.value })}
            rows={2}
            placeholder="Explain why this is the correct answer..."
          />
        </div>

        {/* Actions */}
        <div className="flex gap-2 pt-2">
          <Button variant="primary" size="sm" onClick={handleSaveForm} disabled={saving}>
            <Save className="h-4 w-4 mr-1" />
            {saving ? 'Saving...' : 'Save'}
          </Button>
          <Button variant="ghost" size="sm" onClick={cancelEdit} disabled={saving}>
            Cancel
          </Button>
        </div>
      </div>
    );
  }

  function renderJsonEditor() {
    return (
      <div className="space-y-4">
        <Textarea
          label="Question JSON"
          value={jsonText}
          onChange={(e) => setJsonText(e.target.value)}
          rows={16}
          className="font-mono text-xs"
          placeholder='{ "questionType": "ShortAnswer", "stem": "...", ... }'
        />
        <div className="flex gap-2">
          <Button variant="primary" size="sm" onClick={handleSaveJson} disabled={saving}>
            <Save className="h-4 w-4 mr-1" />
            {saving ? 'Saving...' : 'Save JSON'}
          </Button>
          <Button variant="ghost" size="sm" onClick={cancelEdit} disabled={saving}>
            Cancel
          </Button>
        </div>
      </div>
    );
  }

  // ── Main Render ──────────────────────────────────────────────────────

  return (
    <>
      <AdminTableLayout
        title="Reading Questions Editor"
        description="Add, edit, and manage questions for each reading part."
        eyebrow="Reading authoring"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Content', href: '/admin/content' },
          { label: 'Reading', href: '/admin/content/reading' },
          { label: 'Paper', href: `/admin/content/reading/${paperId}` },
          { label: 'Questions' },
        ]}
        banner={<ReadingWizardSteps paperId={paperId} currentStep="questions" />}
        footer={
          <div className="flex items-center justify-between pt-2">
            <Button asChild variant="ghost" size="sm" startIcon={<ArrowLeft className="h-4 w-4" />}>
              <Link href={`/admin/content/reading/${paperId}/texts`}>Back to PDFs</Link>
            </Button>
            <Button asChild variant="primary" size="sm" endIcon={<ArrowRight className="h-4 w-4" />}>
              <Link href={`/admin/content/reading/${paperId}/validate`}>Next: Validate</Link>
            </Button>
          </div>
        }
      >
        <div className="p-4 sm:p-5 space-y-4">
          <div>
            <h2 className="text-base font-semibold text-admin-fg-strong">Questions</h2>
            <p className="mt-0.5 text-sm text-admin-fg-muted">Manage questions per reading part</p>
          </div>

          {loading && (
            <div className="space-y-3">
              <Skeleton variant="text" className="h-8 w-1/2" />
              <Skeleton variant="card" />
              <Skeleton variant="card" />
            </div>
          )}

          {error && (
            <p className="text-sm text-[var(--admin-danger)] py-4">{error}</p>
          )}

          {!loading && !error && (
            <div className="space-y-4">
              {totalQuestions > 0 && (
                <div
                  className={`flex flex-wrap items-center gap-2 rounded-lg border px-3 py-2 ${
                    allPublished
                      ? 'border-[var(--admin-success)]/40 bg-[var(--admin-success-tint)]'
                      : 'border-admin-border bg-admin-bg-subtle'
                  }`}
                >
                  <ShieldCheck
                    className={`h-4 w-4 shrink-0 ${
                      allPublished ? 'text-[var(--admin-success)]' : 'text-admin-fg-muted'
                    }`}
                  />
                  <span className="text-sm font-medium text-admin-fg-strong">
                    Publish readiness
                  </span>
                  <Badge variant={allPublished ? 'success' : 'warning'} size="sm">
                    {publishedCount} / {totalQuestions} published
                  </Badge>
                  <span className="text-xs text-admin-fg-muted">
                    {allPublished
                      ? 'All questions are Published.'
                      : 'Every question must reach the Published review state before the paper can be published.'}
                  </span>
                </div>
              )}

              <ReadingPartTabs activeTab={activeTab} onTabChange={setActiveTab} counts={counts} />

              {activeSections.length > 0 && (
                <div className="flex gap-1 flex-wrap">
                  {activeSections.map((section) => {
                    const isCurrent = (activeSection?.id ?? activeSections[0]?.id) === section.id;
                    return (
                      <button
                        key={section.id}
                        type="button"
                        onClick={() => { setActiveSectionId(section.id); cancelEdit(); }}
                        className={`px-3 py-1.5 text-xs font-semibold rounded-md border transition-colors ${
                          isCurrent
                            ? 'border-primary bg-primary text-white'
                            : 'border-admin-border bg-admin-bg-surface text-admin-fg-muted hover:text-admin-fg-strong'
                        }`}
                      >
                        {String(section.sectionCode)}
                      </button>
                    );
                  })}
                </div>
              )}

              {!editing && !reviewing && renderQuestionList()}

              {reviewing && (
                <Card surface="tinted-primary">
                  <CardHeader>
                    <div className="min-w-0">
                      <CardTitle className="text-sm">Review workflow</CardTitle>
                      <CardDescription className="truncate">{reviewing.stem}</CardDescription>
                    </div>
                    <div className="ml-auto">
                      <Button variant="ghost" size="sm" onClick={() => setReviewing(null)}>
                        Close
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent>
                    <ReadingReviewPanel
                      paperId={paperId}
                      questionId={reviewing.id}
                      currentState={reviewing.reviewState ?? 'Draft'}
                      onTransitioned={(toState: ReadingReviewState) => {
                        setReviewing((prev) => (prev ? { ...prev, reviewState: toState } : prev));
                        void fetchData();
                      }}
                      onNotify={(variant, message) => setToast({ variant, message })}
                    />
                  </CardContent>
                </Card>
              )}

              {editing && (
                <Card surface="tinted-primary">
                  <CardHeader>
                    <div className="min-w-0">
                      <CardTitle className="text-sm">
                        {form?.id ? 'Edit Question' : 'New Question'}
                      </CardTitle>
                      <CardDescription>Switch between form and JSON editing modes.</CardDescription>
                    </div>
                    <div className="ml-auto flex gap-1">
                      <Button
                        variant={editorMode === 'form' ? 'primary' : 'ghost'}
                        size="sm"
                        onClick={() => setEditorMode('form')}
                        startIcon={<FormInput className="h-3.5 w-3.5" />}
                      >
                        Form
                      </Button>
                      <Button
                        variant={editorMode === 'json' ? 'primary' : 'ghost'}
                        size="sm"
                        onClick={() => setEditorMode('json')}
                        startIcon={<Code className="h-3.5 w-3.5" />}
                      >
                        JSON
                      </Button>
                    </div>
                  </CardHeader>
                  <CardContent>
                    {editorMode === 'form' && renderFormEditor()}
                    {editorMode === 'json' && renderJsonEditor()}
                  </CardContent>
                </Card>
              )}
            </div>
          )}
        </div>
      </AdminTableLayout>

      {toast && (
        <div className="fixed top-4 right-4 z-50">
          <Toast
            variant={toast.variant}
            message={toast.message}
            onClose={() => setToast(null)}
          />
        </div>
      )}
    </>
  );
}
