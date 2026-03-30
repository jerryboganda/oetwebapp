'use client';

import { useAuth } from '@/contexts/auth-context';

export function useCurrentUser() {
  const { user, role, isAuthenticated, loading, pendingMfaChallenge } = useAuth();

  return {
    user,
    role,
    isAuthenticated,
    isLoading: loading,
    pendingMfaChallenge,
  };
}
