'use client';

export const dynamic = 'force-dynamic';

import React, { useState } from 'react';
import Link from 'next/link';
import { useRouter, useSearchParams } from 'next/navigation';
import { resetPassword } from '@/lib/auth-client';
import { PasswordField } from '@/components/auth/password-field';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';

const errorCopy: Record<string, string> = {
  'password-mismatch': 'Your passwords must match before you continue.',
  'password-too-short': 'Your new password must be at least 10 characters long.',
};

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to reset your password right now.';
}

export default function ResetPasswordPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const flowLinks = getAuthFlowLinks('passwordCreate');
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [email, setEmail] = useState(searchParams.get('email') ?? '');
  const [resetToken, setResetToken] = useState(searchParams.get('token') ?? '');
  const [newPassword, setNewPassword] = useState('');
  const [confirmPassword, setConfirmPassword] = useState('');
  const [error, setError] = useState<string | null>(null);
  const initialError = searchParams.get('error') ?? '';

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setError(null);

    if (newPassword !== confirmPassword) {
      setError(errorCopy['password-mismatch']);
      return;
    }

    if (newPassword.length < 10) {
      setError(errorCopy['password-too-short']);
      return;
    }

    setIsSubmitting(true);

    try {
      const normalizedEmail = email.trim();
      await resetPassword({
        email: normalizedEmail,
        resetToken: resetToken.trim(),
        newPassword,
      });

      const params = new URLSearchParams({ email: normalizedEmail });
      const nextPath = searchParams.get('next');
      if (nextPath) {
        params.set('next', nextPath);
      }

      router.push(`${AUTH_ROUTES.passwordResetSuccess}?${params.toString()}`);
    } catch (submitError) {
      setError(readErrorMessage(submitError));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Create New Password"
      title="Set Your New Password"
      subtitle={`Step 3 of 3. Create a fresh password for ${email || 'your account'} and return securely to your OET sign-in.`}
      footer={
        <>
          Need to go back?{' '}
          <Link className={styles.link} href={flowLinks.primary}>
            Return to sign in
          </Link>
        </>
      }
    >
      <form onSubmit={handleSubmit} className={styles.passwordFlowForm}>
        <div className={styles.field}>
          <label htmlFor="reset-email">Email</label>
          <input
            id="reset-email"
            type="email"
            className={styles.input}
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            required
          />
        </div>

        {/* Reset Code field is shown only when we don't already have a token
            from the URL (e.g. the learner skipped the verify step or arrived
            here directly). When the verify page redirected here the token is
            already in state; no need to re-expose it. Hidden field still
            submits the value. */}
        {searchParams.get('token') ? (
          <input type="hidden" value={resetToken} readOnly />
        ) : (
          <div className={styles.field}>
            <label htmlFor="reset-code">Reset Code</label>
            <input
              id="reset-code"
              className={styles.input}
              value={resetToken}
              onChange={(event) => setResetToken(event.target.value)}
              placeholder="Enter your 6 digit reset code"
              required
            />
          </div>
        )}

        <PasswordField
          id="newPassword"
          label="New Password"
          placeholder="Enter your new password"
          value={newPassword}
          onChange={(event) => setNewPassword(event.target.value)}
          required
        />

        <PasswordField
          id="confirmPassword"
          label="Confirm Password"
          placeholder="Confirm your new password"
          value={confirmPassword}
          onChange={(event) => setConfirmPassword(event.target.value)}
          required
        />

        {error || errorCopy[initialError] ? (
          <p className={styles.fieldError} role="alert" aria-live="polite">
            {error ?? errorCopy[initialError]}
          </p>
        ) : null}

        <button
          className={`${styles.submit} ${styles.passwordFlowSubmit}`.trim()}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? 'Resetting Password...' : 'Reset Password'}
        </button>

        <div className={`${styles.footer} ${styles.passwordFlowFooter}`.trim()}>
          <Link className={styles.link} href={AUTH_ROUTES.signIn}>
            Back to sign in
          </Link>
        </div>
      </form>
    </AuthScreenShell>
  );
}
