'use client';

/**
 * Extras — caption tracks. VTT files upload through the existing chunked
 * pipeline (`VideoCaption` role) and register via `POST …/captions`; Bunny
 * sync state is surfaced per track.
 */

import { useState } from 'react';
import { Loader2, Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { uploadFileChunked } from '@/lib/content-upload-api';
import {
  adminAddVideoCaption,
  adminDeleteVideoCaption,
  type AdminVideoDetail,
} from '@/lib/api/video-library';

export interface CaptionsManagerProps {
  video: AdminVideoDetail;
  canWrite: boolean;
  onChanged: () => void;
}

export function CaptionsManager({ video, canWrite, onChanged }: CaptionsManagerProps) {
  const [languageCode, setLanguageCode] = useState('en');
  const [label, setLabel] = useState('English');
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [deletingId, setDeletingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function handleAdd() {
    if (!file) {
      setError('Choose a .vtt caption file first.');
      return;
    }
    if (!languageCode.trim() || !label.trim()) {
      setError('Language code and label are required.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await uploadFileChunked(file, 'VideoCaption');
      await adminAddVideoCaption(video.videoId, {
        mediaAssetId: result.mediaAssetId,
        languageCode: languageCode.trim(),
        label: label.trim(),
      });
      setFile(null);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not add the caption track.');
    } finally {
      setBusy(false);
    }
  }

  async function handleDelete(captionId: string) {
    setDeletingId(captionId);
    setError(null);
    try {
      await adminDeleteVideoCaption(video.videoId, captionId);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not delete the caption track.');
    } finally {
      setDeletingId(null);
    }
  }

  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <p className="text-sm font-bold text-navy">Captions</p>
      <p className="text-xs text-muted">WebVTT (.vtt) tracks, synced to Bunny for in-player subtitles.</p>

      {error ? (
        <div className="mt-3">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      ) : null}

      {(video.captions ?? []).length > 0 ? (
        <ul className="mt-3 space-y-2">
          {video.captions.map((caption) => (
            <li
              key={caption.id}
              className="flex items-center justify-between gap-2 rounded-xl border border-border bg-surface px-3 py-2"
            >
              <div className="flex min-w-0 items-center gap-2">
                <span className="rounded bg-background-light px-1.5 py-0.5 font-mono text-xs text-navy">
                  {caption.languageCode}
                </span>
                <span className="truncate text-sm text-navy">{caption.label}</span>
                <Badge variant={caption.syncedToBunny ? 'success' : 'muted'}>
                  {caption.syncedToBunny ? 'Synced' : 'Sync pending'}
                </Badge>
              </div>
              {canWrite ? (
                <Button
                  type="button"
                  variant="ghost"
                  size="sm"
                  onClick={() => void handleDelete(caption.id)}
                  disabled={deletingId === caption.id}
                  aria-label={`Delete caption ${caption.label}`}
                >
                  {deletingId === caption.id ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin" />
                  ) : (
                    <Trash2 className="h-3.5 w-3.5" />
                  )}
                </Button>
              ) : null}
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-3 text-xs text-muted">No caption tracks yet.</p>
      )}

      {canWrite ? (
        <div className="mt-4 grid gap-2 border-t border-border pt-3 sm:grid-cols-[6rem_1fr_auto_auto]">
          <Input
            label="Language"
            value={languageCode}
            onChange={(e) => setLanguageCode(e.target.value)}
            placeholder="en"
            maxLength={12}
          />
          <Input
            label="Label"
            value={label}
            onChange={(e) => setLabel(e.target.value)}
            placeholder="English"
            maxLength={80}
          />
          <div className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold tracking-tight text-navy">VTT file</span>
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-border bg-surface px-3 py-2.5 text-sm font-bold text-navy hover:bg-background-light">
              <span className="max-w-40 truncate">{file ? file.name : 'Choose .vtt'}</span>
              <input
                type="file"
                accept=".vtt,text/vtt"
                className="hidden"
                onChange={(e) => setFile(e.target.files?.[0] ?? null)}
              />
            </label>
          </div>
          <Button
            type="button"
            variant="primary"
            size="sm"
            className="self-end"
            onClick={() => void handleAdd()}
            disabled={busy}
          >
            {busy ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : <Plus className="mr-1 h-3.5 w-3.5" />}
            Add caption
          </Button>
        </div>
      ) : null}
    </div>
  );
}
