'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { useCurrentUser } from './use-current-user';
import { defaultRouteForRole } from '@/lib/auth-routes';
export function useAdminAuth() {
  const router = useRouter();
  const { role, isAuthenticated, isLoading } = useCurrentUser();
  const resolvedRole = role ?? 'learner';

  useEffect(() => {
    if (isLoading) {
      return;
    }

    if (!isAuthenticated) {
      router.replace('/sign-in?next=/admin');
      return;
    }

    if (resolvedRole !== 'admin') {
      router.replace(defaultRouteForRole(resolvedRole));
    }
  }, [isAuthenticated, isLoading, resolvedRole, router]);

  const requireAdminAccess = () => {
    if (isLoading) {
      return;
    }

    if (!isAuthenticated) {
      router.push('/sign-in?next=/admin');
      return;
    }

    if (resolvedRole !== 'admin') {
      router.push(defaultRouteForRole(resolvedRole));
      return;
    }
  };

  return {
    role: resolvedRole,
    isAuthenticated,
    isLoading,
    requireAdminAccess,
  };
}
