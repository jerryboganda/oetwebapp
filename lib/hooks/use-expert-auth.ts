'use client';

import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ApiError, fetchExpertMe, isApiError } from '@/lib/api';
import type { ExpertMe } from '@/lib/types/expert';
import { useCurrentUser } from './use-current-user';
import { defaultRouteForRole } from '@/lib/auth-routes';

export interface UserRole {
  role: 'learner' | 'expert' | 'admin' | 'sponsor';
  isAuthenticated: boolean;
}

interface ExpertAuthState extends UserRole {
  expert: ExpertMe | null;
  error: ApiError | null;
}

export function useExpertAuth() {
  const router = useRouter();
  const { role, isAuthenticated, isLoading: isUserLoading } = useCurrentUser();
  const [authState, setAuthState] = useState<ExpertAuthState>({
    role: 'learner',
    isAuthenticated: false,
    expert: null,
    error: null,
  });
  const [isLoading, setIsLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    const verifyAuth = async () => {
      if (isUserLoading) {
        setIsLoading(true);
        return;
      }

      if (!isAuthenticated) {
        setAuthState({
          role: 'learner',
          isAuthenticated: false,
          expert: null,
          error: new ApiError(401, 'not_authenticated', 'Please sign in to continue.', false),
        });
        setIsLoading(false);
        router.replace('/sign-in?next=/expert');
        return;
      }

      if (role !== 'expert') {
        setAuthState({
          role: role ?? 'learner',
          isAuthenticated: false,
          expert: null,
          error: new ApiError(403, 'forbidden', 'You do not have permission to access the expert console.', false),
        });
        setIsLoading(false);
        router.replace(defaultRouteForRole(role ?? 'learner'));
        return;
      }

      setIsLoading(true);

      try {
        const expert = await fetchExpertMe();
        if (cancelled) {
          return;
        }

        // Verify expert account is still active (mirrors backend ExpertOnly policy)
        if (expert && 'isActive' in expert && expert.isActive === false) {
          setAuthState({
            role: 'expert',
            isAuthenticated: false,
            expert: null,
            error: new ApiError(403, 'expert_deactivated', 'Your expert account has been deactivated. Please contact support.', false),
          });
          setIsLoading(false);
          router.replace(defaultRouteForRole('learner'));
          return;
        }

        setAuthState({
          role: 'expert',
          isAuthenticated: true,
          expert,
          error: null,
        });
      } catch (error) {
        if (cancelled) {
          return;
        }

        const apiError = isApiError(error)
          ? error
          : new ApiError(0, 'expert_auth_failed', 'Unable to verify expert access.', false);

        setAuthState({
          role: 'learner',
          isAuthenticated: false,
          expert: null,
          error: apiError,
        });

        if (apiError.status === 401) {
          router.replace('/sign-in?next=/expert');
          return;
        }

        if (apiError.status === 403) {
          router.replace(defaultRouteForRole('learner'));
        }
      } finally {
        if (!cancelled) {
          setIsLoading(false);
        }
      }
    };

    void verifyAuth();
    return () => {
      cancelled = true;
    };
  }, [isAuthenticated, isUserLoading, role, router]);

  return {
    ...authState,
    isLoading,
  };
}
