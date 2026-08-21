/**
 * Canonical download locations for the native apps.
 *
 * Desktop installers and the Tauri auto-updater consume the VPS catalog at
 * /desktop/updates/latest.json and /releases/**. Android prefers Play Store
 * in-app update, with a same-origin APK fallback. Clients never need GitHub.
 */
export const NATIVE_RELEASES_URL = '/releases';

/**
 * Direct-download endpoints that resolve the latest installer from the VPS
 * catalog and redirect the browser straight to the file.
 */
export const WINDOWS_DOWNLOAD_URL = '/api/download/windows';
export const MAC_DOWNLOAD_URL = '/api/download/mac';
export const ANDROID_DOWNLOAD_URL = '/api/download/android';

export const GET_APP_PATH = '/get-app';

export const ANDROID_STORE_URL =
  process.env.NEXT_PUBLIC_ANDROID_PLAY_STORE_URL ||
  ANDROID_DOWNLOAD_URL;

export const IOS_STORE_URL = process.env.NEXT_PUBLIC_IOS_APP_STORE_URL || null;

/** Temporary direct IPA resolver used until the App Store listing is configured. */
export const IOS_DIRECT_DOWNLOAD_URL = '/api/download/ios';

/** The current iOS destination: the official store when configured, otherwise
 * the trusted direct-release resolver. */
export const IOS_DOWNLOAD_URL = IOS_STORE_URL || IOS_DIRECT_DOWNLOAD_URL;

/**
 * Android ships as a direct APK (no Play Store listing yet), so app-download
 * CTAs send users to the install-instructions page rather than the bare binary —
 * a raw APK link only downloads a file, it never triggers Android's install step.
 */
export const ANDROID_INSTALL_URL = `${GET_APP_PATH}/android-install`;

export type DesktopOsKind = 'windows' | 'mac' | 'linux' | 'android' | 'ios' | 'unknown';

/** Best-effort OS detection for tailoring the /get-app hero CTA. UX only. */
export function detectVisitorOs(): DesktopOsKind {
  if (typeof navigator === 'undefined') return 'unknown';
  const ua = navigator.userAgent;
  if (/android/i.test(ua)) return 'android';
  if (/iphone|ipad|ipod/i.test(ua)) return 'ios';
  if (/windows/i.test(ua)) return 'windows';
  if (/macintosh|mac os x/i.test(ua)) return 'mac';
  if (/linux/i.test(ua)) return 'linux';
  return 'unknown';
}
