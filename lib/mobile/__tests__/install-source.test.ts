import {
  classifyInstallSource,
  getInstallSource,
  getPlayListingUrl,
  PLAY_INSTALLER_PACKAGE,
} from '@/lib/mobile/install-source';

describe('classifyInstallSource', () => {
  it('maps the Play Store installer to the play channel', () => {
    expect(classifyInstallSource(PLAY_INSTALLER_PACKAGE)).toBe('play');
    expect(classifyInstallSource('com.android.vending')).toBe('play');
  });

  it('maps any other installer to the sideload channel', () => {
    expect(classifyInstallSource('com.google.android.packageinstaller')).toBe('sideload');
    expect(classifyInstallSource('com.android.chrome')).toBe('sideload');
    expect(classifyInstallSource('org.fdroid.fdroid')).toBe('sideload');
  });

  it('maps a missing installer to unknown', () => {
    expect(classifyInstallSource(null)).toBe('unknown');
    expect(classifyInstallSource(undefined)).toBe('unknown');
    expect(classifyInstallSource('')).toBe('unknown');
  });
});

describe('getPlayListingUrl', () => {
  it('points at this app’s Play listing', () => {
    expect(getPlayListingUrl()).toBe(
      'https://play.google.com/store/apps/details?id=com.oetwithdrhesham.app',
    );
  });
});

describe('getInstallSource', () => {
  it('returns unknown off the native shell without touching the bridge', async () => {
    // jsdom: Capacitor.isNativePlatform() is false, so this must resolve the
    // fallback and never attempt the native call.
    await expect(getInstallSource()).resolves.toEqual({
      kind: 'unknown',
      installerPackage: null,
      versionCode: null,
    });
  });
});
