'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { UploadSlot } from './UploadSlot';
import { useWizard } from './WizardShell';
import { ensureBundleSection, ensurePaperWithAssets, type PendingAsset } from './step-helpers';
import {
  ensureReadingCanonical,
  importReadingManifest,
  type ReadingPartCode,
  type ReadingPartManifest,
  type ReadingQuestionManifest,
  type ReadingQuestionType,
  type ReadingTextManifest,
} from '@/lib/mock-wizard/api';

interface TextDraft {
  id: string;
  partCode: ReadingPartCode;
  displayOrder: number;
  title: string;
  bodyHtml: string;
  source: string;
}

interface QuestionDraft {
  id: string;
  partCode: ReadingPartCode;
  displayOrder: number;
  questionType: ReadingQuestionType;
  stem: string;
  optionsCsv: string;
  correctAnswer: string;
  acceptedSynonymsCsv: string;
  /**
   * Wizard Medium #5 (May 2026 audit closure). Surfaces the
   * `caseSensitive` field on the manifest so the author can opt into
   * case-sensitive matching for short-answer / word-pool questions where
   * a clinical term must keep its original capitalisation (e.g. proper
   * nouns, drug brand names). Default false to preserve existing
   * behaviour for already-authored papers.
   */
  caseSensitive: boolean;
  points: number;
  textDisplayOrder: number | null;
}

const QUESTION_TYPES: { value: ReadingQuestionType; label: string }[] = [
  { value: 'WordPool', label: 'Word pool (Part A)' },
  { value: 'ShortAnswer', label: 'Short answer' },
  { value: 'MultipleChoice4', label: 'Multiple choice (4)' },
  { value: 'MultipleChoice3', label: 'Multiple choice (3)' },
  { value: 'TrueFalseNotGiven', label: 'True / False / Not given' },
];

const PART_TARGETS: Record<ReadingPartCode, { items: number; label: string }> = {
  A: { items: 20, label: 'Part A — 20 items' },
  B: { items: 6, label: 'Part B — 6 items' },
  C: { items: 16, label: 'Part C — 16 items' },
};

function makeId() {
  return Math.random().toString(36).slice(2, 10);
}

function wordCount(html: string): number {
  return html.replace(/<[^>]+>/g, ' ').split(/\s+/).filter(Boolean).length;
}

