'use client';

import { Suspense, type ReactNode, useContext } from 'react';
import { AnimatePresence, motion, useReducedMotion } from 'motion/react';
import { AuthGuard } from '@/components/auth/auth-guard';
import { AuthContext, AuthProvider } from '@/contexts/auth-context';
import { NotificationCenterProvider } from '@/contexts/notification-center-context';
import { getMotionPresenceMode, getSurfaceMotion, prefersReducedMotion } from '@/lib/motion';
import type { UserRole } from '@/lib/types/auth';
import { cn } from '@/lib/utils';
import { usePathname } from 'next/navigation';
import { BottomNav, type NavGroup, type NavItem, type ShellUserSummary, Sidebar } from './sidebar';
import { TopNav, type MobileMenuSection } from './top-nav';

export interface AppShellProps {
  children: ReactNode;
  pageTitle?: string;
  subtitle?: string;
  backHref?: string;
  distractionFree?: boolean;
  navActions?: ReactNode;
  className?: string;
  navItems?: NavItem[];
  navGroups?: NavGroup[];
  mobileNavItems?: NavItem[];
  mobileMenuSections?: MobileMenuSection[];
  userSummary?: ShellUserSummary;
  requireAuth?: boolean;
  requiredRole?: UserRole;
  workspaceRole?: UserRole;
}

function ShellFallback() {
  return (
    <div className="flex min-h-screen items-center justify-center px-6">
      <div className="page-surface w-full max-w-sm rounded-[2rem] px-6 py-8 text-center shadow-lg">
        <div className="mx-auto mb-4 flex h-12 w-12 items-center justify-center rounded-2xl bg-primary/10 text-primary">
          <div className="h-5 w-5 rounded-full border-2 border-current border-t-transparent animate-spin" aria-hidden="true" />
        </div>
        <p className="text-sm font-semibold text-navy">Loading workspace...</p>
        <p className="mt-1 text-xs text-muted">Preparing your authenticated session.</p>
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
  navGroups,
  mobileNavItems,
  mobileMenuSections,
  userSummary,
  requireAuth = true,
  requiredRole,
  workspaceRole,
}: AppShellProps) {
  const authContext = useContext(AuthContext);
  const hasAuthProvider = authContext !== null;
  const pathname = usePathname() ?? 'root';
  const reducedMotion = prefersReducedMotion(useReducedMotion());
  const routeMotionProps = getSurfaceMotion('route', reducedMotion);
  const presenceMode = getMotionPresenceMode(reducedMotion);

  const shellBackdrop = (
    <div aria-hidden="true" className="pointer-events-none absolute inset-0 overflow-hidden">
      <div className="absolute left-1/2 top-0 h-64 w-[42rem] -translate-x-1/2 rounded-full bg-primary/10 blur-3xl" />
      <div className="absolute -left-24 top-24 h-72 w-72 rounded-full bg-info/10 blur-3xl" />
      <div className="absolute bottom-0 right-0 h-72 w-72 rounded-full bg-warning/10 blur-3xl" />
    </div>
  );

  const shell = distractionFree ? (
    <div className="relative isolate flex h-[var(--app-viewport-height,100dvh)] flex-col overflow-hidden bg-background-light text-navy">
      {shellBackdrop}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-2xl focus:bg-primary focus:px-4 focus:py-2 focus:text-white focus:shadow-lg"
      >
        Skip to content
      </a>
      <TopNav
        pageTitle={pageTitle}
        actions={navActions}
        items={mobileNavItems ?? navItems}
        sectionedItems={mobileMenuSections}
        userSummary={userSummary}
        workspaceRole={workspaceRole}
      />
      <AnimatePresence initial={!reducedMotion} mode={presenceMode}>
        <motion.main
          id="main-content"
          key={pathname}
          layout="position"
            className={cn('relative z-10 flex flex-1 min-h-0 flex-col overflow-y-auto py-4 lg:py-6', className)}
          {...routeMotionProps}
        >
          {children}
        </motion.main>
      </AnimatePresence>
    </div>
  ) : (
    <div className="relative isolate flex h-[var(--app-viewport-height,100dvh)] flex-col overflow-hidden bg-background-light text-navy lg:flex-row">
      {shellBackdrop}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-2xl focus:bg-primary focus:px-4 focus:py-2 focus:text-white focus:shadow-lg"
      >
        Skip to content
      </a>
      <TopNav
        className="lg:hidden"
        pageTitle={pageTitle}
        actions={navActions}
        items={mobileNavItems ?? navItems}
        sectionedItems={mobileMenuSections}
        userSummary={userSummary}
        workspaceRole={workspaceRole}
      />
      <Sidebar items={navItems} groups={navGroups} userSummary={userSummary} workspaceRole={workspaceRole} />
      <div className="relative z-10 flex min-w-0 flex-1 min-h-0 flex-col">
        <TopNav
          className="hidden lg:flex"
          pageTitle={pageTitle}
          actions={navActions}
          items={navItems}
          userSummary={userSummary}
          workspaceRole={workspaceRole}
        />
        <AnimatePresence initial={!reducedMotion} mode={presenceMode}>
          <motion.main
            id="main-content"
            key={pathname}
            layout="position"
            className={cn('relative flex-1 min-h-0 overflow-y-auto overscroll-contain py-4 pb-[calc(var(--bottom-nav-height)+env(safe-area-inset-bottom))] lg:py-6 lg:pb-6', className)}
            {...routeMotionProps}
          >
            {children}
          </motion.main>
        </AnimatePresence>
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
