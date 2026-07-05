'use client';

// ─────────────────────────────────────────────────────────────────────────────
// NOTE: This component intentionally duplicates the pdf.js loading idiom from
// `components/domain/reading-pdf-viewer.tsx` rather than importing it. The
// duplication is deliberate: reading and writing are independently deployable
// sub-features and coupling them through a shared render helper would make
// deploy-time changes in one module risk-affect the other (deploy-sensitive
// coupling). Keep the two copies in sync manually if the pdf.js major version
// or the worker setup idiom changes.
// ─────────────────────────────────────────────────────────────────────────────

import { useEffect, useRef, useState, KeyboardEvent } from 'react';
import { Hand } from 'lucide-react';
import { cn } from '@/lib/utils';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { usePanScroll, PAN_SURFACE_CLASS } from '@/lib/use-pan-scroll';

export interface WritingStimulusViewerProps {
  /** Authenticated download path, e.g. `/v1/media/{id}/content`. */
  downloadPath: string;
  /** Optional title shown in the toolbar. */
  title?: string;
  /** Optional extra className for the outer container. */
  className?: string;
  /**
   * When true, expose the yellow highlighter tool (drag to mark) and a red ✕
   * delete control on each mark. Used on the editable case-notes surface (reading
   * + writing). When false the marks render read-only with no tools — used on the
   * results / tutor surfaces and for the Answer Sheet. Persistence is the parent's
   * job (the page autosaves the controlled `highlights` to the server).
   */
  allowHighlight?: boolean;
  /**
   * Controlled highlights. When provided, the parent owns the highlight state so
   * it survives across viewer instances (e.g. the forced reading window and the
   * writing view). When omitted, the viewer keeps its own internal state.
   */
  highlights?: Record<number, Highlight[]>;
  onHighlightsChange?: (next: Record<number, Highlight[]>) => void;
}

/** A yellow highlight rectangle stored in fractional page coordinates (0–1) so it
 *  scales with zoom automatically and survives round-tripping to the server. */
export interface Highlight {
  id: string;
  x: number;
  y: number;
  w: number;
  h: number;
}

interface PdfPage {
  pageNumber: number;
  width: number;
  height: number;
}

/** Minimal structural types for the pdf.js objects we use (the dynamic import
 *  is otherwise loosely typed). */
interface PdfPageProxy {
  getViewport: (options: { scale: number }) => { width: number; height: number };
  render: (options: { canvasContext: CanvasRenderingContext2D; viewport: { width: number; height: number } }) => { promise: Promise<void> };
}
interface LoadedPdf {
  numPages: number;
  getPage: (pageNumber: number) => Promise<PdfPageProxy>;
  destroy?: () => Promise<void> | void;
}

const DEFAULT_ZOOM = 100;

/** Clamp a zoom value to the 50–200% range. */
function clampZoom(value: number): number {
  return Math.max(50, Math.min(200, value));
}

/**
 * WritingStimulusViewer — a read-only, anti-exfiltration PDF surface for the
 * OET Writing stimulus PDF.
 *
 * Anti-exfiltration hardening:
 * - Renders exclusively to <canvas> elements — never <embed>, <iframe>,
 *   <object data=blob>, or <a download>.
 * - Context-menu, drag-start, copy, and cut events are always suppressed on
 *   the container and canvases.
 * - A keydown handler on the root element swallows Ctrl/⌘ + S, P, C.
 * - The root carries the Tailwind `print:hidden` class so browsers that honour
 *   CSS print rules won't include the rendered PDF in a print/Save-as-PDF.
 * - The `select-none` class prevents text-selection gestures (the canvas
 *   renders pixels, but the class also blocks selection from child DOM nodes).
 */
