'use client';

/**
 * Recorder fallback for the shared Speaking session engine (23 Sep 2026
 * owner decision): when no live-voice provider is available the candidate
 * records the 5-minute role-play and the server transcribes + grades it.
 *
 * `stop()` is the page's single finalize hook: it stops the recorder once,
 * keeps the blob in memory and uploads it. It is idempotent — call it again
 * after an upload failure to retry the SAME blob; it never discards audio.
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
  | 'error';

export const RECORDING_UPLOAD_FAILED = 'Upload failed — Retry upload';

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
      setError(micError.message);
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
