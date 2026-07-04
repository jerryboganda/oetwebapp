'use client';

/**
 * OET Listening Part A — "PDF overlay" authoring (Method C).
 *
 * Renders the uploaded question-paper PDF (pdf.js → canvas, authenticated blob
 * URL — same mechanism as `reading-pdf-viewer.tsx`) and lets the operator drag a
 * box over each printed blank. Boxes are stored as normalized fractions
 * (0..1) of the page so rendering is resolution-independent; the learner
 * renderer (`PartAPdfOverlayDocument`) draws an input at each box. Blanks are
 * auto-numbered in reading order (page → top→bottom → left→right) so gapOrdinal
 * N binds to the Nth Part A question, mirroring the WYSIWYG gap rule.
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { Minus, Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/admin/ui/button';
import { cn } from '@/lib/utils';
import { fetchAuthorizedObjectUrl } from '@/lib/api';

export interface PartAOverlayBlank {
  page: number;
  xPct: number;
  yPct: number;
  wPct: number;
  hPct: number;
  gapOrdinal: number;
}

export interface PartAPdfOverlayEditorProps {
  /** Authenticated download path of the Part A question-paper PDF. */
  pdfDownloadPath: string | null;
  value: PartAOverlayBlank[];
  onChange: (blanks: PartAOverlayBlank[]) => void;
  disabled?: boolean;
}

interface PdfPage {
  pageNumber: number;
  width: number;
  height: number;
}

/** Re-number blanks in reading order (page, then top→bottom, then left→right). */
function renumber(blanks: PartAOverlayBlank[]): PartAOverlayBlank[] {
  return [...blanks]
    .sort((a, b) => a.page - b.page || a.yPct - b.yPct || a.xPct - b.xPct)
    .map((b, i) => ({ ...b, gapOrdinal: i + 1 }));
}

function clamp01(v: number) {
  return Math.max(0, Math.min(1, v));
}

