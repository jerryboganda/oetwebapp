'use client';

export const dynamic = 'force-dynamic';

import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { requestPasswordReset } from '@/lib/auth-client';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to send the reset OTP right now.';
}

export default function ForgotPasswordPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks('passwordReset');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [email, setEmail] = useState(searchParams.get('email') ?? '');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSubmitting(true);
    setErrorMessage(null);

    try {
      const normalizedEmail = email.trim();
      void requestPasswordReset(normalizedEmail).catch((error) => {
        console.error('Password reset OTP request failed.', error);
      });
      router.push(`${AUTH_ROUTES.passwordResetOtp}?email=${encodeURIComponent(normalizedEmail)}`);
    } catch (error) {
      setErrorMessage(readErrorMessage(error));
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
            We&apos;ll send a 6 digit reset code to this email address.
          </p>
        </div>

        {errorMessage ? (
          <p className={styles.fieldHint} style={{ color: '#c23d69' }}>
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
