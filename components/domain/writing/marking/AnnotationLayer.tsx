'use client';

/**
 * AnnotationLayer — reusable text-span annotation tool for the tutor/expert
 * writing review screens (spec §14).
 *
 * Responsibilities:
 *  - Render the student's response as selectable plain text with existing
 *    annotations painted as inline highlights (severity-coded, but never by
 *    colour alone — each highlight carries a severity glyph + title).
 *  - On selection, surface an origin-aware popover anchored to the selected
 *    range (it opens from the selection rectangle, it does not jump to a fixed
 *    corner) letting the tutor pick criterion + severity + suggestion +
 *    feedback, then emit an onCreate with computed offsets.
 *  - Show a side list of annotations grouped/ordered by position, each
 *    keyboard-reachable and deletable.
 *
 * ── Offset computation approach ───────────────────────────────────────────────
 * Offsets are always relative to the *plain text* of the response container —
 * i.e. `container.textContent`, which equals the original `responseText`
 * (`submission.letterContent`). When the user selects text we walk the
 * container with a TreeWalker over TEXT nodes, accumulating the length of every
 * text node before the selection's start/end containers and adding the local
 * Range offset. This yields stable {startOffset, endOffset} that do not depend
 * on how the text is chunked into highlight <mark> spans. Rendering does the
 * inverse: it slices `responseText` at the sorted, de-overlapped annotation
 * boundaries, so a freshly created annotation lands exactly where it was drawn.
 * Because every highlight span is itself made of text nodes, re-selecting over
 * an already-highlighted region still produces correct global offsets.
 */

import {
  useCallback,
  useEffect,
  useLayoutEffect,
  useMemo,
  useRef,
  useState,
  type CSSProperties,
} from 'react';
import { MessageSquarePlus, Trash2, X } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import type {
  WritingCriterionCode,
  WritingFeedbackAnnotationDto,
  WritingSeverity,
} from '@/lib/writing/types';
import type { WritingAnnotationCreatePayload } from '@/lib/writing/exam-api';
import {
  ANNOTATION_CRITERION_OPTIONS,
  CRITERION_LABEL,
  SEVERITY_ORDER,
  SEVERITY_STYLE,
} from './shared';

export interface AnnotationLayerProps {
  responseText: string;
  annotations: WritingFeedbackAnnotationDto[];
  onCreate: (payload: WritingAnnotationCreatePayload) => Promise<void> | void;
  onDelete: (annotationId: string) => Promise<void> | void;
  /** When true the layer is read-only: selection popover is disabled. */
  readOnly?: boolean;
  busy?: boolean;
  className?: string;
}

interface PendingSelection {
  text: string;
  startOffset: number;
  endOffset: number;
  /** Anchor rect (viewport coords) of the selection, for origin-aware popover. */
  anchor: { top: number; bottom: number; left: number; right: number };
}

interface DraftAnnotation {
  criterion: WritingCriterionCode | 'general';
  severity: WritingSeverity;
  suggestion: string;
  feedbackText: string;
}

const DEFAULT_DRAFT: DraftAnnotation = {
  criterion: 'general',
  severity: 'medium',
  suggestion: '',
  feedbackText: '',
};

/** Walk TEXT nodes to convert a DOM Range into container-relative offsets. */
function getSelectionOffsets(container: HTMLElement, range: Range): { startOffset: number; endOffset: number } {
  const walker = document.createTreeWalker(container, NodeFilter.SHOW_TEXT);
  let node: Node | null = walker.nextNode();
  let offset = 0;
  let startOffset = -1;
  let endOffset = -1;

  while (node) {
    const len = node.textContent?.length ?? 0;
    if (node === range.startContainer) startOffset = offset + range.startOffset;
    if (node === range.endContainer) {
      endOffset = offset + range.endOffset;
      break;
    }
    offset += len;
    node = walker.nextNode();
  }
  return { startOffset, endOffset };
}

interface Segment {
  text: string;
  start: number;
  end: number;
  annotations: WritingFeedbackAnnotationDto[];
}

/**
 * Split responseText into contiguous segments where each segment is covered by
 * a (possibly empty) set of annotations. Overlapping annotations are supported:
 * boundaries are the union of all start/end offsets, and each resulting segment
 * collects every annotation spanning it. The severity used for styling is the
 * most severe annotation on that segment.
 */
