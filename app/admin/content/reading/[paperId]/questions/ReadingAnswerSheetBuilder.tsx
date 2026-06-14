'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Save, Sparkles, Pencil } from 'lucide-react';

import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import {
  upsertReadingQuestion,
  type ReadingPartCode,
  type ReadingQuestionType,
  type ReadingQuestionAdminDto,
  type ReadingPartAdminDto,
  type ReadingSectionAdminDto,
} from '@/lib/reading-authoring-api';
import { readingPublicDisplayNumber } from '@/lib/reading-display-number';

// ── Types ──────────────────────────────────────────────────────────────

export interface ReadingAnswerSheetBuilderProps {
  paperId: string;
  partCode: ReadingPartCode;
  activePart: ReadingPartAdminDto;
  /** null for Part A; the current B/C section otherwise. */
  activeSection: ReadingSectionAdminDto | null;
  onSaved: () => Promise<void> | void;
  onNotify: (variant: 'success' | 'error', message: string) => void;
  onEditDetails?: (question: ReadingQuestionAdminDto) => void;
}

type BuilderRowKind = 'matching' | 'shortText' | 'mcq';

interface BuilderRow {
  id: string | null;
  internalDisplayOrder: number;
  publicNumber: number;
  questionType: ReadingQuestionType;
  kind: BuilderRowKind;
  options: string[];
  correctAnswer: string;
}

interface SlotTemplate {
  internalDisplayOrder: number;
  questionType: ReadingQuestionType;
  kind: BuilderRowKind;
  options: string[];
}

const MCQ_LETTERS = ['A', 'B', 'C', 'D', 'E', 'F'];

const TYPE_LABEL: Partial<Record<ReadingQuestionType, string>> = {
  MatchingTextReference: 'Matching',
  ShortAnswer: 'Short answer',
  SentenceCompletion: 'Sentence',
  MultipleChoice3: 'MCQ (3)',
  MultipleChoice4: 'MCQ (4)',
};

const PART_A_TEXT_OPTIONS = ['Text A', 'Text B', 'Text C', 'Text D'];
const MCQ3_OPTIONS = ['Option A', 'Option B', 'Option C'];
const MCQ4_OPTIONS = ['Option A', 'Option B', 'Option C', 'Option D'];

// Which question types the builder owns for each part. Anything else in a
// section means it was authored with the advanced editor and the builder
// must not overwrite it.
const EXPECTED_TYPES: Record<ReadingPartCode, ReadingQuestionType[]> = {
  A: ['MatchingTextReference', 'ShortAnswer', 'SentenceCompletion'],
  B: ['MultipleChoice3'],
  C: ['MultipleChoice4'],
};

// ── Template generation ────────────────────────────────────────────────

function sectionIndex(section: ReadingSectionAdminDto): number {
  // 'B3' -> 3, 'C2' -> 2. Falls back to the stored displayOrder.
  const parsed = Number(String(section.sectionCode).slice(1));
  return Number.isFinite(parsed) && parsed > 0 ? parsed : section.displayOrder;
}

function templateSlots(partCode: ReadingPartCode, activeSection: ReadingSectionAdminDto | null): SlotTemplate[] {
  if (partCode === 'A') {
    const slots: SlotTemplate[] = [];
    for (let order = 1; order <= 20; order += 1) {
      if (order <= 7) {
        slots.push({ internalDisplayOrder: order, questionType: 'MatchingTextReference', kind: 'matching', options: [...PART_A_TEXT_OPTIONS] });
      } else if (order <= 14) {
        slots.push({ internalDisplayOrder: order, questionType: 'ShortAnswer', kind: 'shortText', options: [] });
      } else {
        slots.push({ internalDisplayOrder: order, questionType: 'SentenceCompletion', kind: 'shortText', options: [] });
      }
    }
    return slots;
  }

  if (!activeSection) return [];

  if (partCode === 'B') {
    // One MultipleChoice3 per B-section, at section-local order = section index (B1->1 … B6->6).
    return [{ internalDisplayOrder: sectionIndex(activeSection), questionType: 'MultipleChoice3', kind: 'mcq', options: [...MCQ3_OPTIONS] }];
  }

  // Part C: eight MultipleChoice4 per section. C1 -> 1..8, C2 -> 9..16.
  const base = (sectionIndex(activeSection) - 1) * 8 + 1;
  return Array.from({ length: 8 }, (_, i) => ({
    internalDisplayOrder: base + i,
    questionType: 'MultipleChoice4' as const,
    kind: 'mcq' as const,
    options: [...MCQ4_OPTIONS],
  }));
}

