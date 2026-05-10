'use client';

import { useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { MfaChallengeForm } from '@/components/auth/mfa-challenge-form';
import { useAuth } from '@/contexts/auth-context';

export default function MfaChallengePage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { loading, pendingMfaChallenge, isAuthenticated, user } = useAuth();
  const nextHref = searchParams?.get('next') ?? null;

  useEffect(() => {
    if (loading) {
      return;
    }

    if (isAuthenticated && user) {
      router.replace(nextHref ?? '/');
      return;
    }

    if (!pendingMfaChallenge) {
      const nextQuery = nextHref ? `?next=${encodeURIComponent(nextHref)}` : '';
      router.replace(`/sign-in${nextQuery}`);
    }
  }, [isAuthenticated, loading, nextHref, pendingMfaChallenge, router, user]);

  return <MfaChallengeForm nextHref={nextHref} />;
}
