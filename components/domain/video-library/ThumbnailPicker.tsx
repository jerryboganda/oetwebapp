'use client';

/**
 * Extras — thumbnail. Auto (Bunny frame grab, available once the encode is
 * ready) vs. custom image uploaded through the existing chunked pipeline
 * (`VideoThumbnail` role) and attached via `POST …/thumbnail`.
 */

import { useState } from 'react';
import { ImageIcon, Loader2, UploadCloud } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { uploadFileChunked } from '@/lib/content-upload-api';
import {
  adminClearVideoThumbnail,
  adminSetVideoThumbnail,
  type AdminVideoDetail,
} from '@/lib/api/video-library';

export interface ThumbnailPickerProps {
  video: AdminVideoDetail;
  canWrite: boolean;
  onChanged: () => void;
}

export function ThumbnailPicker({ video, canWrite, onChanged }: ThumbnailPickerProps) {
  const [busy, setBusy] = useState(false);
  const [progress, setProgress] = useState(0);
  const [error, setError] = useState<string | null>(null);

  async function handleUpload(file: File) {
    setBusy(true);
    setError(null);
    setProgress(0);
    try {
      const result = await uploadFileChunked(file, 'VideoThumbnail', setProgress);
      await adminSetVideoThumbnail(video.videoId, result.mediaAssetId);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Thumbnail upload failed.');
    } finally {
      setBusy(false);
    }
  }

  async function handleUseAuto() {
    setBusy(true);
    setError(null);
    try {
      await adminClearVideoThumbnail(video.videoId);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not switch to the auto thumbnail.');
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="text-sm font-bold text-navy">Thumbnail</p>
          <p className="text-xs text-muted">
            {video.thumbnailMode === 'custom'
              ? 'Using a custom image.'
              : 'Using Bunny’s auto-generated frame (available once the encode is ready).'}
          </p>
        </div>
        {video.thumbnailUrl ? (
          // eslint-disable-next-line @next/next/no-img-element
          <img
            src={video.thumbnailUrl}
            alt="Current video thumbnail"
            className="h-16 w-28 shrink-0 rounded-lg border border-border object-cover"
          />
        ) : (
          <span className="inline-flex h-16 w-28 shrink-0 items-center justify-center rounded-lg border border-dashed border-border text-muted">
            <ImageIcon className="h-5 w-5" />
          </span>
        )}
      </div>

      {error ? (
        <div className="mt-3">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      ) : null}

      <div className="mt-3 flex flex-wrap items-center gap-2">
        <label
          className={`inline-flex items-center gap-2 rounded-xl border border-border bg-surface px-3 py-2 text-sm font-bold text-navy ${canWrite && !busy ? 'cursor-pointer hover:bg-background-light' : 'cursor-not-allowed opacity-60'}`}
        >
          <UploadCloud className="h-4 w-4" />
          <span>Upload custom image</span>
          <input
            type="file"
            accept="image/png,image/jpeg,image/webp"
            className="hidden"
            disabled={!canWrite || busy}
            onChange={(e) => {
              const f = e.target.files?.[0];
              if (f) void handleUpload(f);
              e.target.value = '';
            }}
          />
        </label>
        {video.thumbnailMode === 'custom' ? (
          <Button type="button" variant="ghost" size="sm" onClick={() => void handleUseAuto()} disabled={!canWrite || busy}>
            Use auto thumbnail
          </Button>
        ) : null}
        {busy ? (
          <span className="inline-flex items-center gap-2 text-xs text-muted">
            <Loader2 className="h-3 w-3 animate-spin" />
            {progress > 0 && progress < 1 ? `Uploading… ${Math.round(progress * 100)}%` : 'Saving…'}
          </span>
        ) : null}
      </div>
    </div>
  );
}
