'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  FileText, Music, Image as ImageIcon, Video, File as FileIcon,
  Download, Play, Loader2, ChevronRight,
} from 'lucide-react';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
import { buildDownloadFilename, formatBytes } from '@/lib/materials-tree';
import type { LearnerMaterialFileDto } from '@/lib/materials-api';

const KIND_ICON = {
  audio: { Icon: Music, tone: 'text-blue-500 bg-blue-500/10' },
  video: { Icon: Video, tone: 'text-fuchsia-500 bg-fuchsia-500/10' },
  image: { Icon: ImageIcon, tone: 'text-emerald-500 bg-emerald-500/10' },
  document: { Icon: FileIcon, tone: 'text-amber-500 bg-amber-500/10' },
  pdf: { Icon: FileText, tone: 'text-red-400 bg-red-400/10' },
} as const;

const SUBTEST_TONE: Record<string, string> = {
  listening: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300',
  reading: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300',
  writing: 'bg-amber-100 text-amber-700 dark:bg-amber-900/40 dark:text-amber-300',
  speaking: 'bg-purple-100 text-purple-700 dark:bg-purple-900/40 dark:text-purple-300',
};

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
      objectUrl = await fetchAuthorizedObjectUrl(file.downloadUrl);
      const a = document.createElement('a');
      a.href = objectUrl;
      a.download = buildDownloadFilename(file);
      document.body.appendChild(a);
      a.click();
      a.remove();
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

  return (
    <div className="group rounded-xl border border-border/50 bg-surface/70 px-3 py-2.5 transition-colors hover:border-primary/30 hover:bg-primary/[0.03] sm:px-4 sm:py-3">
      <div className="flex items-center gap-3">
        <span className={cn('flex h-9 w-9 shrink-0 items-center justify-center rounded-lg', tone)}>
          <Icon className="h-4.5 w-4.5" />
        </span>

        <div className="min-w-0 flex-1">
          {path && path.length > 0 && (
            <p className="mb-0.5 flex items-center gap-0.5 truncate text-[10px] text-muted">
              {path.map((segment, i) => (
                <span key={`${segment}-${i}`} className="flex items-center gap-0.5">
                  {i > 0 && <ChevronRight className="h-2.5 w-2.5 shrink-0 opacity-60" />}
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
                'inline-flex items-center rounded-full px-2 py-0.5 text-[10px] font-semibold capitalize',
                SUBTEST_TONE[file.subtestCode?.toLowerCase()] ?? 'bg-muted/20 text-muted',
              )}
            >
              {file.subtestCode}
            </span>
            {file.sizeBytes ? (
              <span className="text-[10px] text-muted">{formatBytes(file.sizeBytes)}</span>
            ) : null}
            {error && <span className="text-[10px] font-semibold text-red-500">Download failed — try again</span>}
          </div>
        </div>

        <div className="flex shrink-0 items-center gap-1.5">
          {file.kind === 'audio' && !audio.src && (
            <button
              type="button"
              onClick={audio.load}
              disabled={audio.loading}
              className="pressable flex items-center gap-1.5 rounded-lg bg-blue-500/10 px-2.5 py-1.5 text-xs font-semibold text-blue-600 transition-colors hover:bg-blue-500/20 disabled:opacity-50 dark:text-blue-300"
              aria-label={`Play ${file.title}`}
            >
              {audio.loading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Play className="h-3.5 w-3.5" />}
              <span className="hidden sm:inline">{audio.loading ? 'Loading…' : 'Play'}</span>
            </button>
          )}

          <button
            type="button"
            onClick={handleDownload}
            disabled={downloading}
            className="pressable flex items-center gap-1.5 rounded-lg bg-primary/10 px-2.5 py-1.5 text-xs font-semibold text-primary-dark transition-colors hover:bg-primary/20 disabled:opacity-50"
            aria-label={`Download ${file.title}`}
          >
            {downloading ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Download className="h-3.5 w-3.5" />}
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
