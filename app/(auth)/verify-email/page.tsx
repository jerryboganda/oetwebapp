'use client';

export const dynamic = 'force-dynamic';

import React, { useEffect, useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { sendEmailVerificationOtp, verifyEmailOtp } from '@/lib/auth-client';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import { OtpCodeInput } from '@/components/auth/otp-code-input';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { useAuth } from '@/contexts/auth-context';
import { AUTH_ROUTES } from '@/lib/auth/routes';

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to verify the OTP code.';
}

export default function VerifyEmailPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { user } = useAuth();
  const email = user?.email ?? searchParams.get('email') ?? '';
  const nextHref = searchParams.get('next');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [otp, setOtp] = useState('');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  useEffect(() => {
    let cancelled = false;

    const sendCode = async () => {
      if (!email) {
        return;
      }

      try {
        const challenge = await sendEmailVerificationOtp(email);
        if (!cancelled) {
          setNotice(
            `Enter the 6 digit verification code sent to ${challenge.destinationHint || email}.`
          );
        }
      } catch (error) {
        if (!cancelled) {
          setErrorMessage(readErrorMessage(error));
        }
      }
    };

    void sendCode();

    return () => {
      cancelled = true;
    };
  }, [email]);

  const handleResend = async () => {
    setOtp('');
    setErrorMessage(null);

    try {
      const challenge = await sendEmailVerificationOtp(email);
      setNotice(
        `Enter the 6 digit verification code sent to ${challenge.destinationHint || email}.`
      );
    } catch (error) {
      setErrorMessage(readErrorMessage(error));
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
      const currentUser = await verifyEmailOtp(email, normalizedOtp);

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
      setErrorMessage(readErrorMessage(error));
    } finally {
      setIsSubmitting(false);
    }
  };

  if (!email) {
    return (
      <AuthScreenShell
        brandHref={AUTH_ROUTES.signIn}
        brandLabel="OET"
        eyebrow="Step Verification"
        title="Verify OTP"
        subtitle="Enter the 6 digit verification code sent to your account to continue."
      >
        <p className={`${styles.notice} ${styles.noticeDanger}`.trim()}>
          A valid email address is required before verification can continue.
        </p>
      </AuthScreenShell>
    );
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
