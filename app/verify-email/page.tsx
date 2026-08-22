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

const VERIFY_EMAIL_CHALLENGE_KEY = 'oet.verify-email.challenge';

function challengeStorageKey(email: string) {
  return `${VERIFY_EMAIL_CHALLENGE_KEY}:${email.trim().toLowerCase()}`;
}

function readStoredVerificationChallenge(email: string): Pick<OtpChallenge, 'destinationHint' | 'expiresAt'> | null {
  if (typeof window === 'undefined') {
    return null;
  }

  try {
    const raw = window.sessionStorage.getItem(challengeStorageKey(email));
    if (!raw) {
      return null;
    }

    const parsed = JSON.parse(raw) as Partial<OtpChallenge>;
    if (!parsed.expiresAt || Date.parse(parsed.expiresAt) <= Date.now()) {
      window.sessionStorage.removeItem(challengeStorageKey(email));
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

  window.sessionStorage.setItem(
    challengeStorageKey(email),
    JSON.stringify({
      destinationHint: challenge.destinationHint,
      expiresAt: challenge.expiresAt,
    }),
  );
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
  const requestedForEmail = React.useRef<string | null>(null);

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

      try {
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

  const handleResend = async () => {
    setOtp('');
    setErrorMessage(null);

    try {
      const challenge = await sendEmailVerificationOtp(email, { forceNew: true });
      writeStoredVerificationChallenge(email, challenge);
      requestedForEmail.current = email;
      setNotice(
        `Enter the 6 digit verification code sent to ${challenge.destinationHint || email}.`
      );
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Unable to verify the OTP code.'));
    }
  };

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    const normalizedOtp = otp.replace(/\D/g, '');

    if (normalizedOtp.length !== 6) {
      setErrorMessage('The OTP is invalid. Enter the 6 digit verification code.');
      return;
    }

    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      // Authenticated case (e.g. the dashboard banner's "Verify now" link):
      // go through AuthContext so its `user` state — and every component
      // reading it, like EmailVerificationBanner — reflects the new
      // isEmailVerified immediately, without needing a hard refresh.
      const currentUser = user
        ? await verifyEmailOtp(normalizedOtp)
        : await verifyEmailOtpRequest(email, normalizedOtp);

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
      setIsSubmitting(false);
    }
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
          <button
            type="button"
            className={styles.link}
            onClick={() => void handleResend()}
          >
            Resend it
          </button>
        </p>
      }
    >
      <form onSubmit={handleSubmit} className={styles.passwordFlowForm}>
        <OtpCodeInput
          value={otp}
          onChange={(value) => {
            setOtp(value.replace(/\D/g, '').slice(0, 6));
            setErrorMessage(null);
          }}
          length={6}
        />

        {notice ? (
          <p className={styles.fieldHint}>{notice}</p>
        ) : null}

        {errorMessage ? (
          <p className={`${styles.notice} ${styles.noticeDanger}`.trim()}>
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
