'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { cn } from '@/lib/utils';
import type { WritingCaseNoteSectionDto, WritingRecipientDto } from '@/lib/writing/types';

/** A pre-parsed case-note section (alternative to raw markdown). */
export interface CaseNoteSection {
  /** Section heading, e.g. "Patient Details". */
  heading: string;
  /** Body lines. A line beginning with "- " renders as a bullet. */
  lines: string[];
}

export interface CaseNotePdfViewerProps {
  /** Raw case-notes markdown. Provide this OR `sections`/`caseNoteSections`. */
  caseNotesMarkdown?: string;
  /** Pre-parsed sections (internal shape — `lines`). */
  sections?: CaseNoteSection[];
  /**
   * Authored case-note sections from the task DTO (spec §4/§5 — `items`).
   * Preferred over `caseNotesMarkdown` when present; rendered as a bulleted
   * list under each heading.
   */
  caseNoteSections?: WritingCaseNoteSectionDto[];
  /**
   * Optional recipient block (To: …) rendered as the document letterhead so
   * the computer-mode viewer matches the paper booklet (spec §10.1).
   */
  recipient?: WritingRecipientDto | null;
  /** Optional task prompt / instruction shown above the case notes. */
  taskPrompt?: string | null;
  /** Document title shown on the toolbar strip. */
  title?: string;
  /**
   * When true the document body is non-selectable and copy/cut/context-menu
   * are blocked — used to honour the OET 5-minute reading-window lock while
   * still letting the learner scroll + zoom. Defaults to false. `locked` is a
   * spec-named alias for the same behaviour.
   */
  readingWindowLocked?: boolean;
  /** Spec-named alias for `readingWindowLocked`. */
  locked?: boolean;
  /** Optional extra className for the outer container. */
  className?: string;
}

/** Convert authored `{ heading, items }` sections to the internal shape. */
function fromDtoSections(dto: WritingCaseNoteSectionDto[]): CaseNoteSection[] {
  return dto.map((s) => ({
    heading: s.heading,
    // Render each item as a bullet line so `renderSectionBody` lists them.
    lines: s.items.map((item) => (/^[-*]\s+/.test(item) ? item : `- ${item}`)),
  }));
}

/** Discrete zoom stops (percent). */
const ZOOM_STOPS = [90, 100, 115, 130] as const;
const DEFAULT_ZOOM_INDEX = 1; // 100%

/**
 * Parse the tiny case-note markdown dialect into sections.
 * Supports `## heading`, `- bullet`, blank-line paragraph breaks, plain lines.
 * Content before the first heading becomes an untitled leading section.
 */
function parseMarkdownSections(md: string): CaseNoteSection[] {
  const sections: CaseNoteSection[] = [];
  let current: CaseNoteSection | null = null;

  for (const raw of md.split('\n')) {
    const line = raw.trimEnd();
    const headingMatch = /^#{1,3}\s+(.*)$/.exec(line);
    if (headingMatch) {
      current = { heading: headingMatch[1], lines: [] };
      sections.push(current);
    } else if (line === '') {
      if (current && current.lines.length > 0) current.lines.push('');
    } else {
      if (!current) {
        current = { heading: '', lines: [] };
        sections.push(current);
      }
      current.lines.push(line);
    }
  }

  for (const section of sections) {
    while (section.lines.length > 0 && section.lines[section.lines.length - 1] === '') {
      section.lines.pop();
    }
  }
  return sections;
}

/** Render one section's body, grouping consecutive bullets into a list. */
function renderSectionBody(lines: string[]): React.ReactNode[] {
  const out: React.ReactNode[] = [];
  let bullets: string[] = [];
  let key = 0;

  const flush = () => {
    if (bullets.length === 0) return;
    out.push(
      <ul key={`ul-${key++}`} className="ml-5 list-disc space-y-1.5">
        {bullets.map((b, i) => (
          <li key={i}>{b}</li>
        ))}
      </ul>,
    );
    bullets = [];
  };

  for (const line of lines) {
    if (/^[-*]\s+/.test(line)) {
      bullets.push(line.replace(/^[-*]\s+/, ''));
    } else if (line === '') {
      flush();
    } else {
      flush();
      out.push(
        <p key={`p-${key++}`} className="leading-7">
          {line}
        </p>,
      );
    }
  }
  flush();
  return out;
}

