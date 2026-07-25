'use client';

import { useEffect } from 'react';
import { useRouter, useSearchParams } from 'next/navigation';
import { DeviceChallengeForm } from '@/components/auth/device-challenge-form';
import { useAuth } from '@/contexts/auth-context';

export default function DeviceVerifyPage() {
  const router = useRouter();
  const searchParams = useSearchParams();
  const { loading, pendingDeviceChallenge, isAuthenticated, user } = useAuth();
  const nextHref = searchParams?.get('next') ?? null;

  useEffect(() => {
    if (loading) {
      return;
    }

    if (isAuthenticated && user) {
      router.replace(nextHref ?? '/');
      return;
    }

    if (!pendingDeviceChallenge) {
      const nextQuery = nextHref ? `?next=${encodeURIComponent(nextHref)}` : '';
      router.replace(`/sign-in${nextQuery}`);
    }
  }, [isAuthenticated, loading, nextHref, pendingDeviceChallenge, router, user]);

  return <DeviceChallengeForm nextHref={nextHref} />;
}
