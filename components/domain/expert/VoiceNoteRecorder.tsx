'use client';

// Phase 7b — Per-criterion voice-note recorder for expert review pages.
//
// Mounts next to each criterion score on the speaking and writing expert
// review surfaces. Captures a short spoken comment (default 60s) via the
// browser MediaRecorder API, plays it back for self-review, and on confirm
// uploads it as a two-step flow:
//   1) POST /v1/media/upload  → returns { id } (MediaAsset)
//   2) POST /v1/expert/{subtest}/reviews/{id}/voice-notes
//      with { mediaAssetId, durationSeconds, writtenNotes (criterion tag),
//      rubricJson ({ criterionCode }) }
//
// The criterion is round-tripped through writtenNotes + rubricJson so this
// component does not require backend schema changes — neither voice-note
// table has a dedicated criterion column. Existing review-level voice notes
// continue to work; this component is purely additive.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Mic, Square, RotateCcw, Upload, Play } from 'lucide-react';
import { Button } from '@/components/ui/button';
import {
  uploadSpeakingReviewCriterionVoiceNote,
  uploadWritingReviewCriterionVoiceNote,
  type ReviewCriterionVoiceNoteResult,
} from '@/lib/api';

export type VoiceNoteSubtest = 'speaking' | 'writing';

export type VoiceNoteUploader = (
  id: string,
  body: { audio: Blob; criterionCode: string; durationMs: number },
) => Promise<ReviewCriterionVoiceNoteResult>;

export interface VoiceNoteRecorderProps {
  reviewRequestId: string;
  criterionCode: string;
  /** Default 60 seconds. Hard ceiling enforced client-side. */
  maxSeconds?: number;
  /** Which review surface this is mounted on. Defaults to 'speaking'. */
  subtest?: VoiceNoteSubtest;
  /**
   * Custom uploader. When provided, overrides the default subtest-based upload
   * (used by the writing marking workspace to post a submission-keyed overall
   * note instead of the ReviewRequest-keyed criterion note).
   */
  uploader?: VoiceNoteUploader;
  /** Called once the recorder has uploaded the voice note. */
  onUploaded?: (voiceNoteId: string, durationMs: number) => void;
  /** Existing playback URL — render a play button + "Replace existing note". */
  existingVoiceNoteUrl?: string;
  /** Forwarded for layout/grid placement. */
  className?: string;
  /** Render as a `<details>` block so the recorder collapses by default. */
  collapsed?: boolean;
}

type RecorderState =
  | 'idle'
  | 'recording'
  | 'paused'
  | 'recorded'
  | 'uploading'
  | 'uploaded'
  | 'error';

const PREFERRED_MIME = 'audio/webm;codecs=opus';
const MAX_RE_RECORDS = 1;

function pickMimeType(): string | undefined {
  if (typeof MediaRecorder === 'undefined') return undefined;
  if (MediaRecorder.isTypeSupported(PREFERRED_MIME)) return PREFERRED_MIME;
  if (MediaRecorder.isTypeSupported('audio/webm')) return 'audio/webm';
  return undefined;
}

