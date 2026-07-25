'use client';

import { type ReactNode, useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useAuth } from '@/contexts/auth-context';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';

interface AuthPageGateProps {
  children: ReactNode;
  nextHref?: string | null;
  mfaNextHref?: string | null;
}

export function AuthPageGate({ children, nextHref, mfaNextHref }: AuthPageGateProps) {
  const router = useRouter();
  const { loading, isAuthenticated, user, pendingMfaChallenge, pendingDeviceChallenge } = useAuth();

  useEffect(() => {
    if (loading) {
      return;
    }

    if (pendingMfaChallenge) {
      const nextValue = mfaNextHref ?? nextHref;
      const nextQuery = nextValue ? `?next=${encodeURIComponent(nextValue)}` : '';
      router.replace(`/mfa/challenge${nextQuery}`);
      return;
    }

    if (pendingDeviceChallenge) {
      const nextValue = mfaNextHref ?? nextHref;
      const nextQuery = nextValue ? `?next=${encodeURIComponent(nextValue)}` : '';
      router.replace(`/device/verify${nextQuery}`);
      return;
    }

    if (isAuthenticated && user) {
      router.replace(resolveAuthenticatedDestination(user, nextHref));
    }
  }, [isAuthenticated, loading, mfaNextHref, nextHref, pendingMfaChallenge, pendingDeviceChallenge, router, user]);

  return <>{children}</>;
}
