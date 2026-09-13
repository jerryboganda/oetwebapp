'use client';

import dynamic from 'next/dynamic';
import { getAppRuntimeKind, type AppRuntimeKind } from '@/lib/runtime-signals';

/**
 * A dynamic-import rejection here is SILENT and sticky: the bridge component
 * never mounts, so no keyboard handling, push, resume, or deep-link wiring
 * runs — for the rest of the SPA session (client-side navigation keeps module
 * state). On a phone cold-starting on flaky mobile data a single failed chunk
 * fetch used to permanently disable the native runtime while the rest of the
 * app worked, which reads to the learner as "the keyboard fix is broken".
 * Retry a few times before giving up.
 */
async function loadBridgeWithRetry(attempts = 3): Promise<typeof import('./mobile-runtime-bridge')> {
  let lastError: unknown;
  for (let attempt = 0; attempt < attempts; attempt += 1) {
    try {
      return await import('./mobile-runtime-bridge');
    } catch (error) {
      lastError = error;
      await new Promise((resolve) => setTimeout(resolve, 750 * (attempt + 1)));
    }
  }
  throw lastError;
}

const NativeMobileRuntimeBridge = dynamic(
  () => loadBridgeWithRetry().then((module) => module.MobileRuntimeBridge),
  { ssr: false },
);

/**
 * Keeps Capacitor lifecycle, push, and deep-link code out of browser and
 * desktop startup. The bootstrap script stamps the runtime kind before
 * hydration, so native shells still begin initialization immediately.
 */
export function MobileRuntimeGate() {
  const runtimeKind: AppRuntimeKind = getAppRuntimeKind();
  return runtimeKind === 'capacitor-native' ? <NativeMobileRuntimeBridge /> : null;
}
