'use client';

/**
 * Recorder fallback for the shared Speaking session engine (23 Sep 2026
 * owner decision): when no live-voice provider is available the candidate
 * records the 5-minute role-play and the server transcribes + grades it.
 *
 * `stop()` is the page's single finalize hook: it stops the recorder once,
 * keeps the blob in memory and uploads it. It is idempotent — call it again
 * after an upload failure to retry the SAME blob; it never discards audio.
 * It resolves true when the audio landed ('uploaded') or when the server
 * permanently refused it ('rejected': a retry cannot help and the caller must
 * not be stranded, but it is never reported as received); false only for a
 * failure worth retrying (network, 5xx, timeout, rate limit, expired sign-in).
 */
import { useCallback, useEffect, useRef, useState } from 'react';
import { ApiError } from '@/lib/api';
import { uploadSpeakingSessionRecording } from '@/lib/api/speaking-sessions';
import { describeMicrophoneError } from '@/lib/mobile/speaking-recorder';

export type SpeakingRecorderStatus =
  | 'idle'
  | 'starting'
  | 'recording'
  | 'uploading'
  | 'uploaded'
  | 'upload_failed'
  | 'rejected'
  | 'error';

export const RECORDING_UPLOAD_FAILED = 'Upload failed — Retry upload';
/** Shown when the server permanently refused the recording and gave no message of its own. */
const RECORDING_NOT_SAVED = 'The recording could not be saved.';

/**
 * A 4xx the server will give again for this recording (upload window closed, session state, invalid audio): a retry
 * cannot help. Not a sign-in expiry (401), a timeout (408) or the rate limit (429): those clear on a retry.
 */
function isPermanentRefusal(caught: unknown): caught is ApiError {
  return caught instanceof ApiError && caught.status >= 400 && caught.status < 500 && ![401, 408, 429].includes(caught.status);
}

/** webm/opus where supported (Chrome, Firefox, Android webview); mp4 for Safari / iOS webviews. */
export function pickRecordingMimeType(): string | undefined {
  if (typeof MediaRecorder === 'undefined' || typeof MediaRecorder.isTypeSupported !== 'function') return undefined;
  return ['audio/webm;codecs=opus', 'audio/webm', 'audio/mp4', 'audio/ogg;codecs=opus']
    .find((type) => MediaRecorder.isTypeSupported(type));
}

export interface UseSpeakingSessionRecorderResult {
  status: SpeakingRecorderStatus;
  error: string | null;
  /** True when the last start failed because microphone permission was refused (drives the app-settings recovery path). */
  micPermissionDenied: boolean;
  /** 0..1 microphone level for the single activity indicator. */
  level: number;
  start: () => Promise<boolean>;
  stop: () => Promise<boolean>;
}

