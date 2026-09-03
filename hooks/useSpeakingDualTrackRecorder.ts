'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import {
  DualTrackRecorder,
  retrieveStoredRecording,
  clearStoredRecording,
  type DualTrackRecorderState,
  type DualTrackRecordingResult,
} from '@/lib/speaking/dual-track-recorder';

export interface UseSpeakingDualTrackRecorderResult {
  state: DualTrackRecorderState;
  hasStoredFallback: boolean;
  startRecording: (stream: MediaStream) => void;
  pauseRecording: () => void;
  resumeRecording: () => void;
  stopRecording: () => Promise<DualTrackRecordingResult | null>;
  retrieveFallback: () => Promise<DualTrackRecordingResult | null>;
  clearFallback: () => Promise<void>;
}

export function useSpeakingDualTrackRecorder(sessionId: string): UseSpeakingDualTrackRecorderResult {
  const recorderRef = useRef<DualTrackRecorder | null>(null);
  const [hasStoredFallback, setHasStoredFallback] = useState(false);
  const [state, setState] = useState<DualTrackRecorderState>({
    isRecording: false,
    isPaused: false,
    elapsedMs: 0,
    chunkCount: 0,
    totalBytes: 0,
    mimeType: 'audio/webm',
  });

  useEffect(() => {
    if (!sessionId) return;
    recorderRef.current = new DualTrackRecorder(sessionId);
    void retrieveStoredRecording(sessionId).then((rec) => {
      setHasStoredFallback(Boolean(rec));
    });

    return () => {
      if (recorderRef.current?.getState().isRecording) {
        void recorderRef.current.stop();
      }
    };
  }, [sessionId]);

  const startRecording = useCallback((stream: MediaStream) => {
    if (!recorderRef.current) {
      recorderRef.current = new DualTrackRecorder(sessionId);
    }
    recorderRef.current.start(stream);
    setState(recorderRef.current.getState());
  }, [sessionId]);

  const pauseRecording = useCallback(() => {
    recorderRef.current?.pause();
    if (recorderRef.current) setState(recorderRef.current.getState());
  }, []);

  const resumeRecording = useCallback(() => {
    recorderRef.current?.resume();
    if (recorderRef.current) setState(recorderRef.current.getState());
  }, []);

  const stopRecording = useCallback(async () => {
    if (!recorderRef.current) return null;
    const result = await recorderRef.current.stop();
    setState(recorderRef.current.getState());
    if (result) {
      setHasStoredFallback(true);
    }
    return result;
  }, []);

  const retrieveFallback = useCallback(async () => {
    return retrieveStoredRecording(sessionId);
  }, [sessionId]);

  const clearFallback = useCallback(async () => {
    await clearStoredRecording(sessionId);
    setHasStoredFallback(false);
  }, [sessionId]);

  return {
    state,
    hasStoredFallback,
    startRecording,
    pauseRecording,
    resumeRecording,
    stopRecording,
    retrieveFallback,
    clearFallback,
  };
}
