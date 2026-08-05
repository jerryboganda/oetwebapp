'use client';

import dynamic from 'next/dynamic';
import { getAppRuntimeKind, type AppRuntimeKind } from '@/lib/runtime-signals';

const ShellControls = dynamic(
  () => import('./ShellControls').then((module) => module.ShellControls),
  { ssr: false },
);
const DesktopAutoUpdater = dynamic(
  () => import('./DesktopAutoUpdater').then((module) => module.DesktopAutoUpdater),
  { ssr: false },
);
const MobileAutoUpdater = dynamic(
  () => import('./MobileAutoUpdater').then((module) => module.MobileAutoUpdater),
  { ssr: false },
);
const ForcedUpdateOverlay = dynamic(
  () => import('./ForcedUpdateOverlay').then((module) => module.ForcedUpdateOverlay),
  { ssr: false },
);

/**
 * Shell-only controls and updater UI. Plain web visitors never render these
 * dynamic entries, so updater/native code is not downloaded on public or auth
 * routes. Native shells retain the same update and forced-gate behavior.
 */
export function RuntimeShellBridges() {
  const runtimeKind: AppRuntimeKind = getAppRuntimeKind();

  if (runtimeKind === 'web') return null;

  return (
    <>
      <ShellControls />
      {runtimeKind === 'desktop' ? <DesktopAutoUpdater /> : <MobileAutoUpdater />}
      <ForcedUpdateOverlay />
    </>
  );
}
