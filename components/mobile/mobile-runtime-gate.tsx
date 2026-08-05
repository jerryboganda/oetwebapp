'use client';

import dynamic from 'next/dynamic';
import { getAppRuntimeKind, type AppRuntimeKind } from '@/lib/runtime-signals';

const NativeMobileRuntimeBridge = dynamic(
  () => import('./mobile-runtime-bridge').then((module) => module.MobileRuntimeBridge),
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
