'use client';

import { useEffect, useMemo, useState } from 'react';
import { ChevronLeft, ChevronRight, Eraser, Highlighter, Pencil, Printer, ZoomIn, ZoomOut } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type { ListeningSessionDto, ListeningSessionQuestionDto } from '@/lib/listening-api';
import { LISTENING_SECTION_LABEL } from '@/lib/listening-sections';
import {
  buildListeningBookletPages,
  formatListeningWallTimer,
  type ListeningPaperBookletPage,
} from '@/lib/listening-paper-simulation';
import { PartARenderer } from '@/components/domain/listening/PartARenderer';
import { PartANotesDocument } from '@/components/domain/listening/PartANotesDocument';

type PaperTool = 'pencil' | 'highlighter' | 'eraser';

export interface ListeningPaperSimulationProps {
  session: ListeningSessionDto;
  answers: Record<string, string>;
  /** Whole-attempt wall-timer seconds remaining (drives the booklet timer). */
  attemptSecondsRemaining: number | null;
  /**
   * Free-navigation / checking-time. In paper mode this stays true so every
   * booklet page is editable for the all-parts review; the player still owns
   * the audio FSM. When false the booklet is read-only (collected).
   */
  freeNavigationActive: boolean;
  onAnswerChange: (questionId: string, value: string) => void;
}

/**
 * Listening paper/booklet answer surface for the static listening answer booklet.
 *
 * IMPORTANT: this component does NOT own the `<audio>` element, the
 * `ListeningAudioTransport`, or the FSM phase machine. Audio stays
 * platform-controlled by the player. This is purely the answer booklet:
 * Part A notes-with-gaps pages, Part B clip pages, and Part C presentation
 * pages, each with editable answers wired back to `onAnswerChange`.
 *
 * Toolbar (pencil / highlighter / eraser) uses the SAME ephemeral DOM-mark
 * approach Reading uses — marks are scoped to `[data-listening-annotate-scope]`
 * note/clip text and never touch answers, autosave, or scoring.
 */
export function ListeningPaperSimulation({
  session,
  answers,
  attemptSecondsRemaining,
  freeNavigationActive,
  onAnswerChange,
}: ListeningPaperSimulationProps) {
  const [tool, setTool] = useState<PaperTool>('pencil');
  const [pageIndex, setPageIndex] = useState(0);
  const [zoomLevel, setZoomLevel] = useState(100);
  // Reset the zoom transform while printing so the printed booklet keeps its
  // true 1:1 scale — the on-screen zoom is purely a reading aid.
  const [printing, setPrinting] = useState(false);

  const pages = useMemo(() => buildListeningBookletPages(session), [session]);
  const questionById = useMemo(
    () => new Map(session.questions.map((question) => [question.id, question] as const)),
    [session.questions],
  );
  const safeIndex = Math.min(pageIndex, Math.max(0, pages.length - 1));
  const activePage = pages[safeIndex];

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

  // Apply the active annotation tool when the candidate finishes a text
  // selection (highlighter/pencil) or release-clicks (eraser). All marks are
  // ephemeral DOM marks scoped strictly to a booklet note/clip — they never
  // touch answers, autosave, or scoring.
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
    const mark = (event.target as HTMLElement).closest('mark[data-listening-annotation]');
    if (mark) unwrapPaperMark(mark);
  };

  if (pages.length === 0) {
    return <ListeningPaperCollectedNotice label="This Listening paper does not contain any authored booklet pages yet." />;
  }

  if (!freeNavigationActive) {
    return <ListeningPaperCollectedNotice label="The Listening answer booklet has been collected." />;
  }

  const zoomScale = printing ? 1 : zoomLevel / 100;

  return (
    <section className="space-y-4" aria-label="Paper-based Listening simulation" data-testid="listening-paper-simulation">
      <div className="flex flex-wrap items-center justify-between gap-3 rounded-lg border border-border bg-surface p-3 shadow-sm">
        <ListeningPaperWallTimer
          label="Listening wall timer"
          seconds={Math.max(0, attemptSecondsRemaining ?? 0)}
        />
        <div className="flex flex-wrap items-center gap-2">
          <ListeningPaperSourceControls />
          <ListeningPaperZoomControls zoomLevel={zoomLevel} onZoomChange={setZoomLevel} />
          <ListeningPaperAnnotationToolbar value={tool} onChange={setTool} />
        </div>
      </div>

      <div className="overflow-x-auto">
        <div
          onMouseUp={handleAnnotateMouseUp}
          onClick={handleAnnotateClick}
          style={{ transform: `scale(${zoomScale})`, transformOrigin: 'top left' }}
          data-paper-tool={tool}
        >
          <ListeningPaperBooklet
            page={activePage}
            pageIndex={safeIndex}
            pageCount={pages.length}
            questionById={questionById}
            answers={answers}
            onPageChange={setPageIndex}
            onAnswerChange={onAnswerChange}
          />
        </div>
      </div>
    </section>
  );
}

