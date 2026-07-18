'use client';

import { Capacitor } from '@capacitor/core';

// ── Types ───────────────────────────────────────────────────────

export interface PushNotificationToken {
  value: string;
}

export interface PushNotificationData {
  id: string;
  title?: string;
  body?: string;
  data: Record<string, string>;
}

export interface PushNotificationHandlers {
  onRegistration?: (token: PushNotificationToken) => void;
  onRegistrationError?: (error: unknown) => void;
  onNotificationReceived?: (notification: PushNotificationData) => void;
  onNotificationActionPerformed?: (notification: PushNotificationData) => void;
}

// ── Lazy Module Loading ─────────────────────────────────────────

type PushModule = typeof import('@capacitor/push-notifications');
let pushModulePromise: Promise<PushModule> | null = null;

function loadPushModule(): Promise<PushModule> {
  pushModulePromise ??= import('@capacitor/push-notifications');
  return pushModulePromise;
}

// Firebase/FCM is now provisioned on the backend and in the Android *source*
// (google-services.json is committed), BUT the currently-installed APKs were built
// before that file existed. google-services.json is processed at Gradle build time
// into baked-in Android resources — it is NOT fetched at runtime, so already-built
// APKs cannot initialize FirebaseApp no matter what this remote JS bundle says. This
// app loads its JS remotely (capacitor.config.ts server.url), so flipping this flag
// takes effect on already-installed apps immediately — before they've been rebuilt
// with the new google-services.json, that reintroduces the exact crash this flag
// was added to prevent. Flip to true only once a new Android build (with
// google-services.json baked in) has shipped and users have updated.
const FCM_REGISTRATION_ENABLED = false;

// ── Permission Check ────────────────────────────────────────────

export async function checkPushPermission(): Promise<'prompt' | 'prompt-with-rationale' | 'granted' | 'denied'> {
  if (!Capacitor.isNativePlatform()) {
    return 'denied';
  }

  try {
    const { PushNotifications } = await loadPushModule();
    const result = await PushNotifications.checkPermissions();
    return result.receive;
  } catch {
    return 'denied';
  }
}

export async function requestPushPermission(): Promise<'granted' | 'denied'> {
  if (!Capacitor.isNativePlatform()) {
    return 'denied';
  }

  try {
    const { PushNotifications } = await loadPushModule();
    const result = await PushNotifications.requestPermissions();
    return result.receive === 'granted' ? 'granted' : 'denied';
  } catch {
    return 'denied';
  }
}

// ── Registration ────────────────────────────────────────────────

export async function registerPushNotifications(
  handlers: PushNotificationHandlers = {},
): Promise<() => void> {
  if (!Capacitor.isNativePlatform()) {
    return () => undefined;
  }

  const cleanup: Array<() => Promise<void> | void> = [];

  try {
    const { PushNotifications } = await loadPushModule();

    const permission = await PushNotifications.requestPermissions();
    if (permission.receive !== 'granted') {
      return () => undefined;
    }

    if (!FCM_REGISTRATION_ENABLED) {
      return () => undefined;
    }

    const registrationListener = await PushNotifications.addListener('registration', (token) => {
      handlers.onRegistration?.({ value: token.value });
    });
    cleanup.push(() => registrationListener.remove());

    const registrationErrorListener = await PushNotifications.addListener('registrationError', (error) => {
      handlers.onRegistrationError?.(error);
    });
    cleanup.push(() => registrationErrorListener.remove());

    const receivedListener = await PushNotifications.addListener(
      'pushNotificationReceived',
      (notification) => {
        handlers.onNotificationReceived?.({
          id: notification.id,
          title: notification.title,
          body: notification.body,
          data: (notification.data as Record<string, string>) ?? {},
        });
      },
    );
    cleanup.push(() => receivedListener.remove());

    const actionListener = await PushNotifications.addListener(
      'pushNotificationActionPerformed',
      (action) => {
        const notification = action.notification;
        handlers.onNotificationActionPerformed?.({
          id: notification.id,
          title: notification.title,
          body: notification.body,
          data: (notification.data as Record<string, string>) ?? {},
        });
      },
    );
    cleanup.push(() => actionListener.remove());

    await PushNotifications.register();
  } catch {
    // Push notification setup is non-critical — fail silently.
  }

  return () => {
    cleanup.forEach((teardown) => {
      try {
        void teardown();
      } catch {
        // Ignore teardown failures.
      }
    });
  };
}

// ── Badge Management ────────────────────────────────────────────

export async function getDeliveredNotifications(): Promise<PushNotificationData[]> {
  if (!Capacitor.isNativePlatform()) {
    return [];
  }

  try {
    const { PushNotifications } = await loadPushModule();
    const result = await PushNotifications.getDeliveredNotifications();
    return result.notifications.map((n) => ({
      id: n.id,
      title: n.title,
      body: n.body,
      data: (n.data as Record<string, string>) ?? {},
    }));
  } catch {
    return [];
  }
}

export async function removeAllDeliveredNotifications(): Promise<void> {
  if (!Capacitor.isNativePlatform()) {
    return;
  }

  try {
    const { PushNotifications } = await loadPushModule();
    await PushNotifications.removeAllDeliveredNotifications();
  } catch {
    // Ignore cleanup failures.
  }
}
