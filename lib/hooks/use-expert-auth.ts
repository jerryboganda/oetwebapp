import { useEffect, useState } from 'react';
import { useRouter } from 'next/navigation';
import { ApiError, fetchExpertMe, isApiError } from '@/lib/api';
import type { ExpertMe } from '@/lib/types/expert';

export interface UserRole {
  role: 'learner' | 'expert' | 'admin';
  isAuthenticated: boolean;
}

interface ExpertAuthState extends UserRole {
  expert: ExpertMe | null;
  error: ApiError | null;
}

export function useExpertAuth() {
  const router = useRouter();
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
      setIsLoading(true);
      try {
        const expert = await fetchExpertMe();
        if (cancelled) return;
        setAuthState({
          role: 'expert',
          isAuthenticated: true,
          expert,
          error: null,
        });
      } catch (error) {
        if (cancelled) return;

        const apiError = isApiError(error)
          ? error
          : new ApiError(0, 'expert_auth_failed', 'Unable to verify expert access.', false);

        setAuthState({
          role: 'learner',
          isAuthenticated: false,
          expert: null,
          error: apiError,
        });

        if (apiError.status === 401 || apiError.status === 403) {
          router.replace('/');
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
  }, [router]);

  return {
    ...authState,
    isLoading,
  };
}