/**
 * CaseNotePdfViewer — presentational, PDF-like case-note surface.
 *
 * Renders the case notes inside a scrollable "document page" with a soft
 * shadow + border to evoke a printed sheet, plus discrete zoom controls
 * (90 / 100 / 115 / 130%) and ctrl/⌘ + wheel zoom. Scrolls independently of
 * its surroundings. Accepts raw markdown (`caseNotesMarkdown`), authored
 * `caseNoteSections` ({ heading, items }), and an optional `recipient` +
 * `taskPrompt` so the computer-mode left pane mirrors the paper booklet.
 * No editing or timing logic lives here — the host owns the reading-window
 * lock and passes `readingWindowLocked` / `locked` to gate selection.
 */
export function CaseNotePdfViewer({
  caseNotesMarkdown,
  sections,
  caseNoteSections,
  recipient,
  taskPrompt,
  title = 'Case notes',
  readingWindowLocked = false,
  locked = false,
  className,
}: CaseNotePdfViewerProps) {
  const [zoomIndex, setZoomIndex] = useState(DEFAULT_ZOOM_INDEX);
  const scrollRef = useRef<HTMLDivElement>(null);

  // `locked` is the spec-named alias; either one engages the reading-window lock.
  const isLocked = readingWindowLocked || locked;

  const resolvedSections = useMemo<CaseNoteSection[]>(() => {
    if (sections) return sections;
    if (caseNoteSections && caseNoteSections.length > 0) return fromDtoSections(caseNoteSections);
    if (caseNotesMarkdown) return parseMarkdownSections(caseNotesMarkdown);
    return [];
  }, [sections, caseNoteSections, caseNotesMarkdown]);

  const zoom = ZOOM_STOPS[zoomIndex];
  const canZoomOut = zoomIndex > 0;
  const canZoomIn = zoomIndex < ZOOM_STOPS.length - 1;

  const zoomOut = useCallback(() => setZoomIndex((i) => Math.max(0, i - 1)), []);
  const zoomIn = useCallback(
    () => setZoomIndex((i) => Math.min(ZOOM_STOPS.length - 1, i + 1)),
    [],
  );
  const resetZoom = useCallback(() => setZoomIndex(DEFAULT_ZOOM_INDEX), []);

  // ctrl/⌘ + wheel to zoom. Non-passive native listener so we can preventDefault
  // the browser's page-zoom; one step per wheel notch.
  useEffect(() => {
    const node = scrollRef.current;
    if (!node) return;
    const onWheel = (e: WheelEvent) => {
      if (!(e.ctrlKey || e.metaKey)) return;
      e.preventDefault();
      if (e.deltaY < 0) setZoomIndex((i) => Math.min(ZOOM_STOPS.length - 1, i + 1));
      else if (e.deltaY > 0) setZoomIndex((i) => Math.max(0, i - 1));
    };
    node.addEventListener('wheel', onWheel, { passive: false });
    return () => node.removeEventListener('wheel', onWheel);
  }, []);

  const lockProps = isLocked
    ? {
        onCopy: (e: React.ClipboardEvent) => e.preventDefault(),
        onCut: (e: React.ClipboardEvent) => e.preventDefault(),
        onContextMenu: (e: React.MouseEvent) => e.preventDefault(),
        onDragStart: (e: React.DragEvent) => e.preventDefault(),
      }
    : {};

  const zoomBtn =
    'flex h-7 w-7 items-center justify-center rounded-md border border-border text-muted transition-colors duration-150 hover:bg-background-light disabled:cursor-not-allowed disabled:opacity-40';

  return (
    <div
      className={cn(
        'flex h-full flex-col overflow-hidden bg-navy/5',
        className,
      )}
    >
      {/* Toolbar */}
      <div className="flex shrink-0 items-center justify-between gap-2 border-b border-border bg-surface/90 px-3 py-2 backdrop-blur">
        <span className="truncate text-sm font-bold text-navy">{title}</span>
        <div className="flex items-center gap-1" role="group" aria-label="Zoom case notes">
          <button type="button" onClick={zoomOut} disabled={!canZoomOut} aria-label="Zoom out" className={zoomBtn}>
            <svg viewBox="0 0 16 16" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden="true">
              <path d="M3 8h10" />
            </svg>
          </button>
          <button
            type="button"
            onClick={resetZoom}
            aria-label={`Zoom level ${zoom} percent. Activate to reset to 100 percent`}
            className="min-w-[3.25rem] rounded-md px-1.5 py-1 text-xs font-bold tabular-nums text-muted transition-colors duration-150 hover:bg-background-light"
          >
            {zoom}%
          </button>
          <button type="button" onClick={zoomIn} disabled={!canZoomIn} aria-label="Zoom in" className={zoomBtn}>
            <svg viewBox="0 0 16 16" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden="true">
              <path d="M8 3v10M3 8h10" />
            </svg>
          </button>
        </div>
      </div>

      {/* Scrollable page surface (independent vertical scroll) */}
      <div
        ref={scrollRef}
        className="flex-1 overflow-auto overscroll-contain px-4 py-5"
        tabIndex={0}
        aria-label={`${title} document`}
      >
        <article
          {...lockProps}
          className={cn(
            'mx-auto origin-top rounded-sm border border-border bg-white px-8 py-9 text-navy shadow-[0_1px_3px_rgba(15,23,42,0.08),0_8px_24px_-12px_rgba(15,23,42,0.25)] transition-transform duration-150 motion-reduce:transition-none',
            isLocked && 'select-none',
          )}
          style={{ width: '100%', maxWidth: '640px', transform: `scale(${zoom / 100})` }}
        >
          {recipient && (recipient.name || recipient.role || recipient.organisation || recipient.address) ? (
            <header className="mb-6 border-b border-border/70 pb-4">
              <p className="text-[10px] font-bold uppercase tracking-wide text-muted">To</p>
              <div className="mt-1 space-y-0.5 text-[15px] leading-6">
                {recipient.name ? <p className="font-bold">{recipient.name}</p> : null}
                {recipient.role ? <p>{recipient.role}</p> : null}
                {recipient.organisation ? <p>{recipient.organisation}</p> : null}
                {recipient.address ? <p className="whitespace-pre-line text-muted">{recipient.address}</p> : null}
              </div>
            </header>
          ) : null}

          {taskPrompt ? (
            <section className="mb-6 space-y-2">
              <h3 className="border-b border-border/70 pb-1 text-sm font-bold uppercase tracking-wide text-navy">
                Writing task
              </h3>
              <div className="space-y-2 text-[15px]">{renderSectionBody(taskPrompt.split('\n'))}</div>
            </section>
          ) : null}

          {resolvedSections.length === 0 ? (
            taskPrompt || recipient ? null : (
              <p className="text-sm italic text-muted">No case notes available.</p>
            )
          ) : (
            <div className="space-y-6 text-[15px]">
              {resolvedSections.map((section, i) => (
                <section key={i} className="space-y-2">
                  {section.heading ? (
                    <h3 className="border-b border-border/70 pb-1 text-sm font-bold uppercase tracking-wide text-navy">
                      {section.heading}
                    </h3>
                  ) : null}
                  <div className="space-y-2">{renderSectionBody(section.lines)}</div>
                </section>
              ))}
            </div>
          )}
        </article>
      </div>
    </div>
  );
}
