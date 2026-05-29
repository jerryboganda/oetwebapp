'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { Camera, FileImage, RotateCcw, CheckCircle2, AlertCircle, Loader2 } from 'lucide-react';
import { cn } from '@/lib/utils';
import { Card, CardContent } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { uploadWritingOcrImages, getWritingOcrJob } from '@/lib/writing/api';
import type { WritingOcrJobDto, WritingOcrStatus } from '@/lib/writing/types';

export interface PaperModeUploaderProps {
  /**
   * Called with the OCR-extracted text once the job reports completed.
   * Parent typically pushes this into the editor body.
   */
  onComplete: (extractedText: string) => void;
  /**
   * Optional submission id for linking the OCR job back to a draft.
   */
  submissionId?: string;
  /**
   * Polling cadence in ms. Defaults to 2 seconds per spec §11.5.
   */
  pollIntervalMs?: number;
  className?: string;
}

const STATUS_LABEL: Record<WritingOcrStatus, string> = {
  pending: 'Queued',
  processing: 'Reading your handwriting…',
  completed: 'Done',
  failed: 'Failed',
  manual_required: 'Needs transcription',
};

/**
 * Paper-mode capture + OCR uploader (per spec §11.5).
 *
 * Workflow:
 *   1. Learner picks one or more images (camera on mobile, file picker
 *      on desktop) via `<input type="file" accept="image/*"
 *      capture="environment">`.
 *   2. We POST a multipart upload to /v1/writing/ocr/upload via
 *      `uploadWritingOcrImages`.
 *   3. We poll GET /v1/writing/ocr/jobs/{id} every 2s until status is
 *      `completed` or `failed`.
 *   4. On `completed`, we surface the extracted text + two CTAs:
 *      "Use this" (passes to onComplete) or "Retake" (resets state).
 *   5. On `failed`, we show the error message + a Retake button.
 */
