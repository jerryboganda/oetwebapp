'use client';

import { useEffect, useMemo, useState } from 'react';
import { useParams, useRouter, useSearchParams } from 'next/navigation';
import { exchangeExternalAuth } from '@/lib/auth-client';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';
import { AuthScreenShell } from '@/components/auth/auth-screen-shell';
import styles from '@/components/auth/auth-screen-shell.module.scss';
import type { ExternalAuthProvider } from '@/lib/types/auth';

function readProvider(value: string | string[] | undefined): ExternalAuthProvider | null {
  if (Array.isArray(value)) {
    return readProvider(value[0]);
  }

  if (value === 'google' || value === 'facebook' || value === 'linkedin') {
    return value;
  }

  return null;
}

export default function ExternalAuthCallbackPage() {
  const params = useParams<{ provider: string }>();
  const router = useRouter();
  const searchParams = useSearchParams();
  const provider = readProvider(params?.provider);
  const [token, setToken] = useState<string | null>(null);
  const [tokenReady, setTokenReady] = useState(false);
  const nextHref = searchParams?.get('next') ?? null;
  const [error, setError] = useState<string | null>(null);

  // Security (H11): read the exchange token from the URL fragment (#token=...) rather than
  // the query string. Fragments are never sent to servers (no Referer leak, no access-log leak,
  // no server-side proxy leak). Immediately strip the fragment via history.replaceState before
  // any async work so the token disappears from the address bar, browser history entry, and
  // window.location.hash for any downstream listeners. Backward-compatibility: if a token is
  // still present in the query string (stale bookmark, older backend), we accept it and strip
  // it from the URL the same way. This can be removed after one deploy cycle.
  useEffect(() => {
    if (typeof window === 'undefined') {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- SSR guard only; runs once before hydration
      setTokenReady(true);
      return;
    }

    const hash = window.location.hash.startsWith('#') ? window.location.hash.slice(1) : window.location.hash;
    const hashParams = new URLSearchParams(hash);
    const hashToken = hashParams.get('token');

    const queryToken = searchParams?.get('token');

    const resolved = hashToken ?? queryToken ?? null;
    setToken(resolved);

    if (hashToken || queryToken) {
      // Strip both hash and ?token= from the visible URL; preserve ?next= if present.
      const url = new URL(window.location.href);
      url.hash = '';
      if (url.searchParams.has('token')) {
        url.searchParams.delete('token');
      }
      window.history.replaceState(null, '', url.toString());
    }

    setTokenReady(true);
  }, [searchParams]);

  const subtitle = useMemo(() => {
    if (provider) {
      return `Finishing ${provider} sign-in and returning you to the OET auth flow.`;
    }

    return 'Finishing external sign-in and returning you to the OET auth flow.';
  }, [provider]);

  useEffect(() => {
    let cancelled = false;

    // Wait for the fragment-stripping effect to run before deciding whether the token is missing.
    if (!tokenReady) {
      return;
    }

    const completeExchange = async () => {
      if (!provider || !token) {
        setError('The external authentication callback is missing required data.');
        return;
      }

      try {
        const result = await exchangeExternalAuth(provider, token);
        if (cancelled) {
          return;
        }

        if (result.status === 'authenticated' && result.session) {
          router.replace(resolveAuthenticatedDestination(result.session.currentUser, nextHref));
          return;
        }

        if (result.status === 'registration_required' && result.registration) {
          const query = new URLSearchParams({
            registrationToken: result.registration.registrationToken,
            email: result.registration.email,
          });

          if (result.registration.firstName) {
            query.set('firstName', result.registration.firstName);
          }
          if (result.registration.lastName) {
            query.set('lastName', result.registration.lastName);
          }
          if (result.registration.nextPath || nextHref) {
            query.set('next', result.registration.nextPath ?? nextHref ?? '');
          }

          router.replace(`/register?${query.toString()}`);
          return;
        }

        setError('The external sign-in response was incomplete.');
      } catch (exchangeError) {
        if (!cancelled) {
          if (exchangeError && typeof exchangeError === 'object' && 'message' in exchangeError && typeof exchangeError.message === 'string') {
            setError(exchangeError.message);
          } else {
            setError('The external sign-in request could not be completed.');
          }
        }
      }
    };

    void completeExchange();

    return () => {
      cancelled = true;
    };
  }, [nextHref, provider, router, token, tokenReady]);

  return (
    <AuthScreenShell
      brandHref="/sign-in"
      brandLabel="OET"
      eyebrow="Social Sign-In"
      title="Completing Sign-In"
      subtitle={subtitle}
    >
      <div className={styles.passwordFlowForm}>
        {error ? (
          <div className={`${styles.notice} ${styles.noticeDanger}`.trim()}>
            {error}
          </div>
        ) : (
          <div className={`${styles.notice} ${styles.noticeSuccess}`.trim()}>
            Please wait while we complete your external sign-in.
          </div>
        )}
      </div>
    </AuthScreenShell>
  );
}
