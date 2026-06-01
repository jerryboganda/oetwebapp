'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { ChevronLeft, ChevronRight, Eraser, FileText, Highlighter, Pencil, Printer, ZoomIn, ZoomOut } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { cn } from '@/lib/utils';
import type { ReadingLearnerStructureDto, ReadingQuestionLearnerDto } from '@/lib/reading-authoring-api';
import { sanitizeBodyHtml } from '@/lib/wizard/sanitize-html';
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
  const [zoomLevel, setZoomLevel] = useState(100);
  // Reset the zoom transform while printing so the printed booklet keeps its
  // true 1:1 scale — the on-screen zoom is purely a reading aid.
  const [printing, setPrinting] = useState(false);
  const phase = getReadingPaperPhase({ partADeadlineAt, partBCDeadlineAt }, nowMs);
  const partA = structure.parts.find((part) => part.partCode === 'A');
  const bcPages = useMemo(() => buildPartBCBookletPages(structure), [structure]);
  const partAPages = useMemo(() => buildPartABookletPages(structure), [structure]);
  const activePage = bcPages[Math.min(pageIndex, Math.max(0, bcPages.length - 1))];

  useEffect(() => {
    if (typeof window === 'undefined') return;
    const before = () => setPrinting(true);
    const after = () => setPrinting(false);
    window.addEventListener('beforeprint', before);
    window.addEventListener('afterprint', after);
    return () => {
      window.removeEventListener('beforeprint', before);
      window.removeEventListener('afterprint', after);
    };
  }, []);

  // Apply the active annotation tool when the learner finishes a text
  // selection (highlighter/pencil) or release-clicks (eraser). All marks are
  // ephemeral DOM marks scoped strictly to a booklet text — they never touch
  // answers, autosave, or scoring.
  const handleAnnotateMouseUp = (event: React.MouseEvent<HTMLDivElement>) => {
    const scope = findPaperAnnotateScope(event.target as Node);
    if (!scope) return;
    if (tool === 'eraser') {
      erasePaperSelection(scope);
      return;
    }
    applyPaperAnnotation(scope, tool);
  };

  const handleAnnotateClick = (event: React.MouseEvent<HTMLDivElement>) => {
    if (tool !== 'eraser') return;
    const mark = (event.target as HTMLElement).closest('mark[data-reading-annotation]');
    if (mark) unwrapPaperMark(mark);
  };

  if (phase === 'expired') {
    return <ReadingPaperCollectedNotice label="The Reading answer booklets have been collected." />;
  }

  const zoomScale = printing ? 1 : zoomLevel / 100;

  return (
    <section className="space-y-4" aria-label="Paper-based Reading simulation">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-surface p-3 shadow-sm">
        <ReadingPaperWallTimer
          label={phase === 'partA' ? 'Part A wall timer' : 'B/C wall timer'}
          seconds={Math.max(0, Math.floor((new Date(phase === 'partA' ? partADeadlineAt : partBCDeadlineAt).getTime() - nowMs) / 1000))}
        />
        <div className="flex flex-wrap items-center gap-2">
          <ReadingPaperSourceControls assets={questionPaperAssets} />
          <ReadingPaperZoomControls zoomLevel={zoomLevel} onZoomChange={setZoomLevel} />
          <ReadingPaperAnnotationToolbar value={tool} onChange={setTool} />
        </div>
      </div>

      <div className="overflow-x-auto">
        <div
          onMouseUp={handleAnnotateMouseUp}
          onClick={handleAnnotateClick}
          style={{ transform: `scale(${zoomScale})`, transformOrigin: 'top left' }}
          data-paper-tool={tool}
        >
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
        </div>
      </div>
    </section>
  );
}