export function PaperModeUploader({
  onComplete,
  submissionId,
  pollIntervalMs = 2000,
  className,
}: PaperModeUploaderProps) {
  const [job, setJob] = useState<WritingOcrJobDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);
  const pollTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  const reset = useCallback(() => {
    if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
    pollTimerRef.current = null;
    setJob(null);
    setError(null);
    setUploading(false);
    if (fileInputRef.current) fileInputRef.current.value = '';
  }, []);

  // Cleanup poll on unmount
  useEffect(() => {
    return () => {
      if (pollTimerRef.current) clearTimeout(pollTimerRef.current);
    };
  }, []);

  // Hold the latest pollOnce in a ref so the recursive setTimeout schedule
  // never has to refer to `pollOnce` before its declaration completes —
  // satisfies react-hooks/immutability without changing runtime behaviour.
  const pollOnceRef = useRef<((jobId: string) => Promise<void>) | null>(null);
  const pollOnce = useCallback(
    async (jobId: string) => {
      try {
        const fresh = await getWritingOcrJob(jobId);
        setJob(fresh);
        if (fresh.status === 'completed' || fresh.status === 'failed' || fresh.status === 'manual_required') {
          return;
        }
        pollTimerRef.current = setTimeout(() => {
          void pollOnceRef.current?.(jobId);
        }, pollIntervalMs);
      } catch (err) {
        setError((err as Error).message ?? 'OCR polling failed');
      }
    },
    [pollIntervalMs],
  );
  pollOnceRef.current = pollOnce;

  const handleFiles = useCallback(
    async (files: FileList | null) => {
      if (!files || files.length === 0) return;
      const fileArray = Array.from(files);
      setError(null);
      setUploading(true);
      try {
        const created = await uploadWritingOcrImages(fileArray, { submissionId });
        setJob(created);
        setUploading(false);
        if (created.status !== 'completed' && created.status !== 'failed' && created.status !== 'manual_required') {
          void pollOnce(created.id);
        }
      } catch (err) {
        setError((err as Error).message ?? 'Upload failed');
        setUploading(false);
      }
    },
    [submissionId, pollOnce],
  );

  const handleUse = () => {
    if (job?.status === 'completed' && job.extractedText) {
      onComplete(job.extractedText);
    }
  };

  const inputId = 'paper-mode-file-input';
  const status = job?.status;
  const isPolling = status === 'pending' || status === 'processing';

  return (
    <Card padding="md" className={cn('flex flex-col gap-3', className)} aria-label="Paper-mode OCR uploader">
      <CardContent>
        <header className="flex items-center gap-2 mb-3">
          <Camera className="w-5 h-5 text-primary shrink-0" aria-hidden="true" />
          <h3 className="font-extrabold text-base">Upload handwritten letter</h3>
        </header>

        {!job ? (
          <div className="rounded-lg border border-dashed border-border bg-surface p-6 text-center">
            <FileImage className="w-10 h-10 text-muted mx-auto mb-2" aria-hidden="true" />
            <p className="text-sm text-muted mb-3">
              Snap a clear photo (or upload a scan) of each page. We&apos;ll extract the text using OCR.
            </p>
            <input
              ref={fileInputRef}
              id={inputId}
              type="file"
              accept="image/*"
              multiple
              // `capture="environment"` triggers the rear camera on mobile;
              // ignored gracefully on desktop where it falls back to the
              // standard file picker.
              capture="environment"
              className="sr-only"
              onChange={(e) => void handleFiles(e.target.files)}
              aria-label="Choose images to upload"
            />
            <label
              htmlFor={inputId}
              className={cn(
                'inline-flex items-center gap-1.5 rounded-lg bg-primary text-white dark:bg-violet-700 px-4 py-2 text-sm font-bold cursor-pointer',
                'hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2',
                uploading && 'opacity-60 pointer-events-none',
              )}
            >
              {uploading ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" aria-hidden="true" />
                  Uploading…
                </>
              ) : (
                <>
                  <Camera className="w-4 h-4" aria-hidden="true" />
                  Take photo / choose file
                </>
              )}
            </label>
          </div>
        ) : (
          <div className="rounded-lg border border-border bg-surface p-4">
            <div className="flex items-center gap-2 mb-2">
              {status === 'completed' ? (
                <CheckCircle2 className="w-5 h-5 text-success" aria-hidden="true" />
              ) : status === 'failed' || status === 'manual_required' ? (
                <AlertCircle className="w-5 h-5 text-danger" aria-hidden="true" />
              ) : (
                <Loader2 className="w-5 h-5 animate-spin text-primary" aria-hidden="true" />
              )}
              <p className="text-sm font-bold" role="status" aria-live="polite">
                {STATUS_LABEL[status ?? 'pending']}
                {job.provider ? ` · ${job.provider}` : ''}
                {typeof job.confidenceScore === 'number'
                  ? ` · ${Math.round(job.confidenceScore)}% confidence`
                  : ''}
              </p>
            </div>

            {status === 'completed' && job.extractedText ? (
              <>
                <pre className="whitespace-pre-wrap text-xs leading-relaxed font-sans p-2 rounded border border-border bg-background-light max-h-64 overflow-y-auto">
                  {job.extractedText}
                </pre>
                <div className="mt-3 flex items-center justify-end gap-2">
                  <Button type="button" variant="outline" size="sm" onClick={reset}>
                    <RotateCcw className="w-3.5 h-3.5 mr-1" aria-hidden="true" />
                    Retake
                  </Button>
                  <Button type="button" variant="primary" size="sm" onClick={handleUse}>
                    Use this text
                  </Button>
                </div>
              </>
            ) : null}

            {status === 'failed' || status === 'manual_required' ? (
              <>
                <p className="text-xs text-danger mt-1">
                  {job.errorMessage ?? 'OCR could not read the image. Try again with better lighting or transcribe manually.'}
                </p>
                <div className="mt-3 flex items-center justify-end">
                  <Button type="button" variant="outline" size="sm" onClick={reset}>
                    <RotateCcw className="w-3.5 h-3.5 mr-1" aria-hidden="true" />
                    Retake
                  </Button>
                </div>
              </>
            ) : null}

            {isPolling ? (
              <p className="text-xs text-muted mt-1">This usually takes 15-60 seconds.</p>
            ) : null}
          </div>
        )}

        {error ? (
          <p className="mt-3 text-xs text-danger" role="alert">
            {error}
          </p>
        ) : null}
      </CardContent>
    </Card>
  );
}
