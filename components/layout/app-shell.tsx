'use client';

import { cn } from '@/lib/utils';
import { AuthGuard } from '@/components/auth/auth-guard';
import { TopNav } from './top-nav';
import { Sidebar, BottomNav, type NavItem, type ShellUserSummary } from './sidebar';
import { Suspense, type ReactNode } from 'react';
import type { UserRole } from '@/lib/types/auth';

export interface AppShellProps {
  children: ReactNode;
  pageTitle?: string;
  /** Optional subtitle shown below the page title (reserved for future use) */
  subtitle?: string;
  /** Optional back navigation href (reserved for future use) */
  backHref?: string;
  /** Hides sidebar/nav for full-screen task modes (writing, speaking, etc.) */
  distractionFree?: boolean;
  /** Extra actions to render in the top nav bar */
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
  const shell = distractionFree ? (
    <div className="min-h-screen flex flex-col bg-background-light">
      <a href="#main-content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[100] focus:px-4 focus:py-2 focus:bg-primary focus:text-white focus:rounded-lg focus:shadow-lg">Skip to content</a>
      <TopNav pageTitle={pageTitle} actions={navActions} items={navItems} userSummary={userSummary} />
      <main id="main-content" className={cn('flex-1 flex flex-col', className)}>
        {children}
      </main>
    </div>
  ) : (
    <div className="flex flex-col lg:flex-row min-h-screen bg-background-light">
      <a href="#main-content" className="sr-only focus:not-sr-only focus:fixed focus:top-2 focus:left-2 focus:z-[100] focus:px-4 focus:py-2 focus:bg-primary focus:text-white focus:rounded-lg focus:shadow-lg">Skip to content</a>
      <TopNav className="lg:hidden" pageTitle={pageTitle} actions={navActions} items={mobileNavItems ?? navItems} userSummary={userSummary} />
      <Sidebar items={navItems} userSummary={userSummary} />
      <div className="flex flex-1 flex-col min-w-0">
        <TopNav className="hidden lg:flex" pageTitle={pageTitle} actions={navActions} items={navItems} userSummary={userSummary} />
        <main id="main-content" className={cn('flex-1 overflow-y-auto pb-20 lg:pb-0', className)}>
          {children}
        </main>
      </div>
      <BottomNav items={mobileNavItems} />
    </div>
  );

  return requireAuth ? (
    <Suspense fallback={<ShellFallback />}>
      <AuthGuard requiredRole={requiredRole}>{shell}</AuthGuard>
    </Suspense>
  ) : shell;
}
