import { getAppRuntimeKind } from '@/lib/runtime-signals';
import { resolveClientIdentity } from '@/lib/client-version';
import { fetchAppReleasePolicy } from '@/lib/api';
import { compareVersions } from '@/lib/mobile/forced-update';

export type UpdatePhase =
  | 'idle'
  | 'checking'
  | 'available'
  | 'downloading'
  | 'installing'
  | 'ready'
  | 'uptodate'
  | 'error';

export interface UpdateState {
  phase: UpdatePhase;
  version?: string;
  currentVersion?: string;
  /** 0..100 download progress (desktop only). */
  progress?: number;
  notes?: string | null;
  storeUrl?: string | null;
  error?: string;
}

interface NativeRelease {
  version: string;
  downloadUrl: string;
}

let desktopCheckInFlight: Promise<UpdateState> | null = null;
let desktopInstallInFlight: Promise<UpdateState> | null = null;

async function fetchNativeRelease(platform: 'android' | 'ios'): Promise<NativeRelease | null> {
  try {
    const response = await fetch(`/api/releases/native?platform=${platform}`, {
      method: 'GET',
      credentials: 'omit',
      cache: 'no-store',
    });
    if (!response.ok) return null;
    const data = await response.json() as Partial<NativeRelease>;
    if (typeof data.version !== 'string' || typeof data.downloadUrl !== 'string') return null;
    return { version: data.version, downloadUrl: data.downloadUrl };
  } catch {
    return null;
  }
}

/** True when the current desktop shell exposes the manual updater bridge. */
export function isDesktopUpdaterAvailable(): boolean {
  return typeof window !== 'undefined'
    && getAppRuntimeKind() === 'desktop'
    && Boolean(window.desktopBridge?.updater);
}

/** True when running inside the Capacitor mobile shell. */
export function isMobileShell(): boolean {
  return typeof window !== 'undefined' && getAppRuntimeKind() === 'capacitor-native';
}

/**
 * Checks for an available update for the current shell without installing.
 * Desktop uses the Tauri updater; mobile compares the installed version to the
 * server release policy. Never throws — returns an `error` phase instead.
 */
export async function checkForUpdates(): Promise<UpdateState> {
  try {
    if (isDesktopUpdaterAvailable()) {
      if (!desktopCheckInFlight) {
        desktopCheckInFlight = window.desktopBridge!.updater!.check()
          .then((result) => ({
            phase: result.available ? 'available' : 'uptodate',
            version: result.version,
            currentVersion: result.currentVersion,
            notes: result.notes ?? null,
          } satisfies UpdateState))
          .finally(() => {
            desktopCheckInFlight = null;
          });
      }
      return await desktopCheckInFlight;
    }

    if (isMobileShell()) {
      const identity = await resolveClientIdentity();
      const platform = identity.platform === 'ios' ? 'ios' : 'android';
      const [policy, nativeRelease] = await Promise.all([
        fetchAppReleasePolicy(platform),
        fetchNativeRelease(platform),
      ]);
      const current = identity.version ?? '0.0.0';
      const latest = nativeRelease && compareVersions(policy.latestVersion, nativeRelease.version) < 0
        ? nativeRelease.version
        : policy.latestVersion;
      const outdated =
        policy.forceUpdate || compareVersions(current, latest) < 0;
      return {
        phase: outdated ? 'available' : 'uptodate',
        version: latest,
        currentVersion: identity.version ?? undefined,
        storeUrl: policy.storeUrl ?? nativeRelease?.downloadUrl ?? null,
      };
    }

    return { phase: 'uptodate' };
  } catch (error) {
    return { phase: 'error', error: error instanceof Error ? error.message : 'Update check failed.' };
  }
}

/**
 * Subscribes to desktop updater lifecycle events (download progress, install,
 * ready, error) pushed from the Rust side. No-op off the desktop shell.
 */
export function subscribeDesktopUpdateEvents(
  listener: (event: {
    phase: 'available' | 'downloading' | 'installing' | 'ready' | 'error';
    version?: string;
    currentVersion?: string;
    progress?: number;
    notes?: string | null;
    error?: string;
  }) => void,
): () => void {
  if (!isDesktopUpdaterAvailable()) return () => {};
  return window.desktopBridge!.updater!.onProgress(listener);
}

/**
 * Downloads, verifies, and stages the desktop update. Resolves once the update
 * is installed (phase 'ready') or on error. Live progress arrives via
 * {@link subscribeDesktopUpdateEvents}. Call {@link relaunchDesktop} afterward.
 */
export async function installDesktopUpdate(): Promise<UpdateState> {
  if (!isDesktopUpdaterAvailable()) {
    return { phase: 'error', error: 'Desktop updater is unavailable.' };
  }
  if (!desktopInstallInFlight) {
    desktopInstallInFlight = (async () => {
      try {
        const result = await window.desktopBridge!.updater!.install();
        return result.ok
          ? { phase: 'ready' as const }
          : { phase: 'error' as const, error: result.error ?? 'Update install failed.' };
      } catch (error) {
        return { phase: 'error' as const, error: error instanceof Error ? error.message : 'Update install failed.' };
      }
    })().finally(() => {
      desktopInstallInFlight = null;
    });
  }
  return await desktopInstallInFlight;
}

/** Restarts the desktop app into the freshly installed version. */
export async function relaunchDesktop(): Promise<void> {
  if (!isDesktopUpdaterAvailable()) return;
  await window.desktopBridge!.updater!.relaunch();
}

/**
 * Mobile update action: try Android's IMMEDIATE in-app update first, then fall
 * back to opening the platform store listing (the iOS path always).
 */
export async function performMobileUpdate(storeUrl?: string | null): Promise<boolean> {
  try {
    const { tryAndroidImmediateUpdate } = await import('@/lib/mobile/android-immediate-update');
    const handled = await tryAndroidImmediateUpdate();
    if (handled) return true;
  } catch {
    // Fall through to the store redirect.
  }
  const { openAppStore } = await import('@/lib/mobile/forced-update');
  return await openAppStore(storeUrl);
}
