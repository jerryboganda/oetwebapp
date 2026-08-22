'use client';

export const dynamic = 'force-dynamic';

import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { requestPasswordReset } from '@/lib/auth-client';
import { obtainFirebaseOtpRecaptchaToken } from '@/lib/auth/firebase-otp-recaptcha';
import { persistPasswordResetOtp } from '@/lib/auth/password-reset-otp';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
import { useRuntimeConfig } from '@/app/providers/RuntimeConfigProvider';
import { readErrorMessage } from '@/lib/read-error-message';

export default function ForgotPasswordPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks('passwordReset');
  const firebaseOtp = useRuntimeConfig().firebaseOtp;
  const smsReady = firebaseOtp.enabled && firebaseOtp.smsEnabled;
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [email, setEmail] = useState(searchParams?.get('email') ?? '');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const normalizedEmail = email.trim();
      const recaptchaToken = await obtainFirebaseOtpRecaptchaToken();
      const challenge = await requestPasswordReset(normalizedEmail, { recaptchaToken });
      persistPasswordResetOtp(normalizedEmail, challenge);
      router.push(`${AUTH_ROUTES.passwordResetOtp}?email=${encodeURIComponent(normalizedEmail)}`);
    } catch (error) {
      setErrorMessage(readErrorMessage(error, 'Unable to send the reset OTP right now.'));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Recover Access"
      title="Find Your Account"
      subtitle="Step 1 of 3. Enter your email address so we can send a reset OTP and verify your identity first."
      footer={
        <>
          Remembered your password?{' '}
          <Link className={styles.link} href={flowLinks.primary}>
            Back to sign in
          </Link>
        </>
      }
    >
      <form action={AUTH_ROUTES.passwordResetOtp} method="get" onSubmit={handleSubmit} className={styles.passwordFlowForm}>
        <div className={styles.field}>
          <label htmlFor="email">Email Address</label>
          <input
            id="email"
            name="email"
            type="email"
            className={styles.input}
            placeholder="Enter your registered email"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
          <p className={styles.fieldHint}>
            {smsReady
              ? "We'll send a 6 digit reset code by SMS when a mobile number is on file, or by email if SMS is unavailable."
              : "We'll send a 6 digit reset code to this email address."}
          </p>
        </div>

        {errorMessage ? (
          <p className={styles.fieldError} role="alert" aria-live="polite">
            {errorMessage}
          </p>
        ) : null}

        <button
          className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? 'Sending OTP...' : 'Send OTP'}
        </button>

        <div className={`${styles.footer} ${styles.passwordFlowFooter}`.trim()}>
          <Link className={styles.link} href={flowLinks.primary}>
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthScreenShell>
  );
}
