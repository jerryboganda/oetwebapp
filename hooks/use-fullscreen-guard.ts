'use client';

/**
 * Mocks V2 Phase 2 — fullscreen integrity guard.
 *
 * Wraps the browser Fullscreen API for `exam` / `final_readiness` strictness
 * mocks. Tracks exit/re-enter events so the player can surface a re-entry CTA
 * and forward each exit into the proctoring telemetry pipe.
 *
 * Browser quirks handled:
 *  - Safari only exposes `webkitRequestFullscreen` / `webkitFullscreenElement`.
 *  - `requestFullscreen()` MUST be called from inside a real user gesture —
 *    the hook only exposes the requestFn; it never calls it from a useEffect.
 *  - `fullscreenchange` fires after both enter and exit; we infer direction
 *    from the current `fullscreenElement` value.
 */

import { useCallback, useEffect, useRef, useState } from 'react';

interface UseFullscreenGuardOptions {
  /** Master switch — when false the hook installs no listeners. */
  enabled: boolean;
  /** Invoked on each detected exit. Receives the cumulative exit count. */
  onExit?: (count: number) => void;
  /** Invoked whenever the document re-enters fullscreen after an exit. */
  onReenter?: () => void;
}

interface DocumentWithVendorFullscreen extends Document {
  webkitFullscreenElement?: Element | null;
  msFullscreenElement?: Element | null;
  mozFullScreenElement?: Element | null;
}

interface ElementWithVendorFullscreen extends Element {
  webkitRequestFullscreen?: () => Promise<void> | void;
  msRequestFullscreen?: () => Promise<void> | void;
  mozRequestFullScreen?: () => Promise<void> | void;
}

export interface UseFullscreenGuardApi {
  /** True while the document is currently in fullscreen. */
  isFullscreen: boolean;
  /** Number of exits observed since the guard was enabled. */
  exitCount: number;
  /** Request fullscreen on the given element (defaults to documentElement). */
  requestFullscreen: (target?: HTMLElement | null) => Promise<void>;
  /** Best-effort exit; safe to call when not in fullscreen. */
  exitFullscreen: () => Promise<void>;
}

function getCurrentFullscreenElement(): Element | null {
  if (typeof document === 'undefined') return null;
  const doc = document as DocumentWithVendorFullscreen;
  return (
    doc.fullscreenElement ??
    doc.webkitFullscreenElement ??
    doc.msFullscreenElement ??
    doc.mozFullScreenElement ??
    null
  );
}

export function useFullscreenGuard(options: UseFullscreenGuardOptions): UseFullscreenGuardApi {
  const { enabled, onExit, onReenter } = options;

  const [isFullscreen, setIsFullscreen] = useState<boolean>(false);
  const [exitCount, setExitCount] = useState<number>(0);

  // Refs let the listener read current state without re-binding on every change.
  const wasFullscreenRef = useRef<boolean>(false);
  const exitCountRef = useRef<number>(0);
  const onExitRef = useRef(onExit);
  const onReenterRef = useRef(onReenter);

  onExitRef.current = onExit;
  onReenterRef.current = onReenter;

  const requestFullscreen = useCallback(async (target?: HTMLElement | null) => {
    if (typeof document === 'undefined') return;
    const el = (target ?? document.documentElement) as ElementWithVendorFullscreen;
    if (!el) return;
    try {
      if (typeof el.requestFullscreen === 'function') {
        await el.requestFullscreen();
        return;
      }
      if (typeof el.webkitRequestFullscreen === 'function') {
        await Promise.resolve(el.webkitRequestFullscreen());
        return;
      }
      if (typeof el.msRequestFullscreen === 'function') {
        await Promise.resolve(el.msRequestFullscreen());
        return;
      }
      if (typeof el.mozRequestFullScreen === 'function') {
        await Promise.resolve(el.mozRequestFullScreen());
        return;
      }
    } catch {
      // Browser may reject if the call escapes its user-gesture window. We
      // swallow because the guard's caller will see `isFullscreen` stay false
      // and can re-prompt; we never want to break the player flow.
    }
  }, []);

  const exitFullscreen = useCallback(async () => {
    if (typeof document === 'undefined') return;
    if (!getCurrentFullscreenElement()) return;
    try {
      const doc = document as Document & {
        webkitExitFullscreen?: () => Promise<void> | void;
        msExitFullscreen?: () => Promise<void> | void;
        mozCancelFullScreen?: () => Promise<void> | void;
      };
      if (typeof doc.exitFullscreen === 'function') {
        await doc.exitFullscreen();
        return;
      }
      if (typeof doc.webkitExitFullscreen === 'function') {
        await Promise.resolve(doc.webkitExitFullscreen());
        return;
      }
      if (typeof doc.msExitFullscreen === 'function') {
        await Promise.resolve(doc.msExitFullscreen());
        return;
      }
      if (typeof doc.mozCancelFullScreen === 'function') {
        await Promise.resolve(doc.mozCancelFullScreen());
        return;
      }
    } catch {
      // ignored — best-effort
    }
  }, []);

  useEffect(() => {
    if (!enabled || typeof document === 'undefined') return;

    const handleChange = () => {
      const nowFullscreen = !!getCurrentFullscreenElement();
      const wasFullscreen = wasFullscreenRef.current;
      wasFullscreenRef.current = nowFullscreen;
      setIsFullscreen(nowFullscreen);

      if (wasFullscreen && !nowFullscreen) {
        const next = exitCountRef.current + 1;
        exitCountRef.current = next;
        setExitCount(next);
        try {
          onExitRef.current?.(next);
        } catch {
          // never let consumer errors break the guard
        }
        return;
      }

      if (!wasFullscreen && nowFullscreen) {
        try {
          onReenterRef.current?.();
        } catch {
          // never let consumer errors break the guard
        }
      }
    };

    // Seed in case the document is already in fullscreen at mount.
    const initial = !!getCurrentFullscreenElement();
    wasFullscreenRef.current = initial;
    setIsFullscreen(initial);

    document.addEventListener('fullscreenchange', handleChange);
    document.addEventListener('webkitfullscreenchange', handleChange as EventListener);
    document.addEventListener('mozfullscreenchange', handleChange as EventListener);
    document.addEventListener('MSFullscreenChange', handleChange as EventListener);

    return () => {
      document.removeEventListener('fullscreenchange', handleChange);
      document.removeEventListener('webkitfullscreenchange', handleChange as EventListener);
      document.removeEventListener('mozfullscreenchange', handleChange as EventListener);
      document.removeEventListener('MSFullscreenChange', handleChange as EventListener);
    };
  }, [enabled]);

  return { isFullscreen, exitCount, requestFullscreen, exitFullscreen };
}