export function WritingStimulusViewer({
  downloadPath,
  title = 'Stimulus',
  className,
  allowHighlight = false,
  highlights: controlledHighlights,
  onHighlightsChange,
}: WritingStimulusViewerProps) {
  const [pdfSrc, setPdfSrc] = useState<string | null>(null);
  const [pages, setPages] = useState<PdfPage[]>([]);
  const [zoom, setZoom] = useState(DEFAULT_ZOOM);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  // ── Yellow highlighter (session-local) ──────────────────────────────────
  // Active only when `allowHighlight`. `markerOn` toggles the single tool; a
  // drag draws a yellow rectangle, a click on an existing mark removes it.
  // Highlights are controlled by the parent when `highlights` is supplied (so
  // they persist across the reading window and the writing view), otherwise the
  // viewer keeps its own internal copy.
  // Active pointer tool: 'pan' (Hand — default, drag to move the page) or
  // 'marker' (yellow highlighter). Mutually exclusive so only one drag gesture
  // is live at a time. On read-only surfaces (no highlighter) it stays 'pan'.
  const [wtool, setWtool] = useState<'pan' | 'marker'>('pan');
  const markerOn = wtool === 'marker';
  const pan = usePanScroll(wtool === 'pan');
  const [internalHighlights, setInternalHighlights] = useState<Record<number, Highlight[]>>({});
  const highlights = controlledHighlights ?? internalHighlights;
  const commitHighlights = (next: Record<number, Highlight[]>) => {
    if (onHighlightsChange) onHighlightsChange(next);
    else setInternalHighlights(next);
  };
  // Explicit per-mark delete — the small red ✕ on each highlight's top-right
  // corner. Available whenever editing is allowed (reading + writing surfaces),
  // independent of whether the draw tool is toggled on.
  const removeHighlight = (page: number, id: string) => {
    commitHighlights({
      ...highlights,
      [page]: (highlights[page] ?? []).filter((h) => h.id !== id),
    });
  };
  const [draft, setDraft] = useState<(Highlight & { page: number }) | null>(null);
  const drawAnchor = useRef<{ page: number; x: number; y: number } | null>(null);
  const highlightIdRef = useRef(0);

  const pointFrac = (e: React.PointerEvent<HTMLDivElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    const x = (e.clientX - rect.left) / rect.width;
    const y = (e.clientY - rect.top) / rect.height;
    return { x: Math.max(0, Math.min(1, x)), y: Math.max(0, Math.min(1, y)) };
  };

  const handleMarkerPointerDown = (page: number) => (e: React.PointerEvent<HTMLDivElement>) => {
    if (!markerOn) return;
    const { x, y } = pointFrac(e);
    // Click inside an existing mark → remove it (the only way to delete a mark).
    const hit = (highlights[page] ?? []).find(
      (h) => x >= h.x && x <= h.x + h.w && y >= h.y && y <= h.y + h.h,
    );
    if (hit) {
      commitHighlights({
        ...highlights,
        [page]: (highlights[page] ?? []).filter((h) => h.id !== hit.id),
      });
      return;
    }
    e.currentTarget.setPointerCapture(e.pointerId);
    drawAnchor.current = { page, x, y };
    setDraft({ id: 'draft', page, x, y, w: 0, h: 0 });
  };

  const handleMarkerPointerMove = (e: React.PointerEvent<HTMLDivElement>) => {
    const anchor = drawAnchor.current;
    if (!anchor) return;
    const { x, y } = pointFrac(e);
    setDraft({
      id: 'draft',
      page: anchor.page,
      x: Math.min(anchor.x, x),
      y: Math.min(anchor.y, y),
      w: Math.abs(x - anchor.x),
      h: Math.abs(y - anchor.y),
    });
  };

  const handleMarkerPointerUp = () => {
    const anchor = drawAnchor.current;
    drawAnchor.current = null;
    if (anchor && draft && draft.w > 0.005 && draft.h > 0.005) {
      highlightIdRef.current += 1;
      const mark: Highlight = { id: `h${highlightIdRef.current}`, x: draft.x, y: draft.y, w: draft.w, h: draft.h };
      commitHighlights({ ...highlights, [anchor.page]: [...(highlights[anchor.page] ?? []), mark] });
    }
    setDraft(null);
  };

  const canvasRefs = useRef<Map<number, HTMLCanvasElement>>(new Map());
  // The single loaded pdf.js document, reused for sizing and every zoom
  // re-render so a zoom change never re-parses the PDF.
  const pdfDocRef = useRef<LoadedPdf | null>(null);

  // ── Step 1: fetch authenticated blob URL ────────────────────────────────
  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;
    setError(null);
    setPdfSrc(null);
    setPages([]);

    (async () => {
      try {
        const url = await fetchAuthorizedObjectUrl(downloadPath);
        objectUrl = url;
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        setPdfSrc(url);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'PDF could not be loaded.');
        }
      }
    })();

    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [downloadPath]);

  // ── Step 2: load the document ONCE, capture page sizes ──────────────────
  // The resolved document is stored in pdfDocRef and reused by the render
  // effect below, so changing zoom never re-parses the PDF.
  useEffect(() => {
    if (!pdfSrc) return;
    const src = pdfSrc;
    let cancelled = false;

    async function loadDocument() {
      setLoading(true);
      setError(null);
      try {
        // Dynamic import mirrors reading-pdf-viewer.tsx — do NOT change to a
        // static import or a different build path without also updating reading.
        const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
        pdfjs.GlobalWorkerOptions.workerSrc = new URL(
          'pdfjs-dist/legacy/build/pdf.worker.mjs',
          import.meta.url,
        ).toString();
        const pdf = (await pdfjs.getDocument({ url: src }).promise) as LoadedPdf;
        if (cancelled) {
          void pdf.destroy?.();
          return;
        }
        pdfDocRef.current = pdf;
        const nextPages: PdfPage[] = [];
        for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber += 1) {
          const page = await pdf.getPage(pageNumber);
          const viewport = page.getViewport({ scale: 1 });
          nextPages.push({ pageNumber, width: viewport.width, height: viewport.height });
        }
        if (!cancelled) setPages(nextPages);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Document could not be loaded.');
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    }

    void loadDocument();
    return () => {
      cancelled = true;
      // Free the parsed PDF in the worker when the source changes / unmounts.
      void pdfDocRef.current?.destroy?.();
      pdfDocRef.current = null;
    };
  }, [pdfSrc]);

  // ── Step 3: (re)render pages to canvas at the current zoom ──────────────
  // Reuses the already-loaded pdfDocRef — no getDocument() on a zoom change.
  useEffect(() => {
    if (pages.length === 0) return;
    let cancelled = false;

    async function renderPages() {
      const pdf = pdfDocRef.current;
      if (!pdf) return;
      for (const info of pages) {
        if (cancelled) return;
        const canvas = canvasRefs.current.get(info.pageNumber);
        if (!canvas) continue;
        const page = await pdf.getPage(info.pageNumber);
        if (cancelled) return;
        // Render at device-pixel density so the canvas isn't upscaled (blurry) on
        // hi-DPI / Retina / 4K screens. Cap the ratio at 3 to bound memory at max
        // zoom; the CSS size stays logical so the %-based yellow-highlight overlay
        // and getBoundingClientRect() pointer math are unaffected. (Kept in sync
        // with reading-pdf-viewer.tsx per the header note.)
        const dpr = Math.min(typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1, 3);
        const viewport = page.getViewport({ scale: (zoom / 100) * dpr });
        const ctx = canvas.getContext('2d');
        if (!ctx) continue;
        canvas.width = Math.floor(viewport.width);
        canvas.height = Math.floor(viewport.height);
        canvas.style.width = `${Math.floor(viewport.width / dpr)}px`;
        canvas.style.height = `${Math.floor(viewport.height / dpr)}px`;
        await page.render({ canvasContext: ctx, viewport }).promise;
        if (cancelled) return;
      }
    }

    void renderPages();
    return () => { cancelled = true; };
  }, [pages, zoom]);

  // ── Anti-exfiltration keyboard handler ──────────────────────────────────
  function handleKeyDown(e: KeyboardEvent<HTMLDivElement>) {
    if (e.ctrlKey || e.metaKey) {
      if (e.key === 's' || e.key === 'S' || e.key === 'p' || e.key === 'P' || e.key === 'c' || e.key === 'C') {
        e.preventDefault();
      }
    }
  }

  // Always-on lock props — even when `locked` is false we block the most
  // obvious exfil vectors on a PDF viewer (right-click-save, drag to desktop).
  const lockProps = {
    onContextMenu: (e: React.MouseEvent) => e.preventDefault(),
    onDragStart: (e: React.DragEvent) => e.preventDefault(),
    onCopy: (e: React.ClipboardEvent) => e.preventDefault(),
    onCut: (e: React.ClipboardEvent) => e.preventDefault(),
  };

  const zoomBtn =
    'flex h-7 w-7 items-center justify-center rounded-md border border-border text-muted transition-colors duration-150 hover:bg-background-light disabled:cursor-not-allowed disabled:opacity-40';

  return (
    <div
      {...lockProps}
      onKeyDown={handleKeyDown}
      draggable={false}
      // `print:hidden` prevents the PDF from appearing in browser print / Save-as-PDF.
      // `select-none` blocks text-selection gestures.
      className={cn(
        'flex h-full flex-col overflow-hidden bg-navy/5 select-none print:hidden',
        className,
      )}
      // Allow the container to be keyboard-focusable so the keydown handler fires.
      tabIndex={-1}
    >
      {/* Toolbar */}
      <div className="flex shrink-0 items-center justify-between gap-2 border-b border-border bg-surface/90 px-3 py-2 backdrop-blur">
        <span className="truncate text-sm font-bold text-navy">{title}</span>
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => setWtool('pan')}
            aria-pressed={wtool === 'pan'}
            aria-label="Hand (move page)"
            title="Hand (move page)"
            className={cn(
              'flex h-7 items-center gap-1 rounded-md border px-2 text-xs font-bold transition-colors duration-150',
              wtool === 'pan'
                ? 'border-amber-400 bg-amber-200 text-amber-900'
                : 'border-border text-muted hover:bg-background-light',
            )}
          >
            <Hand className="h-3.5 w-3.5" aria-hidden="true" />
            Move
          </button>
          {allowHighlight && (
            <button
              type="button"
              onClick={() => setWtool((t) => (t === 'marker' ? 'pan' : 'marker'))}
              aria-pressed={markerOn}
              aria-label="Highlighter"
              title="Highlighter"
              className={cn(
                'flex h-7 items-center gap-1 rounded-md border px-2 text-xs font-bold transition-colors duration-150',
                markerOn
                  ? 'border-amber-400 bg-amber-200 text-amber-900'
                  : 'border-border text-muted hover:bg-background-light',
              )}
            >
              <svg viewBox="0 0 16 16" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                <path d="M9.5 2.5l4 4-7 7-4 1 1-4 6-8z" />
                <path d="M2.5 13.5h5" />
              </svg>
              Highlight
            </button>
          )}
          <div className="flex items-center gap-1" role="group" aria-label="Zoom stimulus PDF">
          <button
            type="button"
            onClick={() => setZoom((z) => clampZoom(z - 10))}
            disabled={zoom <= 50}
            aria-label="Zoom out"
            className={zoomBtn}
          >
            <svg viewBox="0 0 16 16" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden="true">
              <path d="M3 8h10" />
            </svg>
          </button>
          <button
            type="button"
            onClick={() => setZoom(DEFAULT_ZOOM)}
            aria-label={`Zoom level ${zoom} percent. Activate to reset to 100 percent`}
            className="min-w-[3.25rem] rounded-md px-1.5 py-1 text-xs font-bold tabular-nums text-muted transition-colors duration-150 hover:bg-background-light"
          >
            {zoom}%
          </button>
          <button
            type="button"
            onClick={() => setZoom((z) => clampZoom(z + 10))}
            disabled={zoom >= 200}
            aria-label="Zoom in"
            className={zoomBtn}
          >
            <svg viewBox="0 0 16 16" className="h-3.5 w-3.5" fill="none" stroke="currentColor" strokeWidth={2} strokeLinecap="round" aria-hidden="true">
              <path d="M8 3v10M3 8h10" />
            </svg>
          </button>
          </div>
        </div>
      </div>

      {/* Document surface */}
      <div className="flex-1 overflow-auto overscroll-contain bg-background-light p-4">
        {error ? (
          <p className="p-4 text-sm text-danger">{error}</p>
        ) : loading ? (
          <p className="p-4 text-sm text-muted">Loading document…</p>
        ) : (
          <div className="space-y-4">
            {pages.map((page) => (
              <div
                key={page.pageNumber}
                {...lockProps}
                {...pan}
                draggable={false}
                className={cn('relative mx-auto shadow select-none', wtool === 'pan' && PAN_SURFACE_CLASS)}
                style={{ width: page.width * (zoom / 100), height: page.height * (zoom / 100) }}
              >
                <canvas
                  ref={(el) => {
                    if (el) canvasRefs.current.set(page.pageNumber, el);
                    else canvasRefs.current.delete(page.pageNumber);
                  }}
                  {...lockProps}
                  draggable={false}
                  className="block select-none"
                />

                {/* Committed yellow highlights. The wrapper is click-through so
                    scrolling is unaffected; only the red ✕ delete button (edit
                    surfaces) re-enables pointer events. */}
                {(highlights[page.pageNumber] ?? []).map((h) => (
                  <div
                    key={h.id}
                    className="pointer-events-none absolute"
                    style={{
                      left: `${h.x * 100}%`,
                      top: `${h.y * 100}%`,
                      width: `${h.w * 100}%`,
                      height: `${h.h * 100}%`,
                    }}
                  >
                    <div className="absolute inset-0 bg-yellow-300/45 mix-blend-multiply" />
                    {allowHighlight && (
                      <button
                        type="button"
                        // Stop the marker overlay from treating this as a draw/erase gesture.
                        onPointerDown={(e) => {
                          e.stopPropagation();
                          e.preventDefault();
                        }}
                        onClick={(e) => {
                          e.stopPropagation();
                          removeHighlight(page.pageNumber, h.id);
                        }}
                        aria-label="Remove highlight"
                        title="Remove highlight"
                        className="pointer-events-auto absolute -right-2 -top-2 z-30 flex h-4 w-4 items-center justify-center rounded-full bg-red-600 text-white shadow ring-1 ring-white transition-transform duration-100 hover:scale-110"
                      >
                        <svg viewBox="0 0 16 16" className="h-2.5 w-2.5" fill="none" stroke="currentColor" strokeWidth={2.5} strokeLinecap="round" aria-hidden="true">
                          <path d="M4 4l8 8M12 4l-8 8" />
                        </svg>
                      </button>
                    )}
                  </div>
                ))}

                {/* In-progress drag rectangle. */}
                {draft && draft.page === page.pageNumber && (
                  <div
                    className="pointer-events-none absolute bg-yellow-300/45 ring-1 ring-amber-500/60 mix-blend-multiply"
                    style={{
                      left: `${draft.x * 100}%`,
                      top: `${draft.y * 100}%`,
                      width: `${draft.w * 100}%`,
                      height: `${draft.h * 100}%`,
                    }}
                  />
                )}

                {/* Drawing surface — only active while the marker tool is on. When
                    off, the overlay is absent so normal scrolling is unaffected. */}
                {markerOn && (
                  <div
                    className="absolute inset-0 cursor-crosshair"
                    onPointerDown={handleMarkerPointerDown(page.pageNumber)}
                    onPointerMove={handleMarkerPointerMove}
                    onPointerUp={handleMarkerPointerUp}
                  />
                )}

                <span className="absolute bottom-1 right-2 rounded bg-black/60 px-1.5 py-0.5 text-[10px] font-bold text-white pointer-events-none">
                  {page.pageNumber}
                </span>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
