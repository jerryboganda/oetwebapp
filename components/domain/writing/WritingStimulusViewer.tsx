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
import { cn } from '@/lib/utils';
import { fetchAuthorizedObjectUrl } from '@/lib/api';

export interface WritingStimulusViewerProps {
  /** Authenticated download path, e.g. `/v1/media/{id}/content`. */
  downloadPath: string;
  /** Optional title shown in the toolbar. */
  title?: string;
  /** Optional extra className for the outer container. */
  className?: string;
}

interface PdfPage {
  pageNumber: number;
  width: number;
  height: number;
}

const ZOOM_STOPS = [50, 75, 100, 125, 150, 175, 200] as const;
const DEFAULT_ZOOM = 100;

/** Clamp a zoom value to the nearest stop without going below 50 or above 200. */
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
}: WritingStimulusViewerProps) {
  const [pdfSrc, setPdfSrc] = useState<string | null>(null);
  const [pages, setPages] = useState<PdfPage[]>([]);
  const [zoom, setZoom] = useState(DEFAULT_ZOOM);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const canvasRefs = useRef<Map<number, HTMLCanvasElement>>(new Map());

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

  // ── Step 2: load document metadata (numPages + viewport sizes) ──────────
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
        const pdf = await pdfjs.getDocument({ url: src }).promise;
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
    return () => { cancelled = true; };
  }, [pdfSrc]);

  // ── Step 3: render pages to canvas at current zoom ──────────────────────
  useEffect(() => {
    if (!pdfSrc || pages.length === 0) return;
    const src = pdfSrc;
    let cancelled = false;

    async function renderPages() {
      const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
      pdfjs.GlobalWorkerOptions.workerSrc = new URL(
        'pdfjs-dist/legacy/build/pdf.worker.mjs',
        import.meta.url,
      ).toString();
      const pdf = await pdfjs.getDocument({ url: src }).promise;
      for (const info of pages) {
        if (cancelled) return;
        const canvas = canvasRefs.current.get(info.pageNumber);
        if (!canvas) continue;
        const page = await pdf.getPage(info.pageNumber);
        const viewport = page.getViewport({ scale: zoom / 100 });
        const ctx = canvas.getContext('2d');
        if (!ctx) continue;
        canvas.width = viewport.width;
        canvas.height = viewport.height;
        await page.render({ canvasContext: ctx, viewport }).promise;
      }
    }

    void renderPages();
    return () => { cancelled = true; };
  }, [pdfSrc, pages, zoom]);

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
                draggable={false}
                className="relative mx-auto shadow select-none"
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
