'use client';

import { useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { MfaSetupCard } from '@/components/auth/mfa-setup-card';
import { useAuth } from '@/contexts/auth-context';
import { resolvePostAuthDestination } from '@/lib/auth-routes';

export default function MfaSetupPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { loading, user, isAuthenticated } = useAuth();
  const nextHref = searchParams?.get('next') ?? null;

  useEffect(() => {
    if (loading) {
      return;
    }

    if (!isAuthenticated || !user) {
      const nextQuery = nextHref ? `?next=${encodeURIComponent(nextHref)}` : '';
      router.replace(`/sign-in${nextQuery}`);
      return;
    }

    if (user.isAuthenticatorEnabled) {
      router.replace(resolvePostAuthDestination(user, nextHref));
    }
  }, [isAuthenticated, loading, nextHref, router, user]);

  // Don't mount MfaSetupCard (which fires POST /v1/auth/mfa/authenticator/begin
  // in a mount-effect) while the redirect effect above is in flight. Otherwise
  // the begin-setup request races the redirect — on Firefox the request
  // completes mid-navigation and is rejected by the Next.js proxy CSRF gate
  // with 403 because the cookie-jar / x-csrf-token pair is out of sync during
  // the navigation. Also avoids rotating the server-side TOTP secret for users
  // who never intended to re-enroll.
  if (loading || !isAuthenticated || !user || user.isAuthenticatorEnabled) {
    return null;
  }

  return <MfaSetupCard nextHref={nextHref} />;
}
