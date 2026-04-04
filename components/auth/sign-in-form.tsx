'use client';

import React, { useMemo, useState } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import {
  IconBrandFacebook,
  IconBrandGoogle,
  IconBrandLinkedin,
} from '@tabler/icons-react';
import AuthModeSwitch from '@/components/auth/auth-mode-switch';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import { PasswordField } from '@/components/auth/password-field';
import { useAuth } from '@/contexts/auth-context';
import { buildExternalAuthStartHref } from '@/lib/auth-client';
import { useSignupCatalog } from '@/lib/hooks/use-signup-catalog';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';
import { AUTH_ROUTES, getAuthFlowLinks } from '@/lib/auth/routes';
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
  const { externalAuthProviders = [] } = useSignupCatalog();
  const flowLinks = getAuthFlowLinks('signIn');
  const [email, setEmail] = useState(initialEmail ?? '');
  const [password, setPassword] = useState('');
  const [rememberMe, setRememberMe] = useState(false);
  const [error, setError] = useState<string | null>(resolveExternalErrorMessage(externalError));
  const [isSubmitting, setIsSubmitting] = useState(false);
  const [desktopRuntimeInfo, setDesktopRuntimeInfo] = useState<Awaited<ReturnType<NonNullable<typeof window.desktopBridge>['runtime']['info']>> | null>(null);

  React.useEffect(() => {
    let cancelled = false;

    if (!window.desktopBridge?.runtime?.info) {
      return () => {
        cancelled = true;
      };
    }

    void window.desktopBridge.runtime.info().then((runtimeInfo) => {
      if (!cancelled) {
        setDesktopRuntimeInfo(runtimeInfo);
      }
    }).catch(() => {
      if (!cancelled) {
        setDesktopRuntimeInfo(null);
      }
    });

    return () => {
      cancelled = true;
    };
  }, []);

  const socials = useMemo(
    () =>
      externalAuthProviders.map((provider) => ({
        href: buildExternalAuthStartHref(provider, nextHref),
        label:
          provider === 'facebook'
            ? 'Sign in with Facebook'
            : provider === 'google'
              ? 'Sign in with Google'
              : 'Sign in with LinkedIn',
        icon:
          provider === 'facebook' ? (
            <IconBrandFacebook size={18} />
          ) : provider === 'google' ? (
            <IconBrandGoogle size={18} />
          ) : (
            <IconBrandLinkedin size={18} />
          ),
      })),
    [externalAuthProviders, nextHref],
  );

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

      let nextError = readErrorMessage(authError);
      if (
        readErrorCode(authError) === 'invalid_credentials'
        && desktopRuntimeInfo?.isPackaged
        && desktopRuntimeInfo.activeBackendUrl
        && /^https?:\/\/(?:127\.0\.0\.1|localhost|\[::1\])/i.test(desktopRuntimeInfo.activeBackendUrl)
      ) {
        nextError = `${nextError} This desktop build is currently using the local auth server (${desktopRuntimeInfo.activeBackendUrl}), so credentials from your live web account will not work here.`;
      }

      setError(nextError);
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
          <label htmlFor="email">Email address</label>
          <input
            id="email"
            name="email"
            type="email"
            className={styles.input}
            placeholder="name@example.com"
            value={email}
            onChange={(event) => setEmail(event.target.value)}
            autoComplete="email"
            inputMode="email"
            spellCheck={false}
            required
          />
          <p className={styles.fieldHint}>
            We&apos;ll never share your email with anyone else.
          </p>
        </div>

        <div className={styles.signInPasswordField}>
          <PasswordField
            id="password"
            name="password"
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
          {isSubmitting ? 'Signing In…' : 'Sign In'}
        </button>
      </form>
    </AuthScreenShell>
  );
}
