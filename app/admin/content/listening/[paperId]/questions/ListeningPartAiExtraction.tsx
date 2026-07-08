'use client';

/**
 * Listening Part B / Part C — AI extraction (OCR).
 *
 * Part B/C is PDF-backed: the learner reads the MCQ on the question paper and the
 * admin only records the correct option (A/B/C) + an optional rationale. This panel
 * automates that for a WHOLE part in one go: upload the part's question paper
 * (Part C also uploads C2) + the answer-key PDF, the server OCRs + Claude-structures
 * them, and the admin proofreads the pre-filled answers and clicks one Save.
 *
 * Mirrors the per-sub-section `ListeningAnswerSheetBuilder` save shape (stem "See PDF",
 * placeholder options, correct letter, rationale → explanation) but spans the entire
 * part and persists in a single `replaceListeningStructure` call.
 */

import { useCallback, useRef, useState } from 'react';
import { Save, Sparkles, Upload } from 'lucide-react';

import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import {
  importListeningPartBCFromUpload,
  replaceListeningStructure,
  type ListeningAuthoredQuestion,
  type ListeningSubSectionCode,
} from '@/lib/listening-authoring-api';

export type ListeningExtractionPart = 'B' | 'C';

export interface ListeningPartAiExtractionProps {
  paperId: string;
  part: ListeningExtractionPart;
  /** The full authored question list (all 42 items). */
  allQuestions: ListeningAuthoredQuestion[];
  onSaved: (next: ListeningAuthoredQuestion[]) => Promise<void> | void;
  onNotify: (variant: 'success' | 'error', message: string) => void;
}

const MCQ_LETTERS = ['A', 'B', 'C'] as const;
const MCQ3_OPTIONS = ['Option A', 'Option B', 'Option C'];
const SEE_PDF_SENTINEL = 'See PDF';
const PDF_ACCEPT = 'application/pdf,.pdf,image/png,image/jpeg,image/gif,image/webp';

function isSentinelStem(stem: string | undefined): boolean {
  return (stem ?? '').trim().toLowerCase() === SEE_PDF_SENTINEL.toLowerCase();
}
function isPlaceholderOption(text: string | undefined, index: number): boolean {
  const trimmed = (text ?? '').trim();
  return trimmed.length === 0 || trimmed.toLowerCase() === MCQ3_OPTIONS[index].toLowerCase();
}

const PART_RANGE: Record<ListeningExtractionPart, [number, number]> = { B: [25, 30], C: [31, 42] };

/** Map a printed question number to its sub-section code (B: 25→B1…30→B6; C: ≤36→C1 else C2). */
function subSectionFor(part: ListeningExtractionPart, number: number): ListeningSubSectionCode {
  if (part === 'B') return `B${number - 24}` as ListeningSubSectionCode;
  return (number <= 36 ? 'C1' : 'C2') as ListeningSubSectionCode;
}

/** Resolve an existing question's stored correct answer to a letter A/B/C. */
function correctLetterOf(q: ListeningAuthoredQuestion): string {
  const raw = (q.correctAnswer ?? '').trim();
  if (!raw) return '';
  const upper = raw.toUpperCase();
  if (MCQ_LETTERS.includes(upper as (typeof MCQ_LETTERS)[number])) return upper;
  const index = (q.options ?? []).findIndex((opt) => opt.trim() === raw);
  return index >= 0 ? MCQ_LETTERS[index] ?? '' : '';
}

interface Row {
  number: number;
  stem: string;
  options: string[];
  correctAnswer: string;
  rationale: string;
}

