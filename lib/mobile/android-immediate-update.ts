'use client';

import { Capacitor } from '@capacitor/core';

/**
 * Attempts Google Play's IMMEDIATE in-app update flow on Android — the update
 * downloads and installs inside the app with a full-screen Google-owned UI, no
 * store visit. Returns true when it took over the update (caller must NOT also
 * open the store), false when not applicable (iOS, no update available, not a
 * Play-installed build, or the plugin is missing) so the caller can fall back
 * to a store redirect.
 *
 * Android-only by construction; a no-op that returns false everywhere else.
 */
export async function tryAndroidImmediateUpdate(): Promise<boolean> {
  if (!Capacitor.isNativePlatform() || Capacitor.getPlatform() !== 'android') {
    return false;
  }

  try {
    const { AppUpdate, AppUpdateAvailability } = await import('@capawesome/capacitor-app-update');
    const info = await AppUpdate.getAppUpdateInfo();

    if (info.updateAvailability !== AppUpdateAvailability.UPDATE_AVAILABLE) {
      return false;
    }

    if (info.immediateUpdateAllowed) {
      await AppUpdate.performImmediateUpdate();
      return true;
    }

    // Update exists but immediate flow isn't allowed for this build — send the
    // user to the Play listing via the plugin (still counts as handled).
    await AppUpdate.openAppStore();
    return true;
  } catch {
    return false;
  }
}