export function StepReading() {
  const { bundle, refreshBundle, setSavingState, registerCanAdvance, registerStepSubmit } =
    useWizard();
  const existingSection = bundle.sections.find((s) => s.subtestCode === 'reading');
  const existingPaperId = existingSection?.contentPaperId ?? null;

  const [pending, setPending] = useState<Record<string, string>>({});
  const [activeTab, setActiveTab] = useState<ReadingPartCode>('A');
  const [texts, setTexts] = useState<TextDraft[]>([]);
  const [questions, setQuestions] = useState<QuestionDraft[]>([]);
  const [error, setError] = useState<string | null>(null);

  const counts = useMemo(() => {
    const c: Record<ReadingPartCode, number> = { A: 0, B: 0, C: 0 };
    for (const q of questions) c[q.partCode] += 1;
    return c;
  }, [questions]);

  useEffect(() => {
    registerCanAdvance(
      'reading',
      questions.length > 0 || Boolean(existingPaperId),
    );
  }, [existingPaperId, questions.length, registerCanAdvance]);

  function addText(partCode: ReadingPartCode) {
    setTexts((prev) => [
      ...prev,
      {
        id: makeId(),
        partCode,
        displayOrder: prev.filter((t) => t.partCode === partCode).length + 1,
        title: '',
        bodyHtml: '',
        source: '',
      },
    ]);
  }

  function addQuestion(partCode: ReadingPartCode) {
    setQuestions((prev) => [
      ...prev,
      {
        id: makeId(),
        partCode,
        displayOrder: prev.filter((q) => q.partCode === partCode).length + 1,
        questionType: partCode === 'A' ? 'WordPool' : 'MultipleChoice4',
        stem: '',
        optionsCsv: '',
        correctAnswer: '',
        acceptedSynonymsCsv: '',
        caseSensitive: false,
        points: 1,
        textDisplayOrder: null,
      },
    ]);
  }

  function updateText(id: string, patch: Partial<TextDraft>) {
    setTexts((prev) => prev.map((t) => (t.id === id ? { ...t, ...patch } : t)));
  }
  function updateQuestion(id: string, patch: Partial<QuestionDraft>) {
    setQuestions((prev) => prev.map((q) => (q.id === id ? { ...q, ...patch } : q)));
  }
  function removeText(id: string) {
    setTexts((prev) => prev.filter((t) => t.id !== id));
  }
  function removeQuestion(id: string) {
    setQuestions((prev) => prev.filter((q) => q.id !== id));
  }

  const buildManifest = useCallback((): ReadingPartManifest[] => {
    const parts: ReadingPartCode[] = ['A', 'B', 'C'];
    return parts.map((part) => {
      const partTexts: ReadingTextManifest[] = texts
        .filter((t) => t.partCode === part)
        .sort((a, b) => a.displayOrder - b.displayOrder)
        .map((t) => ({
          displayOrder: t.displayOrder,
          title: t.title,
          source: t.source || null,
          bodyHtml: t.bodyHtml,
          wordCount: wordCount(t.bodyHtml),
          topicTag: null,
        }));
      const partQs: ReadingQuestionManifest[] = questions
        .filter((q) => q.partCode === part)
        .sort((a, b) => a.displayOrder - b.displayOrder)
        .map((q) => {
          const options =
            q.questionType === 'MultipleChoice4' || q.questionType === 'MultipleChoice3'
              ? q.optionsCsv.split('|').map((s) => s.trim()).filter(Boolean)
              : [];
          return {
            displayOrder: q.displayOrder,
            points: q.points,
            questionType: q.questionType,
            stem: q.stem,
            optionsJson: JSON.stringify(options),
            correctAnswerJson: JSON.stringify(q.correctAnswer),
            acceptedSynonymsJson: q.acceptedSynonymsCsv
              ? JSON.stringify(q.acceptedSynonymsCsv.split(',').map((s) => s.trim()).filter(Boolean))
              : null,
            caseSensitive: q.caseSensitive,
            explanationMarkdown: null,
            skillTag: null,
            readingTextDisplayOrder: q.textDisplayOrder,
          };
        });
      return {
        partCode: part,
        timeLimitMinutes: part === 'A' ? 15 : 45,
        instructions: null,
        texts: partTexts,
        questions: partQs,
      };
    });
  }, [questions, texts]);

  const submit = useCallback(async () => {
    setError(null);
    setSavingState(true);
    try {
      const pendingAssets: PendingAsset[] = Object.entries(pending).map(([role, mediaAssetId]) => ({
        role: role as PendingAsset['role'],
        mediaAssetId,
      }));
      const { paper } = await ensurePaperWithAssets({
        bundle,
        step: 'reading',
        existingPaperId,
        paperTitleSuffix: 'Reading',
        estimatedDurationMinutes: 60,
        pendingAssets,
      });
      await ensureReadingCanonical(paper.id);
      if (questions.length > 0) {
        await importReadingManifest(paper.id, { parts: buildManifest() }, true);
      }
      await ensureBundleSection(bundle, 'reading', paper.id);
      setPending({});
      await refreshBundle();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Reading save failed.');
      throw err;
    } finally {
      setSavingState(false);
    }
  }, [bundle, buildManifest, existingPaperId, pending, questions.length, refreshBundle, setSavingState]);

  useEffect(() => {
    registerStepSubmit('reading', submit);
    return () => registerStepSubmit('reading', null);
  }, [registerStepSubmit, submit]);

  const visibleTexts = texts.filter((t) => t.partCode === activeTab);
  const visibleQs = questions.filter((q) => q.partCode === activeTab);

  return (
    <div className="space-y-6">
      <header className="space-y-1">
        <h2 className="text-lg font-bold text-navy">Step 3 — Reading</h2>
        <p className="text-sm text-muted">
          Upload the question paper + answer key, then author texts and questions for Parts A
          (20), B (6), C (16).
        </p>
      </header>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <section className="space-y-2">
        <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Required assets</h3>
        <div className="grid gap-3 md:grid-cols-2">
          <UploadSlot
            paperId={existingPaperId}
            role="QuestionPaper"
            label="Question paper (PDF)"
            accept="application/pdf"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, QuestionPaper: id }))}
          />
          <UploadSlot
            paperId={existingPaperId}
            role="AnswerKey"
            label="Answer key (PDF or JSON)"
            accept="application/pdf,application/json"
            deferAttach={!existingPaperId}
            onAttached={(id) => setPending((p) => ({ ...p, AnswerKey: id }))}
          />
        </div>
      </section>

      <section className="space-y-3">
        <div className="flex flex-wrap items-end justify-between gap-2">
          <div>
            <h3 className="text-sm font-bold uppercase tracking-wider text-muted">Authoring</h3>
            <p className="text-xs text-muted">
              Items: A {counts.A}/20 · B {counts.B}/6 · C {counts.C}/16
            </p>
            <p className="mt-1 text-[11px] font-semibold text-muted">
              Time: Part A is a standalone 15-minute window; Parts B and C share a single 45-minute window per OET specification.
            </p>
          </div>
          <div className="flex gap-1">
            {(['A', 'B', 'C'] as const).map((t) => (
              <button
                key={t}
                onClick={() => setActiveTab(t)}
                className={
                  'rounded-xl border px-3 py-1.5 text-xs font-bold ' +
                  (activeTab === t
                    ? 'border-primary bg-primary text-white'
                    : 'border-border bg-surface text-navy hover:bg-background-light')
                }
              >
                {PART_TARGETS[t].label}
              </button>
            ))}
          </div>
        </div>

        <div className="space-y-3 rounded-2xl border border-border bg-background-light p-3">
          <div className="flex items-center justify-between">
            <p className="text-xs font-bold uppercase tracking-wider text-muted">
              Texts (Part {activeTab})
            </p>
            <Button variant="outline" size="sm" onClick={() => addText(activeTab)}>
              <Plus className="mr-1 h-3 w-3" /> Add text
            </Button>
          </div>
          {visibleTexts.length === 0 ? (
            <p className="text-xs text-muted">No texts yet.</p>
          ) : (
            visibleTexts.map((t) => (
              <div key={t.id} className="space-y-2 rounded-xl bg-surface p-3">
                <div className="flex items-center justify-between gap-2">
                  <Badge variant="info">Text #{t.displayOrder}</Badge>
                  <button onClick={() => removeText(t.id)} aria-label="Remove text" className="text-muted hover:text-red-600">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
                <Input
                  label="Title"
                  value={t.title}
                  onChange={(e) => updateText(t.id, { title: e.target.value })}
                />
                <Input
                  label="Source"
                  value={t.source}
                  onChange={(e) => updateText(t.id, { source: e.target.value })}
                  placeholder="e.g. NICE guidance, paragraph 4"
                />
                <Textarea
                  label="Body (HTML allowed)"
                  value={t.bodyHtml}
                  onChange={(e) => updateText(t.id, { bodyHtml: e.target.value })}
                  rows={5}
                />
              </div>
            ))
          )}
        </div>

        <div className="space-y-3 rounded-2xl border border-border bg-background-light p-3">
          <div className="flex items-center justify-between">
            <p className="text-xs font-bold uppercase tracking-wider text-muted">
              Questions (Part {activeTab})
            </p>
            <Button variant="outline" size="sm" onClick={() => addQuestion(activeTab)}>
              <Plus className="mr-1 h-3 w-3" /> Add question
            </Button>
          </div>
          {visibleQs.length === 0 ? (
            <p className="text-xs text-muted">No questions yet.</p>
          ) : (
            visibleQs.map((q) => (
              <div key={q.id} className="space-y-2 rounded-xl bg-surface p-3">
                <div className="flex items-center justify-between gap-2">
                  <Badge variant="info">Q{q.displayOrder}</Badge>
                  <button onClick={() => removeQuestion(q.id)} aria-label="Remove question" className="text-muted hover:text-red-600">
                    <Trash2 className="h-4 w-4" />
                  </button>
                </div>
                <div className="grid gap-2 md:grid-cols-3">
                  <Select
                    label="Type"
                    value={q.questionType}
                    onChange={(e) =>
                      updateQuestion(q.id, { questionType: e.target.value as ReadingQuestionType })
                    }
                    options={QUESTION_TYPES}
                  />
                  <Input
                    label="Points"
                    type="number"
                    value={String(q.points)}
                    onChange={(e) => updateQuestion(q.id, { points: Number(e.target.value) || 1 })}
                  />
                  <Input
                    label="Linked text #"
                    type="number"
                    value={q.textDisplayOrder == null ? '' : String(q.textDisplayOrder)}
                    onChange={(e) =>
                      updateQuestion(q.id, {
                        textDisplayOrder: e.target.value ? Number(e.target.value) : null,
                      })
                    }
                    placeholder="optional"
                  />
                </div>
                <Textarea
                  label="Prompt / stem"
                  value={q.stem}
                  onChange={(e) => updateQuestion(q.id, { stem: e.target.value })}
                  rows={2}
                />
                {(q.questionType === 'MultipleChoice4' || q.questionType === 'MultipleChoice3') ? (
                  <Input
                    label="Options (separate with |)"
                    value={q.optionsCsv}
                    onChange={(e) => updateQuestion(q.id, { optionsCsv: e.target.value })}
                  />
                ) : null}
                <div className="grid gap-2 md:grid-cols-2">
                  <Input
                    label="Correct answer"
                    value={q.correctAnswer}
                    onChange={(e) => updateQuestion(q.id, { correctAnswer: e.target.value })}
                  />
                  {q.questionType === 'ShortAnswer' || q.questionType === 'WordPool' ? (
                    <Input
                      label="Accepted synonyms (CSV)"
                      value={q.acceptedSynonymsCsv}
                      onChange={(e) => updateQuestion(q.id, { acceptedSynonymsCsv: e.target.value })}
                    />
                  ) : null}
                </div>
                {q.questionType === 'ShortAnswer' || q.questionType === 'WordPool' ? (
                  <label className="flex items-center gap-2 text-xs font-semibold text-navy">
                    <input
                      type="checkbox"
                      className="h-4 w-4 rounded border-border text-primary focus:ring-primary/20"
                      checked={q.caseSensitive}
                      onChange={(e) => updateQuestion(q.id, { caseSensitive: e.target.checked })}
                    />
                    Match answers case-sensitively (e.g. for proper nouns or drug brand names)
                  </label>
                ) : null}
              </div>
            ))
          )}
        </div>
      </section>
    </div>
  );
}