export function ListeningPartAiExtraction({ paperId, part, allQuestions, onSaved, onNotify }: ListeningPartAiExtractionProps) {
  const [questionFile, setQuestionFile] = useState<File | null>(null);
  const [questionFile2, setQuestionFile2] = useState<File | null>(null);
  const [answerFile, setAnswerFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [saving, setSaving] = useState(false);
  const [rows, setRows] = useState<Row[] | null>(null);
  const questionInputRef = useRef<HTMLInputElement>(null);
  const question2InputRef = useRef<HTMLInputElement>(null);
  const answerInputRef = useRef<HTMLInputElement>(null);

  const [lo, hi] = PART_RANGE[part];

  const onRun = useCallback(async () => {
    if (!questionFile || !answerFile) {
      onNotify('error', 'Upload the question paper and the answer-key PDF first.');
      return;
    }
    setBusy(true);
    try {
      const result = await importListeningPartBCFromUpload(
        paperId,
        part,
        questionFile,
        part === 'C' ? questionFile2 : null,
        answerFile,
      );
      const byNum = new Map(result.answers.map((a) => [a.number, a]));
      const seeded: Row[] = [];
      for (let n = lo; n <= hi; n += 1) {
        const ai = byNum.get(n);
        const existing = allQuestions.find((q) => q.number === n) ?? null;
        // Seed stem + options from the AI extraction first, then any real existing
        // text (never a placeholder), else blank for the reviewer to fill.
        const seedStem = ai?.stem?.trim()
          ? ai.stem.trim()
          : existing && !isSentinelStem(existing.stem) ? (existing.stem ?? '') : '';
        const aiOptions = [ai?.optionA, ai?.optionB, ai?.optionC];
        const seedOptions = MCQ_LETTERS.map((_, i) => {
          if (aiOptions[i]?.trim()) return aiOptions[i]!.trim();
          const opt = existing?.options?.[i];
          return isPlaceholderOption(opt, i) ? '' : (opt ?? '');
        });
        seeded.push({
          number: n,
          stem: seedStem,
          options: seedOptions,
          correctAnswer: ai?.correctAnswer ?? (existing ? correctLetterOf(existing) : ''),
          rationale: ai?.rationale ?? existing?.explanation ?? '',
        });
      }
      setRows(seeded);
      onNotify(
        result.isStub ? 'error' : 'success',
        result.isStub
          ? `Imported with issues to review: ${result.stubReason ?? result.summary}`
          : `AI extraction ready for Part ${part} — review and Save. ${result.summary}`,
      );
    } catch (e) {
      onNotify('error', e instanceof Error ? e.message : 'AI extraction failed.');
    } finally {
      setBusy(false);
    }
  }, [paperId, part, questionFile, questionFile2, answerFile, allQuestions, lo, hi, onNotify]);

  const patchRow = useCallback((index: number, patch: Partial<Row>) => {
    setRows((prev) => (prev ? prev.map((row, i) => (i === index ? { ...row, ...patch } : row)) : prev));
  }, []);

  const patchOption = useCallback((index: number, optionIndex: number, value: string) => {
    setRows((prev) => (prev
      ? prev.map((row, i) => (i === index
          ? { ...row, options: row.options.map((opt, oi) => (oi === optionIndex ? value : opt)) }
          : row))
      : prev));
  }, []);

  const onSaveAll = useCallback(async () => {
    if (!rows) return;
    const missing = rows.find((r) => !r.correctAnswer);
    if (missing) {
      onNotify('error', `Q${missing.number} needs a correct answer before saving.`);
      return;
    }
    setSaving(true);
    try {
      const built: ListeningAuthoredQuestion[] = rows.map((row) => {
        const previous = allQuestions.find((q) => q.number === row.number) ?? null;
        return {
          ...(previous ?? {}),
          id: previous?.id ?? `lq-${row.number}`,
          number: row.number,
          partCode: subSectionFor(part, row.number),
          type: 'multiple_choice_3',
          stem: row.stem.trim() || SEE_PDF_SENTINEL,
          options: MCQ_LETTERS.map((_, i) => row.options[i]?.trim() || MCQ3_OPTIONS[i]),
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
      onNotify('success', `Saved ${rows.length} Part ${part} answer${rows.length === 1 ? '' : 's'}.`);
      await onSaved(result.questions);
    } catch (e) {
      onNotify('error', e instanceof Error ? e.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }, [rows, allQuestions, part, paperId, onNotify, onSaved]);

  const fileButtonLabel = (file: File | null, fallback: string) => (file ? file.name : fallback);

  return (
    <Card surface="tinted-primary">
      <CardHeader>
        <div className="min-w-0">
          <CardTitle className="flex items-center gap-2 text-sm">
            <Sparkles className="h-4 w-4" />
            AI extraction (OCR) — Part {part}
          </CardTitle>
          <CardDescription>
            Upload the Part {part} question paper{part === 'C' ? 's (C1 + C2)' : ''} and the answer-key PDF. The AI reads the
            question text, the three options and the correct answer for every question (Q{lo}–Q{hi}) — proofread, then Save once.
          </CardDescription>
        </div>
      </CardHeader>
      <CardContent className="space-y-4">
        {/* Upload controls */}
        <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <FilePicker
            label={part === 'C' ? 'Part C1 question paper' : 'Part B question booklet'}
            file={questionFile}
            inputRef={questionInputRef}
            disabled={busy || saving}
            onPick={setQuestionFile}
          />
          {part === 'C' ? (
            <FilePicker
              label="Part C2 question paper"
              file={questionFile2}
              inputRef={question2InputRef}
              disabled={busy || saving}
              onPick={setQuestionFile2}
            />
          ) : null}
          <FilePicker
            label="Answer-key PDF (required)"
            file={answerFile}
            inputRef={answerInputRef}
            disabled={busy || saving}
            onPick={setAnswerFile}
          />
        </div>
        <div>
          <Button
            variant="primary"
            size="sm"
            onClick={onRun}
            loading={busy}
            loadingText="Extracting…"
            disabled={busy || saving || !questionFile || !answerFile}
            startIcon={<Sparkles className="h-4 w-4" />}
          >
            Run extraction
          </Button>
        </div>

        {/* Review list (whole part) */}
        {rows ? (
          <div className="space-y-3 border-t border-admin-border pt-4">
            <div className="flex flex-wrap items-center gap-2">
              <p className="text-xs font-black uppercase tracking-widest text-admin-fg-muted">
                Review Part {part} answers (Q{lo}–Q{hi})
              </p>
              <div className="ml-auto">
                <Button
                  variant="primary"
                  size="sm"
                  onClick={onSaveAll}
                  loading={saving}
                  loadingText="Saving…"
                  disabled={busy || saving}
                  startIcon={<Save className="h-3.5 w-3.5" />}
                >
                  Save all Part {part} answers
                </Button>
              </div>
            </div>
            <div className="space-y-2">
              {rows.map((row, index) => (
                <div key={row.number} className="space-y-2 rounded-lg border border-admin-border bg-admin-bg-subtle px-3 py-2">
                  <div className="flex items-center gap-3">
                    <span className="w-10 shrink-0 text-xs font-mono font-semibold text-admin-fg-muted">Q{row.number}</span>
                    <Badge variant="muted" size="sm" className="shrink-0">{subSectionFor(part, row.number)}</Badge>
                    <Badge variant="muted" size="sm" className="shrink-0">MCQ (3)</Badge>
                    <div className="min-w-0 flex-1">
                      <Select
                        aria-label={`Correct answer for question ${row.number}`}
                        placeholder="Mark correct option"
                        options={MCQ_LETTERS.map((letter, i) => ({
                          value: letter,
                          label: row.options[i]?.trim() ? `${letter}. ${row.options[i].trim()}` : `Option ${letter}`,
                        }))}
                        value={row.correctAnswer}
                        onChange={(e) => patchRow(index, { correctAnswer: e.target.value })}
                      />
                    </div>
                  </div>
                  <Textarea
                    aria-label={`Question ${row.number} stem`}
                    value={row.stem}
                    onChange={(e) => patchRow(index, { stem: e.target.value })}
                    rows={2}
                    placeholder="Question text — shown as the heading on the learner card"
                  />
                  <div className="grid gap-2">
                    {MCQ_LETTERS.map((letter, i) => (
                      <div key={letter} className="flex items-center gap-2">
                        <span className="w-5 shrink-0 text-center text-xs font-black text-admin-fg-muted">{letter}</span>
                        <Input
                          aria-label={`Question ${row.number} option ${letter}`}
                          value={row.options[i] ?? ''}
                          onChange={(e) => patchOption(index, i, e.target.value)}
                          placeholder={`Option ${letter} text`}
                        />
                      </div>
                    ))}
                  </div>
                  <Textarea
                    aria-label={`Rationale for question ${row.number} (optional)`}
                    value={row.rationale}
                    onChange={(e) => patchRow(index, { rationale: e.target.value })}
                    rows={2}
                    placeholder="Why this answer is correct — shown to the learner on the results page (optional)"
                  />
                </div>
              ))}
            </div>
          </div>
        ) : null}
      </CardContent>
    </Card>
  );
}

function FilePicker({
  label,
  file,
  inputRef,
  disabled,
  onPick,
}: {
  label: string;
  file: File | null;
  inputRef: React.RefObject<HTMLInputElement | null>;
  disabled: boolean;
  onPick: (file: File | null) => void;
}) {
  return (
    <div className="space-y-1.5">
      <label className="block text-xs font-black uppercase tracking-widest text-admin-fg-muted">{label}</label>
      <input
        ref={inputRef}
        type="file"
        accept={PDF_ACCEPT}
        className="hidden"
        onChange={(e) => onPick(e.target.files?.[0] ?? null)}
      />
      <Button
        type="button"
        variant={file ? 'secondary' : 'outline'}
        size="sm"
        className="w-full justify-start"
        disabled={disabled}
        onClick={() => inputRef.current?.click()}
        startIcon={<Upload className="h-4 w-4" />}
      >
        <span className="truncate">{file ? file.name : 'Choose PDF / image'}</span>
      </Button>
    </div>
  );
}
