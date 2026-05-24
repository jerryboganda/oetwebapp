'use client';

import { useState, useEffect, useCallback } from 'react';
import { useParams } from 'next/navigation';
import Link from 'next/link';
import { ArrowLeft, ArrowRight, Plus, Trash2, Save, Code, FormInput } from 'lucide-react';

import { AdminRouteWorkspace, AdminRoutePanel, AdminRouteSectionHeader } from '@/components/domain/admin-route-surface';
import { Button } from '@/components/ui/button';
import { Input, Textarea, Select, Checkbox } from '@/components/ui/form-controls';
import { Toast } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { ReadingWizardSteps } from '@/components/domain/admin/reading/ReadingWizardSteps';
import { ReadingPartTabs } from '@/components/domain/admin/reading/ReadingPartTabs';
import {
  getReadingStructureAdmin,
  upsertReadingQuestion,
  removeReadingQuestion,
  type ReadingPartCode,
  type ReadingQuestionType,
  type ReadingQuestionAdminDto,
  type ReadingPartAdminDto,
  type ReadingTextDto,
} from '@/lib/reading-authoring-api';

// ── Constants ──────────────────────────────────────────────────────────

const ALLOWED_TYPES: Record<ReadingPartCode, ReadingQuestionType[]> = {
  A: ['MatchingTextReference', 'ShortAnswer', 'SentenceCompletion'],
  B: ['MultipleChoice3'],
  C: ['MultipleChoice4'],
};

const TYPE_LABELS: Record<ReadingQuestionType, string> = {
  MatchingTextReference: 'Matching Text Reference',
  ShortAnswer: 'Short Answer',
  SentenceCompletion: 'Sentence Completion',
  MultipleChoice3: 'Multiple Choice (3)',
  MultipleChoice4: 'Multiple Choice (4)',
};

// ── Form state interfaces ──────────────────────────────────────────────

interface QuestionFormState {
  id: string | null;
  readingPartId: string;
  readingTextId: string | null;
  displayOrder: number;
  points: number;
  questionType: ReadingQuestionType;
  stem: string;
  caseSensitive: boolean;
  correctAnswer: string;
  acceptedSynonyms: string;
  options: string[];
  explanationMarkdown: string;
  skillTag: string;
}

type EditorMode = 'form' | 'json';

function emptyFormState(partId: string, partCode: ReadingPartCode, nextOrder: number): QuestionFormState {
  const defaultType = ALLOWED_TYPES[partCode][0];
  const optionCount = defaultType === 'MultipleChoice4' ? 4 : defaultType === 'MultipleChoice3' ? 3 : 0;
  return {
    id: null,
    readingPartId: partId,
    readingTextId: null,
    displayOrder: nextOrder,
    points: 1,
    questionType: defaultType,
    stem: '',
    caseSensitive: false,
    correctAnswer: '',
    acceptedSynonyms: '',
    options: Array(optionCount).fill(''),
    explanationMarkdown: '',
    skillTag: '',
  };
}

