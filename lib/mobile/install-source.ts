'use client';

import { Capacitor, registerPlugin } from '@capacitor/core';

/**
 * Native installer source (Android only).
 *
 * Play App Signing re-signs the uploaded AAB with the Play app-signing key,
 * which differs from the upload key used for the direct-download APK on the
 * VPS release feed. Android refuses to install one over the other ("App not
 * installed") and no app code can override OS signature enforcement, so the
 * update UI must never cross the channels: Play-installed copies update via
 * the Play listing, sideloaded copies via the direct APK.
 */

export const PLAY_INSTALLER_PACKAGE = 'com.android.vending';

/** Package name of the Play Store listing for update deep-links. */
const PLAY_LISTING_URL = 'https://play.google.com/store/apps/details?id=com.oetwithdrhesham.app';

export type InstallSourceKind = 'play' | 'sideload' | 'unknown';

export interface InstallSourceInfo {
  kind: InstallSourceKind;
  /** Raw installer package (e.g. com.android.vending), null when the OS reports none. */
  installerPackage: string | null;
  /** Installed versionCode, null when unreadable (web / old shell). */
  versionCode: number | null;
}

interface InstallerSourcePlugin {
  getInstallSource(): Promise<{ installerPackage: string | null; isPlayInstalled: boolean; versionCode: number }>;
}

const InstallerSource = registerPlugin<InstallerSourcePlugin>('InstallerSource');

/**
 * Pure classifier — maps a raw installer package name to a channel. Kept
 * separate from the bridge call so it is unit-testable without native code.
 */
export function classifyInstallSource(installerPackage: string | null | undefined): InstallSourceKind {
  if (!installerPackage) {
    return 'unknown';
  }

  return installerPackage === PLAY_INSTALLER_PACKAGE ? 'play' : 'sideload';
}

export function getPlayListingUrl(): string {
  return PLAY_LISTING_URL;
}

/**
 * Best-effort installer source. No-throw: returns `unknown` on web/desktop,
 * old shells without the plugin (feature-detected via isPluginAvailable),
 * and when the native call rejects — callers fall back to the direct-APK
 * flow with a Play cross-link, which is today's behavior.
 */
export async function getInstallSource(): Promise<InstallSourceInfo> {
  const fallback: InstallSourceInfo = { kind: 'unknown', installerPackage: null, versionCode: null };

  if (!Capacitor.isNativePlatform() || Capacitor.getPlatform() !== 'android') {
    return fallback;
  }

  if (!Capacitor.isPluginAvailable('InstallerSource')) {
    return fallback;
  }

  try {
    const result = await InstallerSource.getInstallSource();
    return {
      kind: classifyInstallSource(result.installerPackage),
      installerPackage: result.installerPackage,
      versionCode: typeof result.versionCode === 'number' ? result.versionCode : null,
    };
  } catch {
    return fallback;
  }
}
