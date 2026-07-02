'use client';

import { RefreshCcw, Smartphone } from 'lucide-react';
import { openAppStore } from '@/lib/mobile/forced-update';

/**
 * Shown inside a NATIVE shell whose build predates the video attestation
 * bridge (desktop < v0.4.0, mobile < v1.2.0). Never a dead end: desktop
 * auto-updates on restart via the Tauri updater; mobile deep-links the store.
 */
export function UpdateAppNotice({ platform }: { platform: 'desktop' | 'capacitor-native' }) {
  return (
    <div className="flex h-full min-h-[280px] w-full flex-col items-center justify-center gap-4 bg-navy px-6 py-10 text-center">
      <span className="flex h-16 w-16 items-center justify-center rounded-full bg-white/10">
        <RefreshCcw className="h-8 w-8 text-white" aria-hidden="true" />
      </span>
      <div>
        <h2 className="text-lg font-bold text-white">Update your app to watch videos</h2>
        <p className="mx-auto mt-2 max-w-md text-sm leading-6 text-white/70">
          {platform === 'desktop'
            ? 'This desktop app version predates the Video Library. It updates automatically — fully close the app and reopen it to install the latest version, then come back to this video.'
            : 'This app version predates the Video Library. Install the latest update from the store, then come back to this video.'}
        </p>
      </div>
      {platform === 'capacitor-native' && (
        <button
          type="button"
          onClick={() => {
            void openAppStore();
          }}
          className="inline-flex items-center gap-2 rounded-lg bg-primary px-4 py-2.5 text-sm font-semibold text-white hover:bg-primary-dark active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600"
        >
          <Smartphone className="h-4 w-4" aria-hidden="true" />
          Open store to update
        </button>
      )}
    </div>
  );
}