function parseExisting(question: ReadingQuestionAdminDto): { options: string[]; correctAnswer: string } {
  let options: string[] = [];
  try {
    const parsed = JSON.parse(question.optionsJson);
    if (Array.isArray(parsed)) options = parsed.map((value) => String(value));
  } catch { /* keep empty */ }

  let correctAnswer = '';
  try {
    const parsed = JSON.parse(question.correctAnswerJson);
    correctAnswer = typeof parsed === 'string' ? parsed : '';
  } catch { /* keep empty */ }

  return { options, correctAnswer };
}

function buildRows(
  partCode: ReadingPartCode,
  activeSection: ReadingSectionAdminDto | null,
  scopeQuestions: ReadingQuestionAdminDto[],
): BuilderRow[] {
  return templateSlots(partCode, activeSection).map((slot) => {
    const existing = scopeQuestions.find(
      (q) => q.displayOrder === slot.internalDisplayOrder && q.questionType === slot.questionType,
    );
    const seeded = existing ? parseExisting(existing) : null;
    return {
      id: existing?.id ?? null,
      internalDisplayOrder: slot.internalDisplayOrder,
      publicNumber: readingPublicDisplayNumber(partCode, slot.internalDisplayOrder),
      questionType: slot.questionType,
      kind: slot.kind,
      options: seeded && seeded.options.length > 0 ? seeded.options : slot.options,
      correctAnswer: seeded?.correctAnswer ?? '',
    };
  });
}

// ── Component ──────────────────────────────────────────────────────────

