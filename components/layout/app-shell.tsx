'use client';

import { Suspense, type ReactNode, useContext } from 'react';
import { AuthGuard } from '@/components/auth/auth-guard';
import { AuthContext, AuthProvider } from '@/contexts/auth-context';
import { NotificationCenterProvider } from '@/contexts/notification-center-context';
import type { UserRole } from '@/lib/types/auth';
import { cn } from '@/lib/utils';
import { BottomNav, type NavItem, type ShellUserSummary, Sidebar } from './sidebar';
import { TopNav } from './top-nav';

export interface AppShellProps {
  children: ReactNode;
  pageTitle?: string;
  subtitle?: string;
  backHref?: string;
  distractionFree?: boolean;
  navActions?: ReactNode;
  className?: string;
  navItems?: NavItem[];
  mobileNavItems?: NavItem[];
  userSummary?: ShellUserSummary;
  requireAuth?: boolean;
  requiredRole?: UserRole;
}

function ShellFallback() {
  return (
    <div className="flex min-h-screen items-center justify-center bg-background-light px-6">
      <div className="space-y-2 text-center">
        <p className="text-sm font-semibold text-navy">Loading workspace...</p>
        <p className="text-xs text-muted">Preparing your authenticated session.</p>
      </div>
    </div>
  );
}

export function AppShell({
  children,
  pageTitle,
  distractionFree = false,
  navActions,
  className,
  navItems,
  mobileNavItems,
  userSummary,
  requireAuth = true,
  requiredRole,
}: AppShellProps) {
  const authContext = useContext(AuthContext);
  const hasAuthProvider = authContext !== null;

  const shell = distractionFree ? (
    <div className="min-h-screen flex flex-col bg-background-light">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[100] focus:px-4 focus:py-2 focus:bg-primary focus:text-white focus:rounded-lg focus:shadow-lg"
      >
        Skip to content
      </a>
      <TopNav pageTitle={pageTitle} actions={navActions} items={navItems} userSummary={userSummary} />
      <main id="main-content" className={cn('flex-1 flex flex-col', className)}>
        {children}
      </main>
    </div>
  ) : (
    <div className="flex min-h-screen flex-col bg-background-light lg:flex-row">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[100] focus:px-4 focus:py-2 focus:bg-primary focus:text-white focus:rounded-lg focus:shadow-lg"
      >
        Skip to content
      </a>
      <TopNav
        className="lg:hidden"
        pageTitle={pageTitle}
        actions={navActions}
        items={mobileNavItems ?? navItems}
        userSummary={userSummary}
      />
      <Sidebar items={navItems} userSummary={userSummary} />
      <div className="flex min-w-0 flex-1 flex-col">
        <TopNav
          className="hidden lg:flex"
          pageTitle={pageTitle}
          actions={navActions}
          items={navItems}
          userSummary={userSummary}
        />
        <main id="main-content" className={cn('flex-1 overflow-y-auto pb-20 lg:pb-0', className)}>
          {children}
        </main>
      </div>
      <BottomNav items={mobileNavItems} />
    </div>
  );

  const shellWithNotifications = requireAuth
    ? <NotificationCenterProvider>{shell}</NotificationCenterProvider>
    : shell;

  const content = requireAuth ? (
    <Suspense fallback={<ShellFallback />}>
      <AuthGuard requiredRole={requiredRole}>{shellWithNotifications}</AuthGuard>
    </Suspense>
  ) : shellWithNotifications;

  return requireAuth && !hasAuthProvider ? <AuthProvider>{content}</AuthProvider> : content;
}
