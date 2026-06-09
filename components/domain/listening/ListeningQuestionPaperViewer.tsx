'use client';

import { useEffect, useState } from 'react';
import { FileText, Loader2 } from 'lucide-react';
import { fetchAuthorizedObjectUrl } from '@/lib/api';

/**
 * Learner-facing question-paper PDF pane for the Listening player.
 *
 * Listening question papers are uploaded per part (A / B / C) — with optional
 * per-section overrides — exactly like the Reading module. The player resolves
 * the URL for the current section (section code → parent part fallback) and
 * passes it here. We mirror the Reading PDF viewer's auth strategy: the media is
 * served by the authenticated `/v1/media/{id}/content` endpoint, which pdf.js /
 * an <iframe> cannot fetch with a bearer token directly, so we fetch the bytes
 * authenticated and hand the element a local blob URL instead.
 *
 * Kept deliberately lightweight (no annotations / pdf.js canvas) so it can mount
 * inside the exam player without touching the attempt FSM.
 */
export function ListeningQuestionPaperViewer({
  url,
  partLabel,
}: {
  url: string;
  partLabel?: string | null;
}) {
  const [src, setSrc] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;
    let objectUrl: string | null = null;
    setError(null);
    setSrc(null);
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
        if (!cancelled) setError(err instanceof Error ? err.message : 'Question paper could not be loaded.');
      } finally {
        if (!cancelled) setLoading(false);
      }
    })();
    return () => {
      cancelled = true;
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [url]);

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
        {loading ? (
          <div className="flex h-24 items-center justify-center gap-2 text-sm text-muted">
            <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />
            Loading question paper…
          </div>
        ) : error ? (
          <p className="px-2 py-4 text-sm text-red-600" role="alert">{error}</p>
        ) : src ? (
          <iframe
            src={src}
            title={`Question paper${partLabel ? ` for Part ${partLabel}` : ''}`}
            className="h-[70vh] w-full rounded-lg border border-border bg-white"
          />
        ) : null}
      </div>
    </details>
  );
}
