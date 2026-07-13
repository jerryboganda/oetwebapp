'use client';

import dynamic from 'next/dynamic';
import { usePathname } from 'next/navigation';
import type { ReactNode } from 'react';
import { useAuth } from '@/contexts/auth-context';

const DynamicNotificationCenterProvider = dynamic<{ children: ReactNode }>(
  () => import('@/contexts/notification-center-context').then((module) => module.NotificationCenterProvider),
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
  '/privacy',
  '/terms',
] as const;

function isAuthRoute(pathname: string | null): boolean {
  if (!pathname) return false;

  return AUTH_ROUTE_PREFIXES.some(
    (prefix) => pathname === prefix || pathname.startsWith(`${prefix}/`),
  );
}

/**
 * Keeps notification state above page-local shells while avoiding the
 * notification/SignalR chunk until an authenticated workspace is rendered.
 */
export function AuthenticatedNotificationCenter({ children }: { children: ReactNode }) {
  const pathname = usePathname();
  const { isAuthenticated } = useAuth();

  if (!isAuthenticated || isAuthRoute(pathname)) {
    return <>{children}</>;
  }

  return (
    <DynamicNotificationCenterProvider>
      {children}
    </DynamicNotificationCenterProvider>
  );
}
