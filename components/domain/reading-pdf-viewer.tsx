'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import type { ComponentType } from 'react';
import { Eraser, Highlighter, Minus, MousePointer2, PenLine, Plus, Redo2, RotateCcw, Square, Trash2, Undo2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { ReadingPaperAnnotationDto, ReadingPaperAnnotationKind } from '@/lib/reading-authoring-api';

type Tool = 'select' | 'Text' | 'Rectangle' | 'Freehand';

export interface ReadingPdfAsset {
  id: string;
  part: string | null;
  title: string;
  downloadPath: string;
}

export interface ReadingPdfViewerProps {
  paperId: string;
  partCode: 'A' | 'B' | 'C';
  assets: ReadingPdfAsset[];
  annotations: ReadingPaperAnnotationDto[];
  readOnly?: boolean;
  onCreateAnnotation?: (annotation: {
    contentPaperAssetId: string;
    pageNumber: number;
    kind: ReadingPaperAnnotationKind;
    geometryJson: unknown;
  }) => Promise<void>;
  onDeleteAnnotation?: (annotationId: string) => Promise<void>;
  onClearAsset?: (assetId: string) => Promise<void>;
  onClearPaper?: () => Promise<void>;
  className?: string;
}

interface PdfPage {
  pageNumber: number;
  width: number;
  height: number;
}

type RectGeometry = { x: number; y: number; width: number; height: number };
type FreehandGeometry = { points: Array<{ x: number; y: number }> };

export function ReadingPdfViewer({
  partCode,
  assets,
  annotations,
  readOnly = false,
  onCreateAnnotation,
  onDeleteAnnotation,
  onClearAsset,
  onClearPaper,
  className,
}: ReadingPdfViewerProps) {
  const asset = useMemo(() => assets.find((candidate) => candidate.part === partCode) ?? null, [assets, partCode]);
  const [zoom, setZoom] = useState(100);
  const [tool, setTool] = useState<Tool>('select');
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [pages, setPages] = useState<PdfPage[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [pending, setPending] = useState(false);
  const [undoStack, setUndoStack] = useState<ReadingPaperAnnotationDto[]>([]);
  const [redoStack, setRedoStack] = useState<ReadingPaperAnnotationDto[]>([]);
  const [draft, setDraft] = useState<{ pageNumber: number; kind: Tool; geometry: RectGeometry | FreehandGeometry } | null>(null);
  const canvases = useRef<Map<number, HTMLCanvasElement>>(new Map());
  const dragStart = useRef<{ pageNumber: number; x: number; y: number } | null>(null);
  const freehand = useRef<Array<{ x: number; y: number }>>([]);

  const assetAnnotations = useMemo(
    () => asset ? annotations.filter((annotation) => annotation.contentPaperAssetId === asset.id) : [],
    [annotations, asset],
  );

  useEffect(() => {
    let cancelled = false;
    async function loadPdf() {
      if (!asset) {
        setPages([]);
        return;
      }
      setLoading(true);
      setError(null);
      try {
        const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
        pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/legacy/build/pdf.worker.mjs', import.meta.url).toString();
        const pdf = await pdfjs.getDocument({ url: asset.downloadPath, withCredentials: true }).promise;
        const nextPages: PdfPage[] = [];
        for (let pageNumber = 1; pageNumber <= pdf.numPages; pageNumber += 1) {
          const page = await pdf.getPage(pageNumber);
          const viewport = page.getViewport({ scale: 1 });
          nextPages.push({ pageNumber, width: viewport.width, height: viewport.height });
        }
        if (!cancelled) setPages(nextPages);
      } catch (err) {
        if (!cancelled) setError(err instanceof Error ? err.message : 'PDF could not be loaded.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    }
    void loadPdf();
    return () => { cancelled = true; };
  }, [asset]);

  useEffect(() => {
    let cancelled = false;
    async function renderPages() {
      if (!asset || pages.length === 0) return;
      const pdfjs = await import('pdfjs-dist/legacy/build/pdf.mjs');
      pdfjs.GlobalWorkerOptions.workerSrc = new URL('pdfjs-dist/legacy/build/pdf.worker.mjs', import.meta.url).toString();
      const pdf = await pdfjs.getDocument({ url: asset.downloadPath, withCredentials: true }).promise;
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
    }
    void renderPages();
    return () => { cancelled = true; };
  }, [asset, pages, zoom]);

  const pushCreate = useCallback(async (pageNumber: number, kind: ReadingPaperAnnotationKind, geometryJson: unknown) => {
    if (!asset || readOnly || !onCreateAnnotation) return;
    setPending(true);
    try {
      await onCreateAnnotation({ contentPaperAssetId: asset.id, pageNumber, kind, geometryJson });
      setRedoStack([]);
    } finally {
      setPending(false);
    }
  }, [asset, onCreateAnnotation, readOnly]);

  const deleteSelected = useCallback(async () => {
    if (readOnly || !selectedId || !onDeleteAnnotation) return;
    const annotation = annotations.find((candidate) => candidate.id === selectedId);
    if (!annotation) return;
    setPending(true);
    try {
      await onDeleteAnnotation(selectedId);
      setUndoStack((prev) => [...prev, annotation]);
      setRedoStack([]);
      setSelectedId(null);
    } finally {
      setPending(false);
    }
  }, [annotations, onDeleteAnnotation, readOnly, selectedId]);

  const undoDelete = useCallback(async () => {
    if (readOnly || !onCreateAnnotation || undoStack.length === 0) return;
    const annotation = undoStack[undoStack.length - 1];
    setPending(true);
    try {
      await onCreateAnnotation({
        contentPaperAssetId: annotation.contentPaperAssetId,
        pageNumber: annotation.pageNumber,
        kind: annotation.kind,
        geometryJson: annotation.geometry,
      });
      setUndoStack((prev) => prev.slice(0, -1));
      setRedoStack((prev) => [...prev, annotation]);
    } finally {
      setPending(false);
    }
  }, [onCreateAnnotation, readOnly, undoStack]);

  const redoDelete = useCallback(async () => {
    if (readOnly || !onDeleteAnnotation || redoStack.length === 0) return;
    const original = redoStack[redoStack.length - 1];
    const latest = annotations.find((candidate) =>
      candidate.contentPaperAssetId === original.contentPaperAssetId
      && candidate.pageNumber === original.pageNumber
      && candidate.kind === original.kind
      && JSON.stringify(candidate.geometry) === JSON.stringify(original.geometry));
    if (!latest) return;
    await onDeleteAnnotation(latest.id);
    setRedoStack((prev) => prev.slice(0, -1));
    setUndoStack((prev) => [...prev, latest]);
  }, [annotations, onDeleteAnnotation, readOnly, redoStack]);

  const normalizedPoint = (event: React.PointerEvent<HTMLElement>) => {
    const rect = event.currentTarget.getBoundingClientRect();
    return {
      x: clamp01((event.clientX - rect.left) / rect.width),
      y: clamp01((event.clientY - rect.top) / rect.height),
    };
  };

  if (!asset) {
    return (
      <section className={cn('rounded-[20px] border border-border bg-surface p-6 text-center text-sm text-muted shadow-sm', className)}>
        No Part {partCode} PDF is attached to this Reading paper.
      </section>
    );
  }

  return (
    <section className={cn('rounded-[20px] border border-border bg-surface shadow-sm', className)} aria-label={`Part ${partCode} PDF`}>
      <div className="flex flex-wrap items-center justify-between gap-2 border-b border-border p-3">
        <div className="flex items-center gap-2">
          <Badge variant="muted">Part {partCode}</Badge>
          <span className="text-sm font-semibold text-navy">{asset.title}</span>
        </div>
        <div className="flex flex-wrap items-center gap-1">
          {!readOnly ? (
            <>
              <ToolbarButton active={tool === 'select'} label="Select" onClick={() => setTool('select')} icon={MousePointer2} />
              <ToolbarButton active={tool === 'Text'} label="Text highlight" onClick={() => setTool('Text')} icon={Highlighter} />
              <ToolbarButton active={tool === 'Rectangle'} label="Rectangle" onClick={() => setTool('Rectangle')} icon={Square} />
              <ToolbarButton active={tool === 'Freehand'} label="Marker" onClick={() => setTool('Freehand')} icon={PenLine} />
              <ToolbarButton disabled={!selectedId || pending} label="Delete" onClick={() => void deleteSelected()} icon={Trash2} />
              <ToolbarButton disabled={undoStack.length === 0 || pending} label="Undo" onClick={() => void undoDelete()} icon={Undo2} />
              <ToolbarButton disabled={redoStack.length === 0 || pending} label="Redo" onClick={() => void redoDelete()} icon={Redo2} />
              <ToolbarButton disabled={pending} label="Clear PDF" onClick={() => { if (confirm('Clear highlights on this PDF?')) void onClearAsset?.(asset.id); }} icon={Eraser} />
              <ToolbarButton disabled={pending} label="Clear paper" onClick={() => { if (confirm('Clear all highlights on this paper?')) void onClearPaper?.(); }} icon={RotateCcw} />
            </>
          ) : <Badge variant="info">Read-only highlights</Badge>}
          <ToolbarButton label="Zoom out" onClick={() => setZoom((z) => Math.max(50, z - 10))} icon={Minus} />
          <button type="button" className="rounded-md px-2 py-1 text-xs font-bold text-navy" onClick={() => setZoom(100)}>{zoom}%</button>
          <ToolbarButton label="Zoom in" onClick={() => setZoom((z) => Math.min(200, z + 10))} icon={Plus} />
        </div>
      </div>
      {error ? <p className="p-4 text-sm text-danger">{error}</p> : null}
      {loading ? <p className="p-4 text-sm text-muted">Loading PDF…</p> : null}
      <div className="max-h-[72vh] space-y-4 overflow-auto bg-background-light p-4">
        {pages.map((page) => {
          const width = page.width * (zoom / 100);
          const height = page.height * (zoom / 100);
          const pageAnnotations = assetAnnotations.filter((annotation) => annotation.pageNumber === page.pageNumber);
          return (
            <div
              key={page.pageNumber}
              className="relative mx-auto bg-white shadow"
              style={{ width, height }}
              onPointerDown={(event) => {
                if (readOnly || tool === 'select') return;
                const point = normalizedPoint(event);
                dragStart.current = { pageNumber: page.pageNumber, ...point };
                freehand.current = [point];
                (event.currentTarget as HTMLElement).setPointerCapture(event.pointerId);
              }}
              onPointerMove={(event) => {
                if (!dragStart.current || dragStart.current.pageNumber !== page.pageNumber) return;
                const point = normalizedPoint(event);
                if (tool === 'Freehand') {
                  freehand.current = [...freehand.current, point];
                  setDraft({ pageNumber: page.pageNumber, kind: 'Freehand', geometry: { points: freehand.current } });
                } else if (tool === 'Text' || tool === 'Rectangle') {
                  const start = dragStart.current;
                  setDraft({ pageNumber: page.pageNumber, kind: tool, geometry: toRect(start, point) });
                }
              }}
              onPointerUp={(event) => {
                if (!dragStart.current || dragStart.current.pageNumber !== page.pageNumber) return;
                const currentDraft = draft;
                dragStart.current = null;
                freehand.current = [];
                setDraft(null);
                (event.currentTarget as HTMLElement).releasePointerCapture(event.pointerId);
                if (!currentDraft || currentDraft.kind === 'select') return;
                if ('width' in currentDraft.geometry && (currentDraft.geometry.width < 0.002 || currentDraft.geometry.height < 0.002)) return;
                void pushCreate(page.pageNumber, currentDraft.kind as ReadingPaperAnnotationKind, currentDraft.geometry);
              }}
            >
              <canvas ref={(el) => { if (el) canvases.current.set(page.pageNumber, el); else canvases.current.delete(page.pageNumber); }} />
              <div className="absolute inset-0" aria-hidden>
                {pageAnnotations.map((annotation) => (
                  <AnnotationOverlay
                    key={annotation.id}
                    annotation={annotation}
                    selected={annotation.id === selectedId}
                    onSelect={() => !readOnly && setSelectedId(annotation.id)}
                  />
                ))}
                {draft && draft.pageNumber === page.pageNumber ? (
                  <DraftOverlay kind={draft.kind} geometry={draft.geometry} />
                ) : null}
              </div>
              <span className="absolute bottom-1 right-2 rounded bg-black/60 px-1.5 py-0.5 text-[10px] font-bold text-white">
                {page.pageNumber}
              </span>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function ToolbarButton({ label, icon: Icon, onClick, active, disabled }: {
  label: string;
  icon: ComponentType<{ className?: string }>;
  onClick?: () => void;
  active?: boolean;
  disabled?: boolean;
}) {
  return (
    <Button
      type="button"
      variant="ghost"
      size="sm"
      className={cn('h-8 px-2', active && 'bg-warning/20 text-amber-800')}
      onClick={onClick}
      disabled={disabled}
      title={label}
      aria-label={label}
    >
      <Icon className="h-4 w-4" />
    </Button>
  );
}

function AnnotationOverlay({ annotation, selected, onSelect }: {
  annotation: ReadingPaperAnnotationDto;
  selected: boolean;
  onSelect: () => void;
}) {
  const geometry = annotation.geometry as Partial<RectGeometry & FreehandGeometry> | null;
  if (!geometry) return null;
  if (annotation.kind === 'Freehand' && Array.isArray(geometry.points)) {
    const points = geometry.points.map((p) => `${p.x * 100},${p.y * 100}`).join(' ');
    return (
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        <polyline points={points} fill="none" stroke="rgba(250,204,21,0.72)" strokeWidth="0.7" strokeLinecap="round" strokeLinejoin="round" onClick={onSelect} />
      </svg>
    );
  }
  if (typeof geometry.x !== 'number' || typeof geometry.y !== 'number'
    || typeof geometry.width !== 'number' || typeof geometry.height !== 'number') return null;
  return (
    <button
      type="button"
      className={cn(
        'absolute border border-amber-500/40 bg-yellow-300/35',
        annotation.kind === 'Text' && 'bg-yellow-300/55 mix-blend-multiply',
        selected && 'ring-2 ring-primary',
      )}
      style={{
        left: `${geometry.x * 100}%`,
        top: `${geometry.y * 100}%`,
        width: `${geometry.width * 100}%`,
        height: `${geometry.height * 100}%`,
      }}
      onClick={onSelect}
      aria-label={`Select ${annotation.kind} annotation`}
    />
  );
}

function DraftOverlay({ kind, geometry }: { kind: Tool; geometry: RectGeometry | FreehandGeometry }) {
  if ('points' in geometry) {
    const points = geometry.points.map((p) => `${p.x * 100},${p.y * 100}`).join(' ');
    return (
      <svg className="absolute inset-0 h-full w-full" viewBox="0 0 100 100" preserveAspectRatio="none">
        <polyline points={points} fill="none" stroke="rgba(250,204,21,0.72)" strokeWidth="0.7" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
    );
  }
  return (
    <div
      className={cn('absolute border border-dashed border-amber-600 bg-yellow-300/30', kind === 'Text' && 'bg-yellow-300/50')}
      style={{
        left: `${geometry.x * 100}%`,
        top: `${geometry.y * 100}%`,
        width: `${geometry.width * 100}%`,
        height: `${geometry.height * 100}%`,
      }}
    />
  );
}

function toRect(a: { x: number; y: number }, b: { x: number; y: number }): RectGeometry {
  const x = Math.min(a.x, b.x);
  const y = Math.min(a.y, b.y);
  return { x, y, width: Math.abs(a.x - b.x), height: Math.abs(a.y - b.y) };
}

function clamp01(value: number) {
  return Math.max(0, Math.min(1, value));
}
