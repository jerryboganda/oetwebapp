'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useCurrentUser } from './use-current-user';
import { defaultRouteForRole } from '@/lib/auth-routes';

export function useSponsorAuth() {
  const router = useRouter();
  const { role, isAuthenticated, isLoading } = useCurrentUser();
  const resolvedRole = role ?? 'learner';

  useEffect(() => {
    if (isLoading) {
      return;
    }

    if (!isAuthenticated) {
      router.replace('/sign-in?next=/sponsor');
      return;
    }

    if (resolvedRole !== 'sponsor') {
      router.replace(defaultRouteForRole(resolvedRole));
    }
  }, [isAuthenticated, isLoading, resolvedRole, router]);

  return {
    role: resolvedRole,
    isAuthenticated,
    isLoading,
  };
}
