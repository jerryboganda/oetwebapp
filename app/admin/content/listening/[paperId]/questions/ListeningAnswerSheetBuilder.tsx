'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Save, Sparkles, Pencil } from 'lucide-react';

import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Select, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import {
  replaceListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningSubSectionCode,
} from '@/lib/listening-authoring-api';

// ── Types ──────────────────────────────────────────────────────────────
//
// Mirrors app/admin/content/reading/[paperId]/questions/ReadingAnswerSheetBuilder.tsx
// so Listening Part B/C authoring is identical to Reading: read the question on
// the uploaded PDF (shown on the left), then record only the machine-gradable
// answer key (correct option letter) + an optional rationale per item. The stem
// is stored as "See PDF" — the real question text lives on the question paper.
//
// Difference from Reading (rulebook-driven): Listening Part B AND Part C are
// 3-option MCQ (A/B/C), whereas Reading Part C is 4-option. Counts also differ
// (Listening B = 6×1, C = 2×6 = 12).

export type ListeningBuilderPart = 'B' | 'C';

export interface ListeningAnswerSheetBuilderProps {
  paperId: string;
  /** 'B' or 'C' (Part A is authored on the dedicated notes editor). */
  partCode: ListeningBuilderPart;
  /** The active B/C sub-section (B1–B6 / C1–C2). */
  activeSection: ListeningSubSectionCode;
  /** The full authored question list (all 42 items). */
  allQuestions: ListeningAuthoredQuestion[];
  onSaved: (next: ListeningAuthoredQuestion[]) => Promise<void> | void;
  onNotify: (variant: 'success' | 'error', message: string) => void;
  onEditDetails?: (question: ListeningAuthoredQuestion) => void;
}

interface BuilderRow {
  id: string | null;
  number: number;
  /** Letter of the correct option: 'A' | 'B' | 'C' (empty until chosen). */
  correctAnswer: string;
  /** Optional admin rationale → stored as `explanation`; learner-visible on review. */
  rationale: string;
}

// Canonical printed-paper numbering per sub-section (OET 1–42). Part B is one
// MCQ per extract (B1→25 … B6→30); Part C is six MCQ per extract
// (C1→31–36, C2→37–42).
const SUB_SECTION_NUMBER_RANGES: Record<ListeningSubSectionCode, [number, number]> = {
  A1: [1, 12], A2: [13, 24],
  B1: [25, 25], B2: [26, 26], B3: [27, 27], B4: [28, 28], B5: [29, 29], B6: [30, 30],
  C1: [31, 36], C2: [37, 42],
};

const MCQ_LETTERS = ['A', 'B', 'C'] as const;
// Placeholder option texts — identical to Reading's builder. The real option
// text is on the PDF; we only persist which letter is correct.
const MCQ3_OPTIONS = ['Option A', 'Option B', 'Option C'];
const LETTER_OPTIONS = MCQ_LETTERS.map((letter, idx) => ({
  value: letter,
  label: `${letter}. ${MCQ3_OPTIONS[idx]}`,
}));

function rangeFor(section: ListeningSubSectionCode): number[] {
  const [lo, hi] = SUB_SECTION_NUMBER_RANGES[section];
  const out: number[] = [];
  for (let n = lo; n <= hi; n += 1) out.push(n);
  return out;
}

function normalizeCode(code: string): string {
  return code.trim().toUpperCase();
}

/** Resolve an existing question's stored correct answer to a letter A/B/C. */
function correctLetterOf(q: ListeningAuthoredQuestion): string {
  const raw = (q.correctAnswer ?? '').trim();
  if (!raw) return '';
  const upper = raw.toUpperCase();
  if (MCQ_LETTERS.includes(upper as (typeof MCQ_LETTERS)[number])) return upper;
  // Legacy papers store the option *text* as the correct answer — map it back
  // to a letter by position so the builder seeds correctly.
  const index = (q.options ?? []).findIndex((opt) => opt.trim() === raw);
  return index >= 0 ? MCQ_LETTERS[index] ?? '' : '';
}

function buildRows(
  section: ListeningSubSectionCode,
  sectionQuestions: ListeningAuthoredQuestion[],
): BuilderRow[] {
  return rangeFor(section).map((number) => {
    const existing = sectionQuestions.find((q) => q.number === number);
    return {
      id: existing?.id ?? null,
      number,
      correctAnswer: existing ? correctLetterOf(existing) : '',
      rationale: existing?.explanation ?? '',
    };
  });
}

