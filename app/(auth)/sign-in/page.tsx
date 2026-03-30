'use client';

import { useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { SignInForm } from '@/components/auth/sign-in-form';
import { useAuth } from '@/contexts/auth-context';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';

export default function SignInPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { loading, isAuthenticated, user, pendingMfaChallenge } = useAuth();
  const nextHref = searchParams.get('next');
  const initialEmail = searchParams.get('email');
  const externalError = searchParams.get('externalError');

  useEffect(() => {
    if (loading) {
      return;
    }

    if (pendingMfaChallenge) {
      const nextQuery = nextHref ? `?next=${encodeURIComponent(nextHref)}` : '';
      router.replace(`/mfa/challenge${nextQuery}`);
      return;
    }

    if (isAuthenticated && user) {
      router.replace(resolveAuthenticatedDestination(user, nextHref));
    }
  }, [isAuthenticated, loading, nextHref, pendingMfaChallenge, router, user]);

  return <SignInForm nextHref={nextHref} initialEmail={initialEmail} externalError={externalError} />;
}
