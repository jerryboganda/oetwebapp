'use client';

import { useCallback, useEffect, useRef, useState } from 'react';

/**
 * Pronunciation recorder hook — thin wrapper over the Web Audio + MediaRecorder
 * APIs with:
 *   - Permission-primer logic (returns a stable `permission` state)
 *   - Level-meter tick via Web Audio AnalyserNode
 *   - Configurable max-duration auto-stop (OET Speaking drills benefit from a
 *     60-90 second cap so learners don't record marathons)
 *   - Automatic mime-type negotiation (prefers WebM/Opus, then MP4/AAC, then OGG)
 *   - Produces a Blob + objectURL + duration when stopped
 *   - Cleanly releases resources on unmount
 */
export type RecorderStatus =
  | 'idle'
  | 'requesting-permission'
  | 'ready'
  | 'recording'
  | 'stopping'
  | 'stopped'
  | 'error';

export type RecorderPermission = 'unknown' | 'granted' | 'denied';

export type PronunciationRecorderResult = {
  blob: Blob;
  url: string;
  mimeType: string;
  durationMs: number;
};

export type UsePronunciationRecorderOptions = {
  /** Hard cap on recording length in ms. Default 60_000. */
  maxDurationMs?: number;
  /** Called once per frame with a 0-1 level reading for UI meter. */
  onLevel?: (level: number) => void;
  /** Called when max duration reached and recording auto-stopped. */
  onMaxDurationReached?: () => void;
};

const PREFERRED_MIME_TYPES = [
  'audio/webm;codecs=opus',
  'audio/webm',
  'audio/ogg;codecs=opus',
  'audio/ogg',
  'audio/mp4',
  'audio/aac',
];

function pickMimeType(): string {
  if (typeof MediaRecorder === 'undefined') return 'audio/webm';
  for (const mt of PREFERRED_MIME_TYPES) {
    try {
      if (MediaRecorder.isTypeSupported(mt)) return mt;
    } catch {
      /* ignore */
    }
  }
  return 'audio/webm';
}

