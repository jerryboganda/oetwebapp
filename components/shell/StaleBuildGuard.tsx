'use client';

import { useEffect, useRef, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { hardReload } from '@/lib/shell/hard-reload';

/** Reads the build id Next.js already stamps onto every page (window.__NEXT_DATA__.buildId)
 * without re-declaring the global — Next's own types already own that
 * property with a stricter shape, and redeclaring it here conflicts. */
function readCurrentBuildId(): string | undefined {
  const nextData = (window as unknown as { __NEXT_DATA__?: { buildId?: string } }).__NEXT_DATA__;
  return nextData?.buildId;
}

/**
 * The Capacitor mobile shell loads this app live from the server (see
 * capacitor.config.ts server.url) rather than bundling it — deploying a fix
 * updates the site immediately. But resuming the app from the background
 * (App.addListener('appStateChange') in lib/mobile/runtime.ts) never
 * re-navigates the WebView; it only refreshes the session token in the
 * background. A learner who had the app open across a deploy keeps running
 * the JavaScript that was loaded at the last real cold start, indefinitely,
 * no matter how many times they background/foreground it. This produced two
 * rounds of "the fix still isn't working" reports for the device-verification
 * OTP bug that were actually just this — the fix was live, their app process
 * was not.
 *
 * Scoped deliberately narrow: only the (auth) route group, only on the
 * Capacitor native shell, and only a manual "Refresh" action rather than an
 * automatic reload — these screens hold nothing worth losing (an in-progress
 * OTP entry is trivially redone), so this is safe where an exam/mock
 * attempt would not be.
 */
export function StaleBuildGuard() {
  const [isStale, setIsStale] = useState(false);
  const checkedBuildId = useRef<string | null>(null);

  useEffect(() => {
    if (getAppRuntimeKind() !== 'capacitor-native') {
      return;
    }

    const currentBuildId = readCurrentBuildId();
    if (!currentBuildId) {
      // No build id to compare against (e.g. a dev server without a real
      // build). Nothing meaningful to detect.
      return;
    }
    checkedBuildId.current = currentBuildId;

    const checkForNewBuild = async () => {
      try {
        const response = await fetch(window.location.href, {
          cache: 'no-store',
          headers: { Accept: 'text/html' },
        });
        if (!response.ok) return;
        const html = await response.text();
        const match = html.match(/"buildId":"([^"]+)"/);
        const liveBuildId = match?.[1];
        if (liveBuildId && checkedBuildId.current && liveBuildId !== checkedBuildId.current) {
          setIsStale(true);
        }
      } catch {
        // Offline or a transient network blip — not evidence of staleness,
        // and the app is otherwise unusable regardless, so stay silent.
      }
    };

    void checkForNewBuild();

    const onVisible = () => {
      if (document.visibilityState === 'visible') {
        void checkForNewBuild();
      }
    };
    document.addEventListener('visibilitychange', onVisible);
    window.addEventListener('focus', onVisible);

    return () => {
      document.removeEventListener('visibilitychange', onVisible);
      window.removeEventListener('focus', onVisible);
    };
  }, []);

  if (!isStale) {
    return null;
  }

  return (
    <div
      role="status"
      aria-live="polite"
      style={{
        position: 'fixed',
        top: 'calc(env(safe-area-inset-top, 0px) + 0.5rem)',
        left: '0.75rem',
        right: '0.75rem',
        zIndex: 2147483000,
        display: 'flex',
        alignItems: 'center',
        justifyContent: 'space-between',
        gap: '0.75rem',
        borderRadius: '0.75rem',
        border: '1px solid rgba(16, 35, 63, 0.12)',
        background: '#10233f',
        color: '#f7f5ef',
        padding: '0.65rem 0.85rem',
        fontSize: '0.85rem',
        boxShadow: '0 8px 24px rgba(0, 0, 0, 0.25)',
      }}
    >
      <span>A newer version of this page is available.</span>
      <button
        type="button"
        onClick={() => void hardReload()}
        style={{
          display: 'inline-flex',
          alignItems: 'center',
          gap: '0.35rem',
          flexShrink: 0,
          borderRadius: '9999px',
          background: '#f7f5ef',
          color: '#10233f',
          border: 'none',
          padding: '0.4rem 0.75rem',
          fontWeight: 600,
          fontSize: '0.8rem',
        }}
      >
        <RefreshCw size={14} />
        Refresh
      </button>
    </div>
  );
}