export function PartAPdfOverlayEditor({ pdfDownloadPath, value, onChange, disabled = false }: PartAPdfOverlayEditorProps) {
  const [zoom, setZoom] = useState(100);
  const [pages, setPages] = useState<PdfPage[]>([]);
  const [pdfSrc, setPdfSrc] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const canvases = useRef<Map<number, HTMLCanvasElement>>(new Map());
  const dragStart = useRef<{ pageNumber: number; x: number; y: number } | null>(null);
  const [draft, setDraft] = useState<{ pageNumber: number; x: number; y: number; w: number; h: number } | null>(null);

  // Fetch the PDF as an authenticated blob URL (pdf.js can't attach the Bearer).
  useEffect(() => {
    if (!pdfDownloadPath) {
      setPdfSrc(null);
      return;
    }
    let cancelled = false;
    let objectUrl: string | null = null;
    setError(null);
    setPdfSrc(null);
    (async () => {
      try {
        const url = await fetchAuthorizedObjectUrl(pdfDownloadPath);
        objectUrl = url;
        if (cancelled) {
          URL.revokeObjectURL(url);
          return;
        }
        setPdfSrc(url);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'PDF could not be loaded.');
      }
    })();
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [pdfDownloadPath]);

  // Measure page sizes.
  useEffect(() => {
    if (!pdfSrc) {
      setPages([]);
      return;
    }
    const src = pdfSrc;
    let cancelled = false;
    (async () => {
      setLoading(true);
      setError(null);
      try {
        const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
        pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/legacy/build/pdf.worker.mjs', import.meta.url).toString();
        const pdf = await pdfjs.getDocument({ url: src }).promise;
        const next: PdfPage[] = [];
        for (let p = 1; p <= pdf.numPages; p += 1) {
          const page = await pdf.getPage(p);
          const vp = page.getViewport({ scale: 1 });
          next.push({ pageNumber: p, width: vp.width, height: vp.height });
        }
        if (!cancelled) setPages(next);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Document could not be loaded.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [pdfSrc]);

  // Render pages to canvas at the current zoom.
  useEffect(() => {
    if (!pdfSrc || pages.length === 0) return;
    let cancelled = false;
    (async () => {
      const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
      pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/legacy/build/pdf.worker.mjs', import.meta.url).toString();
      const pdf = await pdfjs.getDocument({ url: pdfSrc }).promise;
      for (const info of pages) {
        if (cancelled) return;
        const canvas = canvases.current.get(info.pageNumber);
        if (!canvas) continue;
        const page = await pdf.getPage(info.pageNumber);
        // Render at device-pixel density so the canvas isn't upscaled (blurry) on
        // hi-DPI / Retina / 4K screens. Cap the ratio at 3 to bound memory; the CSS
        // size stays logical so the %-based author boxes stay aligned to the blanks.
        const dpr = Math.min(typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1, 3);
        const viewport = page.getViewport({ scale: (zoom / 100) * dpr });
        const ctx = canvas.getContext('2d');
        if (!ctx) continue;
        canvas.width = Math.floor(viewport.width);
        canvas.height = Math.floor(viewport.height);
        canvas.style.width = `${Math.floor(viewport.width / dpr)}px`;
        canvas.style.height = `${Math.floor(viewport.height / dpr)}px`;
        await page.render({ canvasContext: ctx, viewport }).promise;
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [pdfSrc, pages, zoom]);

  const normalizedPoint = (event: React.PointerEvent<HTMLElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    return {
      x: clamp01((event.clientX - rect.left) / rect.width),
      y: clamp01((event.clientY - rect.top) / rect.height),
    };
  };

  const addBlank = useCallback(
    (pageNumber: number, x: number, y: number, w: number, h: number) => {
      onChange(renumber([...value, { page: pageNumber, xPct: x, yPct: y, wPct: w, hPct: h, gapOrdinal: value.length + 1 }]));
    },
    [onChange, value],
  );

  const removeBlank = useCallback(
    (ordinal: number) => onChange(renumber(value.filter((b) => b.gapOrdinal !== ordinal))),
    [onChange, value],
  );

  if (!pdfDownloadPath) {
    return (
      <div className="rounded-2xl border border-dashed border-border bg-background-light p-6 text-center text-sm text-muted">
        Upload the Part A question-paper PDF (PDFs tab) first — then drag boxes over each blank here.
      </div>
    );
  }

  return (
    <div className="rounded-2xl border border-border bg-surface shadow-sm">
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border p-3">
        <p className="text-xs text-admin-fg-muted">
          Drag a box over each printed blank. Boxes auto-number top-to-bottom ({value.length} placed).
        </p>
        <div className="flex items-center gap-1">
          <Button type="button" variant="ghost" size="sm" onClick={() => setZoom((z) => Math.max(50, z - 10))} aria-label="Zoom out">
            <Minus className="h-4 w-4" />
          </Button>
          <button type="button" className="rounded-md px-2 py-1 text-xs font-bold text-navy" onClick={() => setZoom(100)}>{zoom}%</button>
          <Button type="button" variant="ghost" size="sm" onClick={() => setZoom((z) => Math.min(200, z + 10))} aria-label="Zoom in">
            <Plus className="h-4 w-4" />
          </Button>
        </div>
      </div>
      {error ? <p className="p-4 text-sm text-danger">{error}</p> : null}
      {loading ? <p className="p-4 text-sm text-muted">Loading document…</p> : null}
      <div className="max-h-[72vh] space-y-4 overflow-auto bg-background-light p-4">
        {pages.map((page) => {
          const width = page.width * (zoom / 100);
          const height = page.height * (zoom / 100);
          const pageBlanks = value.filter((b) => b.page === page.pageNumber);
          return (
            <div
              key={page.pageNumber}
              className={cn('relative mx-auto bg-white shadow', !disabled && 'cursor-crosshair')}
              style={{ width, height }}
              onPointerDown={(event) => {
                if (disabled) return;
                const p = normalizedPoint(event);
                dragStart.current = { pageNumber: page.pageNumber, x: p.x, y: p.y };
                setDraft({ pageNumber: page.pageNumber, x: p.x, y: p.y, w: 0, h: 0 });
                (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
              }}
              onPointerMove={(event) => {
                if (!dragStart.current || dragStart.current.pageNumber !== page.pageNumber) return;
                const p = normalizedPoint(event);
                const s = dragStart.current;
                setDraft({
                  pageNumber: page.pageNumber,
                  x: Math.min(s.x, p.x),
                  y: Math.min(s.y, p.y),
                  w: Math.abs(p.x - s.x),
                  h: Math.abs(p.y - s.y),
                });
              }}
              onPointerUp={(event) => {
                if (!dragStart.current || dragStart.current.pageNumber !== page.pageNumber) return;
                const d = draft;
                dragStart.current = null;
                setDraft(null);
                (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
                if (!d || d.w < 0.01 || d.h < 0.005) return; // ignore tiny/stray drags
                addBlank(page.pageNumber, d.x, d.y, d.w, d.h);
              }}
            >
              <canvas
                ref={(el) => {
                  if (el) canvases.current.set(page.pageNumber, el);
                  else canvases.current.delete(page.pageNumber);
                }}
              />
              <div className="absolute inset-0">
                {pageBlanks.map((b) => (
                  <div
                    key={b.gapOrdinal}
                    className="absolute flex items-center justify-center rounded border-2 border-primary bg-primary/15"
                    style={{ left: `${b.xPct * 100}%`, top: `${b.yPct * 100}%`, width: `${b.wPct * 100}%`, height: `${b.hPct * 100}%` }}
                  >
                    <span className="rounded bg-primary px-1 text-[10px] font-bold leading-tight text-white">{b.gapOrdinal}</span>
                    {!disabled ? (
                      <button
                        type="button"
                        aria-label={`Remove blank ${b.gapOrdinal}`}
                        onClick={(e) => {
                          e.stopPropagation();
                          removeBlank(b.gapOrdinal);
                        }}
                        className="absolute -right-2 -top-2 rounded-full bg-danger p-0.5 text-white shadow"
                      >
                        <Trash2 className="h-3 w-3" />
                      </button>
                    ) : null}
                  </div>
                ))}
                {draft && draft.pageNumber === page.pageNumber ? (
                  <div
                    className="absolute rounded border-2 border-dashed border-primary bg-primary/10"
                    style={{ left: `${draft.x * 100}%`, top: `${draft.y * 100}%`, width: `${draft.w * 100}%`, height: `${draft.h * 100}%` }}
                  />
                ) : null}
              </div>
              <span className="absolute bottom-1 right-2 rounded bg-black/60 px-1.5 py-0.5 text-[10px] font-bold text-white">{page.pageNumber}</span>
            </div>
          );
        })}
      </div>
    </div>
  );
}