export function ReadingPaperZoomControls({ zoomLevel, onZoomChange }: { zoomLevel: number; onZoomChange: (next: number) => void }) {
  const changeZoom = (next: number) => onZoomChange(Math.min(150, Math.max(75, next)));
  return (
    <div className="inline-flex items-center gap-1 rounded-lg border border-border bg-background-light p-1" aria-label="Paper zoom controls">
      <button
        type="button"
        className="rounded-md p-2 text-muted hover:bg-surface hover:text-navy disabled:cursor-not-allowed disabled:opacity-50"
        onClick={() => changeZoom(zoomLevel - 5)}
        disabled={zoomLevel <= 75}
        aria-label="Zoom out"
      >
        <ZoomOut className="h-4 w-4" aria-hidden="true" />
      </button>
      <span className="w-11 text-center font-mono text-xs font-bold text-navy" aria-live="polite">{zoomLevel}%</span>
      <button
        type="button"
        className="rounded-md p-2 text-muted hover:bg-surface hover:text-navy disabled:cursor-not-allowed disabled:opacity-50"
        onClick={() => changeZoom(zoomLevel + 5)}
        disabled={zoomLevel >= 150}
        aria-label="Zoom in"
      >
        <ZoomIn className="h-4 w-4" aria-hidden="true" />
      </button>
    </div>
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
    <section className="min-h-[620px] rounded-lg border border-border bg-background-light p-5 shadow-sm" aria-label={title}>
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
    <article className="rounded-md border border-border/60 bg-surface p-4 shadow-xs">
      <h3 className="mb-3 text-sm font-bold text-navy">{page.label}</h3>
      {page.textIds.map((textId) => {
        const text = textById.get(textId);
        if (!text) return null;
        return (
          <div key={text.id} className="space-y-2">
            <h4 className="text-base font-bold text-navy">{text.title}</h4>
            <div className="prose prose-sm max-w-none text-navy selection:bg-warning/30" data-reading-annotate-scope="paper-text" dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(text.bodyHtml) }} />
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
    <section className="rounded-lg border border-border bg-background-light p-5 shadow-sm" aria-label="Parts B and C combined booklet">
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

      {page.partCode === 'B' ? (
        <ReadingPartBPairs page={page} textById={textById} questionById={questionById} answers={answers} locked={locked} onAnswerChange={onAnswerChange} />
      ) : (
        <ReadingPartCColumns page={page} textById={textById} questionById={questionById} answers={answers} locked={locked} onAnswerChange={onAnswerChange} />
      )}
    </section>
  );
}

type ReadingTextLearner = ReadingLearnerStructureDto['parts'][number]['texts'][number];
type ReadingTextMap = Map<string, ReadingTextLearner>;
type ReadingQuestionMap = Map<string, ReadingQuestionLearnerDto>;

interface ReadingStackProps {
  page: ReadingPaperBookletPage;
  textById: ReadingTextMap;
  questionById: ReadingQuestionMap;
  answers: Record<string, string>;
  locked: boolean;
  onAnswerChange: (question: ReadingQuestionLearnerDto, value: unknown) => void;
}

/** Resolves the page's questions that belong to a given text, in page order. */
function questionsForText(page: ReadingPaperBookletPage, questionById: ReadingQuestionMap, textId: string): ReadingQuestionLearnerDto[] {
  return page.questionIds
    .map((id) => questionById.get(id))
    .filter((question): question is ReadingQuestionLearnerDto => Boolean(question) && question!.readingTextId === textId);
}

/**
 * Part B — each short extract paired with its single 3-option question. The
 * extract sits on the left and its question on the right (stacked on mobile);
 * all pairs scroll on one page.
 */
export function ReadingPartBPairs({ page, textById, questionById, answers, locked, onAnswerChange }: ReadingStackProps) {
  return (
    <div className="space-y-4">
      {page.textIds.map((textId) => {
        const text = textById.get(textId);
        if (!text) return null;
        const questions = questionsForText(page, questionById, textId);
        return (
          <article key={text.id} className="grid gap-4 rounded-md border border-border/60 bg-surface p-4 lg:grid-cols-2">
            <div>
              <h3 className="text-base font-bold text-navy">{text.title}</h3>
              <div className="prose prose-sm mt-3 max-w-none text-navy selection:bg-warning/30" data-reading-annotate-scope="paper-text" dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(text.bodyHtml) }} />
            </div>
            <div className="space-y-3">
              {questions.map((question) => (
                <PaperQuestionControl key={question.id} question={question} answers={answers} locked={locked} onAnswerChange={onAnswerChange} />
              ))}
            </div>
          </article>
        );
      })}
    </div>
  );
}

/**
 * Part C — one long passage on the left (sticky on desktop) with its eight
 * four-option questions stacked on the right. On mobile/tablet the passage
 * stacks above its questions.
 */
