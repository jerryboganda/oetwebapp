'use client';

import React, { useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  IconBrandFacebook,
  IconBrandGoogle,
  IconBrandLinkedin,
} from '@tabler/icons-react';
import { useAuth } from '@/contexts/auth-context';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';
import { buildExternalAuthStartHref } from '@/lib/auth-client';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
import AuthModeSwitch from '@/components/auth/auth-mode-switch';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import { PasswordField } from '@/components/auth/password-field';
import styles from '@/components/auth/auth-screen-shell.module.scss';

interface SignInFormProps {
  nextHref?: string | null;
  initialEmail?: string | null;
  externalError?: string | null;
}

function readErrorCode(error: unknown): string | null {
  if (!error || typeof error !== 'object' || !('code' in error)) {
    return null;
  }

  return String(error.code);
}

function readErrorMessage(error: unknown): string {
  if (error && typeof error === 'object' && 'message' in error && typeof error.message === 'string') {
    return error.message;
  }

  return 'Unable to complete the authentication request.';
}

function resolveExternalErrorMessage(errorCode?: string | null): string | null {
  switch (errorCode) {
    case 'external_auth_cancelled':
      return 'The social sign-in flow was cancelled before completion.';
    case 'external_auth_not_configured':
      return 'This social sign-in provider is not configured yet.';
    case 'external_auth_role_not_supported':
      return 'Social sign-in is available for learner self-service accounts only.';
    case 'account_suspended':
      return 'This account is not available for sign-in.';
    case 'external_auth_failed':
      return 'The social sign-in request could not be completed.';
    default:
      return errorCode ? 'The social sign-in request could not be completed.' : null;
  }
}

export function SignInForm({ nextHref, initialEmail, externalError }: SignInFormProps) {
  const router = useRouter();
  const { signIn } = useAuth();
  const flowLinks = getAuthFlowLinks('signIn');
  const [email, setEmail] = useState(initialEmail ?? '');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [error, setError] = useState<string | null>(resolveExternalErrorMessage(externalError));
  const [isSubmitting, setIsSubmitting] = useState(false);

  const socials = useMemo(() => ([
    {
      href: buildExternalAuthStartHref('facebook', nextHref),
      label: 'Sign in with Facebook',
      icon: <IconBrandFacebook size={18} />,
    },
    {
      href: buildExternalAuthStartHref('google', nextHref),
      label: 'Sign in with Google',
      icon: <IconBrandGoogle size={18} />,
    },
    {
      href: buildExternalAuthStartHref('linkedin', nextHref),
      label: 'Sign in with LinkedIn',
      icon: <IconBrandLinkedin size={18} />,
    },
  ]), [nextHref]);

  const handleSubmit = async (event: React.FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    setIsSubmitting(true);
    setError(null);

    try {
      const result = await signIn(email.trim(), password, rememberMe);

      if (result.status === 'mfa_required') {
        const nextQuery = nextHref ? `?next=${encodeURIComponent(nextHref)}` : '';
        router.replace(`/mfa/challenge${nextQuery}`);
        return;
      }

      router.replace(resolveAuthenticatedDestination(result.session.currentUser, nextHref));
    } catch (authError) {
      if (readErrorCode(authError) === 'email_verification_required') {
        const query = new URLSearchParams({ email: email.trim() });
        if (nextHref) {
          query.set('next', nextHref);
        }

        router.replace(`/verify-email?${query.toString()}`);
        return;
      }

      setError(readErrorMessage(authError));
    } finally {
      setIsSubmitting(false);
    }
  };

  return (
    <AuthScreenShell
      brandHref={AUTH_ROUTES.signIn}
      brandLabel="OET"
      eyebrow="Welcome Back"
      title="Login to your Account"
      footer={
        <>
          New here?{' '}
          <Link className={styles.link} href={flowLinks.primary}>
            Create an account
          </Link>
        </>
      }
      terms={
        <Link className={styles.link} href={AUTH_ROUTES.terms}>
          Terms of use &amp; Conditions
        </Link>
      }
      socials={socials}
    >
      <form onSubmit={handleSubmit} className={styles.signInForm}>
        <AuthModeSwitch mode="signIn" />

        <div className={styles.field}>
          <label htmlFor="username">Email address</label>
          <input
            id="username"
            type="email"
            className={styles.input}
            placeholder="Enter your email address"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            required
          />
          <p className={styles.fieldHint}>
            We&apos;ll never share your email with anyone else.
          </p>
        </div>

        <div className={styles.signInPasswordField}>
          <PasswordField
            id="password"
            label="Password"
            placeholder="Enter your password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
            autoComplete="current-password"
            required
          />
        </div>

        <div className={`${styles.metaRow} ${styles.signInMetaRow}`.trim()}>
          <label className={styles.checkbox} htmlFor="remember">
            <input
              id="remember"
              type="checkbox"
              checked={rememberMe}
              onChange={(event) => setRememberMe(event.target.checked)}
            />
            <span>Remember Me</span>
          </label>

          <Link className={styles.link} href={flowLinks.secondary}>
            Forgot password?
          </Link>
        </div>

        {error ? (
          <p className={`${styles.notice} ${styles.noticeDanger}`.trim()}>
            {error}
          </p>
        ) : null}

        <button
          className={`${styles.submit} ${styles.signInSubmit}`.trim()}
          type="submit"
          disabled={isSubmitting}
        >
          {isSubmitting ? 'Submitting...' : 'Submit'}
        </button>
      </form>
    </AuthScreenShell>
  );
}
