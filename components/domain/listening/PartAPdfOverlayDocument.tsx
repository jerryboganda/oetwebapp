'use client';

/**
 * OET Listening Part A — learner renderer for the "PDF overlay" method.
 *
 * Renders the question-paper PDF (pdf.js → canvas) and draws a fill-in input at
 * each authored blank (normalized 0..1 placement). The Nth blank (by gapOrdinal)
 * binds to the Nth Part A question, so answers flow into the same answer store
 * as the WYSIWYG note renderer. `locked` makes the inputs read-only (review /
 * tutor surfaces).
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import type { PartAOverlayBlank } from '@/components/domain/listening/admin/PartAPdfOverlayEditor';

export interface PartAPdfOverlayDocumentProps {
  pdfDownloadPath: string | null;
  blanks: PartAOverlayBlank[];
  /** Part A questions in number order; blank gapOrdinal N → questions[N-1]. */
  questions: Array<{ id: string; number: number }>;
  answers: Record<string, string>;
  onAnswerChange: (questionId: string, value: string) => void;
  locked?: boolean;
  highlightingEnabled?: boolean;
}

interface PdfPage {
  pageNumber: number;
  width: number;
  height: number;
}

export function PartAPdfOverlayDocument({
  pdfDownloadPath,
  blanks,
  questions,
  answers,
  onAnswerChange,
  locked = false,
}: PartAPdfOverlayDocumentProps) {
  const [zoom] = useState(100);
  const [pages, setPages] = useState<PdfPage[]>([]);
  const [pdfSrc, setPdfSrc] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const canvases = useRef<Map<number, HTMLCanvasElement>>(new Map());

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

  useEffect(() => {
    if (!pdfSrc) {
      setPages([]);
      return;
    }
    const src = pdfSrc;
    let cancelled = false;
    (async () => {
      setLoading(true);
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
        const viewport = page.getViewport({ scale: zoom / 100 });
        const ctx = canvas.getContext('2d');
        if (!ctx) continue;
        canvas.width = viewport.width;
        canvas.height = viewport.height;
        await page.render({ canvasContext: ctx, viewport }).promise;
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [pdfSrc, pages, zoom]);

  const questionForOrdinal = useCallback(
    (ordinal: number) => {
      const sorted = [...questions].sort((a, b) => a.number - b.number);
      return sorted[ordinal - 1] ?? null;
    },
    [questions],
  );

  if (!pdfDownloadPath) {
    return (
      <p className="rounded-2xl border border-dashed border-border bg-background p-6 text-center text-sm text-muted">
        No question-paper PDF is attached for this consultation.
      </p>
    );
  }

  return (
    <div data-testid="part-a-pdf-overlay-document">
      {error ? <p className="p-2 text-sm text-danger">{error}</p> : null}
      {loading ? <p className="p-2 text-sm text-muted">Loading paper…</p> : null}
      <div className="space-y-4">
        {pages.map((page) => {
          const width = page.width * (zoom / 100);
          const height = page.height * (zoom / 100);
          const pageBlanks = blanks.filter((b) => b.page === page.pageNumber);
          return (
            <div key={page.pageNumber} className="relative mx-auto bg-white shadow" style={{ width, height }}>
              <canvas
                ref={(el) => {
                  if (el) canvases.current.set(page.pageNumber, el);
                  else canvases.current.delete(page.pageNumber);
                }}
              />
              <div className="absolute inset-0">
                {pageBlanks.map((b) => {
                  const q = questionForOrdinal(b.gapOrdinal);
                  const valueStr = q ? answers[q.id] ?? '' : '';
                  return (
                    <input
                      key={b.gapOrdinal}
                      type="text"
                      aria-label={`Answer ${b.gapOrdinal}`}
                      value={valueStr}
                      readOnly={locked || !q}
                      onChange={(e) => q && onAnswerChange(q.id, e.target.value)}
                      className="absolute rounded border-2 border-primary/70 bg-white/95 px-1 text-sm text-navy outline-none focus:border-primary focus:ring-2 focus:ring-primary/30"
                      style={{
                        left: `${b.xPct * 100}%`,
                        top: `${b.yPct * 100}%`,
                        width: `${b.wPct * 100}%`,
                        height: `${b.hPct * 100}%`,
                      }}
                    />
                  );
                })}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}
