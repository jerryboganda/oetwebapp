'use client';

import { Capacitor } from '@capacitor/core';

// ── Types ───────────────────────────────────────────────────────

export interface AppVersionInfo {
  currentVersion: string;
  currentBuild: string;
  platform: string;
}

export interface UpdateCheckResult {
  updateRequired: boolean;
  currentVersion: string;
  latestVersion: string | null;
  storeUrl: string | null;
}

// ── Configuration ───────────────────────────────────────────────

const DEFAULT_ANDROID_STORE_URL = 'https://play.google.com/store/apps/details?id=com.oetprep.learner';

function getStoreUrl(platform: 'android' | 'ios'): string | null {
  if (platform === 'android') {
    return process.env.NEXT_PUBLIC_ANDROID_PLAY_STORE_URL || DEFAULT_ANDROID_STORE_URL;
  }

  return process.env.NEXT_PUBLIC_IOS_APP_STORE_URL || null;
}

// ── Version Retrieval ───────────────────────────────────────────

export async function getAppVersion(): Promise<AppVersionInfo | null> {
  if (!Capacitor.isNativePlatform()) {
    return null;
  }

  try {
    const { App } = await import('@capacitor/app');
    const info = await App.getInfo();
    return {
      currentVersion: info.version,
      currentBuild: info.build,
      platform: Capacitor.getPlatform(),
    };
  } catch {
    return null;
  }
}

// ── Version Comparison ──────────────────────────────────────────

function compareVersions(current: string, latest: string): number {
  const currentParts = current.split('.').map(Number);
  const latestParts = latest.split('.').map(Number);
  const maxLength = Math.max(currentParts.length, latestParts.length);

  for (let i = 0; i < maxLength; i++) {
    const a = currentParts[i] ?? 0;
    const b = latestParts[i] ?? 0;

    if (a < b) return -1;
    if (a > b) return 1;
  }

  return 0;
}

// ── Update Check ────────────────────────────────────────────────

/**
 * Check if the app needs a forced update by calling the backend version endpoint.
 *
 * The backend should expose a GET endpoint that returns:
 * ```json
 * {
 *   "minVersion": "1.0.0",
 *   "latestVersion": "1.2.0",
 *   "forceUpdate": false
 * }
 * ```
 */
export async function checkForUpdate(
  versionCheckUrl: string,
): Promise<UpdateCheckResult> {
  const appVersion = await getAppVersion();
  if (!appVersion) {
    return {
      updateRequired: false,
      currentVersion: 'unknown',
      latestVersion: null,
      storeUrl: null,
    };
  }

  const platform = appVersion.platform as 'android' | 'ios';
  const storeUrl = getStoreUrl(platform);

  try {
    const response = await fetch(versionCheckUrl, {
      method: 'GET',
      headers: { 'Content-Type': 'application/json' },
    });

    if (!response.ok) {
      return {
        updateRequired: false,
        currentVersion: appVersion.currentVersion,
        latestVersion: null,
        storeUrl,
      };
    }

    const data = (await response.json()) as {
      minVersion?: string;
      latestVersion?: string;
      forceUpdate?: boolean;
      storeUrl?: string | null;
    };

    const minVersion = data.minVersion;
    const latestVersion = data.latestVersion ?? null;
    const effectiveStoreUrl = data.storeUrl ?? storeUrl;

    const updateRequired =
      (data.forceUpdate === true) ||
      (typeof minVersion === 'string' && compareVersions(appVersion.currentVersion, minVersion) < 0);

    return {
      updateRequired,
      currentVersion: appVersion.currentVersion,
      latestVersion,
      storeUrl: effectiveStoreUrl,
    };
  } catch {
    // Network error — don't block the user.
    return {
      updateRequired: false,
      currentVersion: appVersion.currentVersion,
      latestVersion: null,
      storeUrl,
    };
  }
}

// ── Store Redirect ──────────────────────────────────────────────

export async function openAppStore(): Promise<void> {
  const platform = Capacitor.getPlatform() as 'android' | 'ios';
  const storeUrl = getStoreUrl(platform);

  if (!storeUrl) {
    return;
  }

  try {
    const { Browser } = await import('@capacitor/browser');
    await Browser.open({ url: storeUrl });
  } catch {
    // Fallback: try window.open
    if (typeof window !== 'undefined') {
      window.open(storeUrl, '_blank', 'noopener,noreferrer');
    }
  }
}
