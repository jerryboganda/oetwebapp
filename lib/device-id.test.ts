import { beforeEach, describe, expect, it, vi } from 'vitest';

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
});