export function ListeningAnswerSheetBuilder({
  paperId,
  partCode,
  activeSection,
  allQuestions,
  onSaved,
  onNotify,
  onEditDetails,
}: ListeningAnswerSheetBuilderProps) {
  const sectionQuestions = useMemo(
    () => allQuestions.filter((q) => normalizeCode(q.partCode) === activeSection),
    [allQuestions, activeSection],
  );

  // Any item in this section authored as something other than MCQ-3 was made
  // with the advanced form; the builder must not silently overwrite it.
  const blocked = useMemo(
    () => sectionQuestions.some((q) => q.type !== 'multiple_choice_3'),
    [sectionQuestions],
  );

  const signature = useMemo(
    () => JSON.stringify(sectionQuestions.map((q) => [q.id, q.number, q.type, q.correctAnswer, q.options])),
    [sectionQuestions],
  );

  const [rows, setRows] = useState<BuilderRow[]>([]);
  const [shown, setShown] = useState(false);
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    if (blocked) {
      setRows([]);
      setShown(false);
      return;
    }
    if (sectionQuestions.length > 0) {
      setRows(buildRows(activeSection, sectionQuestions));
      setShown(true);
    } else {
      setRows([]);
      setShown(false);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [activeSection, signature, blocked]);

  const handleGenerate = useCallback(() => {
    setRows(buildRows(activeSection, sectionQuestions));
    setShown(true);
  }, [activeSection, sectionQuestions]);

  const patchRow = useCallback((index: number, patch: Partial<BuilderRow>) => {
    setRows((prev) => prev.map((row, i) => (i === index ? { ...row, ...patch } : row)));
  }, []);

  async function handleSaveAll() {
    const invalid = rows.find((row) => !row.correctAnswer);
    if (invalid) {
      onNotify('error', `Q${invalid.number} needs a correct answer before saving.`);
      return;
    }

    setSaving(true);
    try {
      // Build the section's MCQ-3 questions and merge them into the full list,
      // leaving every other sub-section untouched, then persist in one call.
      const built: ListeningAuthoredQuestion[] = rows.map((row) => {
        const previous = row.id ? sectionQuestions.find((q) => q.id === row.id) ?? null : null;
        return {
          ...(previous ?? {}),
          id: row.id ?? `lq-${row.number}`,
          number: row.number,
          partCode: activeSection as ListeningAuthoredQuestion['partCode'],
          type: 'multiple_choice_3',
          stem: 'See PDF',
          options: [...MCQ3_OPTIONS],
          correctAnswer: row.correctAnswer,
          acceptedAnswers: [],
          explanation: row.rationale.trim() ? row.rationale.trim() : null,
          skillTag: previous?.skillTag ?? null,
          transcriptExcerpt: previous?.transcriptExcerpt ?? null,
          distractorExplanation: previous?.distractorExplanation ?? null,
          points: previous?.points ?? 1,
        } as ListeningAuthoredQuestion;
      });

      const builtNumbers = new Set(built.map((q) => q.number));
      const others = allQuestions.filter((q) => !builtNumbers.has(q.number));
      const next = [...others, ...built].sort((a, b) => a.number - b.number);

      const result = await replaceListeningStructure(paperId, next);
      onNotify('success', `Saved ${rows.length} answer${rows.length === 1 ? '' : 's'}.`);
      await onSaved(result.questions);
    } catch (err: unknown) {
      onNotify('error', err instanceof Error ? err.message : 'Save failed');
    } finally {
      setSaving(false);
    }
  }

  return (
    <Card surface="tinted-primary">
      <CardHeader>
        <div className="min-w-0">
          <CardTitle className="text-sm">Answer sheet — Part {partCode} {activeSection}</CardTitle>
          <CardDescription>
            Read the question on the PDF, then record the correct option for each item.
          </CardDescription>
        </div>
        {shown && !blocked ? (
          <div className="ml-auto">
            <Button variant="primary" size="sm" onClick={handleSaveAll} disabled={saving} startIcon={<Save className="h-3.5 w-3.5" />}>
              {saving ? 'Saving…' : 'Save all answers'}
            </Button>
          </div>
        ) : null}
      </CardHeader>
      <CardContent>
        {blocked ? (
          <InlineAlert variant="warning">
            These questions were authored with the advanced editor. Edit them in the question list below to avoid overwriting custom content.
          </InlineAlert>
        ) : !shown ? (
          <div className="text-center py-6 space-y-3">
            <p className="text-sm text-admin-fg-muted">No answers yet for Part {partCode} {activeSection}.</p>
            <Button variant="primary" size="sm" onClick={handleGenerate} startIcon={<Sparkles className="h-4 w-4" />}>
              Generate from PDF template
            </Button>
          </div>
        ) : (
          <div className="space-y-2">
            {rows.map((row, index) => {
              const existing = row.id ? sectionQuestions.find((q) => q.id === row.id) ?? null : null;
              return (
                <div
                  key={row.number}
                  className="space-y-2 rounded-lg border border-admin-border bg-admin-bg-subtle px-3 py-2"
                >
                  <div className="flex items-center gap-3">
                    <span className="w-10 shrink-0 text-xs font-mono font-semibold text-admin-fg-muted">Q{row.number}</span>
                    <Badge variant="muted" size="sm" className="shrink-0">MCQ (3)</Badge>
                    <div className="flex-1 min-w-0">
                      <Select
                        aria-label={`Correct answer for question ${row.number}`}
                        placeholder="Select answer"
                        options={LETTER_OPTIONS}
                        value={row.correctAnswer}
                        onChange={(e) => patchRow(index, { correctAnswer: e.target.value })}
                      />
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
                  <Textarea
                    aria-label={`Rationale for question ${row.number} (optional)`}
                    value={row.rationale}
                    onChange={(e) => patchRow(index, { rationale: e.target.value })}
                    rows={2}
                    placeholder="Why this answer is correct — shown to the learner on the results page (optional)"
                  />
                </div>
              );
            })}
          </div>
        )}
      </CardContent>
    </Card>
  );
}