export function ReadingPartCColumns({ page, textById, questionById, answers, locked, onAnswerChange }: ReadingStackProps) {
  return (
    <div className="space-y-6">
      {page.textIds.map((textId) => {
        const text = textById.get(textId);
        if (!text) return null;
        const questions = questionsForText(page, questionById, textId);
        return (
          <div key={text.id} className="grid items-start gap-4 lg:grid-cols-2">
            <article className="rounded-md border border-border/60 bg-surface p-4 lg:sticky lg:top-4 lg:max-h-[calc(100vh-9rem)] lg:overflow-y-auto">
              <h3 className="text-base font-bold text-navy">{text.title}</h3>
              <div className="prose prose-sm mt-3 max-w-none text-navy selection:bg-warning/30" data-reading-annotate-scope="paper-text" dangerouslySetInnerHTML={{ __html: sanitizeBodyHtml(text.bodyHtml) }} />
            </article>
            <div className="space-y-3">
              {questions.map((question) => (
                <PaperQuestionControl key={question.id} question={question} answers={answers} locked={locked} onAnswerChange={onAnswerChange} />
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

export function ReadingPaperMcqCircles({ question, value, locked, onChange }: { question: ReadingQuestionLearnerDto; value: unknown; locked: boolean; onChange: (value: string) => void }) {
  const options = toPaperOptionList(question.options);
  return (
    <div className="grid gap-2">
      {options.map((option, index) => {
        const letter = option.value || String.fromCharCode(65 + index);
        return (
          <label key={`${question.id}-${letter}`} className="flex min-h-10 cursor-pointer items-center gap-3 rounded-md border border-border bg-background-light px-3 py-2 text-sm transition-colors focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/20">
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
    <div className="rounded-md border border-border bg-surface p-3">
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

// ─── Paper-mode annotations ──────────────────────────────────────────────────
// Ephemeral DOM marks applied with the active toolbar tool over the printed
// booklet text. Scoped strictly to `[data-reading-annotate-scope]` containers
// so marks never leak into question/answer controls. Highlighter = yellow
// fill, pencil = blue underline, eraser = remove marks. None of this touches
// answers, autosave, or scoring.

const PAPER_HIGHLIGHT_CLASS = 'rounded-[2px] bg-amber-200/80 text-navy dark:bg-amber-300/40 dark:text-amber-50';
const PAPER_PENCIL_CLASS = 'bg-transparent underline decoration-2 decoration-sky-600 underline-offset-2 dark:decoration-sky-400';

/** Climbs to the nearest annotation scope element, or null. */
function findPaperAnnotateScope(node: Node | null): HTMLElement | null {
  let current: Node | null = node;
  while (current && current !== document.body) {
    if (current instanceof HTMLElement && current.hasAttribute('data-reading-annotate-scope')) {
      return current;
    }
    current = current.parentNode;
  }
  return null;
}

function unwrapPaperMark(mark: Element): void {
  const parent = mark.parentNode;
  if (!parent) return;
  while (mark.firstChild) parent.insertBefore(mark.firstChild, mark);
  parent.removeChild(mark);
  parent.normalize();
}

/** Wraps the current scope-bound selection in a tool-styled mark. */
function applyPaperAnnotation(scope: HTMLElement, tool: PaperTool): void {
  if (typeof window === 'undefined') return;
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return;
  const range = selection.getRangeAt(0);
  if (!scope.contains(range.startContainer) || !scope.contains(range.endContainer)) return;

  const offsetWithin = (node: Node, nodeOffset: number): number => {
    const measure = document.createRange();
    measure.selectNodeContents(scope);
    try {
      measure.setEnd(node, nodeOffset);
    } catch {
      return 0;
    }
    return (measure.cloneContents().textContent ?? '').length;
  };

  const a = offsetWithin(range.startContainer, range.startOffset);
  const b = offsetWithin(range.endContainer, range.endOffset);
  const start = Math.min(a, b);
  const end = Math.max(a, b);
  if (end - start < 1) return;

  const className = tool === 'highlighter' ? PAPER_HIGHLIGHT_CLASS : PAPER_PENCIL_CLASS;
  const segments: Array<{ node: Text; from: number; to: number }> = [];
  const walker = document.createTreeWalker(scope, NodeFilter.SHOW_TEXT);
  let offset = 0;
  let node = walker.nextNode() as Text | null;
  while (node) {
    const len = node.textContent?.length ?? 0;
    const from = Math.max(start, offset);
    const to = Math.min(end, offset + len);
    if (from < to) segments.push({ node, from: from - offset, to: to - offset });
    offset += len;
    node = walker.nextNode() as Text | null;
  }
  for (const segment of segments.reverse()) {
    const markRange = document.createRange();
    markRange.setStart(segment.node, segment.from);
    markRange.setEnd(segment.node, segment.to);
    const mark = document.createElement('mark');
    mark.setAttribute('data-reading-annotation', tool);
    mark.className = className;
    try {
      markRange.surroundContents(mark);
    } catch {
      // Segment crosses an element boundary — skip; remaining segments still apply.
    }
  }
  selection.removeAllRanges();
}

/** Removes annotation marks intersected by the current selection. */
function erasePaperSelection(scope: HTMLElement): void {
  if (typeof window === 'undefined') return;
  const selection = window.getSelection();
  if (!selection || selection.rangeCount === 0 || selection.isCollapsed) return;
  const range = selection.getRangeAt(0);
  scope.querySelectorAll('mark[data-reading-annotation]').forEach((mark) => {
    let intersects = false;
    try {
      intersects = range.intersectsNode(mark);
    } catch {
      intersects = false;
    }
    if (intersects) unwrapPaperMark(mark);
  });
  selection.removeAllRanges();
}

