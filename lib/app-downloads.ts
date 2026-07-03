/**
 * Canonical download locations for the native apps.
 *
 * Desktop installers (Windows NSIS .exe / macOS .dmg) are published as GitHub
 * Release assets — the same feed the Tauri auto-updater consumes
 * (see src-tauri/tauri.conf.json updater endpoints). The Android app ships
 * via Play Store (fallback: the signed APK attached to mobile releases).
 */
export const GITHUB_RELEASES_URL = 'https://github.com/jerryboganda/oetwebapp/releases/latest';

export const ANDROID_STORE_URL =
  process.env.NEXT_PUBLIC_ANDROID_PLAY_STORE_URL ||
  'https://play.google.com/store/apps/details?id=com.oetprep.learner';

export const IOS_STORE_URL = process.env.NEXT_PUBLIC_IOS_APP_STORE_URL || null;

export const GET_APP_PATH = '/get-app';

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
