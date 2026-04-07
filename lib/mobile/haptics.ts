'use client';

import { Capacitor } from '@capacitor/core';
import type { ImpactStyle as CapacitorImpactStyle, NotificationType as CapacitorNotificationType } from '@capacitor/haptics';

type NativeHapticsModule = typeof import('@capacitor/haptics');

export type HapticImpactStyle = 'LIGHT' | 'MEDIUM' | 'HEAVY';
export type HapticNotificationType = 'SUCCESS' | 'WARNING' | 'ERROR';

let hapticsModulePromise: Promise<NativeHapticsModule> | null = null;

function loadHapticsModule() {
  hapticsModulePromise ??= import('@capacitor/haptics');
  return hapticsModulePromise;
}

async function runHapticAction(action: (haptics: NativeHapticsModule['Haptics']) => Promise<void>) {
  if (!Capacitor.isNativePlatform()) {
    return;
  }

  try {
    const { Haptics } = await loadHapticsModule();
    await action(Haptics);
  } catch {
    // Native haptics are best-effort and should never block UI work.
  }
}

export function triggerImpactHaptic(style: HapticImpactStyle = 'MEDIUM') {
  return runHapticAction((haptics) => haptics.impact({ style: style as CapacitorImpactStyle }));
}

export function triggerNotificationHaptic(type: HapticNotificationType = 'SUCCESS') {
  return runHapticAction((haptics) => haptics.notification({ type: type as CapacitorNotificationType }));
}