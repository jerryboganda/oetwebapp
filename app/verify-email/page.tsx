'use client';

export const dynamic = 'force-dynamic';

import React, { Suspense, useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { sendEmailVerificationOtp, verifyEmailOtp as verifyEmailOtpRequest } from '@/lib/auth-client';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import { OtpCodeInput } from '@/components/auth/otp-code-input';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { useAuth } from '@/contexts/auth-context';
import { AUTH_ROUTES } from '@/lib/auth/routes';
import { loadStoredSession } from '@/lib/auth-storage';
import { readErrorMessage } from '@/lib/read-error-message';
import type { OtpChallenge } from '@/lib/types/auth';
import {
  clearAutoSendRequest,
  hasAutoSendRequested,
  markAutoSendRequested,
} from './auto-send-guard';

const VERIFY_EMAIL_CHALLENGE_KEY = 'oet.verify-email.challenge';

// Challenge state lives in LOCALSTORAGE, not sessionStorage. On Android/iOS the
// OS routinely kills the backgrounded WebView while the learner checks their
// mail app; Capacitor then cold-reloads the page on return and sessionStorage
// is wiped — which used to make this effect see "no challenge" and silently
// request a NEW OTP, invalidating the code sitting in the learner's inbox.
// localStorage survives WebView process death on both platforms, so the
// original OTP session stays active until it truly expires or the learner
// explicitly taps Resend.
function challengeStorageKey(email: string) {
  return `${VERIFY_EMAIL_CHALLENGE_KEY}:${email.trim().toLowerCase()}`;
}

function readStoredVerificationChallenge(email: string): Pick<OtpChallenge, 'destinationHint' | 'expiresAt'> | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    const raw = window.localStorage.getItem(challengeStorageKey(email));
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<OtpChallenge>;
    if (!parsed.expiresAt || Date.parse(parsed.expiresAt) <= Date.now()) {
      window.localStorage.removeItem(challengeStorageKey(email));
      return null;
    }

    return {
      destinationHint: parsed.destinationHint ?? '',
      expiresAt: parsed.expiresAt,
    };
  } catch {
    return null;
  }
}

function writeStoredVerificationChallenge(email: string, challenge: OtpChallenge) {
  if (typeof window === 'undefined') {
    return;
  }

  window.localStorage.setItem(
    challengeStorageKey(email),
    JSON.stringify({
      destinationHint: challenge.destinationHint,
      expiresAt: challenge.expiresAt,
    }),
  );
}

function clearStoredVerificationChallenge(email: string) {
  if (typeof window === 'undefined') {
    return;
  }

  try {
    window.localStorage.removeItem(challengeStorageKey(email));
  } catch {
    // Storage unavailable — nothing to clean up.
  }
}

export default function VerifyEmailPage() {
  return (
    <Suspense fallback={<VerifyEmailFallback />}>
      <VerifyEmailContent />
    </Suspense>
  );
}

function VerifyEmailFallback() {
  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Step Verification"
      title="Verify OTP"
      subtitle="Enter the 6 digit verification code sent to your account to continue."
    >
      <p className={styles.fieldHint}>Preparing verification...</p>
    </AuthScreenShell>
  );
}

