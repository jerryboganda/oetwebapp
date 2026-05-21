'use client';

import { useEffect, useRef, type ReactNode } from 'react';
import { Maximize2, Printer, ShieldAlert } from 'lucide-react';
import type { ListeningPresentationMode } from '@/lib/listening/modes';

export interface ListeningPlayerSkinShellProps {
  mode: ListeningPresentationMode;
  /**
   * When true (default), the Home skin requests fullscreen on first user
   * interaction and the Paper skin emits print-friendly CSS. Tests pass
   * `enableSideEffects={false}` to avoid touching browser globals.
   */
  enableSideEffects?: boolean;
  children: ReactNode;
}

/**
 * Single-component skin wrapper for the Listening player. Wraps the existing
 * `<AppShell>` subtree without duplicating the player; visual + behavioural
 * differences flow from the `mode` prop:
 *
 *   - `computer` → pass-through (no chrome change).
 *   - `home`     → kiosk visuals + fullscreen + paste/context-menu block.
 *   - `paper`    → printable booklet styles + "Print" affordance + bubble-sheet
 *                  CSS hooks (the page reuses the standard renderers; print
 *                  CSS reflows them into the booklet layout).
 *
 * Per Wave 3 of the OET Listening gap-fill plan we deliberately *wrap* rather
 * than fork the 1400+ line player file. The player stays in one place; the
 * skin owns chrome and side effects only.
 */
export function ListeningPlayerSkinShell({
  mode,
  enableSideEffects = true,
  children,
}: ListeningPlayerSkinShellProps) {
  const rootRef = useRef<HTMLDivElement>(null);

  // Home (OET@Home) — kiosk side effects. Wrapped in a single effect so the
  // unmount cleanup symmetrically reverses every listener.
  useEffect(() => {
    if (!enableSideEffects || mode !== 'home') return;
    if (typeof document === 'undefined') return;

    const root = rootRef.current ?? document.documentElement;

    // Block paste and right-click to mirror the supervised OET@Home runtime.
    const blockEvent = (event: Event) => {
      event.preventDefault();
      event.stopPropagation();
    };
    document.addEventListener('paste', blockEvent);
    document.addEventListener('contextmenu', blockEvent);

    // Request fullscreen on first user interaction; browsers reject
    // unprompted fullscreen calls outside a user gesture so we listen for
    // the next click/keydown rather than calling on mount.
    let triggered = false;
    const enterFullscreen = () => {
      if (triggered) return;
      triggered = true;
      try {
        if (root.requestFullscreen && !document.fullscreenElement) {
          void root.requestFullscreen().catch(() => {/* user dismissed */});
        }
      } catch { /* unsupported */ }
    };
    document.addEventListener('click', enterFullscreen, { once: true });
    document.addEventListener('keydown', enterFullscreen, { once: true });

    return () => {
      document.removeEventListener('paste', blockEvent);
      document.removeEventListener('contextmenu', blockEvent);
      document.removeEventListener('click', enterFullscreen);
      document.removeEventListener('keydown', enterFullscreen);
    };
  }, [enableSideEffects, mode]);

  if (mode === 'computer') {
    return <div ref={rootRef} data-listening-skin="computer">{children}</div>;
  }

  if (mode === 'home') {
    return (
      <div
        ref={rootRef}
        data-listening-skin="home"
        className="min-h-screen bg-navy text-white"
      >
        <div className="sticky top-0 z-50 flex items-center gap-3 border-b border-white/10 bg-black/60 px-4 py-2 text-sm font-semibold backdrop-blur">
          <ShieldAlert className="h-4 w-4 text-warning" aria-hidden="true" />
          <span>OET@Home — kiosk mode. Do not switch tabs or windows.</span>
          <span className="ml-auto flex items-center gap-1 text-xs uppercase tracking-widest text-white/70">
            <Maximize2 className="h-3 w-3" aria-hidden="true" />
            Fullscreen on first interaction
          </span>
        </div>
        <div className="listening-home-surface">
          {children}
        </div>
      </div>
    );
  }

  // mode === 'paper'
  return (
    <div ref={rootRef} data-listening-skin="paper" className="listening-paper-skin">
      <div className="sticky top-0 z-50 flex items-center gap-3 border-b border-border bg-warning/10 px-4 py-2 text-sm font-semibold print:hidden">
        <Printer className="h-4 w-4 text-warning" aria-hidden="true" />
        <span>OET on Paper simulation — printable booklet styles active.</span>
        <button
          type="button"
          onClick={() => { if (typeof window !== 'undefined') window.print(); }}
          className="ml-auto inline-flex items-center gap-1 rounded-lg border border-warning/40 px-3 py-1 text-warning hover:bg-warning/20"
        >
          <Printer className="h-3 w-3" aria-hidden="true" />
          Print booklet
        </button>
      </div>
      {children}
    </div>
  );
}