export function useSpeakingSessionRecorder(sessionId: string): UseSpeakingSessionRecorderResult {
  const [status, setStatus] = useState<SpeakingRecorderStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const [micPermissionDenied, setMicPermissionDenied] = useState(false);
  const [level, setLevel] = useState(0);

  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  // ponytail: in-memory only — a tab close before a successful upload loses
  // the audio; persist to IndexedDB if that shows up in support tickets.
  const blobRef = useRef<Blob | null>(null);
  const startedAtRef = useRef<number | null>(null);
  const durationSecondsRef = useRef(0);
  const uploadedRef = useRef(false);
  const stopPromiseRef = useRef<Promise<boolean> | null>(null);
  const contextRef = useRef<AudioContext | null>(null);
  const frameRef = useRef<number | null>(null);

  const releaseMic = useCallback(() => {
    if (frameRef.current !== null) window.cancelAnimationFrame(frameRef.current);
    frameRef.current = null;
    void contextRef.current?.close().catch(() => undefined);
    contextRef.current = null;
    streamRef.current?.getTracks().forEach((track) => track.stop());
    streamRef.current = null;
    setLevel(0);
  }, []);

  useEffect(() => () => {
    const recorder = recorderRef.current;
    if (recorder && recorder.state !== 'inactive') {
      try {
        recorder.stop();
      } catch {}
    }
    releaseMic();
  }, [releaseMic]);

  const start = useCallback(async () => {
    if (recorderRef.current || blobRef.current) return true;
    setError(null);
    setMicPermissionDenied(false);
    setStatus('starting');
    try {
      if (!navigator.mediaDevices?.getUserMedia || typeof MediaRecorder === 'undefined') {
        throw new Error('this browser cannot record audio. Use an updated Chrome, Edge, Safari or the mobile app.');
      }
      const stream = await navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true } });
      streamRef.current = stream;
      const mimeType = pickRecordingMimeType();
      const recorder = mimeType ? new MediaRecorder(stream, { mimeType }) : new MediaRecorder(stream);
      chunksRef.current = [];
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data);
      };
      recorder.start(1000);
      recorderRef.current = recorder;
      startedAtRef.current = Date.now();
      setStatus('recording');

      // Level meter is cosmetic; recording works even if the context stays suspended.
      try {
        const context = new AudioContext();
        contextRef.current = context;
        const analyser = context.createAnalyser();
        analyser.fftSize = 256;
        context.createMediaStreamSource(stream).connect(analyser);
        const data = new Uint8Array(analyser.fftSize);
        const tick = () => {
          analyser.getByteTimeDomainData(data);
          let peak = 0;
          for (let index = 0; index < data.length; index += 1) peak = Math.max(peak, Math.abs(data[index] - 128));
          setLevel(Math.min(1, peak / 64));
          frameRef.current = window.requestAnimationFrame(tick);
        };
        tick();
      } catch {}
      return true;
    } catch (caught) {
      releaseMic();
      recorderRef.current = null;
      const micError = describeMicrophoneError(caught);
      setMicPermissionDenied(micError.permissionDenied);
      // The shared microphone copy names "Start recording"; the control on this panel is "Start speaking".
      setError(micError.message.replace('Start recording', 'Start speaking'));
      setStatus('error');
      return false;
    }
  }, [releaseMic]);

  const finalizeBlob = useCallback(async (): Promise<Blob | null> => {
    if (blobRef.current) return blobRef.current;
    const recorder = recorderRef.current;
    if (!recorder) return null;
    if (recorder.state !== 'inactive') {
      await new Promise<void>((resolve) => {
        recorder.addEventListener('stop', () => resolve(), { once: true });
        recorder.stop();
      });
    }
    if (startedAtRef.current !== null) {
      durationSecondsRef.current = Math.round((Date.now() - startedAtRef.current) / 1000);
    }
    releaseMic();
    const blob = new Blob(chunksRef.current, { type: recorder.mimeType || pickRecordingMimeType() || 'audio/webm' });
    blobRef.current = blob;
    return blob;
  }, [releaseMic]);

  const stop = useCallback(() => {
    if (uploadedRef.current) return Promise.resolve(true);
    if (stopPromiseRef.current) return stopPromiseRef.current;
    const run = async () => {
      const blob = await finalizeBlob();
      if (!blob || blob.size === 0) {
        setError('No audio was recorded. Press Start speaking and try again.');
        setStatus('error');
        return false;
      }
      setStatus('uploading');
      setError(null);
      try {
        await uploadSpeakingSessionRecording(sessionId, blob, durationSecondsRef.current);
        uploadedRef.current = true;
        setStatus('uploaded');
        return true;
      } catch (caught) {
        console.warn('Speaking recording upload failed', caught instanceof ApiError ? caught.code : caught);
        if (isPermanentRefusal(caught)) {
          // The server will never take this recording, so the caller may move on: a retry loop would strand the
          // learner. It is never reported as uploaded.
          setError(caught.userMessage || RECORDING_NOT_SAVED);
          setStatus('rejected');
          return true;
        }
        setError(RECORDING_UPLOAD_FAILED);
        setStatus('upload_failed');
        return false;
      }
    };
    const promise = run().finally(() => {
      stopPromiseRef.current = null;
    });
    stopPromiseRef.current = promise;
    return promise;
  }, [finalizeBlob, sessionId]);

  return { status, error, micPermissionDenied, level, start, stop };
}
