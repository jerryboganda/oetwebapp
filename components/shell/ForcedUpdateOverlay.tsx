'use client';

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { AlertTriangle, CheckCircle2, Download, Loader2, RefreshCw } from 'lucide-react';
import { motion } from 'motion/react';
import { Button } from '@/components/ui/button';
import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { hardReload } from '@/lib/shell/hard-reload';
import {
  installDesktopUpdate,
  isDesktopUpdaterAvailable,
  performMobileUpdate,
  relaunchDesktop,
  subscribeDesktopUpdateEvents,
  type UpdateState,
} from '@/lib/shell/update-controller';
import { useAppVersionGate } from '@/app/providers/AppVersionGateProvider';

/**
 * Full-screen, non-dismissible gate shown when the shell is out of date. Desktop
 * auto-downloads + installs the required update (then offers Restart); mobile
 * sends the user to the store / Android in-app update; the web fallback (which
 * the backend prevents from ever triggering) offers a hard reload.
 */
export function ForcedUpdateOverlay() {
  const { blocked } = useAppVersionGate();
  const [runtime] = useState(() => (typeof window === 'undefined' ? 'web' : getAppRuntimeKind()));
  const [state, setState] = useState<UpdateState>({ phase: 'idle' });
  const startedRef = useRef(false);
  const unsubRef = useRef<(() => void) | undefined>(undefined);

  const startDesktopInstall = useCallback(async () => {
    if (startedRef.current) return;
    startedRef.current = true;
    unsubRef.current = subscribeDesktopUpdateEvents((event) => setState((prev) => ({ ...prev, ...event })));
    setState({ phase: 'downloading', progress: 0 });
    const result = await installDesktopUpdate();
    setState((prev) => ({ ...prev, ...result }));
  }, []);

  useEffect(() => {
    if (!blocked) return;
    if (runtime === 'desktop' && isDesktopUpdaterAvailable()) {
      void startDesktopInstall();
    }
    return () => {
      unsubRef.current?.();
      unsubRef.current = undefined;
    };
  }, [blocked, runtime, startDesktopInstall]);

  if (!blocked) return null;

  return (
    <div
      className="fixed inset-0 z-[100] flex items-center justify-center bg-navy/80 px-4 backdrop-blur-sm"
      role="alertdialog"
      aria-modal="true"
      aria-label="Update required"
      style={{ paddingTop: 'env(safe-area-inset-top)', paddingBottom: 'env(safe-area-inset-bottom)' }}
    >
      <motion.div
        initial={{ opacity: 0, y: 14, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.2 }}
        className="w-full max-w-md rounded-3xl border border-border bg-surface p-7 text-center shadow-2xl"
      >
        {renderContent()}
      </motion.div>
    </div>
  );

  function renderContent() {
    if (state.phase === 'downloading') {
      return (
        <Panel icon={<Download className="h-9 w-9 text-primary" />} title="Downloading required update…">
          <ProgressBar value={state.progress ?? 0} />
        </Panel>
      );
    }

    if (state.phase === 'installing') {
      return <Panel icon={<Loader2 className="h-9 w-9 animate-spin text-primary" />} title="Installing update…" />;
    }

    if (state.phase === 'ready') {
      return (
        <Panel icon={<CheckCircle2 className="h-9 w-9 text-emerald-500" />} title="Update installed" subtitle="Restart to continue.">
          <Button variant="primary" fullWidth onClick={() => void relaunchDesktop()}>Restart now</Button>
        </Panel>
      );
    }

    if (state.phase === 'error') {
      return (
        <Panel icon={<AlertTriangle className="h-9 w-9 text-danger" />} title="Update failed" subtitle={state.error}>
          <Button
            variant="primary"
            fullWidth
            onClick={() => {
              startedRef.current = false;
              void startDesktopInstall();
            }}
          >
            Try again
          </Button>
        </Panel>
      );
    }

    // Initial forced prompt (idle) — action depends on the runtime.
    if (runtime === 'desktop' && isDesktopUpdaterAvailable()) {
      return <Panel icon={<Loader2 className="h-9 w-9 animate-spin text-primary" />} title="Preparing update…" />;
    }

    if (runtime === 'capacitor-native') {
      return (
        <Panel
          icon={<AlertTriangle className="h-9 w-9 text-primary" />}
          title="Update required"
          subtitle="A newer version of the app is required to continue. Update now to keep going."
        >
          <Button variant="primary" fullWidth onClick={() => void performMobileUpdate()}>Update now</Button>
        </Panel>
      );
    }

    // Desktop without the manual updater bridge, or web fallback.
    return (
      <Panel
        icon={<RefreshCw className="h-9 w-9 text-primary" />}
        title="Update required"
        subtitle={
          runtime === 'desktop'
            ? 'A newer version is required. Fully close and reopen the app to install the latest update, then reload.'
            : 'A newer version is required to continue. Reload to get the latest version.'
        }
      >
        <Button variant="primary" fullWidth onClick={() => void hardReload()}>Reload</Button>
      </Panel>
    );
  }
}

function Panel({ icon, title, subtitle, children }: { icon: ReactNode; title: string; subtitle?: string; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-4">
      <span className="flex h-16 w-16 items-center justify-center rounded-full bg-lavender/40 dark:bg-white/5">{icon}</span>
      <div>
        <h2 className="text-lg font-bold text-navy">{title}</h2>
        {subtitle ? <p className="mx-auto mt-2 max-w-sm text-sm leading-6 text-muted">{subtitle}</p> : null}
      </div>
      {children ? <div className="w-full">{children}</div> : null}
    </div>
  );
}

function ProgressBar({ value }: { value: number }) {
  const clamped = Math.max(0, Math.min(100, Math.round(value)));
  return (
    <div className="w-full">
      <div className="h-2.5 w-full overflow-hidden rounded-full bg-lavender/50 dark:bg-white/10">
        <div
          className="h-full rounded-full bg-gradient-to-r from-primary to-violet-400 transition-[width] duration-200 ease-out"
          style={{ width: `${clamped}%` }}
        />
      </div>
      <p className="mt-1.5 text-xs font-medium text-muted">{clamped}%</p>
    </div>
  );
}
