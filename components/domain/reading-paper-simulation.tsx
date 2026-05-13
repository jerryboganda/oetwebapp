'use client';

import { useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight, Eraser, FileText, Highlighter, Pencil, Printer } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { cn } from '@/lib/utils';
import type { ReadingLearnerStructureDto, ReadingQuestionLearnerDto } from '@/lib/reading-authoring-api';
import {
  buildPartABookletPages,
  buildPartBCBookletPages,
  formatWallTimer,
  getReadingPaperPhase,
  type ReadingPaperBookletPage,
} from '@/lib/reading-paper-simulation';

type PaperTool = 'pencil' | 'highlighter' | 'eraser';

type QuestionPaperAsset = NonNullable<ReadingLearnerStructureDto['paper']['questionPaperAssets']>[number];

export interface ReadingPaperSimulationProps {
  structure: ReadingLearnerStructureDto;
  answers: Record<string, string>;
  partADeadlineAt: string;
  partBCDeadlineAt: string;
  nowMs: number;
  locked: boolean;
  questionPaperAssets?: QuestionPaperAsset[];
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}

export function ReadingPaperSimulation({
  structure,
  answers,
  partADeadlineAt,
  partBCDeadlineAt,
  nowMs,
  locked,
  questionPaperAssets = [],
  onAnswerChange,
}: ReadingPaperSimulationProps) {
  const [tool, setTool] = useState<PaperTool>('pencil');
  const [pageIndex, setPageIndex] = useState(0);
  const phase = getReadingPaperPhase({ partADeadlineAt, partBCDeadlineAt }, nowMs);
  const partA = structure.parts.find((part) => part.partCode === 'A');
  const bcPages = useMemo(() => buildPartBCBookletPages(structure), [structure]);
  const partAPages = useMemo(() => buildPartABookletPages(structure), [structure]);
  const activePage = bcPages[Math.min(pageIndex, Math.max(0, bcPages.length - 1))];

  if (phase === 'expired') {
    return <ReadingPaperCollectedNotice label="The Reading answer booklets have been collected." />;
  }

  return (
    <section className="space-y-4" aria-label="Paper-based Reading simulation">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-surface p-3 shadow-sm">
        <ReadingPaperWallTimer
          label={phase === 'partA' ? 'Part A wall timer' : 'B/C wall timer'}
          seconds={Math.max(0, Math.floor((new Date(phase === 'partA' ? partADeadlineAt : partBCDeadlineAt).getTime() - nowMs) / 1000))}
        />
        <div className="flex flex-wrap items-center gap-2">
          <ReadingPaperSourceControls assets={questionPaperAssets} />
          <ReadingPaperAnnotationToolbar value={tool} onChange={setTool} />
        </div>
      </div>

      {phase === 'partA' && partA ? (
        <div className="grid gap-4 xl:grid-cols-[minmax(0,1.15fr)_minmax(360px,0.85fr)]">
          <ReadingPaperBooklet title="Part A Text Booklet" pages={partAPages} structure={structure} />
          <ReadingPartAAnswerSheet part={partA} answers={answers} locked={locked} onAnswerChange={onAnswerChange} />
        </div>
      ) : (
        <ReadingPartBCBooklet
          structure={structure}
          page={activePage}
          pageIndex={pageIndex}
          pageCount={bcPages.length}
          answers={answers}
          locked={locked}
          onPageChange={setPageIndex}
          onAnswerChange={onAnswerChange}
        />
      )}
    </section>
  );
}

