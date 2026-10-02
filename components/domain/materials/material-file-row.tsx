'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  FileText, Music, Image as ImageIcon, Video, File as FileIcon,
  Download, Play, Loader2, ChevronRight,
} from 'lucide-react';
import { cardClassName } from '@/components/ui/card';
import { fetchAuthorizedBlob, fetchAuthorizedObjectUrl } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import { isNativeDownloadPlatform, saveBlobNative } from '@/lib/mobile/file-download';
import { buildDownloadFilename, formatBytes } from '@/lib/materials-tree';
import type { LearnerMaterialFileDto } from '@/lib/materials-api';

const KIND_ICON = {
  audio: { Icon: Music, tone: 'text-info bg-info/10' },
  video: { Icon: Video, tone: 'text-primary bg-primary/10' },
  image: { Icon: ImageIcon, tone: 'text-success-strong bg-success/10' },
  document: { Icon: FileIcon, tone: 'text-warning-strong bg-warning/10' },
  pdf: { Icon: FileText, tone: 'text-danger-strong bg-danger/10' },
} as const;

// Sub-test identity chips (DESIGN.md §2), not status colours.
const SUBTEST_TONE: Record<string, string> = {
  listening: 'bg-skill-listening/10 text-skill-listening',
  reading: 'bg-skill-reading/10 text-skill-reading',
  writing: 'bg-skill-writing/10 text-skill-writing',
  speaking: 'bg-skill-speaking/10 text-skill-speaking',
};

const ROW_ACTION =
  'pressable flex min-h-11 min-w-11 items-center justify-center gap-1.5 rounded-lg px-2.5 py-1.5 text-xs font-semibold transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary disabled:opacity-50 lg:min-h-8 lg:min-w-0';

/**
 * Native <audio>/<img> can't carry a bearer token, so authorised media is
 * fetched into a blob URL first. The URL is revoked on unmount.
 */
function useAuthorizedMedia(downloadUrl: string) {
  const [src, setSrc] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const urlRef = useRef<string | null>(null);

  useEffect(() => () => {
    if (urlRef.current) URL.revokeObjectURL(urlRef.current);
  }, []);

  const load = useCallback(async () => {
    if (urlRef.current || loading) return;
    setLoading(true);
    try {
      const url = await fetchAuthorizedObjectUrl(downloadUrl);
      urlRef.current = url;
      setSrc(url);
    } finally {
      setLoading(false);
    }
  }, [downloadUrl, loading]);

  return { src, loading, load };
}

export function MaterialFileRow({
  file,
  path,
}: {
  file: LearnerMaterialFileDto;
  /** Ancestor folder names, shown only in search results for context. */
  path?: string[];
}) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState(false);
  const audio = useAuthorizedMedia(file.downloadUrl);

  const { Icon, tone } = KIND_ICON[file.kind] ?? KIND_ICON.pdf;

  const handleDownload = useCallback(async () => {
    if (downloading) return;
    setDownloading(true);
    setError(false);
    let objectUrl: string | null = null;
    try {
      if (isNativeDownloadPlatform()) {
        // The browser `<a download>` convention is a no-op in Capacitor's
        // native WebView — there's no download manager for blob: URLs there.
        const blob = await fetchAuthorizedBlob(file.downloadUrl);
        await saveBlobNative(blob, buildDownloadFilename(file));
      } else {
        objectUrl = await fetchAuthorizedObjectUrl(file.downloadUrl);
        const a = document.createElement('a');
        a.href = objectUrl;
        a.download = buildDownloadFilename(file);
        document.body.appendChild(a);
        a.click();
        a.remove();
      }
      analytics.track('material_file_downloaded', {
        fileId: file.id, kind: file.kind, subtest: file.subtestCode,
      });
    } catch {
      setError(true);
    } finally {
      if (objectUrl) URL.revokeObjectURL(objectUrl);
      setDownloading(false);
    }
  }, [downloading, file]);

  // A row of actions, not one link: it highlights on hover but does not lift.
  return (
    <div className={cn(cardClassName({ padding: 'none' }), 'px-3 py-2.5 transition-colors hover:border-primary/30 sm:px-4 sm:py-3')}>
      <div className="flex items-center gap-3">
        <span className={cn('flex h-10 w-10 shrink-0 items-center justify-center rounded-xl', tone)}>
          <Icon className="size-4.5" aria-hidden="true" />
        </span>

        <div className="min-w-0 flex-1">
          {path && path.length > 0 && (
            <p className="mb-0.5 flex items-center gap-0.5 truncate text-3xs text-muted">
              {path.map((segment, i) => (
                <span key={`${segment}-${i}`} className="flex items-center gap-0.5">
                  {i > 0 && <ChevronRight className="h-2.5 w-2.5 shrink-0 opacity-60 rtl:rotate-180" aria-hidden="true" />}
                  {segment}
                </span>
              ))}
            </p>
          )}
          <p className="truncate text-sm font-semibold text-navy" title={file.title}>
            {file.title}
          </p>
          <div className="mt-1 flex flex-wrap items-center gap-2">
            <span
              className={cn(
                'inline-flex items-center rounded-full px-2 py-0.5 text-3xs font-semibold capitalize',
                SUBTEST_TONE[file.subtestCode?.toLowerCase()] ?? 'bg-muted/20 text-muted',
              )}
            >
              {file.subtestCode}
            </span>
            {file.sizeBytes ? (
              <span className="text-3xs tabular-nums text-muted">{formatBytes(file.sizeBytes)}</span>
            ) : null}
            {error && <span className="text-3xs font-semibold text-danger-strong">Download failed — try again</span>}
          </div>
        </div>

        <div className="flex shrink-0 items-center gap-1.5">
          {file.kind === 'audio' && !audio.src && (
            <button
              type="button"
              onClick={audio.load}
              disabled={audio.loading}
              className={cn(ROW_ACTION, 'bg-info/10 text-info hover:bg-info/20')}
              aria-label={`Play ${file.title}`}
            >
              {audio.loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <Play className="h-3.5 w-3.5" aria-hidden="true" />}
              <span className="hidden sm:inline">{audio.loading ? 'Loading…' : 'Play'}</span>
            </button>
          )}

          <button
            type="button"
            onClick={handleDownload}
            disabled={downloading}
            className={cn(ROW_ACTION, 'bg-primary/10 text-primary-dark hover:bg-primary/20')}
            aria-label={`Download ${file.title}`}
          >
            {downloading ? <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" /> : <Download className="h-3.5 w-3.5" aria-hidden="true" />}
            <span className="hidden sm:inline">{downloading ? 'Saving…' : 'Download'}</span>
          </button>
        </div>
      </div>

      {audio.src && (
        <audio src={audio.src} controls autoPlay className="mt-2.5 h-9 w-full" preload="none" />
      )}
    </div>
  );
}
