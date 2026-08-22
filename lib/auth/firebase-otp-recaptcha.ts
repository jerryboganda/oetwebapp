import { getRuntimeConfig } from '@/lib/runtime-config';

const CONTAINER_ID = 'oet-firebase-otp-recaptcha';

type RecaptchaVerifierLike = {
  verify: () => Promise<string>;
  clear: () => void;
};

let verifier: RecaptchaVerifierLike | null = null;

function ensureContainer(): HTMLElement {
  let node = document.getElementById(CONTAINER_ID);
  if (node) {
    return node;
  }

  node = document.createElement('div');
  node.id = CONTAINER_ID;
  node.setAttribute('aria-hidden', 'true');
  node.style.position = 'fixed';
  node.style.left = '-9999px';
  node.style.bottom = '0';
  node.style.width = '1px';
  node.style.height = '1px';
  node.style.overflow = 'hidden';
  document.body.appendChild(node);
  return node;
}

export function resetFirebaseOtpRecaptcha(): void {
  try {
    verifier?.clear();
  } catch {
    // The widget may already be gone after a navigation.
  }
  verifier = null;
}

/**
 * Invisible reCAPTCHA token for Firebase Phone Auth.
 * Returns null when SMS OTP is off, the browser cannot load Firebase, or
 * verification fails — callers then omit the token so the API falls back to
 * Brevo email.
 */
export async function obtainFirebaseOtpRecaptchaToken(): Promise<string | null> {
  if (typeof window === 'undefined' || typeof document === 'undefined') {
    return null;
  }

  const config = getRuntimeConfig().firebaseOtp;
  if (!config.enabled || !config.smsEnabled || !config.webKey || !config.projectId) {
    return null;
  }

  try {
    const [{ initializeApp, getApps, getApp }, { getAuth, RecaptchaVerifier }] = await Promise.all([
      import('firebase/app'),
      import('firebase/auth'),
    ]);

    const app = getApps().length > 0
      ? getApp()
      : initializeApp({
          apiKey: config.webKey,
          authDomain: config.authDomain ?? `${config.projectId}.firebaseapp.com`,
          projectId: config.projectId,
        });

    resetFirebaseOtpRecaptcha();
    const container = ensureContainer();
    const nextVerifier = new RecaptchaVerifier(getAuth(app), container, { size: 'invisible' });
    verifier = nextVerifier;
    const token = await nextVerifier.verify();
    return token.trim().length > 0 ? token : null;
  } catch {
    resetFirebaseOtpRecaptcha();
    return null;
  }
}
