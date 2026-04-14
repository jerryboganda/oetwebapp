import { Capacitor } from '@capacitor/core';

interface BiometricResult {
  verified: boolean;
  error?: string;
}

interface NativeBiometricPlugin {
  checkBiometry(): Promise<{
    isAvailable: boolean;
    biometryType: 'TOUCH_ID' | 'FACE_ID' | 'FINGERPRINT' | 'IRIS' | 'NONE';
    reason?: string;
  }>;
  verifyIdentity(options: {
    reason?: string;
    title?: string;
    subtitle?: string;
    description?: string;
    negativeButtonText?: string;
    maxAttempts?: number;
    useFallback?: boolean;
  }): Promise<void>;
}

let biometricPlugin: NativeBiometricPlugin | null = null;

async function getBiometricPlugin(): Promise<NativeBiometricPlugin | null> {
  if (!Capacitor.isNativePlatform()) return null;

  if (biometricPlugin) return biometricPlugin;

  try {
    const mod = await import('@aparajita/capacitor-biometric-auth');
    biometricPlugin = mod.BiometricAuth as unknown as NativeBiometricPlugin;
    return biometricPlugin;
  } catch {
    return null;
  }
}

export async function isBiometricAvailable(): Promise<boolean> {
  const plugin = await getBiometricPlugin();
  if (!plugin) return false;

  try {
    const result = await plugin.checkBiometry();
    return result.isAvailable;
  } catch {
    return false;
  }
}

export async function getBiometryType(): Promise<string> {
  const plugin = await getBiometricPlugin();
  if (!plugin) return 'NONE';

  try {
    const result = await plugin.checkBiometry();
    return result.biometryType;
  } catch {
    return 'NONE';
  }
}

export async function authenticateWithBiometrics(
  reason = 'Verify your identity to access OET Prep'
): Promise<BiometricResult> {
  const plugin = await getBiometricPlugin();
  if (!plugin) {
    return { verified: false, error: 'Biometric authentication not available' };
  }

  try {
    await plugin.verifyIdentity({
      reason,
      title: 'OET Prep Authentication',
      subtitle: 'Biometric verification required',
      description: reason,
      negativeButtonText: 'Use Password',
      maxAttempts: 3,
      useFallback: true,
    });
    return { verified: true };
  } catch (error) {
    const message = error instanceof Error ? error.message : 'Biometric verification failed';
    return { verified: false, error: message };
  }
}

export async function biometricGateForSecureStorage(): Promise<boolean> {
  const available = await isBiometricAvailable();
  if (!available) return true; // Allow access if biometrics not available

  const result = await authenticateWithBiometrics(
    'Authenticate to access your secure credentials'
  );
  return result.verified;
}
