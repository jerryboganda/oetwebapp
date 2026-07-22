import { describe, expect, it, vi, beforeEach } from 'vitest';

// ── Mock Capacitor Core ─────────────────────────────────────────

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: vi.fn(() => true),
    getPlatform: vi.fn(() => 'android'),
  },
}));

// ── Mock App Plugin ─────────────────────────────────────────────

const mockApp = {
  getInfo: vi.fn(),
};

vi.mock('@capacitor/app', () => ({
  App: mockApp,
}));

// ── Mock Browser Plugin ─────────────────────────────────────────

const mockBrowser = {
  open: vi.fn(),
};

vi.mock('@capacitor/browser', () => ({
  Browser: mockBrowser,
}));

const mockNativeAppUpdate = {
  openAppStore: vi.fn(),
};

vi.mock('@capawesome/capacitor-app-update', () => ({
  AppUpdate: mockNativeAppUpdate,
}));

import { compareVersions, getAppVersion, checkForUpdate, openAppStore } from '@/lib/mobile/forced-update';

describe('forced-update', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    vi.restoreAllMocks();
  });

  describe('getAppVersion', () => {
    it('returns version info from native plugin', async () => {
      mockApp.getInfo.mockResolvedValue({ version: '1.2.0', build: '5', name: 'OET Prep', id: 'com.oetprep.learner' });
      const result = await getAppVersion();
      expect(result).toEqual({
        currentVersion: '1.2.0',
        currentBuild: '5',
        platform: 'android',
      });
    });

    it('returns null on error', async () => {
      mockApp.getInfo.mockRejectedValue(new Error('not available'));
      const result = await getAppVersion();
      expect(result).toBeNull();
    });
  });

  describe('checkForUpdate', () => {
    it('detects forced update when current version is below minimum', async () => {
      mockApp.getInfo.mockResolvedValue({ version: '1.0.0', build: '1', name: 'OET Prep', id: 'com.oetprep.learner' });

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ minVersion: '1.1.0', latestVersion: '1.2.0', forceUpdate: false }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      );

      const result = await checkForUpdate('https://api.example.com/version');
      expect(result.updateRequired).toBe(true);
      expect(result.currentVersion).toBe('1.0.0');
      expect(result.latestVersion).toBe('1.2.0');

      fetchSpy.mockRestore();
    });

    it('detects no update needed when current version meets minimum', async () => {
      mockApp.getInfo.mockResolvedValue({ version: '1.2.0', build: '5', name: 'OET Prep', id: 'com.oetprep.learner' });

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ minVersion: '1.1.0', latestVersion: '1.2.0', forceUpdate: false }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      );

      const result = await checkForUpdate('https://api.example.com/version');
      expect(result.updateRequired).toBe(false);

      fetchSpy.mockRestore();
    });

    it('detects forced update flag from backend', async () => {
      mockApp.getInfo.mockResolvedValue({ version: '1.2.0', build: '5', name: 'OET Prep', id: 'com.oetprep.learner' });

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockResolvedValue(
        new Response(JSON.stringify({ minVersion: '1.0.0', latestVersion: '1.3.0', forceUpdate: true }), {
          status: 200,
          headers: { 'Content-Type': 'application/json' },
        })
      );

      const result = await checkForUpdate('https://api.example.com/version');
      expect(result.updateRequired).toBe(true);

      fetchSpy.mockRestore();
    });

    it('does not block on network error', async () => {
      mockApp.getInfo.mockResolvedValue({ version: '1.0.0', build: '1', name: 'OET Prep', id: 'com.oetprep.learner' });

      const fetchSpy = vi.spyOn(globalThis, 'fetch').mockRejectedValue(new Error('network error'));

      const result = await checkForUpdate('https://api.example.com/version');
      expect(result.updateRequired).toBe(false);

      fetchSpy.mockRestore();
    });
  });

  describe('compareVersions', () => {
    it('compares numeric segments and safely ignores prerelease/build suffixes', () => {
      expect(compareVersions('1.9.0', '1.10.0')).toBe(-1);
      expect(compareVersions('v1.3.3+42', '1.3.3')).toBe(0);
      expect(compareVersions('1.3.4-beta.1', '1.3.3')).toBe(1);
    });

    it('fails open for malformed version input', () => {
      expect(compareVersions('unknown', '1.3.3')).toBe(0);
      expect(compareVersions('1.x.0', '1.3.3')).toBe(0);
    });
  });

  describe('openAppStore', () => {
    it('opens the signed Android download fallback when Play is not configured', async () => {
      mockBrowser.open.mockResolvedValue(undefined);
      await expect(openAppStore()).resolves.toBe(true);
      expect(mockBrowser.open).toHaveBeenCalledWith({
        url: 'https://app.oetwithdrhesham.co.uk/api/download/android',
      });
    });

    it('uses the native store app for an official server-provided listing', async () => {
      mockNativeAppUpdate.openAppStore.mockResolvedValue(undefined);
      await expect(openAppStore('https://play.google.com/store/apps/details?id=com.oetprep.learner')).resolves.toBe(true);
      expect(mockNativeAppUpdate.openAppStore).toHaveBeenCalledOnce();
      expect(mockBrowser.open).not.toHaveBeenCalled();
    });
  });
});