export function ReadingPaperSourceControls({ assets }: { assets: QuestionPaperAsset[] }) {
  const [openingAssetId, setOpeningAssetId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const handleOpen = async (asset: QuestionPaperAsset) => {
    setOpeningAssetId(asset.id);
    setError(null);
    const openedWindow = window.open('about:blank', '_blank');
    if (openedWindow) {
      openedWindow.opener = null;
    }
    try {
      const objectUrl = await fetchAuthorizedObjectUrl(asset.downloadPath);
      if (openedWindow && !openedWindow.closed) {
        openedWindow.location.assign(objectUrl);
      } else {
        const fallbackWindow = window.open(objectUrl, '_blank', 'noopener,noreferrer');
        if (!fallbackWindow) {
          throw new Error('The browser blocked the PDF popup.');
        }
      }
      window.setTimeout(() => URL.revokeObjectURL(objectUrl), 10_000);
    } catch (err) {
      if (openedWindow && !openedWindow.closed) {
        openedWindow.close();
      }
      setError(err instanceof Error ? err.message : 'Unable to open original paper');
    } finally {
      setOpeningAssetId(null);
    }
  };

  const handlePrint = () => {
    if (typeof window !== 'undefined') window.print();
  };

  return (
    <div className="flex flex-wrap items-center gap-2" aria-label="Original paper controls">
      {assets.map((asset, index) => (
        <Button
          key={asset.id}
          type="button"
          variant="outline"
          size="sm"
          onClick={() => void handleOpen(asset)}
          disabled={openingAssetId === asset.id}
        >
          <FileText className="h-4 w-4" aria-hidden="true" />
          {asset.part ? `PDF ${asset.part}` : index === 0 ? 'Original PDF' : asset.title}
        </Button>
      ))}
      <Button variant="outline" size="sm" onClick={handlePrint} aria-label="Print paper view">
        <Printer className="h-4 w-4" aria-hidden="true" />
        Print
      </Button>
      {error ? <span className="text-xs font-medium text-danger" role="alert">{error}</span> : null}
    </div>
  );
}

export function ReadingPaperWallTimer({ label, seconds }: { label: string; seconds: number }) {
  return (
    <div className="inline-flex items-center gap-3 rounded-lg bg-background-light px-4 py-2" role="timer" aria-label={`${label}, ${formatWallTimer(seconds)} remaining`}>
      <span className="text-xs font-black uppercase text-muted">{label}</span>
      <span className="font-mono text-xl font-black text-navy">{formatWallTimer(seconds)}</span>
    </div>
  );
}

export function ReadingPaperAnnotationToolbar({ value, onChange }: { value: PaperTool; onChange: (tool: PaperTool) => void }) {
  const tools: Array<{ value: PaperTool; label: string; icon: typeof Pencil }> = [
    { value: 'pencil', label: 'Pencil', icon: Pencil },
    { value: 'highlighter', label: 'Highlighter', icon: Highlighter },
    { value: 'eraser', label: 'Eraser', icon: Eraser },
  ];

  return (
    <div className="inline-flex rounded-lg border border-border bg-background-light p-1" aria-label="Paper annotation tools">
      {tools.map((tool) => {
        const Icon = tool.icon;
        return (
          <button
            key={tool.value}
            type="button"
            className={cn('rounded-md p-2 text-muted hover:bg-surface hover:text-navy', value === tool.value && 'bg-surface text-primary')}
            aria-label={tool.label}
            aria-pressed={value === tool.value}
            onClick={() => onChange(tool.value)}
          >
            <Icon className="h-4 w-4" aria-hidden="true" />
          </button>
        );
      })}
    </div>
  );
}

export function ReadingPaperBooklet({
  title,
  pages,
  structure,
}: {
  title: string;
  pages: ReadingPaperBookletPage[];
  structure: ReadingLearnerStructureDto;
}) {
  const textById = new Map(structure.parts.flatMap((part) => part.texts.map((text) => [text.id, text] as const)));

  return (
    <section className="min-h-[620px] rounded-lg border border-border bg-[#fbfaf7] p-5 shadow-sm" aria-label={title}>
      <div className="mb-4 flex items-center justify-between gap-3">
        <h2 className="text-sm font-black uppercase text-muted">{title}</h2>
        <Badge variant="muted">Booklet</Badge>
      </div>
      <div className="space-y-5">
        {pages.map((page) => (
          <ReadingPaperBookletPage key={page.id} page={page} textById={textById} />
        ))}
      </div>
    </section>
  );
}

export function ReadingPaperBookletPage({ page, textById }: { page: ReadingPaperBookletPage; textById: Map<string, ReadingLearnerStructureDto['parts'][number]['texts'][number]> }) {
  return (
    <article className="rounded-md border border-border/60 bg-white p-4 shadow-xs">
      <h3 className="mb-3 text-sm font-bold text-navy">{page.label}</h3>
      {page.textIds.map((textId) => {
        const text = textById.get(textId);
        if (!text) return null;
        return (
          <div key={text.id} className="space-y-2">
            <h4 className="text-base font-bold text-navy">{text.title}</h4>
            <div className="prose prose-sm max-w-none text-navy selection:bg-warning/30" dangerouslySetInnerHTML={{ __html: text.bodyHtml }} />
          </div>
        );
      })}
    </article>
  );
}

export function ReadingPartAAnswerSheet({
  part,
  answers,
  locked,
  onAnswerChange,
}: {
  part: ReadingLearnerStructureDto['parts'][number];
  answers: Record<string, string>;
  locked: boolean;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}) {
  return (
    <section className="rounded-lg border border-border bg-surface p-5 shadow-sm" aria-label="Part A answer sheet">
      <h2 className="text-sm font-black uppercase text-muted">Part A Answer Sheet</h2>
      <div className="mt-4 grid gap-3">
        {part.questions.map((question) => (
          <PaperQuestionControl key={question.id} question={question} answers={answers} locked={locked} onAnswerChange={onAnswerChange} />
        ))}
      </div>
    </section>
  );
}

export function ReadingPartBCBooklet({
  structure,
  page,
  pageIndex,
  pageCount,
  answers,
  locked,
  onPageChange,
  onAnswerChange,
}: {
  structure: ReadingLearnerStructureDto;
  page: ReadingPaperBookletPage | undefined;
  pageIndex: number;
  pageCount: number;
  answers: Record<string, string>;
  locked: boolean;
  onPageChange: (next: number) => void;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}) {
  const textById = new Map(structure.parts.flatMap((part) => part.texts.map((text) => [text.id, text] as const)));
  const questionById = new Map(structure.parts.flatMap((part) => part.questions.map((question) => [question.id, question] as const)));

  if (!page) return <ReadingPaperCollectedNotice label="No B/C booklet pages are available." />;

  return (
    <section className="rounded-lg border border-border bg-[#fbfaf7] p-5 shadow-sm" aria-label="Parts B and C combined booklet">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-sm font-black uppercase text-muted">Parts B and C Combined Booklet</h2>
          <p className="mt-1 text-sm font-semibold text-navy">{page.label}</p>
        </div>
        <div className="flex items-center gap-2">
          <Button variant="outline" size="sm" disabled={pageIndex <= 0} onClick={() => onPageChange(pageIndex - 1)}>
            <ChevronLeft className="h-4 w-4" aria-hidden="true" />
            Previous
          </Button>
          <span className="min-w-16 text-center text-xs font-bold text-muted">{pageIndex + 1}/{pageCount}</span>
          <Button variant="outline" size="sm" disabled={pageIndex >= pageCount - 1} onClick={() => onPageChange(pageIndex + 1)}>
            Next
            <ChevronRight className="h-4 w-4" aria-hidden="true" />
          </Button>
        </div>
      </div>

      <div className="grid gap-4 lg:grid-cols-2">
        <div className="space-y-4">
          {page.textIds.map((textId) => {
            const text = textById.get(textId);
            return text ? (
              <article key={text.id} className="rounded-md border border-border/60 bg-white p-4">
                <h3 className="text-base font-bold text-navy">{text.title}</h3>
                <div className="prose prose-sm mt-3 max-w-none text-navy selection:bg-warning/30" dangerouslySetInnerHTML={{ __html: text.bodyHtml }} />
              </article>
            ) : null;
          })}
        </div>
        <div className="space-y-3">
          {page.questionIds.map((questionId) => {
            const question = questionById.get(questionId);
            return question ? <PaperQuestionControl key={question.id} question={question} answers={answers} locked={locked} onAnswerChange={onAnswerChange} /> : null;
          })}
        </div>
      </div>
    </section>
  );
}

export function ReadingPaperMcqCircles({ question, value, locked, onChange }: { question: ReadingQuestionLearnerDto; value: unknown; locked: boolean; onChange: (value: string) => void }) {
  const options = toPaperOptionList(question.options);
  return (
    <div className="grid gap-2">
      {options.map((option, index) => {
        const letter = option.value || String.fromCharCode(65 + index);
        return (
          <label key={`${question.id}-${letter}`} className="flex min-h-10 cursor-pointer items-center gap-3 rounded-md border border-border bg-background-light px-3 py-2 text-sm">
            <input className="sr-only" type="radio" name={question.id} disabled={locked} checked={value === letter} onChange={() => onChange(letter)} />
            <span className={cn('flex h-6 w-6 items-center justify-center rounded-full border-2 border-navy text-xs font-black', value === letter && 'bg-navy text-white')}>{letter}</span>
            <span className="leading-6 text-navy">{option.label}</span>
          </label>
        );
      })}
    </div>
  );
}

export function ReadingPaperCollectedNotice({ label }: { label: string }) {
  return (
    <section className="rounded-lg border border-border bg-surface p-8 text-center shadow-sm">
      <p className="text-base font-bold text-navy">{label}</p>
    </section>
  );
}

function PaperQuestionControl({
  question,
  answers,
  locked,
  onAnswerChange,
}: {
  question: ReadingQuestionLearnerDto;
  answers: Record<string, string>;
  locked: boolean;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}) {
  const current = parseAnswer(answers[question.id] ?? '');
  return (
    <div className="rounded-md border border-border bg-white p-3">
      <p className="text-xs font-black uppercase text-muted">Question {question.displayOrder}</p>
      <h3 className="mt-1 text-sm font-bold leading-6 text-navy selection:bg-warning/30">{question.stem}</h3>
      <div className="mt-3">
        {question.questionType === 'MultipleChoice3' || question.questionType === 'MultipleChoice4' ? (
          <ReadingPaperMcqCircles question={question} value={current} locked={locked} onChange={(value) => onAnswerChange(question, value)} />
        ) : (
          <input
            className="min-h-10 w-full rounded-md border border-border bg-background-light px-3 py-2 text-sm text-navy outline-none focus:border-primary disabled:opacity-70"
            disabled={locked}
            value={typeof current === 'string' ? current : ''}
            onChange={(event) => onAnswerChange(question, event.target.value)}
          />
        )}
      </div>
    </div>
  );
}

function parseAnswer(valueJson: string): unknown {
  if (!valueJson) return null;
  try { return JSON.parse(valueJson); }
  catch { return valueJson; }
}

function toPaperOptionList(options: unknown): Array<{ value: string; label: string }> {
  if (!Array.isArray(options)) return [];
  return options.map((option, index) => {
    if (typeof option === 'string') return { value: String.fromCharCode(65 + index), label: option };
    if (option && typeof option === 'object') {
      const record = option as Record<string, unknown>;
      return {
        value: String(record.value ?? record.key ?? record.letter ?? String.fromCharCode(65 + index)),
        label: String(record.label ?? record.text ?? record.title ?? record.value ?? ''),
      };
    }
    return { value: String.fromCharCode(65 + index), label: String(option ?? '') };
  });
}