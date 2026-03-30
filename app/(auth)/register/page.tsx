'use client';

import { useEffect } from 'react';
import { useRouter } from 'next/navigation';
import { RegisterForm } from '@/components/auth/register-form';
import { useAuth } from '@/contexts/auth-context';
import { resolveAuthenticatedDestination } from '@/lib/auth-routes';

export default function RegisterPage() {
  const router = useRouter();
  const { loading, isAuthenticated, user, pendingMfaChallenge } = useAuth();

  useEffect(() => {
    if (loading) {
      return;
    }

    if (pendingMfaChallenge) {
      router.replace('/mfa/challenge');
      return;
    }

    if (isAuthenticated && user) {
      router.replace(resolveAuthenticatedDestination(user));
    }
  }, [isAuthenticated, loading, pendingMfaChallenge, router, user]);

  return <RegisterForm />;
}
