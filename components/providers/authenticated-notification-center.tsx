'use client';

import dynamic from 'next/dynamic';
import { usePathname } from 'next/navigation';
import type { ReactNode } from 'react';
import { useAuth } from '@/contexts/auth-context';

const DynamicWorkspaceProviders = dynamic<{ children: ReactNode }>(
  () => import('./authenticated-workspace-providers').then((module) => module.AuthenticatedWorkspaceProviders),
  { ssr: false },
);

const AUTH_ROUTE_PREFIXES = [
  '/sign-in',
  '/register',
  '/forgot-password',
  '/reset-password',
  '/verify-email',
  '/mfa',
  '/auth',
] as const;

function isAuthRoute(pathname: string | null): boolean {
  if (!pathname) return false;

  return AUTH_ROUTE_PREFIXES.some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`),
  );
}

/**
 * Keeps authenticated workspace providers above page-local shells while avoiding
 * notification/SignalR and product-tour chunks on public/auth routes.
 */
export function AuthenticatedNotificationCenter({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated || isAuthRoute(pathname)) {
    return <>{children}</>;
  }

  return (
    <DynamicWorkspaceProviders>
      {children}
    </DynamicWorkspaceProviders>
  );
}