function buildSegments(text: string, annotations: WritingFeedbackAnnotationDto[]): Segment[] {
  const len = text.length;
  const valid = annotations.filter(
    (a) => a.startOffset >= 0 && a.endOffset <= len && a.endOffset > a.startOffset,
  );
  if (valid.length === 0) return [{ text, start: 0, end: len, annotations: [] }];

  const boundaries = new Set<number>([0, len]);
  for (const a of valid) {
    boundaries.add(a.startOffset);
    boundaries.add(a.endOffset);
  }
  const sorted = [...boundaries].filter((b) => b >= 0 && b <= len).sort((a, b) => a - b);

  const segments: Segment[] = [];
  for (let i = 0; i < sorted.length - 1; i += 1) {
    const start = sorted[i];
    const end = sorted[i + 1];
    if (end <= start) continue;
    const covering = valid.filter((a) => a.startOffset <= start && a.endOffset >= end);
    segments.push({ text: text.slice(start, end), start, end, annotations: covering });
  }
  return segments;
}

function mostSevere(annotations: WritingFeedbackAnnotationDto[]): WritingSeverity {
  for (const sev of SEVERITY_ORDER) {
    if (annotations.some((a) => a.severity === sev)) return sev;
  }
  return 'low';
}

export function AnnotationLayer({
  responseText,
  annotations,
  onCreate,
  onDelete,
  readOnly = false,
  busy = false,
  className,
}: AnnotationLayerProps) {
  const containerRef = useRef<HTMLDivElement>(null);
  const wrapperRef = useRef<HTMLDivElement>(null);
  const popoverRef = useRef<HTMLDivElement>(null);
  const firstFieldRef = useRef<HTMLSelectElement>(null);

  const [pending, setPending] = useState<PendingSelection | null>(null);
  const [draft, setDraft] = useState<DraftAnnotation>(DEFAULT_DRAFT);
  const [saving, setSaving] = useState(false);
  const [activeId, setActiveId] = useState<string | null>(null);
  const [popoverStyle, setPopoverStyle] = useState<CSSProperties>({});

  const segments = useMemo(() => buildSegments(responseText, annotations), [responseText, annotations]);

  const sortedAnnotations = useMemo(
    () => [...annotations].sort((a, b) => a.startOffset - b.startOffset),
    [annotations],
  );

  const captureSelection = useCallback(() => {
    if (readOnly) return;
    const selection = window.getSelection();
    const container = containerRef.current;
    if (!selection || selection.isCollapsed || selection.rangeCount === 0 || !container) {
      return;
    }
    const range = selection.getRangeAt(0);
    if (!container.contains(range.startContainer) || !container.contains(range.endContainer)) {
      return;
    }
    const { startOffset, endOffset } = getSelectionOffsets(container, range);
    if (startOffset < 0 || endOffset <= startOffset) return;

    const rect = range.getBoundingClientRect();
    setPending({
      text: selection.toString(),
      startOffset,
      endOffset,
      anchor: { top: rect.top, bottom: rect.bottom, left: rect.left, right: rect.right },
    });
    setDraft(DEFAULT_DRAFT);
  }, [readOnly]);

  // Origin-aware positioning: place the popover just under the selection rect,
  // flipping above when it would overflow the viewport bottom, and clamping
  // horizontally to the wrapper. Computed relative to the wrapper so it scrolls
  // with the content.
  useLayoutEffect(() => {
    if (!pending || !wrapperRef.current || !popoverRef.current) return;
    const wrapperRect = wrapperRef.current.getBoundingClientRect();
    const pop = popoverRef.current.getBoundingClientRect();
    const gap = 8;
    const anchor = pending.anchor;

    let top = anchor.bottom - wrapperRect.top + gap;
    const spaceBelow = window.innerHeight - anchor.bottom;
    if (spaceBelow < pop.height + gap && anchor.top - wrapperRect.top > pop.height + gap) {
      top = anchor.top - wrapperRect.top - pop.height - gap;
    }

    const anchorMidX = (anchor.left + anchor.right) / 2 - wrapperRect.left;
    let left = anchorMidX - pop.width / 2;
    const maxLeft = wrapperRect.width - pop.width - 4;
    left = Math.max(4, Math.min(left, Math.max(4, maxLeft)));

    setPopoverStyle({ top: Math.max(0, top), left });
  }, [pending]);

  // Focus the first field when the popover opens (keyboard accessibility).
  useEffect(() => {
    if (pending) firstFieldRef.current?.focus();
  }, [pending]);

  // Dismiss the popover on outside click / Escape.
  useEffect(() => {
    if (!pending) return;
    const onPointerDown = (e: PointerEvent) => {
      if (popoverRef.current && !popoverRef.current.contains(e.target as Node)) {
        setPending(null);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') setPending(null);
    };
    document.addEventListener('pointerdown', onPointerDown, true);
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('pointerdown', onPointerDown, true);
      document.removeEventListener('keydown', onKey);
    };
  }, [pending]);

  const closePopover = useCallback(() => {
    setPending(null);
    window.getSelection()?.removeAllRanges();
  }, []);

  const handleSave = useCallback(async () => {
    if (!pending || !draft.feedbackText.trim()) return;
    setSaving(true);
    try {
      await onCreate({
        criterion: draft.criterion === 'general' ? null : draft.criterion,
        highlightedText: pending.text,
        startOffset: pending.startOffset,
        endOffset: pending.endOffset,
        severity: draft.severity,
        suggestion: draft.suggestion.trim() || null,
        feedbackText: draft.feedbackText.trim(),
      });
      closePopover();
    } finally {
      setSaving(false);
    }
  }, [pending, draft, onCreate, closePopover]);

  const handleDelete = useCallback(
    async (id: string) => {
      await onDelete(id);
      if (activeId === id) setActiveId(null);
    },
    [onDelete, activeId],
  );

  return (
    <div className={cn('grid gap-4 lg:grid-cols-[minmax(0,1fr)_18rem]', className)}>
      {/* Response + highlights + popover */}
      <div ref={wrapperRef} className="relative">
        <div
          ref={containerRef}
          id="writing-response-content"
          role="article"
          aria-label="Student response. Select text to add an annotation."
          onMouseUp={captureSelection}
          className="whitespace-pre-wrap rounded-xl border border-border bg-background-light p-4 font-serif text-[15px] leading-relaxed text-navy selection:bg-primary/20"
        >
          {segments.map((seg) => {
            if (seg.annotations.length === 0) {
              return <span key={seg.start}>{seg.text}</span>;
            }
            const severity = mostSevere(seg.annotations);
            const style = SEVERITY_STYLE[severity];
            const isActive = seg.annotations.some((a) => a.id === activeId);
            const title = seg.annotations
              .map((a) => `${SEVERITY_STYLE[a.severity].label}: ${a.feedbackText}`)
              .join('\n');
            return (
              <mark
                key={seg.start}
                data-annotation-ids={seg.annotations.map((a) => a.id).join(' ')}
                title={title}
                onClick={() => setActiveId(seg.annotations[0]?.id ?? null)}
                className={cn(
                  'cursor-pointer rounded-[2px] px-px text-navy transition-colors',
                  style.highlightClass,
                  isActive && 'ring-2 ring-primary/50',
                )}
              >
                <span aria-hidden="true" className="mr-0.5 select-none text-[10px] opacity-70">
                  {style.glyph}
                </span>
                {seg.text}
              </mark>
            );
          })}
        </div>

        {pending ? (
          <div
            ref={popoverRef}
            role="dialog"
            aria-label="Add annotation for selected text"
            style={popoverStyle}
            className="absolute z-30 w-80 max-w-[calc(100vw-2rem)] rounded-xl border border-border bg-surface p-3 shadow-lg motion-safe:animate-in motion-safe:fade-in motion-safe:zoom-in-95 motion-safe:duration-150"
          >
            <div className="mb-2 flex items-start justify-between gap-2">
              <p className="flex items-center gap-1.5 text-xs font-bold uppercase tracking-wider text-muted">
                <MessageSquarePlus className="h-3.5 w-3.5" aria-hidden="true" /> New annotation
              </p>
              <button
                type="button"
                onClick={closePopover}
                className="text-muted hover:text-foreground"
                aria-label="Cancel annotation"
              >
                <X className="h-4 w-4" />
              </button>
            </div>
            <p className="mb-2 truncate rounded bg-background-light px-2 py-1 text-xs italic text-muted" title={pending.text}>
              &ldquo;{pending.text.slice(0, 90)}{pending.text.length > 90 ? '…' : ''}&rdquo;
            </p>

            <div className="grid grid-cols-2 gap-2">
              <label className="flex flex-col gap-1 text-[11px] font-bold uppercase tracking-wider text-muted">
                Criterion
                <select
                  ref={firstFieldRef}
                  value={draft.criterion}
                  onChange={(e) => setDraft((d) => ({ ...d, criterion: e.target.value as DraftAnnotation['criterion'] }))}
                  className="rounded-lg border border-border bg-surface px-2 py-1.5 text-sm font-normal normal-case text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                >
                  {ANNOTATION_CRITERION_OPTIONS.map((opt) => (
                    <option key={opt.value} value={opt.value}>{opt.label}</option>
                  ))}
                </select>
              </label>
              <label className="flex flex-col gap-1 text-[11px] font-bold uppercase tracking-wider text-muted">
                Severity
                <select
                  value={draft.severity}
                  onChange={(e) => setDraft((d) => ({ ...d, severity: e.target.value as WritingSeverity }))}
                  className="rounded-lg border border-border bg-surface px-2 py-1.5 text-sm font-normal normal-case text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                >
                  {SEVERITY_ORDER.map((sev) => (
                    <option key={sev} value={sev}>{SEVERITY_STYLE[sev].glyph} {SEVERITY_STYLE[sev].label}</option>
                  ))}
                </select>
              </label>
            </div>

            <label className="mt-2 flex flex-col gap-1 text-[11px] font-bold uppercase tracking-wider text-muted">
              Feedback
              <textarea
                rows={2}
                value={draft.feedbackText}
                onChange={(e) => setDraft((d) => ({ ...d, feedbackText: e.target.value }))}
                placeholder="What is the issue here?"
                className="rounded-lg border border-border bg-surface px-2 py-1.5 text-sm font-normal normal-case text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
              />
            </label>
            <label className="mt-2 flex flex-col gap-1 text-[11px] font-bold uppercase tracking-wider text-muted">
              Suggestion <span className="font-normal normal-case text-muted">(optional)</span>
              <input
                type="text"
                value={draft.suggestion}
                onChange={(e) => setDraft((d) => ({ ...d, suggestion: e.target.value }))}
                placeholder="Suggested rewrite"
                className="rounded-lg border border-border bg-surface px-2 py-1.5 text-sm font-normal normal-case text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
              />
            </label>

            <div className="mt-3 flex justify-end gap-2">
              <Button size="sm" variant="outline" onClick={closePopover}>Cancel</Button>
              <Button
                size="sm"
                onClick={() => void handleSave()}
                loading={saving}
                disabled={!draft.feedbackText.trim() || busy}
              >
                Add annotation
              </Button>
            </div>
          </div>
        ) : null}
      </div>

      {/* Side list */}
      <aside aria-label="Annotations list" className="space-y-2">
        <div className="flex items-center justify-between">
          <h4 className="text-xs font-bold uppercase tracking-wider text-muted">
            Annotations ({sortedAnnotations.length})
          </h4>
        </div>
        {sortedAnnotations.length === 0 ? (
          <p className="rounded-lg border border-dashed border-border bg-background-light p-3 text-xs text-muted">
            {readOnly
              ? 'No annotations on this response.'
              : 'Select text in the response to add an annotation.'}
          </p>
        ) : (
          <ul className="space-y-2">
            {sortedAnnotations.map((a) => {
              const style = SEVERITY_STYLE[a.severity];
              const isActive = a.id === activeId;
              return (
                <li key={a.id}>
                  <div
                    tabIndex={0}
                    role="button"
                    aria-pressed={isActive}
                    onClick={() => setActiveId(isActive ? null : a.id)}
                    onKeyDown={(e) => {
                      if (e.key === 'Enter' || e.key === ' ') {
                        e.preventDefault();
                        setActiveId(isActive ? null : a.id);
                      }
                    }}
                    className={cn(
                      'rounded-lg border border-l-4 border-border bg-surface p-2.5 text-left transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/50',
                      style.borderClass,
                      isActive && 'ring-2 ring-primary/40',
                    )}
                  >
                    <div className="mb-1 flex items-center justify-between gap-2">
                      <span className={cn('inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[10px] font-bold uppercase tracking-wide', style.badgeClass)}>
                        <span aria-hidden="true">{style.glyph}</span> {style.tag}
                      </span>
                      <span className="text-[10px] font-semibold uppercase tracking-wide text-muted">
                        {a.criterion ? CRITERION_LABEL[a.criterion] : 'General'}
                      </span>
                    </div>
                    <p className="text-xs italic text-muted line-clamp-2" title={a.highlightedText}>
                      &ldquo;{a.highlightedText}&rdquo;
                    </p>
                    <p className="mt-1 text-sm text-navy">{a.feedbackText}</p>
                    {a.suggestion ? (
                      <p className="mt-1 text-xs text-success">
                        <span className="font-semibold">Suggestion:</span> {a.suggestion}
                      </p>
                    ) : null}
                    {!readOnly ? (
                      <div className="mt-2 flex justify-end">
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            void handleDelete(a.id);
                          }}
                          className="inline-flex items-center gap-1 text-xs text-muted hover:text-error focus:outline-none focus-visible:ring-2 focus-visible:ring-error/50 rounded"
                          aria-label={`Delete annotation: ${a.feedbackText.slice(0, 40)}`}
                        >
                          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" /> Delete
                        </button>
                      </div>
                    ) : null}
                  </div>
                </li>
              );
            })}
          </ul>
        )}
      </aside>
    </div>
  );
}
