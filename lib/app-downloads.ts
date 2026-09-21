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

/**
 * Temporary kill-switch for the public macOS download (17 Sep 2026 handover):
 * the public Mac DMG has not passed protected in-app Bunny playback QA on a
 * real Mac, so the Mac download is disabled and candidates are directed to the
 * Web App until a signed, notarized build passes. Windows is unaffected.
 * Set NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED=1 (deploy.yml build arg) to arm it;
 * remove the arg (or set 0) to re-enable. A function (not a load-time const)
 * so server routes see the value per request and tests can arm it per test —
 * Next's build-time inlining of NEXT_PUBLIC_* applies the same way inside the
 * function body.
 */
export function isMacDownloadDisabled(): boolean {
  const raw = process.env.NEXT_PUBLIC_MAC_DOWNLOAD_DISABLED;
  return raw === '1' || raw === 'true';
}

/**
 * UI-facing Mac destination: the resolver endpoint, or `null` while the
 * kill-switch is armed. `null` renders the disabled (non-clickable) download
 * badge — the same mechanism iOS uses before its store link exists. Call this
 * at render time (do not bake it into module scope) so the armed state is
 * testable and stays consistent with the server-side routes.
 */
export function macDownloadHref(): string | null {
  return isMacDownloadDisabled() ? null : MAC_DOWNLOAD_URL;
}

export const GET_APP_PATH = '/get-app';

/**
 * Official Google Play listing. Google Play is now the primary Android
 * distribution channel (Closed Testing today, production after), so this is
 * the default in-app update destination: an app installed from Play must be
 * sent to Play, never offered a manual APK download (12 Sep 2026 brief).
 */
export const ANDROID_PLAY_STORE_URL =
  process.env.NEXT_PUBLIC_ANDROID_PLAY_STORE_URL ||
  'https://play.google.com/store/apps/details?id=com.oetwithdrhesham.app';

export const ANDROID_STORE_URL = ANDROID_PLAY_STORE_URL;

/** Official App Store listing, once the release is live. */
export const IOS_STORE_URL = process.env.NEXT_PUBLIC_IOS_APP_STORE_URL || null;

/** Public TestFlight external-testing link — the approved one-tap candidate
 * path until the App Store release is live. */
export const IOS_TESTFLIGHT_URL = process.env.NEXT_PUBLIC_IOS_TESTFLIGHT_URL || null;

/**
 * Direct IPA resolver. Retained for internal/administrative use only — it is
 * deliberately NOT part of the candidate-facing chain below. iOS has no
 * sideloading, so a raw .ipa cannot be installed by an ordinary candidate;
 * publishing it as a download button is a dead end (12 Sep 2026 brief:
 * "Do not publish a fake raw .ipa download button that ordinary candidates
 * cannot [install]").
 */
export const IOS_DIRECT_DOWNLOAD_URL = '/api/download/ios';

/**
 * The candidate-facing iOS destination: the App Store listing when configured,
 * otherwise the TestFlight public link. `null` when neither is available — the
 * UI must then show a non-clickable "coming soon" state, never a fake download.
 */
export const IOS_DOWNLOAD_URL: string | null = IOS_STORE_URL || IOS_TESTFLIGHT_URL;

/** Which approved channel `IOS_DOWNLOAD_URL` resolves to, for labelling. */
export const IOS_DOWNLOAD_CHANNEL: 'app-store' | 'testflight' | null =
  IOS_STORE_URL ? 'app-store' : IOS_TESTFLIGHT_URL ? 'testflight' : null;

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