export function usePronunciationRecorder(options: UsePronunciationRecorderOptions = {}) {
  const { maxDurationMs = 60_000, onLevel, onMaxDurationReached } = options;

  const [status, setStatus] = useState<RecorderStatus>('idle');
  const [permission, setPermission] = useState<RecorderPermission>('unknown');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [level, setLevel] = useState(0);
  const [elapsedMs, setElapsedMs] = useState(0);
  const [result, setResult] = useState<PronunciationRecorderResult | null>(null);

  const recorderRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const streamRef = useRef<MediaStream | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const analyserRef = useRef<AnalyserNode | null>(null);
  const rafRef = useRef<number | null>(null);
  const startTimeRef = useRef<number>(0);
  const stopTimerRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const mimeTypeRef = useRef<string>('audio/webm');
  const resolveStopRef = useRef<((r: PronunciationRecorderResult) => void) | null>(null);
  const resultRef = useRef<PronunciationRecorderResult | null>(null);

  const onLevelRef = useRef(onLevel);
  useEffect(() => { onLevelRef.current = onLevel; }, [onLevel]);

  const onMaxDurationReachedRef = useRef(onMaxDurationReached);
  useEffect(() => { onMaxDurationReachedRef.current = onMaxDurationReached; }, [onMaxDurationReached]);

  const cleanup = useCallback(() => {
    if (rafRef.current !== null) {
      cancelAnimationFrame(rafRef.current);
      rafRef.current = null;
    }
    if (stopTimerRef.current) {
      clearTimeout(stopTimerRef.current);
      stopTimerRef.current = null;
    }
    if (recorderRef.current && recorderRef.current.state !== 'inactive') {
      try { recorderRef.current.stop(); } catch { /* ignore */ }
    }
    recorderRef.current = null;
    if (streamRef.current) {
      streamRef.current.getTracks().forEach((t) => t.stop());
      streamRef.current = null;
    }
    if (audioContextRef.current) {
      audioContextRef.current.close().catch(() => { /* ignore */ });
      audioContextRef.current = null;
    }
    analyserRef.current = null;
    if (resultRef.current?.url) {
      URL.revokeObjectURL(resultRef.current.url);
    }
  }, []);

  useEffect(() => cleanup, [cleanup]);

  const requestPermission = useCallback(async (): Promise<boolean> => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setErrorMessage('Your browser does not support microphone recording.');
      setPermission('denied');
      setStatus('error');
      return false;
    }
    setStatus('requesting-permission');
    try {
      const stream = await navigator.mediaDevices.getUserMedia({
        audio: {
          channelCount: 1,
          echoCancellation: true,
          noiseSuppression: true,
          autoGainControl: true,
        },
      });
      streamRef.current = stream;
      setPermission('granted');
      setStatus('ready');
      setErrorMessage(null);
      return true;
    } catch (err) {
      setPermission('denied');
      setStatus('error');
      const name = err instanceof Error ? err.name : '';
      setErrorMessage(
        name === 'NotAllowedError'
          ? 'Microphone access was denied. Allow microphone access in your browser settings to record.'
          : name === 'NotFoundError'
            ? 'No microphone was detected on this device.'
            : 'Could not access the microphone.',
      );
      return false;
    }
  }, []);

  const start = useCallback(async (): Promise<boolean> => {
    if (status === 'recording') return true;
    if (!streamRef.current) {
      const ok = await requestPermission();
      if (!ok) return false;
    }
    const stream = streamRef.current;
    if (!stream) return false;

    try {
      const mimeType = pickMimeType();
      mimeTypeRef.current = mimeType;
      const recorder = new MediaRecorder(stream, { mimeType });
      chunksRef.current = [];
      recorderRef.current = recorder;

      recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) {
          chunksRef.current.push(event.data);
        }
      };
      recorder.onstop = () => {
        const blob = new Blob(chunksRef.current, { type: mimeTypeRef.current });
        const url = URL.createObjectURL(blob);
        const durationMs = Date.now() - startTimeRef.current;
        const res: PronunciationRecorderResult = { blob, url, mimeType: mimeTypeRef.current, durationMs };
        resultRef.current = res;
        setResult(res);
        setElapsedMs(durationMs);
        setStatus('stopped');
        if (rafRef.current !== null) {
          cancelAnimationFrame(rafRef.current);
          rafRef.current = null;
        }
        setLevel(0);
        if (resolveStopRef.current) {
          resolveStopRef.current(res);
          resolveStopRef.current = null;
        }
      };

      // Level meter via AnalyserNode
      const audioContext = new (window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext)();
      audioContextRef.current = audioContext;
      const source = audioContext.createMediaStreamSource(stream);
      const analyser = audioContext.createAnalyser();
      analyser.fftSize = 512;
      source.connect(analyser);
      analyserRef.current = analyser;

      const dataArray = new Uint8Array(analyser.fftSize);
      const tick = () => {
        const a = analyserRef.current;
        if (!a) return;
        a.getByteTimeDomainData(dataArray);
        let sum = 0;
        for (let i = 0; i < dataArray.length; i++) {
          const v = (dataArray[i] - 128) / 128;
          sum += v * v;
        }
        const rms = Math.sqrt(sum / dataArray.length);
        const normalised = Math.min(1, rms * 3);
        setLevel(normalised);
        onLevelRef.current?.(normalised);
        setElapsedMs(Date.now() - startTimeRef.current);
        rafRef.current = requestAnimationFrame(tick);
      };

      startTimeRef.current = Date.now();
      recorder.start(200);
      setStatus('recording');
      setResult(null);
      resultRef.current = null;
      rafRef.current = requestAnimationFrame(tick);

      if (maxDurationMs > 0) {
        stopTimerRef.current = setTimeout(() => {
          onMaxDurationReachedRef.current?.();
          if (recorderRef.current && recorderRef.current.state === 'recording') {
            recorderRef.current.stop();
          }
        }, maxDurationMs);
      }
      return true;
    } catch (err) {
      setStatus('error');
      setErrorMessage(err instanceof Error ? err.message : 'Failed to start recording.');
      return false;
    }
  }, [maxDurationMs, requestPermission, status]);

  const stop = useCallback(async (): Promise<PronunciationRecorderResult | null> => {
    if (!recorderRef.current) return null;
    if (recorderRef.current.state === 'inactive') return result;
    setStatus('stopping');
    if (stopTimerRef.current) {
      clearTimeout(stopTimerRef.current);
      stopTimerRef.current = null;
    }
    return await new Promise<PronunciationRecorderResult | null>((resolve) => {
      resolveStopRef.current = (r) => resolve(r);
      recorderRef.current?.stop();
    });
  }, [result]);

  const reset = useCallback(() => {
    if (result?.url) {
      URL.revokeObjectURL(result.url);
    }
    setResult(null);
    resultRef.current = null;
    setLevel(0);
    setErrorMessage(null);
    if (status === 'stopped' || status === 'error') {
      setStatus(streamRef.current ? 'ready' : 'idle');
    }
  }, [result, status]);

  return {
    status,
    permission,
    errorMessage,
    level,
    elapsedMs,
    result,
    requestPermission,
    start,
    stop,
    reset,
  };
}
