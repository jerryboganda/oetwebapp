import { describe, expect, it, vi, beforeEach } from 'vitest';

// ── Mock Capacitor Core ─────────────────────────────────────────

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => true),
    getPlatform: vi.fn(() => 'android'),
  },
}));

// ── Mock Push Notifications ─────────────────────────────────────

const mockPushNotifications = {
  checkPermissions: vi.fn(),
  requestPermissions: vi.fn(),
  register: vi.fn(),
  addListener: vi.fn(),
  getDeliveredNotifications: vi.fn(),
  removeAllDeliveredNotifications: vi.fn(),
};

vi.mock('@capacitor/push-notifications', () => ({
  PushNotifications: mockPushNotifications,
}));

import {
  checkPushPermission,
  requestPushPermission,
  registerPushNotifications,
  getDeliveredNotifications,
  removeAllDeliveredNotifications,
} from '@/lib/mobile/push-notifications';

describe('push-notifications', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  describe('checkPushPermission', () => {
    it('returns the current permission state', async () => {
      mockPushNotifications.checkPermissions.mockResolvedValue({ receive: 'granted' });
      const result = await checkPushPermission();
      expect(result).toBe('granted');
    });

    it('returns denied on error', async () => {
      mockPushNotifications.checkPermissions.mockRejectedValue(new Error('fail'));
      const result = await checkPushPermission();
      expect(result).toBe('denied');
    });
  });

  describe('requestPushPermission', () => {
    it('returns granted when user accepts', async () => {
      mockPushNotifications.requestPermissions.mockResolvedValue({ receive: 'granted' });
      const result = await requestPushPermission();
      expect(result).toBe('granted');
    });

    it('returns denied when user rejects', async () => {
      mockPushNotifications.requestPermissions.mockResolvedValue({ receive: 'denied' });
      const result = await requestPushPermission();
      expect(result).toBe('denied');
    });
  });

  describe('registerPushNotifications', () => {
    // FCM_REGISTRATION_ENABLED is false (see push-notifications.ts) --
    // google-services.json was removed when the Android applicationId
    // changed, so FirebaseApp is never initialized and register() would
    // crash. These tests cover the current (disabled) behavior; when the
    // flag is re-enabled they should be restored to assert registration
    // actually happens.
    it('requests permission but skips FCM registration while disabled', async () => {
      mockPushNotifications.requestPermissions.mockResolvedValue({ receive: 'granted' });

      const cleanup = await registerPushNotifications({
        onRegistration: vi.fn(),
      });

      expect(mockPushNotifications.requestPermissions).toHaveBeenCalled();
      expect(mockPushNotifications.register).not.toHaveBeenCalled();
      expect(mockPushNotifications.addListener).not.toHaveBeenCalled();
      expect(typeof cleanup).toBe('function');
    });

    it('returns noop cleanup when permission denied', async () => {
      mockPushNotifications.requestPermissions.mockResolvedValue({ receive: 'denied' });
      const cleanup = await registerPushNotifications();
      expect(mockPushNotifications.register).not.toHaveBeenCalled();
      expect(typeof cleanup).toBe('function');
    });

    it('does not invoke onRegistration handler while FCM registration is disabled', async () => {
      const onRegistration = vi.fn();
      mockPushNotifications.requestPermissions.mockResolvedValue({ receive: 'granted' });

      await registerPushNotifications({ onRegistration });

      expect(mockPushNotifications.addListener).not.toHaveBeenCalled();
      expect(onRegistration).not.toHaveBeenCalled();
    });
  });

  describe('getDeliveredNotifications', () => {
    it('returns mapped notifications', async () => {
      mockPushNotifications.getDeliveredNotifications.mockResolvedValue({
        notifications: [
          { id: '1', title: 'Test', body: 'Body', data: { route: '/dashboard' } },
        ],
      });

      const result = await getDeliveredNotifications();
      expect(result).toHaveLength(1);
      expect(result[0]).toEqual({
        id: '1',
        title: 'Test',
        body: 'Body',
        data: { route: '/dashboard' },
      });
    });

    it('returns empty array on error', async () => {
      mockPushNotifications.getDeliveredNotifications.mockRejectedValue(new Error('fail'));
      const result = await getDeliveredNotifications();
      expect(result).toEqual([]);
    });
  });

  describe('removeAllDeliveredNotifications', () => {
    it('calls the native API', async () => {
      mockPushNotifications.removeAllDeliveredNotifications.mockResolvedValue(undefined);
      await removeAllDeliveredNotifications();
      expect(mockPushNotifications.removeAllDeliveredNotifications).toHaveBeenCalled();
    });
  });
});