function formatTimer(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds < 0) seconds = 0;
  const total = Math.floor(seconds);
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function VoiceNoteRecorder({
  reviewRequestId,
  criterionCode,
  maxSeconds = 60,
  subtest = 'speaking',
  uploader,
  onUploaded,
  existingVoiceNoteUrl,
  className = '',
  collapsed = false,
}: VoiceNoteRecorderProps) {
  const [state, setState] = useState<RecorderState>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [elapsedSeconds, setElapsedSeconds] = useState(0);
  const [playbackUrl, setPlaybackUrl] = useState<string | null>(null);
  const [reRecordCount, setReRecordCount] = useState(0);

  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const startedAtRef = useRef<number>(0);
  const durationMsRef = useRef<number>(0);
  const tickTimerRef = useRef<number | null>(null);
  const blobRef = useRef<Blob | null>(null);

  const canRecord = reRecordCount <= MAX_RE_RECORDS;

  const stopTracks = useCallback(() => {
    streamRef.current?.getTracks().forEach((track) => {
      try { track.stop(); } catch {/* ignore */}
    });
    streamRef.current = null;
    recorderRef.current = null;
    if (tickTimerRef.current != null) {
      window.clearInterval(tickTimerRef.current);
      tickTimerRef.current = null;
    }
  }, []);

  const revokePlaybackUrl = useCallback(() => {
    if (playbackUrl) {
      try { URL.revokeObjectURL(playbackUrl); } catch {/* ignore */}
    }
  }, [playbackUrl]);

  const resetToIdle = useCallback(() => {
    stopTracks();
    revokePlaybackUrl();
    chunksRef.current = [];
    blobRef.current = null;
    setPlaybackUrl(null);
    setElapsedSeconds(0);
    setErrorMessage(null);
    setState('idle');
  }, [revokePlaybackUrl, stopTracks]);

  useEffect(() => {
    return () => {
      stopTracks();
      if (playbackUrl) {
        try { URL.revokeObjectURL(playbackUrl); } catch {/* ignore */}
      }
    };
    // We intentionally only cleanup on unmount.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const handleStartRecording = useCallback(async () => {
    setErrorMessage(null);
    if (typeof window === 'undefined' || !navigator?.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
      setErrorMessage('Audio recording is not supported in this browser.');
      setState('error');
      return;
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const mime = pickMimeType();
      const recorder = mime ? new MediaRecorder(stream, { mimeType: mime }) : new MediaRecorder(stream);
      recorderRef.current = recorder;
      chunksRef.current = [];
      startedAtRef.current = Date.now();
      durationMsRef.current = 0;

      recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) chunksRef.current.push(event.data);
      };
      recorder.onstop = () => {
        const stoppedAt = Date.now();
        durationMsRef.current = stoppedAt - startedAtRef.current;
        const blob = new Blob(chunksRef.current, { type: recorder.mimeType || mime || 'audio/webm' });
        blobRef.current = blob;
        const objectUrl = URL.createObjectURL(blob);
        setPlaybackUrl(objectUrl);
        setElapsedSeconds(Math.min(maxSeconds, Math.floor(durationMsRef.current / 1000)));
        stopTracks();
        setState('recorded');
      };

      recorder.start();
      setState('recording');
      setElapsedSeconds(0);

      tickTimerRef.current = window.setInterval(() => {
        const next = Math.floor((Date.now() - startedAtRef.current) / 1000);
        setElapsedSeconds(next);
        if (next >= maxSeconds) {
          try { recorderRef.current?.stop(); } catch {/* ignore */}
        }
      }, 250);
    } catch (err) {
      setErrorMessage(err instanceof Error && err.name === 'NotAllowedError'
        ? 'Microphone permission was denied. Allow mic access to record a note.'
        : 'Could not start the microphone. Check device permissions.');
      setState('error');
      stopTracks();
    }
  }, [maxSeconds, stopTracks]);

  const handleStopRecording = useCallback(() => {
    if (recorderRef.current && recorderRef.current.state !== 'inactive') {
      try { recorderRef.current.stop(); } catch {/* ignore */}
    } else {
      stopTracks();
      setState('recorded');
    }
  }, [stopTracks]);

  const handleReRecord = useCallback(() => {
    if (!canRecord) return;
    setReRecordCount((current) => current + 1);
    resetToIdle();
  }, [canRecord, resetToIdle]);

  const handleUpload = useCallback(async () => {
    if (!blobRef.current) {
      setErrorMessage('Nothing to upload. Record a note first.');
      setState('error');
      return;
    }
    setState('uploading');
    setErrorMessage(null);
    try {
      const upload = uploader ?? (subtest === 'writing' ? uploadWritingReviewCriterionVoiceNote : uploadSpeakingReviewCriterionVoiceNote);
      const result: ReviewCriterionVoiceNoteResult = await upload(reviewRequestId, {
        audio: blobRef.current,
        criterionCode,
        durationMs: durationMsRef.current,
      });
      onUploaded?.(result.voiceNoteId, durationMsRef.current);
      setState('uploaded');
    } catch (err) {
      setErrorMessage(err instanceof Error ? err.message : 'Upload failed. Please try again.');
      setState('error');
    }
  }, [criterionCode, onUploaded, reviewRequestId, subtest, uploader]);

  const timerLabel = useMemo(() => `${formatTimer(elapsedSeconds)} / ${formatTimer(maxSeconds)}`, [elapsedSeconds, maxSeconds]);

  const body = (
    <div className={`flex flex-col gap-2 ${className}`} data-testid="voice-note-recorder">
      {existingVoiceNoteUrl ? (
        <div className="flex items-center gap-2">
          <audio
            controls
            src={existingVoiceNoteUrl}
            preload="metadata"
            className="h-8 flex-1 min-w-0"
            aria-label={`Existing voice note for ${criterionCode}`}
          />
          <span className="text-[11px] text-muted">Existing note</span>
        </div>
      ) : null}

      <div className="flex flex-wrap items-center gap-2">
        {state === 'idle' && (
          <Button
            type="button"
            size="sm"
            variant="outline"
            onClick={() => void handleStartRecording()}
            disabled={!canRecord}
            aria-label={`Record voice note for ${criterionCode}`}
          >
            <Mic className="h-3.5 w-3.5" /> Record
          </Button>
        )}

        {state === 'recording' && (
          <Button
            type="button"
            size="sm"
            variant="destructive"
            onClick={handleStopRecording}
            aria-label="Stop recording"
          >
            <Square className="h-3.5 w-3.5" /> Stop
          </Button>
        )}

        {state === 'recorded' && (
          <>
            <Button
              type="button"
              size="sm"
              variant="primary"
              onClick={() => void handleUpload()}
              aria-label="Upload voice note"
            >
              <Upload className="h-3.5 w-3.5" /> {existingVoiceNoteUrl ? 'Replace' : 'Upload'}
            </Button>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={handleReRecord}
              disabled={!canRecord}
              aria-label="Re-record voice note"
            >
              <RotateCcw className="h-3.5 w-3.5" /> Re-record
            </Button>
          </>
        )}

        {state === 'uploading' && (
          <Button type="button" size="sm" variant="primary" disabled loading aria-label="Uploading voice note">
            Uploading...
          </Button>
        )}

        {state === 'uploaded' && (
          <>
            <span className="inline-flex items-center gap-1 rounded-full bg-green-50 px-2 py-1 text-[11px] font-semibold text-green-700">
              Uploaded
            </span>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={handleReRecord}
              disabled={!canRecord}
              aria-label="Record a new note (replaces previous)"
            >
              <RotateCcw className="h-3.5 w-3.5" /> New
            </Button>
          </>
        )}

        {state === 'error' && (
          <>
            <Button
              type="button"
              size="sm"
              variant="outline"
              onClick={() => { setState('idle'); setErrorMessage(null); }}
              aria-label="Dismiss error and retry"
            >
              <RotateCcw className="h-3.5 w-3.5" /> Retry
            </Button>
          </>
        )}

        {(state === 'recording' || state === 'recorded' || state === 'uploading') && (
          <span className="font-mono text-xs text-muted" aria-live="polite">{timerLabel}</span>
        )}

        {state === 'recording' && (
          <span className="inline-flex items-center gap-1 text-[11px] font-semibold text-red-600" aria-live="polite">
            <span className="inline-block h-2 w-2 animate-pulse rounded-full bg-red-500" aria-hidden="true" /> Recording
          </span>
        )}
      </div>

      {playbackUrl && (state === 'recorded' || state === 'uploading' || state === 'uploaded') && (
        <audio
          controls
          src={playbackUrl}
          preload="metadata"
          className="h-8 w-full"
          aria-label={`Playback of recorded voice note for ${criterionCode}`}
        />
      )}

      {errorMessage && (
        <p className="text-[11px] font-medium text-red-600" role="alert">{errorMessage}</p>
      )}
    </div>
  );

  if (collapsed) {
    return (
      <details className="rounded-md border border-border bg-surface p-2 text-xs open:bg-muted">
        <summary className="cursor-pointer select-none text-xs font-semibold text-navy">
          <span className="inline-flex items-center gap-1">
            <Play className="h-3 w-3" aria-hidden="true" /> Voice note
            {state === 'uploaded' ? <span className="ml-1 inline-block h-1.5 w-1.5 rounded-full bg-green-500" aria-label="uploaded" /> : null}
          </span>
        </summary>
        <div className="mt-2">{body}</div>
      </details>
    );
  }

  return body;
}

export default VoiceNoteRecorder;
