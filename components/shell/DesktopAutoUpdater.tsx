'use client';

import { useEffect, useRef, useState } from 'react';
import { CheckCircle2, Download, Loader2 } from 'lucide-react';
import { motion } from 'motion/react';
import {
  checkForUpdates,
  installDesktopUpdate,
  isDesktopUpdaterAvailable,
  relaunchDesktop,
  subscribeDesktopUpdateEvents,
  type UpdateState,
} from '@/lib/shell/update-controller';
import { useAppVersionGate } from '@/app/providers/AppVersionGateProvider';

/**
 * Fully automatic desktop updater: on launch it checks once, and if a newer
 * signed build exists it downloads, installs, and relaunches — no button, no
 * admin config. Shows a small progress card so the restart isn't a surprise.
 * If anything fails or there's no update, it silently disappears (never traps).
 * No-op off the desktop shell.
 */
export function DesktopAutoUpdater() {
  const { blocked } = useAppVersionGate();
  const [state, setState] = useState<UpdateState | null>(null);
  const ranRef = useRef(false);
  const unsubRef = useRef<(() => void) | undefined>(undefined);

  useEffect(() => {
    // When the gate is actively blocking, the ForcedUpdateOverlay owns the
    // install flow — stand down to avoid a double install.
    if (ranRef.current || blocked) return;
    if (!isDesktopUpdaterAvailable()) {
      ranRef.current = true;
      return;
    }
    ranRef.current = true;

    void (async () => {
      const check = await checkForUpdates();
      if (check.phase !== 'available') return; // already latest → stay hidden

      unsubRef.current = subscribeDesktopUpdateEvents((event) => setState((prev) => ({ ...(prev ?? { phase: 'downloading' }), ...event })));
      setState({ phase: 'downloading', progress: 0, version: check.version });

      const result = await installDesktopUpdate();
      unsubRef.current?.();
      unsubRef.current = undefined;

      if (result.phase === 'ready') {
        setState({ phase: 'ready', version: check.version });
        await relaunchDesktop();
      } else {
        // Install failed — don't block the user; just disappear.
        setState(null);
      }
    })();

    return () => {
      unsubRef.current?.();
      unsubRef.current = undefined;
    };
  }, [blocked]);

  if (!state) return null;

  return (
    <div className="fixed inset-0 z-[95] flex items-center justify-center bg-navy/70 px-4 backdrop-blur-sm">
      <motion.div
        initial={{ opacity: 0, y: 10, scale: 0.98 }}
        animate={{ opacity: 1, y: 0, scale: 1 }}
        transition={{ duration: 0.18 }}
        className="w-full max-w-sm rounded-3xl border border-border bg-surface p-6 text-center shadow-2xl"
      >
        <span className="mx-auto mb-3 flex h-14 w-14 items-center justify-center rounded-full bg-lavender/40 dark:bg-white/5">
          {state.phase === 'ready'
            ? <CheckCircle2 className="h-8 w-8 text-emerald-500" />
            : <Download className="h-8 w-8 text-primary" />}
        </span>
        <h2 className="text-base font-bold text-navy">
          {state.phase === 'ready'
            ? 'Update ready — restarting…'
            : `Updating${state.version ? ` to v${state.version}` : ''}…`}
        </h2>
        {state.phase === 'ready' ? (
          <Loader2 className="mx-auto mt-3 h-5 w-5 animate-spin text-primary" />
        ) : (
          <div className="mt-4">
            <div className="h-2.5 w-full overflow-hidden rounded-full bg-lavender/50 dark:bg-white/10">
              <div
                className="h-full rounded-full bg-gradient-to-r from-primary to-violet-400 transition-[width] duration-200 ease-out"
                style={{ width: `${Math.max(0, Math.min(100, Math.round(state.progress ?? 0)))}%` }}
              />
            </div>
            <p className="mt-1.5 text-xs font-medium text-muted">{Math.max(0, Math.min(100, Math.round(state.progress ?? 0)))}%</p>
          </div>
        )}
      </motion.div>
    </div>
  );
}