export function ReadingAnswerSheetBuilder({
  paperId,
  partCode,
  activePart,
  activeSection,
  onSaved,
  onNotify,
  onEditDetails,
}: ReadingAnswerSheetBuilderProps) {
  const scopeQuestions = useMemo<ReadingQuestionAdminDto[]>(
    () => (partCode === 'A' ? activePart.questions : (activeSection?.questions ?? [])),
    [partCode, activePart.questions, activeSection],
  );

  // Re-seed whenever the scope (part/section) or its persisted questions change.
  const scopeSignature = useMemo(
    () => JSON.stringify(scopeQuestions.map((q) => [q.id, q.displayOrder, q.questionType, q.correctAnswerJson, q.optionsJson])),
    [scopeQuestions],
  );

  const blocked = useMemo(
    () => scopeQuestions.some((q) => !EXPECTED_TYPES[partCode].includes(q.questionType)),
    [partCode, scopeQuestions],
  );

  const needsSection = partCode !== 'A' && !activeSection;

  const [rows, setRows] = useState<BuilderRow[]>([]);
  const [shown, setShown] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (blocked || needsSection) {
      setRows([]);
      setShown(false);
      return;
    }
    if (scopeQuestions.length > 0) {
      // Already authored via the builder — seed and show in place.
      setRows(buildRows(partCode, activeSection, scopeQuestions));
      setShown(true);
    } else {
      setRows([]);
      setShown(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [partCode, activePart.id, activeSection?.id, scopeSignature, blocked, needsSection]);

  const handleGenerate = useCallback(() => {
    setRows(buildRows(partCode, activeSection, scopeQuestions));
    setShown(true);
  }, [partCode, activeSection, scopeQuestions]);

  const updateRow = useCallback((index: number, correctAnswer: string) => {
    setRows((prev) => prev.map((row, i) => (i === index ? { ...row, correctAnswer } : row)));
  }, []);

  async function handleSaveAll() {
    const invalid = rows.find((row) => (row.kind === 'shortText' ? !row.correctAnswer.trim() : !row.correctAnswer));
    if (invalid) {
      onNotify('error', `Q${invalid.publicNumber} needs a correct answer before saving.`);
      return;
    }

    setSaving(true);
    try {
      for (const row of rows) {
        await upsertReadingQuestion(paperId, {
          id: row.id,
          readingPartId: activePart.id,
          readingSectionId: partCode === 'A' ? null : (activeSection?.id ?? null),
          readingTextId: null,
          displayOrder: row.internalDisplayOrder,
          points: 1,
          questionType: row.questionType,
          stem: 'See PDF',
          caseSensitive: false,
          optionsJson: JSON.stringify(row.options),
          correctAnswerJson: JSON.stringify(row.correctAnswer),
          acceptedSynonymsJson: null,
          explanationMarkdown: null,
          evidenceSentence: null,
          skillTag: null,
          difficulty: null,
          paragraphIndex: null,
          distractorRationale: null,
          boxExplanationsJson: null,
        });
      }
      onNotify('success', `Saved ${rows.length} answer${rows.length === 1 ? '' : 's'}.`);
      await onSaved();
    } catch (err: unknown) {
      onNotify('error', err instanceof Error ? err.message : 'Save failed');
    } finally {
      setSaving(false);
    }
  }

  const sectionLabel = partCode === 'A' ? '' : ` ${activeSection?.sectionCode ?? ''}`.trimEnd();

  return (
    <Card surface="tinted-primary">
      <CardHeader>
        <div className="min-w-0">
          <CardTitle className="text-sm">Answer sheet — Part {partCode}{sectionLabel}</CardTitle>
          <CardDescription>
            Read the question on the PDF, then record the machine-gradable answer for each item.
          </CardDescription>
        </div>
        {shown && !blocked && !needsSection ? (
          <div className="ml-auto">
            <Button variant="primary" size="sm" onClick={handleSaveAll} disabled={saving} startIcon={<Save className="h-3.5 w-3.5" />}>
              {saving ? 'Saving…' : 'Save all answers'}
            </Button>
          </div>
        ) : null}
      </CardHeader>
      <CardContent>
        {needsSection ? (
          <p className="text-sm text-admin-fg-muted">Select a section to build its answer sheet.</p>
        ) : blocked ? (
          <InlineAlert variant="warning">
            These questions were authored with the advanced editor. Edit them in the question list below to avoid overwriting custom content.
          </InlineAlert>
        ) : !shown ? (
          <div className="text-center py-6 space-y-3">
            <p className="text-sm text-admin-fg-muted">No answers yet for Part {partCode}{sectionLabel}.</p>
            <Button variant="primary" size="sm" onClick={handleGenerate} startIcon={<Sparkles className="h-4 w-4" />}>
              Generate from PDF template
            </Button>
          </div>
        ) : (
          <div className="space-y-2">
            {rows.map((row, index) => {
              const letterOptions = row.options.map((opt, idx) => ({
                value: MCQ_LETTERS[idx],
                label: `${MCQ_LETTERS[idx]}. ${opt}`,
              }));
              const existing = row.id ? scopeQuestions.find((q) => q.id === row.id) ?? null : null;
              return (
                <div
                  key={row.internalDisplayOrder}
                  className="flex items-center gap-3 rounded-lg border border-admin-border bg-admin-bg-subtle px-3 py-2"
                >
                  <span className="w-10 shrink-0 text-xs font-mono font-semibold text-admin-fg-muted">Q{row.publicNumber}</span>
                  <Badge variant="muted" size="sm" className="shrink-0">{TYPE_LABEL[row.questionType] ?? row.questionType}</Badge>
                  <div className="flex-1 min-w-0">
                    {row.kind === 'shortText' ? (
                      <Input
                        aria-label={`Correct answer for question ${row.publicNumber}`}
                        value={row.correctAnswer}
                        onChange={(e) => updateRow(index, e.target.value)}
                        placeholder="Correct answer"
                      />
                    ) : (
                      <Select
                        aria-label={`Correct answer for question ${row.publicNumber}`}
                        placeholder="Select answer"
                        options={letterOptions}
                        value={row.correctAnswer}
                        onChange={(e) => updateRow(index, e.target.value)}
                      />
                    )}
                  </div>
                  {existing && onEditDetails ? (
                    <Button
                      variant="ghost"
                      size="sm"
                      onClick={() => onEditDetails(existing)}
                      startIcon={<Pencil className="h-3.5 w-3.5" />}
                    >
                      Edit details
                    </Button>
                  ) : null}
                </div>
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
