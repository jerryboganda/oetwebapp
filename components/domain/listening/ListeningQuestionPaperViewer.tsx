'use client';

import { useEffect, useRef, useState } from 'react';
import { FileText, Loader2 } from 'lucide-react';
import { fetchAuthorizedObjectUrl } from '@/lib/api';

/**
 * Learner-facing question-paper PDF pane for the Listening player.
 *
 * Listening question papers are uploaded per part (A / B / C) — with optional
 * per-section overrides — exactly like the Reading module. The player resolves
 * the URL for the current section (section code → parent part fallback) and
 * passes it here.
 *
 * Auth + rendering strategy (mirrors the Reading PDF viewer / Part A overlay):
 * the media is served by the authenticated `/v1/media/{id}/content` endpoint,
 * which pdf.js / an <iframe> cannot fetch with a bearer token directly, so we
 * fetch the bytes authenticated and hand them to a local blob URL. We then
 * render through **pdf.js → canvas** rather than dropping the blob into an
 * <iframe>: the media endpoint serves `application/octet-stream`, and Chrome's
 * built-in PDF viewer refuses to render a blob iframe whose content-type isn't
 * `application/pdf` (it shows a broken-document icon). pdf.js decodes the raw
 * bytes directly, so it is content-type-agnostic and renders reliably.
 *
 * Image assets (rare legacy papers uploaded as PNG/JPG) fall back to an <img>.
 */
interface PdfPage {
  pageNumber: number;
  width: number;
  height: number;
}

function detectAssetKind(downloadPath: string): 'pdf' | 'image' {
  const normalized = downloadPath.split('?')[0].toLowerCase();
  if (normalized.startsWith('data:image/')) return 'image';
  if (/\.(png|jpe?g|gif|webp|bmp|svg)$/.test(normalized)) return 'image';
  return 'pdf';
}

export function ListeningQuestionPaperViewer({
  url,
  partLabel,
}: {
  url: string;
  partLabel?: string | null;
}) {
  const [src, setSrc] = useState<string | null>(null);
  const [pages, setPages] = useState<PdfPage[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const canvases = useRef<Map<number, HTMLCanvasElement>>(new Map());
  const assetKind = detectAssetKind(url);

  // 1) Resolve the authenticated media URL to a local blob URL.
  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;
    setError(null);
    setSrc(null);
    setPages([]);
    setLoading(true);
    (async () => {
      try {
        const resolved = await fetchAuthorizedObjectUrl(url);
        objectUrl = resolved;
        if (cancelled) {
          URL.revokeObjectURL(resolved);
          return;
        }
        setSrc(resolved);
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Question paper could not be loaded.');
          setLoading(false);
        }
      }
    })();
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url]);

  // 2) Parse the PDF and measure page dimensions (skipped for image assets).
  useEffect(() => {
    if (!src) return;
    if (assetKind === 'image') {
      setPages([{ pageNumber: 1, width: 0, height: 0 }]);
      setLoading(false);
      return;
    }
    const source = src;
    let cancelled = false;
    (async () => {
      try {
        const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
        pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/legacy/build/pdf.worker.mjs', import.meta.url).toString();
        const pdf = await pdfjs.getDocument({ url: source }).promise;
        const next: PdfPage[] = [];
        for (let p = 1; p <= pdf.numPages; p += 1) {
          const page = await pdf.getPage(p);
          const vp = page.getViewport({ scale: 1 });
          next.push({ pageNumber: p, width: vp.width, height: vp.height });
        }
        if (!cancelled) setPages(next);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'Question paper could not be loaded.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
    };
  }, [src, assetKind]);

  // 3) Render each page to its canvas.
  useEffect(() => {
    if (!src || assetKind !== 'pdf' || pages.length === 0) return;
    const source = src;
    let cancelled = false;
    (async () => {
      const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
      pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/legacy/build/pdf.worker.mjs', import.meta.url).toString();
      const pdf = await pdfjs.getDocument({ url: source }).promise;
      for (const info of pages) {
        if (cancelled) return;
        const canvas = canvases.current.get(info.pageNumber);
        if (!canvas) continue;
        const page = await pdf.getPage(info.pageNumber);
        // Render at device-pixel density (was a flat scale:1) so the canvas isn't
        // upscaled (blurry) on hi-DPI / Retina / 4K screens. Cap the ratio at 3 to
        // bound memory. The canvas keeps its responsive `w-full` display size — the
        // wrapper caps it at maxWidth:page.width and height auto-preserves the
        // (unchanged) aspect ratio, so only sharpness improves.
        const dpr = Math.min(typeof window !== 'undefined' ? window.devicePixelRatio || 1 : 1, 3);
        const viewport = page.getViewport({ scale: dpr });
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
  }, [src, assetKind, pages]);

  return (
    <details
      className="group rounded-2xl border border-border bg-surface"
      open
      data-testid="listening-question-paper"
    >
      <summary className="flex cursor-pointer list-none items-center gap-2 px-4 py-3 text-sm font-bold text-fg">
        <FileText className="h-4 w-4 text-muted" aria-hidden="true" />
        Question paper{partLabel ? ` — Part ${partLabel}` : ''}
        <span className="ml-auto text-xs font-medium text-muted group-open:hidden">Show</span>
        <span className="ml-auto hidden text-xs font-medium text-muted group-open:inline">Hide</span>
      </summary>
      <div className="border-t border-border p-3">
        {error ? (
          <p className="px-2 py-4 text-sm text-red-600" role="alert">{error}</p>
        ) : loading ? (
          <div className="flex h-24 items-center justify-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            Loading question paper…
          </div>
        ) : src ? (
          <div className="max-h-[70vh] space-y-4 overflow-auto rounded-lg bg-background-light p-3">
            {assetKind === 'image' ? (
              // eslint-disable-next-line @next/next/no-img-element
              <img
                src={src}
                alt={`Question paper${partLabel ? ` for Part ${partLabel}` : ''}`}
                className="mx-auto block w-full max-w-3xl rounded border border-border bg-white shadow"
                draggable={false}
              />
            ) : (
              pages.map((page) => (
                <div key={page.pageNumber} className="relative mx-auto w-full bg-white shadow" style={{ maxWidth: page.width }}>
                  <canvas
                    ref={(el) => {
                      if (el) canvases.current.set(page.pageNumber, el);
                      else canvases.current.delete(page.pageNumber);
                    }}
                    className="block w-full"
                  />
                </div>
              ))
            )}
          </div>
        ) : null}
      </div>
    </details>
  );
}