function questionToFormState(q: ReadingQuestionAdminDto): QuestionFormState {
  let options: string[] = [];
  let correctAnswer = '';
  let acceptedSynonyms = '';

  try {
    const parsed = JSON.parse(q.optionsJson);
    if (Array.isArray(parsed)) options = parsed;
  } catch { /* empty */ }

  try {
    correctAnswer = JSON.parse(q.correctAnswerJson) ?? '';
  } catch {
    correctAnswer = q.correctAnswerJson ?? '';
  }

  if (q.acceptedSynonymsJson) {
    try {
      const arr = JSON.parse(q.acceptedSynonymsJson);
      if (Array.isArray(arr)) acceptedSynonyms = arr.join(', ');
    } catch { /* empty */ }
  }

  return {
    id: q.id,
    readingPartId: q.readingPartId,
    readingTextId: q.readingTextId,
    displayOrder: q.displayOrder,
    points: q.points,
    questionType: q.questionType,
    stem: q.stem,
    caseSensitive: q.caseSensitive,
    correctAnswer,
    acceptedSynonyms,
    options,
    explanationMarkdown: q.explanationMarkdown ?? '',
    skillTag: q.skillTag ?? '',
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
      skillTag: q.skillTag,
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
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<{ variant: 'success' | 'error'; message: string } | null>(null);

  // Editor state
  const [editing, setEditing] = useState(false);
  const [editorMode, setEditorMode] = useState<EditorMode>('form');
  const [form, setForm] = useState<QuestionFormState | null>(null);
  const [jsonText, setJsonText] = useState('');
  const [saving, setSaving] = useState(false);

  const activePart = parts.find((p) => p.partCode === activeTab) ?? null;

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
    void fetchData();
  }, [fetchData]);

  const counts: { A: number; B: number; C: number } = {
    A: parts.find((p) => p.partCode === 'A')?.questions.length ?? 0,
    B: parts.find((p) => p.partCode === 'B')?.questions.length ?? 0,
    C: parts.find((p) => p.partCode === 'C')?.questions.length ?? 0,
  };

  // ── Handlers ─────────────────────────────────────────────────────────

  function startAdd() {
    if (!activePart) return;
    const nextOrder = activePart.questions.length + 1;
    const newForm = emptyFormState(activePart.id, activeTab, nextOrder);
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
        skillTag: null,
      },
      null,
      2,
    ));
    setEditorMode('form');
    setEditing(true);
  }

  function startEdit(q: ReadingQuestionAdminDto) {
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

  function handleTypeChange(newType: ReadingQuestionType) {
    if (!form) return;
    const optionCount = newType === 'MultipleChoice4' ? 4 : newType === 'MultipleChoice3' ? 3 : 0;
    setForm({
      ...form,
      questionType: newType,
      options: Array(optionCount).fill('').map((_, i) => form.options[i] ?? ''),
      correctAnswer: '',
    });
  }

  function buildPayload(f: QuestionFormState) {
    const isMultiChoice = f.questionType === 'MultipleChoice3' || f.questionType === 'MultipleChoice4';
    const optionsJson = isMultiChoice ? JSON.stringify(f.options) : '[]';
    const correctAnswerJson = JSON.stringify(f.correctAnswer);
    const synonymsArr = f.acceptedSynonyms
      .split(',')
      .map((s) => s.trim())
      .filter(Boolean);
    const acceptedSynonymsJson = synonymsArr.length > 0 ? JSON.stringify(synonymsArr) : null;

    return {
      id: f.id,
      readingPartId: f.readingPartId,
      readingTextId: f.readingTextId || null,
      displayOrder: f.displayOrder,
      points: f.points,
      questionType: f.questionType,
      stem: f.stem,
      optionsJson,
      correctAnswerJson,
      acceptedSynonymsJson,
      caseSensitive: f.caseSensitive,
      explanationMarkdown: f.explanationMarkdown || null,
      skillTag: f.skillTag || null,
    };
  }

  async function handleSaveForm() {
    if (!form) return;
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
        skillTag: parsed.skillTag ?? null,
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

  // ── Render Helpers ───────────────────────────────────────────────────

  function renderQuestionList() {
    if (!activePart) return <p className="text-admin-text-muted text-sm">No part data found.</p>;
    const questions = activePart.questions;

    if (questions.length === 0) {
      return (
        <div className="text-center py-8">
          <p className="text-admin-text-muted text-sm mb-3">No questions yet for Part {activeTab}.</p>
          <Button variant="primary" size="sm" onClick={startAdd}>
            <Plus className="h-4 w-4 mr-1" />
            Add First Question
          </Button>
        </div>
      );
    }

    return (
      <div className="space-y-2">
        {questions.map((q) => (
          <div
            key={q.id}
            className="flex items-center gap-3 rounded-lg border border-admin-border bg-admin-surface-raised/30 px-3 py-2"
          >
            <span className="text-xs font-mono text-admin-text-muted w-6 text-center shrink-0">
              {q.displayOrder}
            </span>
            <div className="flex-1 min-w-0">
              <p className="text-sm text-admin-text truncate">{q.stem}</p>
            </div>
            <Badge variant="default" className="shrink-0 text-xs">
              {TYPE_LABELS[q.questionType] ?? q.questionType}
            </Badge>
            <span className="text-xs text-admin-text-muted shrink-0">{q.points}pt</span>
            <div className="flex gap-1 shrink-0">
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
    const isMultiChoice = form.questionType === 'MultipleChoice3' || form.questionType === 'MultipleChoice4';
    const isShortOrSentence = form.questionType === 'ShortAnswer' || form.questionType === 'SentenceCompletion';
    const isMatching = form.questionType === 'MatchingTextReference';
    const optionLabels = form.questionType === 'MultipleChoice4' ? ['A', 'B', 'C', 'D'] : ['A', 'B', 'C'];

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
              { value: '', label: '— None —' },
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
            <p className="text-xs font-medium text-admin-text-muted uppercase tracking-wide">Options</p>
            {form.options.map((opt, idx) => (
              <Input
                key={idx}
                label={`Option ${optionLabels[idx]}`}
                value={opt}
                onChange={(e) => {
                  const updated = [...form.options];
                  updated[idx] = e.target.value;
                  setForm({ ...form, options: updated });
                }}
                placeholder={`Option ${optionLabels[idx]} text...`}
              />
            ))}
            <Select
              label="Correct Answer"
              value={form.correctAnswer}
              onChange={(e) => setForm({ ...form, correctAnswer: e.target.value })}
              options={[
                { value: '', label: '— Select correct answer —' },
                ...form.options.map((opt, idx) => ({
                  value: opt || optionLabels[idx],
                  label: `${optionLabels[idx]}: ${opt || '(empty)'}`,
                })),
              ]}
            />
          </div>
        )}

        {/* Short Answer / Sentence Completion */}
        {isShortOrSentence && (
          <div className="space-y-2">
            <Input
              label="Correct Answer"
              value={form.correctAnswer}
              onChange={(e) => setForm({ ...form, correctAnswer: e.target.value })}
              placeholder="The expected answer..."
            />
            <Textarea
              label="Accepted Synonyms (comma-separated)"
              value={form.acceptedSynonyms}
              onChange={(e) => setForm({ ...form, acceptedSynonyms: e.target.value })}
              rows={2}
              placeholder="synonym1, synonym2, synonym3"
            />
          </div>
        )}

        {/* Matching Text Reference */}
        {isMatching && (
          <div>
            <Input
              label="Correct Answer (text reference letter/number)"
              value={form.correctAnswer}
              onChange={(e) => setForm({ ...form, correctAnswer: e.target.value })}
              placeholder="e.g. A, B, 1, 2..."
            />
          </div>
        )}

        {/* Common fields */}
        <div className="space-y-2 border-t border-admin-border pt-4">
          <Textarea
            label="Explanation (optional — shown on review)"
            value={form.explanationMarkdown}
            onChange={(e) => setForm({ ...form, explanationMarkdown: e.target.value })}
            rows={2}
            placeholder="Explain why this is the correct answer..."
          />
          <Input
            label="Skill Tag (optional)"
            value={form.skillTag}
            onChange={(e) => setForm({ ...form, skillTag: e.target.value })}
            placeholder="e.g. scanning, inference, vocabulary"
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
    <AdminRouteWorkspace>
      {toast && (
        <div className="fixed top-4 right-4 z-50">
          <Toast
            variant={toast.variant}
            message={toast.message}
            onClose={() => setToast(null)}
          />
        </div>
      )}

      <AdminRouteSectionHeader
        title="Reading Questions Editor"
        description="Add, edit, and manage questions for each reading part."
        eyebrow="Reading Authoring"
        accent="blue"
      />

      <ReadingWizardSteps paperId={paperId} currentStep="questions" />

      <AdminRoutePanel title="Questions" description="Manage questions per reading part">
        {loading && (
          <div className="flex items-center justify-center py-12">
            <div className="animate-spin h-6 w-6 border-2 border-primary border-t-transparent rounded-full" />
          </div>
        )}

        {error && (
          <div className="text-red-400 text-sm py-4">{error}</div>
        )}

        {!loading && !error && (
          <div className="space-y-4">
            <ReadingPartTabs activeTab={activeTab} onTabChange={setActiveTab} counts={counts} />

            {!editing && renderQuestionList()}

            {editing && (
              <div className="border border-admin-border rounded-xl bg-admin-surface-raised/20 p-4 space-y-4">
                <div className="flex items-center justify-between">
                  <h3 className="text-sm font-semibold text-admin-text">
                    {form?.id ? 'Edit Question' : 'New Question'}
                  </h3>
                  <div className="flex gap-1">
                    <Button
                      variant={editorMode === 'form' ? 'primary' : 'ghost'}
                      size="sm"
                      onClick={() => setEditorMode('form')}
                    >
                      <FormInput className="h-3.5 w-3.5 mr-1" />
                      Form
                    </Button>
                    <Button
                      variant={editorMode === 'json' ? 'primary' : 'ghost'}
                      size="sm"
                      onClick={() => setEditorMode('json')}
                    >
                      <Code className="h-3.5 w-3.5 mr-1" />
                      JSON
                    </Button>
                  </div>
                </div>

                {editorMode === 'form' && renderFormEditor()}
                {editorMode === 'json' && renderJsonEditor()}
              </div>
            )}
          </div>
        )}
      </AdminRoutePanel>

      {/* Navigation */}
      <div className="flex items-center justify-between pt-2">
        <Link
          href={`/admin/content/reading/${paperId}/texts`}
          className="inline-flex items-center gap-1 text-sm text-admin-text-muted hover:text-admin-text transition-colors"
        >
          <ArrowLeft className="h-4 w-4" />
          Back to Texts
        </Link>
        <Link href={`/admin/content/reading/${paperId}/validate`}>
          <Button variant="primary" size="sm">
            Next: Validate
            <ArrowRight className="h-4 w-4 ml-1" />
          </Button>
        </Link>
      </div>
    </AdminRouteWorkspace>
  );
}
