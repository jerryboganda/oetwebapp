'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { ArrowDownToLine, RefreshCw } from 'lucide-react';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { hardReload } from '@/lib/shell/hard-reload';
import { useAppVersionGate } from '@/app/providers/AppVersionGateProvider';
import { UpdateDialog } from './UpdateDialog';

/**
 * Top-right control cluster shown only inside the desktop/mobile shells (never
 * on the website). "Reload" hard-reloads (Ctrl+F5: drop caches, re-fetch fresh
 * settings from the server); "Check for updates" opens the animated UpdateDialog.
 * Hidden while the forced-update overlay is up (it covers the screen).
 */
export function ShellControls() {
  // Detect the runtime only after mount: the server always renders as the
  // website (it can't know the client is a shell), so computing this during
  // hydration would mismatch. Deferring keeps the first client render === SSR.
  const [mounted, setMounted] = useState(false);
  useEffect(() => setMounted(true), []);
  const { blocked } = useAppVersionGate();
  const [updateOpen, setUpdateOpen] = useState(false);

  if (!mounted || getAppRuntimeKind() === 'web' || blocked) return null;

  return (
    <>
      <div
        className="fixed z-[60] flex items-center gap-0.5 rounded-full border border-border bg-surface/90 p-1 shadow-lg backdrop-blur"
        style={{
          top: 'max(0.5rem, env(safe-area-inset-top))',
          right: 'max(0.5rem, env(safe-area-inset-right))',
        }}
      >
        <IconButton label="Reload (fetch latest from server)" onClick={() => void hardReload()}>
          <RefreshCw className="h-4 w-4" aria-hidden="true" />
        </IconButton>
        <IconButton label="Check for updates" onClick={() => setUpdateOpen(true)}>
          <ArrowDownToLine className="h-4 w-4" aria-hidden="true" />
        </IconButton>
      </div>
      <UpdateDialog open={updateOpen} onClose={() => setUpdateOpen(false)} />
    </>
  );
}

function IconButton({ label, onClick, children }: { label: string; onClick: () => void; children: ReactNode }) {
  return (
    <button
      type="button"
      onClick={onClick}
      aria-label={label}
      title={label}
      className="flex h-9 w-9 items-center justify-center rounded-full text-muted transition-colors hover:bg-lavender/40 hover:text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary active:scale-95 motion-reduce:active:scale-100 dark:hover:bg-white/10"
    >
      {children}
    </button>
  );
}
