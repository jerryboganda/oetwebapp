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

  return <MfaSetupCard nextHref={nextHref} />;
}
