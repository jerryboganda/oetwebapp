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
  const token = searchParams.get('token');
  const nextHref = searchParams.get('next');
  const [error, setError] = useState<string | null>(null);

  const subtitle = useMemo(() => {
    if (provider) {
      return `Finishing ${provider} sign-in and returning you to the OET auth flow.`;
    }

    return 'Finishing external sign-in and returning you to the OET auth flow.';
  }, [provider]);

  useEffect(() => {
    let cancelled = false;

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
  }, [nextHref, provider, router, token]);

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
