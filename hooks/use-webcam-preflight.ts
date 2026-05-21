'use client';

/**
 * Mocks V2 Phase 2 — webcam pre-flight hook.
 *
 * Mirrors the mic-check pattern: requests `getUserMedia({ video: true })`
 * on demand (must be triggered from a user gesture), then exposes the
 * stream plus a small status machine that the parent component renders.
 *
 * Status reflects browser-level outcomes:
 *  - `idle`         — never requested, or stream stopped
 *  - `requesting`   — awaiting browser permission prompt
 *  - `granted`      — stream is live and persisted in sessionStorage
 *  - `denied`       — user blocked the camera (NotAllowedError / SecurityError)
 *  - `unavailable`  — no device present (NotFoundError / OverconstrainedError)
 *
 * The session-storage record lets the parent skip re-prompting on a
 * remount within the same browser tab; it never bypasses the actual OS
 * permission gate.
 */

import { useCallback, useEffect, useRef, useState } from 'react';

export type WebcamPreflightStatus =
  | 'idle'
  | 'requesting'
  | 'granted'
  | 'denied'
  | 'unavailable';

interface UseWebcamPreflightOptions {
  /** sessionStorage key used to remember a previous pass within the tab. */
  storageKey: string;
}

export interface UseWebcamPreflightApi {
  status: WebcamPreflightStatus;
  /** Last error message, if any, from the most recent attempt. */
  errorMessage: string | null;
  /** Active MediaStream while status === 'granted'. */
  stream: MediaStream | null;
  /** Trigger the browser permission prompt. Safe to call repeatedly. */
  requestPermission: () => Promise<void>;
  /** Tear down the stream and reset to `idle`. */
  reset: () => void;
}

function stopStream(stream: MediaStream | null) {
  stream?.getTracks().forEach((track) => {
    try {
      track.stop();
    } catch {
      // ignored — track may already be stopped
    }
  });
}

export function useWebcamPreflight(options: UseWebcamPreflightOptions): UseWebcamPreflightApi {
  const { storageKey } = options;

  const [status, setStatus] = useState<WebcamPreflightStatus>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [stream, setStream] = useState<MediaStream | null>(null);
  const streamRef = useRef<MediaStream | null>(null);

  // Hydrate from sessionStorage on mount. We intentionally do NOT auto-grab a
  // stream — getUserMedia requires a user gesture and would prompt unexpectedly.
  // The stored '1' simply tells the parent the user already passed once.
  useEffect(() => {
    if (typeof window === 'undefined') return;
    try {
      if (window.sessionStorage.getItem(storageKey) === '1') {
        setStatus('granted');
      }
    } catch {
      // sessionStorage unavailable (private mode) — fall through with idle.
    }
  }, [storageKey]);

  // Always release the stream on unmount.
  useEffect(() => {
    return () => {
      stopStream(streamRef.current);
      streamRef.current = null;
    };
  }, []);

  const persistGranted = useCallback(() => {
    if (typeof window === 'undefined') return;
    try {
      window.sessionStorage.setItem(storageKey, '1');
    } catch {
      // ignored — best effort
    }
  }, [storageKey]);

  const requestPermission = useCallback(async () => {
    if (typeof navigator === 'undefined' || !navigator.mediaDevices?.getUserMedia) {
      setStatus('unavailable');
      setErrorMessage('Your browser does not support camera access. Please use a recent Chrome, Edge, or Safari.');
      return;
    }

    setErrorMessage(null);
    setStatus('requesting');

    try {
      const next = await navigator.mediaDevices.getUserMedia({ video: true });
      // Replace any pre-existing stream.
      stopStream(streamRef.current);
      streamRef.current = next;
      setStream(next);
      setStatus('granted');
      persistGranted();
    } catch (err) {
      const name = (err as DOMException | undefined)?.name ?? '';
      const message = err instanceof Error ? err.message : 'Camera access failed.';

      if (name === 'NotFoundError' || name === 'OverconstrainedError' || name === 'DevicesNotFoundError') {
        setStatus('unavailable');
        setErrorMessage('No camera was found on this device. Connect a webcam and try again.');
        return;
      }

      if (
        name === 'NotAllowedError' ||
        name === 'PermissionDeniedError' ||
        name === 'SecurityError'
      ) {
        setStatus('denied');
        setErrorMessage('Camera access was blocked. Please enable the camera permission and try again.');
        return;
      }

      // Other errors (NotReadableError, AbortError, etc) — treat as denied so
      // the parent surfaces a retry CTA instead of a dead-end.
      setStatus('denied');
      setErrorMessage(message);
    }
  }, [persistGranted]);

  const reset = useCallback(() => {
    stopStream(streamRef.current);
    streamRef.current = null;
    setStream(null);
    setStatus('idle');
    setErrorMessage(null);
    if (typeof window !== 'undefined') {
      try {
        window.sessionStorage.removeItem(storageKey);
      } catch {
        // ignored
      }
    }
  }, [storageKey]);

  return { status, errorMessage, stream, requestPermission, reset };
}