function VerifyEmailContent() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { user, loading, verifyEmailOtp, refreshSession } = useAuth();
  const email =
    user?.email ?? searchParams?.get('email') ?? loadStoredSession()?.currentUser?.email ?? '';
  const nextHref = searchParams?.get('next') ?? null;
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [otp, setOtp] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  // Seconds until Resend unlocks. Mirrors the server's 60s cooldown so the
  // button never invites a request the backend will reject.
  const [resendIn, setResendIn] = useState(0);
  const requestedForEmail = React.useRef<string | null>(null);
  // Synchronous re-entrancy lock for submitOtp. `isSubmitting` state is
  // NOT enough: on mobile the auto-submit-on-6th-digit (below) can race the
  // on-screen numeric keypad's own implicit "Go"/"Done" form submission, and
  // a second submit can reach submitOtp before the first call's
  // setIsSubmitting(true) has actually committed and re-rendered. That raced
  // second request finds the OTP already consumed by the first and fails
  // with "invalid OTP" — even though the first request already verified the
  // account. A ref flips synchronously, so it closes that gap.
  const submitLockRef = React.useRef(false);

  useEffect(() => {
    if (resendIn <= 0) {
      return;
    }
    const timer = setTimeout(() => setResendIn((s) => s - 1), 1000);
    return () => clearTimeout(timer);
  }, [resendIn]);

  useEffect(() => {
    if (user?.isEmailVerified) {
      router.replace(resolveAuthenticatedDestination(user, nextHref));
      return;
    }

    if (!loading && !email) {
      router.replace(AUTH_ROUTES.signIn);
    }
  }, [email, loading, nextHref, router, user]);

  useEffect(() => {
    let cancelled = false;

    const sendCode = async () => {
      if (!email || user?.isEmailVerified || requestedForEmail.current === email) {
        return;
      }

      requestedForEmail.current = email;

      const stored = readStoredVerificationChallenge(email);
      if (stored) {
        if (!cancelled) {
          setNotice(
            `Enter the 6 digit verification code sent to ${stored.destinationHint || email}.`
          );
        }
        return;
      }

      // A challenge was already auto-sent for this email earlier in this
      // WebView session but its storage record is gone (private mode,
      // blocked storage, etc.). Do NOT silently rotate the OTP — the learner
      // may still have a valid code in their inbox. Show the notice and let
      // them use Resend explicitly if they truly need a new code. (The
      // backend additionally reuses any still-valid challenge when
      // forceNew is false.)
      if (hasAutoSendRequested(email)) {
        if (!cancelled) {
          setNotice(`Enter the 6 digit verification code sent to ${email}.`);
        }
        return;
      }

      try {
        markAutoSendRequested(email);
        const challenge = await sendEmailVerificationOtp(email);
        writeStoredVerificationChallenge(email, challenge);
        if (cancelled) {
          return;
        }

        if (user) {
          const refreshed = await refreshSession();
          if (cancelled) {
            return;
          }
          if (refreshed?.currentUser.isEmailVerified) {
            router.replace(resolveAuthenticatedDestination(refreshed.currentUser, nextHref));
            return;
          }
        }

        setNotice(
          `Enter the 6 digit verification code sent to ${challenge.destinationHint || email}.`
        );
      } catch (error) {
        requestedForEmail.current = null;
        clearAutoSendRequest();
        if (!cancelled) {
          setErrorMessage(readErrorMessage(error, 'Unable to verify the OTP code.'));
        }
      }
    };

    void sendCode();

    return () => {
      cancelled = true;
    };
  }, [email, nextHref, refreshSession, router, user, user?.isEmailVerified]);

  const startCooldown = () => setResendIn(60);

  const handleResend = async () => {
    if (resendIn > 0) {
      return;
    }

    setOtp('');
    setErrorMessage(null);

    try {
      const challenge = await sendEmailVerificationOtp(email, { forceNew: true });
      writeStoredVerificationChallenge(email, challenge);
      requestedForEmail.current = email;
      markAutoSendRequested(email);
      startCooldown();
      setNotice(
        `Enter the 6 digit verification code sent to ${challenge.destinationHint || email}.`
      );
    } catch (error) {
      // Server enforces the same 60s cooldown; surface its remaining time
      // instead of a generic failure so the learner knows exactly what to do.
      const message = readErrorMessage(error, 'Unable to verify the OTP code.');
      if (/cooldown|wait \d+ seconds/i.test(message)) {
        startCooldown();
        setNotice(message);
        return;
      }
      setErrorMessage(message);
    }
  };

  // Verifies the code that is PASSED IN — never the `otp` state. The auto-submit
  // path calls this from inside the OtpCodeInput change handler, where the
  // `setOtp(next)` for the 6th digit has NOT committed yet: reading state here
  // saw the previous render's 5-digit value, tripped the length guard below and
  // rendered a false "The OTP is invalid" without ever sending the request.
  const submitOtp = async (code: string) => {
    if (submitLockRef.current) {
      return;
    }

    // Take the lock BEFORE the length guard so the critical section starts at
    // the first statement instead of after an early return, and release it in
    // one finally. The invalid-length path has no await, so the lock is handed
    // back synchronously and the "Verify OTP" button still works immediately.
    submitLockRef.current = true;

    try {
      const normalizedOtp = code.replace(/\D/g, '');

      if (normalizedOtp.length !== 6) {
        setErrorMessage('The OTP is invalid. Enter the 6 digit verification code.');
        return;
      }

      setIsSubmitting(true);
      setErrorMessage(null);

      // Authenticated case (e.g. the dashboard banner's "Verify now" link):
      // go through AuthContext so its `user` state — and every component
      // reading it, like EmailVerificationBanner — reflects the new
      // isEmailVerified immediately, without needing a hard refresh.
      const currentUser = user
        ? await verifyEmailOtp(normalizedOtp)
        : await verifyEmailOtpRequest(email, normalizedOtp);

      // The challenge is consumed — drop its record so a later visit to this
      // screen for the same email starts clean instead of showing a stale
      // "code sent to …" notice.
      clearStoredVerificationChallenge(email);

      if (user) {
        router.replace(resolveAuthenticatedDestination(currentUser, nextHref));
        return;
      }

      const params = new URLSearchParams({ email });
      if (nextHref) {
        params.set('next', nextHref);
      }

      router.replace(`${AUTH_ROUTES.signIn}?${params.toString()}`);
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Unable to verify the OTP code.'));
    } finally {
      submitLockRef.current = false;
      setIsSubmitting(false);
    }
  };

  // Manual path: the button, and the physical Enter key on desktop. Here `otp`
  // IS the committed value, so passing it straight through is correct.
  const handleSubmit = (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    void submitOtp(otp);
  };

  if (loading || !email || user?.isEmailVerified) {
    return <VerifyEmailFallback />;
  }

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Step Verification"
      title="Verify OTP"
      subtitle={`Enter the 6 digit verification code sent to ${email} to continue into your account.`}
      footer={
        <p className={styles.resend}>
          Did not receive a code?{' '}
          {resendIn > 0 ? (
            <span className={styles.resendCountdown}>
              Resend available in {resendIn}s
            </span>
          ) : (
            <button
              type="button"
              className={styles.link}
              onClick={() => void handleResend()}
            >
              Resend it
            </button>
          )}
        </p>
      }
    >
      <form onSubmit={handleSubmit} className={styles.passwordFlowForm}>
        <OtpCodeInput
          value={otp}
          onChange={(value) => {
            const next = value.replace(/\D/g, '').slice(0, 6);
            // `otp` here is the PREVIOUS committed value. The same stale closure
            // that caused the bug is exactly what makes this a correct
            // "did the code actually change?" test.
            const previous = otp;
            setOtp(next);
            setErrorMessage(null);
            // Auto-submit the moment all six digits are in — no extra click.
            // Call submitOtp DIRECTLY with `next`: requestSubmit() dispatched a
            // nested synchronous submit event, and the handler on the other end
            // of it closed over the pre-update `otp` (5 digits), so this screen
            // showed "The OTP is invalid" and never sent the request.
            // `next !== previous` only suppresses a byte-identical re-fire — a
            // stricter "<6 → 6 transition" guard would block overwriting a digit
            // in place, stranding mobile learners whose Verify button is behind
            // the keypad.
            if (next.length === 6 && next !== previous && !isSubmitting) {
              void submitOtp(next);
            }
          }}
          length={6}
        />

        {notice ? (
          <p className={styles.fieldHint}>{notice}</p>
        ) : null}

        {errorMessage ? (
          <p role="alert" aria-live="assertive" className={`${styles.notice} ${styles.noticeDanger}`.trim()}>
            {errorMessage}
          </p>
        ) : null}

        <button className={styles.submit} type="submit" disabled={isSubmitting}>
          {isSubmitting ? 'Verifying...' : 'Verify OTP'}
        </button>

        <div className={styles.footer}>
          <Link className={styles.link} href={AUTH_ROUTES.signIn}>
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthScreenShell>
  );
}
