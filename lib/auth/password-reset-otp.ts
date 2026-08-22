import type { OtpChallenge } from '@/lib/types/auth';

const STORAGE_KEY = 'oet_password_reset_otp';

export interface StoredPasswordResetOtp {
  email: string;
  deliveryChannel: string;
  destinationHint: string;
}

export function persistPasswordResetOtp(email: string, challenge: OtpChallenge): void {
  if (typeof window === 'undefined') {
    return;
  }

  const payload: StoredPasswordResetOtp = {
    email,
    deliveryChannel: challenge.deliveryChannel || 'email',
    destinationHint: challenge.destinationHint || email,
  };
  window.sessionStorage.setItem(STORAGE_KEY, JSON.stringify(payload));
}

export function loadPasswordResetOtp(email?: string | null): StoredPasswordResetOtp | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    const raw = window.sessionStorage.getItem(STORAGE_KEY);
    if (!raw) {
      return null;
    }
    const parsed = JSON.parse(raw) as StoredPasswordResetOtp;
    if (email && parsed.email && parsed.email !== email) {
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}
