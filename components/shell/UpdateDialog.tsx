'use client';

import { useCallback, useEffect, useRef, useState, type ReactNode } from 'react';
import { AlertTriangle, CheckCircle2, Download, Loader2 } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import {
  checkForUpdates,
  installDesktopUpdate,
  isMobileShell,
  performMobileUpdate,
  relaunchDesktop,
  subscribeDesktopUpdateEvents,
  type UpdateState,
} from '@/lib/shell/update-controller';

/**
 * Animated "Check for updates" popup for the desktop/mobile shells. Dismissible
 * — used from the top-right ShellControls. Desktop drives the full download →
 * install → restart flow with a live progress bar; mobile hands off to the
 * store / Android in-app update.
 */
export function UpdateDialog({ open, onClose }: { open: boolean; onClose: () => void }) {
  const [state, setState] = useState<UpdateState>({ phase: 'idle' });
  const unsubRef = useRef<(() => void) | undefined>(undefined);

  const runCheck = useCallback(async () => {
    setState({ phase: 'checking' });
    setState(await checkForUpdates());
  }, []);

  useEffect(() => {
    if (!open) return;
    void runCheck();
    return () => {
      unsubRef.current?.();
      unsubRef.current = undefined;
    };
  }, [open, runCheck]);

  const startDesktopInstall = useCallback(async () => {
    unsubRef.current = subscribeDesktopUpdateEvents((event) => {
      setState((prev) => ({ ...prev, ...event }));
    });
    setState((prev) => ({ ...prev, phase: 'downloading', progress: 0 }));
    const result = await installDesktopUpdate();
    setState((prev) => ({ ...prev, ...result }));
  }, []);

  return (
    <Modal open={open} onClose={onClose} title="Check for updates" size="sm">
      <div className="flex flex-col items-center gap-5 py-2 text-center">{renderBody()}</div>
    </Modal>
  );

  function renderBody() {
    switch (state.phase) {
      case 'idle':
      case 'checking':
        return <Status icon={<Loader2 className="h-9 w-9 animate-spin text-primary" />} title="Checking for updates…" />;

      case 'uptodate':
        return (
          <>
            <Status
              icon={<CheckCircle2 className="h-9 w-9 text-emerald-500" />}
              title="You're on the latest version"
              subtitle={state.currentVersion ? `Version ${state.currentVersion}` : undefined}
            />
            <Button variant="secondary" onClick={onClose}>Close</Button>
          </>
        );

      case 'available':
        return (
          <>
            <Status
              icon={<Download className="h-9 w-9 text-primary" />}
              title={state.version ? `Update available — v${state.version}` : 'Update available'}
              subtitle={state.notes || undefined}
            />
            <div className="flex flex-col items-center gap-2">
              {isMobileShell() ? (
                <Button
                  variant="primary"
                  onClick={() => {
                    void performMobileUpdate(state.storeUrl).then((handled) => {
                      if (!handled) setState({ phase: 'error', error: 'No update download is configured for this device.' });
                    });
                  }}
                >
                  Update now
                </Button>
              ) : (
                <Button variant="primary" onClick={() => void startDesktopInstall()}>Update now</Button>
              )}
              <Button variant="ghost" size="sm" onClick={onClose}>Later</Button>
            </div>
          </>
        );

      case 'downloading':
        return (
          <>
            <Status icon={<Download className="h-9 w-9 text-primary" />} title="Downloading update…" />
            <ProgressBar value={state.progress ?? 0} />
          </>
        );

      case 'installing':
        return <Status icon={<Loader2 className="h-9 w-9 animate-spin text-primary" />} title="Installing update…" />;

      case 'ready':
        return (
          <>
            <Status
              icon={<CheckCircle2 className="h-9 w-9 text-emerald-500" />}
              title="Update ready"
              subtitle="Restart to finish installing."
            />
            <Button variant="primary" onClick={() => void relaunchDesktop()}>Restart now</Button>
          </>
        );

      case 'error':
        return (
          <>
            <Status
              icon={<AlertTriangle className="h-9 w-9 text-danger" />}
              title="Update failed"
              subtitle={state.error}
            />
            <Button variant="secondary" onClick={() => void runCheck()}>Try again</Button>
          </>
        );

      default:
        return null;
    }
  }
}

function Status({ icon, title, subtitle }: { icon: ReactNode; title: string; subtitle?: string }) {
  return (
    <div className="flex flex-col items-center gap-2">
      <span className="flex h-16 w-16 items-center justify-center rounded-full bg-lavender/40 dark:bg-white/5">
        {icon}
      </span>
      <h3 className="text-base font-bold text-navy">{title}</h3>
      {subtitle ? <p className="max-w-xs text-sm leading-6 text-muted">{subtitle}</p> : null}
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
