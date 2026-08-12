'use client';

import { useEffect, useRef, type ReactNode } from 'react';
import { ShieldAlert } from 'lucide-react';
import type { ListeningPresentationMode } from '@/lib/listening/modes';

export interface ListeningPlayerSkinShellProps {
  mode: ListeningPresentationMode;
  /**
   * When true (default), the Home skin installs non-blocking guidance
   * listeners. Tests pass `enableSideEffects={false}` to avoid touching
   * browser globals.
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
 *   - `home`     → kiosk visuals + paste/context-menu block; fullscreen is
 *                  advisory and never requested by this skin.
 *   - computer-based delivery has no printable-booklet skin.
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

  // Home (OET@Home) — non-blocking kiosk guidance side effects. Wrapped in a
  // single effect so unmount cleanup symmetrically reverses every listener.
  useEffect(() => {
    if (!enableSideEffects || mode !== 'home') return;
    if (typeof document === 'undefined') return;

    // Block paste and right-click to mirror the supervised OET@Home runtime.
    const blockEvent = (event: Event) => {
      event.preventDefault();
      event.stopPropagation();
    };
    document.addEventListener('paste', blockEvent);
    document.addEventListener('contextmenu', blockEvent);

    return () => {
      document.removeEventListener('paste', blockEvent);
      document.removeEventListener('contextmenu', blockEvent);
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
        <div className="flex items-center gap-3 border-b border-white/10 bg-navy px-4 py-2 text-sm font-semibold">
          <ShieldAlert className="h-4 w-4 text-warning" aria-hidden="true" />
          <span>OET@Home guidance mode. Keep this test window visible when possible.</span>
        </div>
        <div className="listening-home-surface">
          {children}
        </div>
      </div>
    );
  }

  // Unknown/legacy values fail closed to the computer-based skin.
  return (
    <div ref={rootRef} data-listening-skin="computer">{children}</div>
  );
}
