'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { checkForUpdates, isMobileShell } from '@/lib/shell/update-controller';
import { UpdateDialog } from './UpdateDialog';

const RECHECK_INTERVAL_MS = 4 * 60 * 60 * 1000;

/**
 * Automatically discovers mobile releases on launch, when connectivity
 * returns, when the app resumes, and periodically during long sessions. The
 * platform-confirmed update action remains user initiated (required by iOS and
 * Android install security), while Android uses Play's immediate in-app flow
 * whenever the installed distribution supports it.
 */
export function MobileAutoUpdater() {
  const [open, setOpen] = useState(false);
  const checkingRef = useRef(false);
  const availableVersionRef = useRef<string | null>(null);
  const dismissedVersionRef = useRef<string | null>(null);

  const check = useCallback(async () => {
    if (!isMobileShell() || checkingRef.current) return;
    checkingRef.current = true;
    try {
      const result = await checkForUpdates();
      if (result.phase === 'available' && result.version !== dismissedVersionRef.current) {
        availableVersionRef.current = result.version ?? null;
        setOpen(true);
      }
    } finally {
      checkingRef.current = false;
    }
  }, []);

  useEffect(() => {
    if (!isMobileShell()) return;
    void check();

    const onOnline = () => void check();
    const onVisibility = () => {
      if (document.visibilityState === 'visible') void check();
    };
    window.addEventListener('online', onOnline);
    document.addEventListener('visibilitychange', onVisibility);
    const interval = window.setInterval(() => void check(), RECHECK_INTERVAL_MS);

    return () => {
      window.removeEventListener('online', onOnline);
      document.removeEventListener('visibilitychange', onVisibility);
      window.clearInterval(interval);
    };
  }, [check]);

  return (
    <UpdateDialog
      open={open}
      onClose={() => {
        dismissedVersionRef.current = availableVersionRef.current;
        setOpen(false);
      }}
    />
  );
}
