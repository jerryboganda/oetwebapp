'use client';

import { useEffect } from 'react';
import { usePathname, useRouter } from 'next/navigation';
import { Lock } from 'lucide-react';
import type { ReactNode } from 'react';
import type { UserRole } from '@/lib/types/auth';
import { useAuth } from '@/contexts/auth-context';
import { defaultRouteForRole } from '@/lib/auth-routes';

interface AuthGuardProps {
  children: ReactNode;
  requiredRole?: UserRole;
}

export function AuthGuard({ children, requiredRole }: AuthGuardProps) {
  const router = useRouter();
  const pathname = usePathname();
  const { loading, isAuthenticated, role, pendingMfaChallenge } = useAuth();
  const nextPath = pathname ?? '/';
  const isAuthRoute =
    nextPath === '/sign-in' ||
    nextPath === '/register' ||
    nextPath === '/register/success' ||
    nextPath === '/terms' ||
    nextPath === '/forgot-password' ||
    nextPath === '/forgot-password/verify' ||
    nextPath === '/reset-password' ||
    nextPath === '/reset-password/success' ||
    nextPath === '/verify-email' ||
    nextPath === '/mfa/challenge' ||
    nextPath === '/mfa/setup' ||
    nextPath.startsWith('/auth/callback/');

  useEffect(() => {
    if (isAuthRoute) {
      return;
    }

    if (loading) {
      return;
    }

    if (pendingMfaChallenge) {
      router.replace(`/mfa/challenge?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (!isAuthenticated) {
      router.replace(`/sign-in?next=${encodeURIComponent(nextPath)}`);
      return;
    }

    if (requiredRole && role !== requiredRole) {
      router.replace(role ? defaultRouteForRole(role) : '/');
    }
  }, [isAuthenticated, isAuthRoute, loading, nextPath, pendingMfaChallenge, requiredRole, role, router]);

  if (isAuthRoute) {
    return <>{children}</>;
  }

  if (loading || pendingMfaChallenge || !isAuthenticated || (requiredRole && role !== requiredRole)) {
    return (
      <div className="flex min-h-screen w-full items-center justify-center bg-background-light px-6">
        <div className="flex flex-col items-center gap-4 text-center text-muted">
          <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Lock className="h-6 w-6 animate-pulse" />
          </div>
          <div className="space-y-1">
            <p className="text-sm font-semibold text-navy">Checking your session</p>
            <p className="text-xs text-muted">We&apos;re routing you to the correct workspace.</p>
          </div>
        </div>
      </div>
    );
  }

  return <>{children}</>;
}
