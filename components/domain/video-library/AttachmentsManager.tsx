'use client';

/**
 * Extras — PDF attachments (worksheets, transcripts, cheat-sheets). Files
 * upload through the existing chunked pipeline (`VideoAttachment` role),
 * register via `POST …/attachments`, and reorder via `PUT …/attachments/order`.
 */

import { useState } from 'react';
import { ArrowDown, ArrowUp, FileText, Loader2, Plus, Trash2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { uploadFileChunked } from '@/lib/content-upload-api';
import {
  adminAddVideoAttachment,
  adminDeleteVideoAttachment,
  adminOrderVideoAttachments,
  type AdminVideoDetail,
} from '@/lib/api/video-library';

export interface AttachmentsManagerProps {
  video: AdminVideoDetail;
  canWrite: boolean;
  onChanged: () => void;
}

export function AttachmentsManager({ video, canWrite, onChanged }: AttachmentsManagerProps) {
  const [title, setTitle] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [busy, setBusy] = useState(false);
  const [rowBusyId, setRowBusyId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  const attachments = [...(video.attachments ?? [])].sort((a, b) => a.sortOrder - b.sortOrder);

  async function handleAdd() {
    if (!file) {
      setError('Choose a PDF file first.');
      return;
    }
    if (!title.trim()) {
      setError('Attachment title is required.');
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const result = await uploadFileChunked(file, 'VideoAttachment');
      await adminAddVideoAttachment(video.videoId, {
        mediaAssetId: result.mediaAssetId,
        title: title.trim(),
      });
      setTitle('');
      setFile(null);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not add the attachment.');
    } finally {
      setBusy(false);
    }
  }

  async function handleDelete(attachmentId: string) {
    setRowBusyId(attachmentId);
    setError(null);
    try {
      await adminDeleteVideoAttachment(video.videoId, attachmentId);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not delete the attachment.');
    } finally {
      setRowBusyId(null);
    }
  }

  async function handleMove(index: number, direction: -1 | 1) {
    const target = index + direction;
    if (target < 0 || target >= attachments.length) return;
    const orderedIds = attachments.map((a) => a.id);
    [orderedIds[index], orderedIds[target]] = [orderedIds[target], orderedIds[index]];
    setRowBusyId(attachments[index].id);
    setError(null);
    try {
      await adminOrderVideoAttachments(video.videoId, orderedIds);
      onChanged();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not reorder the attachments.');
    } finally {
      setRowBusyId(null);
    }
  }

  return (
    <div className="rounded-2xl border border-border bg-background-light p-4">
      <p className="text-sm font-bold text-navy">PDF attachments</p>
      <p className="text-xs text-muted">Downloadable resources shown beside the video.</p>

      {error ? (
        <div className="mt-3">
          <InlineAlert variant="error">{error}</InlineAlert>
        </div>
      ) : null}

      {attachments.length > 0 ? (
        <ul className="mt-3 space-y-2">
          {attachments.map((attachment, index) => (
            <li
              key={attachment.id}
              className="flex items-center justify-between gap-2 rounded-xl border border-border bg-surface px-3 py-2"
            >
              <div className="flex min-w-0 items-center gap-2">
                <FileText className="h-4 w-4 shrink-0 text-muted" />
                <span className="truncate text-sm text-navy">{attachment.title}</span>
              </div>
              {canWrite ? (
                <div className="flex items-center gap-1">
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => void handleMove(index, -1)}
                    disabled={index === 0 || rowBusyId !== null}
                    aria-label={`Move ${attachment.title} up`}
                  >
                    <ArrowUp className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => void handleMove(index, 1)}
                    disabled={index === attachments.length - 1 || rowBusyId !== null}
                    aria-label={`Move ${attachment.title} down`}
                  >
                    <ArrowDown className="h-3.5 w-3.5" />
                  </Button>
                  <Button
                    type="button"
                    variant="ghost"
                    size="sm"
                    onClick={() => void handleDelete(attachment.id)}
                    disabled={rowBusyId !== null}
                    aria-label={`Delete ${attachment.title}`}
                  >
                    {rowBusyId === attachment.id ? (
                      <Loader2 className="h-3.5 w-3.5 animate-spin" />
                    ) : (
                      <Trash2 className="h-3.5 w-3.5" />
                    )}
                  </Button>
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      ) : (
        <p className="mt-3 text-xs text-muted">No attachments yet.</p>
      )}

      {canWrite ? (
        <div className="mt-4 grid gap-2 border-t border-border pt-3 sm:grid-cols-[1fr_auto_auto]">
          <Input
            label="Attachment title"
            value={title}
            onChange={(e) => setTitle(e.target.value)}
            placeholder='e.g. "Lesson worksheet"'
            maxLength={160}
          />
          <div className="flex flex-col gap-1.5">
            <span className="text-sm font-semibold tracking-tight text-navy">PDF file</span>
            <label className="inline-flex cursor-pointer items-center gap-2 rounded-xl border border-border bg-surface px-3 py-2.5 text-sm font-bold text-navy hover:bg-background-light">
              <span className="max-w-40 truncate">{file ? file.name : 'Choose .pdf'}</span>
              <input
                type="file"
                accept="application/pdf"
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
            Add attachment
          </Button>
        </div>
      ) : null}
    </div>
  );
}
