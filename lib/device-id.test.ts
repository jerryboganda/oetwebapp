import { beforeEach, describe, expect, it, vi } from 'vitest';
import { Capacitor } from '@capacitor/core';
import { getSecureItem, setSecureItem } from '@/lib/mobile/secure-storage';

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => false),
  },
}));

vi.mock('@/lib/mobile/secure-storage', () => ({
  getSecureItem: vi.fn(async () => null),
  setSecureItem: vi.fn(async () => true),
}));

function clearDeviceCookie(): void {
  document.cookie = 'oet_device_id=; Max-Age=0; Path=/';
}

describe('device-id', () => {
  beforeEach(() => {
    window.localStorage.clear();
    clearDeviceCookie();
    vi.mocked(Capacitor.isNativePlatform).mockReturnValue(false);
    vi.mocked(getSecureItem).mockResolvedValue(null);
    vi.mocked(setSecureItem).mockResolvedValue(true);
    vi.resetModules();
  });

  it('reuses the first-party device cookie when browser localStorage was cleared', async () => {
    document.cookie = 'oet_device_id=stable-device-id; Path=/';

    const { getDeviceId } = await import('./device-id');

    expect(getDeviceId()).toBe('stable-device-id');
    expect(window.localStorage.getItem('oet_device_id')).toBe('stable-device-id');
  });

  it('persists a newly generated identity in both web storage layers', async () => {
    const { getDeviceId } = await import('./device-id');

    const id = getDeviceId();
    expect(id).toBeTruthy();
    expect(window.localStorage.getItem('oet_device_id')).toBe(id);
    expect(document.cookie).toContain(`oet_device_id=${encodeURIComponent(id ?? '')}`);
  });

  it('fails closed when a generated native identity cannot be persisted', async () => {
    vi.mocked(Capacitor.isNativePlatform).mockReturnValue(true);
    vi.mocked(setSecureItem).mockResolvedValue(false);

    const { getDeviceIdForRequest } = await import('./device-id');

    await expect(getDeviceIdForRequest()).resolves.toBeNull();
    expect(setSecureItem).toHaveBeenCalledOnce();
  });
});