export function ListeningPaperZoomControls({ zoomLevel, onZoomChange }: { zoomLevel: number; onZoomChange: (next: number) => void }) {
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

export function ListeningPaperSourceControls() {
  const handlePrint = () => {
    if (typeof window !== 'undefined') window.print();
  };
  return (
    <div className="flex flex-wrap items-center gap-2" aria-label="Paper controls">
      <Button variant="outline" size="sm" onClick={handlePrint} aria-label="Print paper view">
        <Printer className="h-4 w-4" aria-hidden="true" />
        Print
      </Button>
    </div>
  );
}

export function ListeningPaperWallTimer({ label, seconds }: { label: string; seconds: number }) {
  return (
    <div className="inline-flex items-center gap-3 rounded-lg bg-background-light px-4 py-2" role="timer" aria-label={`${label}, ${formatListeningWallTimer(seconds)} remaining`}>
      <span className="text-xs font-black uppercase text-muted">{label}</span>
      <span className="font-mono text-xl font-black text-navy">{formatListeningWallTimer(seconds)}</span>
    </div>
  );
}

export function ListeningPaperAnnotationToolbar({ value, onChange }: { value: PaperTool; onChange: (tool: PaperTool) => void }) {
  const tools: Array<{ value: PaperTool; label: string; icon: typeof Pencil }> = [
    { value: 'pencil', label: 'Pencil', icon: Pencil },
    { value: 'highlighter', label: 'Highlighter', icon: Highlighter },
    { value: 'eraser', label: 'Eraser', icon: Eraser },
  ];

  return (
    <div className="inline-flex rounded-lg border border-border bg-background-light p-1" aria-label="Paper annotation tools">
      {tools.map((entry) => {
        const Icon = entry.icon;
        return (
          <button
            key={entry.value}
            type="button"
            className={cn('rounded-md p-2 text-muted hover:bg-surface hover:text-navy', value === entry.value && 'bg-surface text-primary')}
            aria-label={entry.label}
            aria-pressed={value === entry.value}
            onClick={() => onChange(entry.value)}
          >
            <Icon className="h-4 w-4" aria-hidden="true" />
          </button>
        );
      })}
    </div>
  );
}

export function ListeningPaperBooklet({
  page,
  pageIndex,
  pageCount,
  questionById,
  answers,
  onPageChange,
  onAnswerChange,
}: {
  page: ListeningPaperBookletPage | undefined;
  pageIndex: number;
  pageCount: number;
  questionById: Map<string, ListeningSessionQuestionDto>;
  answers: Record<string, string>;
  onPageChange: (next: number) => void;
  onAnswerChange: (questionId: string, value: string) => void;
}) {
  if (!page) return <ListeningPaperCollectedNotice label="No booklet pages are available." />;

  const sectionLabel = LISTENING_SECTION_LABEL[page.section];

  return (
    <section className="rounded-lg border border-border bg-background-light p-5 shadow-sm" aria-label="Listening answer booklet">
      <div className="mb-4 flex flex-wrap items-center justify-between gap-3">
        <div>
          <h2 className="text-sm font-black uppercase text-muted">Listening Answer Booklet</h2>
          <p className="mt-1 text-sm font-semibold text-navy">{page.label}</p>
        </div>
        <div className="flex items-center gap-2">
          <Badge variant="muted">{sectionLabel}</Badge>
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

      <article className="rounded-md border border-border/60 bg-surface p-4 shadow-xs">
        {page.extract ? (
          <div className="mb-3 flex flex-wrap items-center gap-2 text-xs text-muted">
            <span className="font-semibold text-navy">{page.extract.title}</span>
            {page.extract.accentCode ? <span>{page.extract.accentCode}</span> : null}
          </div>
        ) : null}

        {page.kind === 'notes' && page.extract?.notesBody?.trim() ? (
          // Part A with an authored notes body → render as ONE continuous
          // note-completion document (PartANotesDocument) instead of 12 separate cards.
          <PartANotesDocument
            partLabel={sectionLabel}
            notesBody={page.extract.notesBody}
            questions={page.questionIds
              .map((id) => questionById.get(id))
              .filter((q): q is ListeningSessionQuestionDto => q !== undefined)
              .map((q) => ({ id: q.id, number: q.number }))}
            answers={answers}
            onAnswerChange={onAnswerChange}
            highlightingEnabled
          />
        ) : (
          // Legacy Part A (no notesBody) or Part B/C → per-question renderer.
          <div className="space-y-4">
            {page.questionIds.map((questionId) => {
              const question = questionById.get(questionId);
              if (!question) return null;
              return (
                <ListeningPaperQuestion
                  key={question.id}
                  question={question}
                  sectionLabel={sectionLabel}
                  value={answers[question.id] ?? ''}
                  onAnswerChange={onAnswerChange}
                />
              );
            })}
          </div>
        )}
      </article>
    </section>
  );
}

/**
 * One booklet question. Part A (no options) renders the notes-with-gaps
 * `PartARenderer` so editable gaps stay identical to computer mode. Part B/C
 * (options present) render the OMR-style A/B/C answer bubbles.
 */
export function ListeningPaperQuestion({
  question,
  sectionLabel,
  value,
  onAnswerChange,
}: {
  question: ListeningSessionQuestionDto;
  sectionLabel: string;
  value: string;
  onAnswerChange: (questionId: string, value: string) => void;
}) {
  if (question.options.length === 0) {
    return (
      <div
        id={`listening-question-${question.id}`}
        className="scroll-mt-48"
        data-listening-annotate-scope="paper-note"
      >
        <PartARenderer
          questionNumber={question.number}
          partLabel={sectionLabel}
          prompt={question.text}
          inputId={`listening-answer-${question.id}`}
          value={value}
          onChange={(next) => onAnswerChange(question.id, next)}
          highlightingEnabled
        />
      </div>
    );
  }

  return (
    <div id={`listening-question-${question.id}`} className="scroll-mt-48 rounded-2xl border border-border bg-surface p-5 shadow-sm">
      <div className="mb-3 flex flex-wrap items-center gap-2">
        <Badge variant="info">Q{question.number}</Badge>
        <span className="text-xs font-black uppercase tracking-widest text-muted">{sectionLabel}</span>
      </div>
      <p
        className="mb-4 rounded-xl bg-background-light p-3 text-[1.0625rem] font-medium leading-relaxed text-navy selection:bg-warning/30"
        data-listening-annotate-scope="paper-note"
      >
        {question.text}
      </p>
      <ListeningPaperOmrBubbles question={question} value={value} onChange={(next) => onAnswerChange(question.id, next)} />
    </div>
  );
}

/** OMR-style A/B/C answer bubbles for Part B/C multiple-choice questions. */
export function ListeningPaperOmrBubbles({
  question,
  value,
  onChange,
}: {
  question: ListeningSessionQuestionDto;
  value: string;
  onChange: (value: string) => void;
}) {
  return (
    <div role="radiogroup" aria-label={`Question ${question.number} options`} className="grid gap-2">
      {question.options.map((option, index) => {
        const letter = String.fromCharCode(65 + index);
        const isSelected = value === option;
        return (
          <label
            key={`${question.id}-${letter}`}
            className="flex min-h-12 cursor-pointer items-center gap-3 rounded-md border border-border bg-background-light px-3 py-2 text-sm transition-colors focus-within:border-primary focus-within:ring-2 focus-within:ring-primary/20"
          >
            <input
              className="sr-only"
              type="radio"
              name={question.id}
              checked={isSelected}
              onChange={() => onChange(option)}
            />
            <span className={cn('flex h-7 w-7 shrink-0 items-center justify-center rounded-full border-2 border-navy text-xs font-black', isSelected && 'bg-navy text-white dark:bg-violet-700 dark:border-violet-700')}>
              {letter}
            </span>
            <span className="leading-6 text-navy">{option}</span>
          </label>
        );
      })}
    </div>
  );
}

export function ListeningPaperCollectedNotice({ label }: { label: string }) {
  return (
    <section className="rounded-lg border border-border bg-surface p-8 text-center shadow-sm">
      <p className="text-base font-bold text-navy">{label}</p>
    </section>
  );
}

// ─── Paper-mode annotations ──────────────────────────────────────────────────
// Ephemeral DOM marks applied with the active toolbar tool over the printed
// booklet note/clip text. Scoped strictly to `[data-listening-annotate-scope]`
// containers so marks never leak into answer controls. Highlighter = yellow
// fill, pencil = blue underline, eraser = remove marks. None of this touches
// answers, autosave, or scoring. Mirrors the Reading paper annotation engine.

const PAPER_HIGHLIGHT_CLASS = 'rounded-[2px] bg-amber-200/80 text-navy dark:bg-amber-300/40 dark:text-amber-50';
const PAPER_PENCIL_CLASS = 'bg-transparent underline decoration-2 decoration-sky-600 underline-offset-2 dark:decoration-sky-400';

/** Climbs to the nearest annotation scope element, or null. */
function findPaperAnnotateScope(node: Node | null): HTMLElement | null {
  let current: Node | null = node;
  while (current && current !== document.body) {
    if (current instanceof HTMLElement && current.hasAttribute('data-listening-annotate-scope')) {
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
    mark.setAttribute('data-listening-annotation', tool);
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
  scope.querySelectorAll('mark[data-listening-annotation]').forEach((mark) => {
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
