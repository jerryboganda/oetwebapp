'use client';

import { useEffect, useState, type ReactNode } from 'react';
import { ArrowDownToLine, RefreshCw } from 'lucide-react';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { hardReload } from '@/lib/shell/hard-reload';
import { useAppVersionGate } from '@/app/providers/AppVersionGateProvider';
import { UpdateDialog } from './UpdateDialog';

/**
 * Compact control cluster pinned to the TOP-CENTER, shown only inside the
 * desktop/mobile shells (never on the website). Styled in the platform's
 * lavender/violet brand theme. "Reload" hard-reloads (Ctrl+F5: drop caches,
 * re-fetch fresh settings from the server); "Check for updates" opens the
 * animated UpdateDialog. Hidden while the forced-update overlay is up.
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
        className="fixed left-1/2 z-[60] flex -translate-x-1/2 items-center gap-0.5 rounded-full border border-primary/20 bg-lavender/70 px-1 py-0.5 shadow-md shadow-primary/10 backdrop-blur dark:border-white/15 dark:bg-white/10"
        style={{ top: 'max(0.375rem, env(safe-area-inset-top))' }}
      >
        <IconButton label="Reload (fetch latest from server)" onClick={() => void hardReload()}>
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
        </IconButton>
        <IconButton label="Check for updates" onClick={() => setUpdateOpen(true)}>
          <ArrowDownToLine className="h-3.5 w-3.5" aria-hidden="true" />
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
      className="flex h-7 w-7 items-center justify-center rounded-full text-primary transition-colors hover:bg-primary/15 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary active:scale-95 motion-reduce:active:scale-100 dark:text-violet-200 dark:hover:bg-white/15"
    >
      {children}
    </button>
  );
}
