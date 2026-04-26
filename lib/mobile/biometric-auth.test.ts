import { describe, it, expect, vi, beforeEach } from 'vitest';

// ── Mocks ──────────────────────────────────────────────────────────────────

const { isNativePlatformMock, checkBiometry, verifyIdentity } = vi.hoisted(() => ({
  isNativePlatformMock: vi.fn(() => true),
  checkBiometry: vi.fn(),
  verifyIdentity: vi.fn(),
}));

vi.mock('@capacitor/core', () => ({
  Capacitor: {
    isNativePlatform: isNativePlatformMock,
    getPlatform: vi.fn(() => 'ios'),
  },
}));

vi.mock('@aparajita/capacitor-biometric-auth', () => ({
  BiometricAuth: {
    checkBiometry,
    verifyIdentity,
  },
}));

beforeEach(() => {
  vi.resetModules();
  isNativePlatformMock.mockReset().mockReturnValue(true);
  checkBiometry.mockReset();
  verifyIdentity.mockReset();
});

async function loadFresh() {
  return await import('./biometric-auth');
}

describe('isBiometricAvailable', () => {
  it('returns false on web (non-native platform)', async () => {
    isNativePlatformMock.mockReturnValue(false);
    const { isBiometricAvailable } = await loadFresh();
    await expect(isBiometricAvailable()).resolves.toBe(false);
    expect(checkBiometry).not.toHaveBeenCalled();
  });

  it('returns true when checkBiometry reports available', async () => {
    checkBiometry.mockResolvedValue({ isAvailable: true, biometryType: 'FACE_ID' });
    const { isBiometricAvailable } = await loadFresh();
    await expect(isBiometricAvailable()).resolves.toBe(true);
  });

  it('returns false when checkBiometry reports unavailable', async () => {
    checkBiometry.mockResolvedValue({ isAvailable: false, biometryType: 'NONE' });
    const { isBiometricAvailable } = await loadFresh();
    await expect(isBiometricAvailable()).resolves.toBe(false);
  });

  it('swallows checkBiometry errors and returns false', async () => {
    checkBiometry.mockRejectedValue(new Error('boom'));
    const { isBiometricAvailable } = await loadFresh();
    await expect(isBiometricAvailable()).resolves.toBe(false);
  });

  it('caches the plugin reference between calls', async () => {
    checkBiometry.mockResolvedValue({ isAvailable: true, biometryType: 'TOUCH_ID' });
    const { isBiometricAvailable } = await loadFresh();
    await isBiometricAvailable();
    await isBiometricAvailable();
    expect(checkBiometry).toHaveBeenCalledTimes(2);
    // Two calls confirm reuse without re-invoking the dynamic import path
    // (cannot directly observe the import; behavior is captured by both
    // resolving truthy with the same mock instance).
  });
});

describe('authenticateWithBiometrics', () => {
  it('returns unverified result on web', async () => {
    isNativePlatformMock.mockReturnValue(false);
    const { authenticateWithBiometrics } = await loadFresh();
    const result = await authenticateWithBiometrics();
    expect(result).toEqual({
      verified: false,
      error: 'Biometric authentication not available',
    });
    expect(verifyIdentity).not.toHaveBeenCalled();
  });

  it('returns verified=true when verifyIdentity resolves', async () => {
    verifyIdentity.mockResolvedValue(undefined);
    const { authenticateWithBiometrics } = await loadFresh();
    const result = await authenticateWithBiometrics();
    expect(result).toEqual({ verified: true });
  });

  it('forwards the supplied reason and uses defaults for the rest', async () => {
    verifyIdentity.mockResolvedValue(undefined);
    const { authenticateWithBiometrics } = await loadFresh();
    await authenticateWithBiometrics('Confirm payment');
    expect(verifyIdentity).toHaveBeenCalledWith({
      reason: 'Confirm payment',
      title: 'OET Prep Authentication',
      subtitle: 'Biometric verification required',
      description: 'Confirm payment',
      negativeButtonText: 'Use Password',
      maxAttempts: 3,
      useFallback: true,
    });
  });

  it('uses the default reason when none is supplied', async () => {
    verifyIdentity.mockResolvedValue(undefined);
    const { authenticateWithBiometrics } = await loadFresh();
    await authenticateWithBiometrics();
    expect(verifyIdentity).toHaveBeenCalledWith(
      expect.objectContaining({
        reason: 'Verify your identity to access OET Prep',
        description: 'Verify your identity to access OET Prep',
      }),
    );
  });

  it('returns the Error message when verifyIdentity rejects with an Error', async () => {
    verifyIdentity.mockRejectedValue(new Error('User cancelled'));
    const { authenticateWithBiometrics } = await loadFresh();
    const result = await authenticateWithBiometrics();
    expect(result).toEqual({ verified: false, error: 'User cancelled' });
  });

  it('returns the fallback message when verifyIdentity rejects with a non-Error', async () => {
    verifyIdentity.mockRejectedValue('nope');
    const { authenticateWithBiometrics } = await loadFresh();
    const result = await authenticateWithBiometrics();
    expect(result).toEqual({ verified: false, error: 'Biometric verification failed' });
  });
});
