'use client';

import { Suspense, type ReactNode, useContext, useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { AuthGuard } from '@/components/auth/auth-guard';
import { EmailVerificationBanner } from '@/components/domain/email-verification-banner';
import { AuthContext, AuthProvider } from '@/contexts/auth-context';
import type { UserRole } from '@/lib/types/auth';
import { cn } from '@/lib/utils';
import { usePathname } from 'next/navigation';
import { type NavGroup, type NavItem, type ShellUserSummary, Sidebar } from './sidebar';
import { BottomNav } from './bottom-nav';
import { GlobalSearch } from './global-search';
import { isExamOrLiveRoute } from './learner-dashboard-route-policy';
import { TopNav, type MobileMenuSection } from './top-nav';
import { TourAutoTrigger } from '@/components/onboarding/tour-auto-trigger';

/** Scrolls the main content area to top on every mount (triggered by route change via key={pathname}). */
function ScrollReset() {
  useEffect(() => {
    const el = document.getElementById('main-content');
    if (el) el.scrollTop = 0;
  }, []);
  return null;
}

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
      <div className="page-surface w-full max-w-sm rounded-[2rem] px-6 py-8 text-center shadow-lg" role="status" aria-live="polite">
        <div className="mx-auto mb-4 h-8 w-8 rounded-full border-2 border-primary/30 border-t-primary animate-spin" aria-hidden="true" />
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
  // Route entrance only after a client navigation, never on first paint
  // (adjust-state-on-prop-change; false on server and client, so no hydration diff).
  const [firstPath] = useState(pathname);
  const [navigated, setNavigated] = useState(false);
  if (!navigated && pathname !== firstPath) setNavigated(true);
  const isAdminWorkspace = workspaceRole === 'admin' || requiredRole === 'admin';
  const isLearnerWorkspace = (workspaceRole ?? requiredRole) === 'learner' && !isAdminWorkspace;
  const [sidebarCollapsed, setSidebarCollapsed] = useState(false);

  // Ctrl/⌘K palette: one per shell (staff shells render two TopNavs), signed-in
  // only (public pages render this shell too), never on exam/live routes.
  const searchRole = workspaceRole ?? requiredRole;
  const searchEnabled = Boolean(authContext?.isAuthenticated)
    && Boolean(searchRole)
    && !distractionFree
    && !isExamOrLiveRoute(pathname);
  const [searchOpen, setSearchOpen] = useState(false);
  // Close it when the route changes under a shell that stays mounted.
  const [searchPath, setSearchPath] = useState(pathname);
  if (searchPath !== pathname) {
    setSearchPath(pathname);
    setSearchOpen(false);
  }
  const openSearch = searchEnabled ? () => setSearchOpen(true) : undefined;
  const searchSections = navGroups?.length ? navGroups : mobileMenuSections?.length ? mobileMenuSections : [{ label: 'Go to', items: navItems ?? [] }];

  const shellBackdrop = isAdminWorkspace ? null : (
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
      {/* No route animation here: exam players and focus workspaces (DESIGN.md §5). */}
      <motion.main
        id="main-content"
        tabIndex={-1}
        key={pathname}
        layout="position"
        className={cn('relative z-10 flex flex-1 min-h-0 flex-col overflow-y-auto py-4 lg:py-6', className)}
      >
        <ScrollReset />
        {children}
      </motion.main>
    </div>
  ) : isLearnerWorkspace ? (
    // Learner workspace: one full-width header across the top, sidebar beneath
    // it. Admin/expert keep the original sidebar-beside-header arrangement.
    <div className="relative isolate flex h-[var(--app-viewport-height,100dvh)] flex-col overflow-hidden bg-background-light text-navy">
      {shellBackdrop}
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:left-4 focus:top-4 focus:z-[100] focus:rounded-2xl focus:bg-primary focus:px-4 focus:py-2 focus:text-white focus:shadow-lg"
      >
        Skip to content
      </a>
      <TopNav
        showBrand
        pageTitle={pageTitle}
        actions={navActions}
        items={mobileNavItems ?? navItems}
        sectionedItems={mobileMenuSections}
        userSummary={userSummary}
        workspaceRole={workspaceRole}
        onOpenSearch={openSearch}
        onToggleSidebar={() => setSidebarCollapsed((current) => !current)}
        sidebarCollapsed={sidebarCollapsed}
      />
      <div className="relative z-10 flex min-h-0 flex-1">
        <Sidebar
          hideBrand
          className={cn(sidebarCollapsed && 'lg:hidden')}
          items={navItems}
          groups={navGroups}
          userSummary={userSummary}
          workspaceRole={workspaceRole}
        />
        <div className="flex min-w-0 flex-1 min-h-0 flex-col">
          {/* The bottom padding / scroll-padding clear the fixed bottom nav by 1rem, so
              the last content and scrolled-to controls are never under it
              (globals.css adds the AI-assistant launcher's height when it shows).
              Same enter-only contract as the staff shell: client navigations only
              (a hard load never animates, protecting LCP), never on exam/live routes. */}
          <motion.main
            id="main-content"
            tabIndex={-1}
            key={pathname}
            layout="position"
            className={cn(
              'relative flex-1 min-h-0 overflow-y-auto overscroll-contain py-4 pb-[calc(var(--bottom-nav-height)+var(--safe-area-inset-bottom)+1rem)] scroll-pb-[calc(var(--bottom-nav-height)+var(--safe-area-inset-bottom)+1rem)] lg:py-6 lg:pb-6 lg:scroll-pb-6',
              navigated && !isExamOrLiveRoute(pathname) && 'page-enter',
              className,
            )}
          >
            <ScrollReset />
            <EmailVerificationBanner />
            {children}
          </motion.main>
        </div>
      </div>
      <BottomNav items={mobileNavItems} />
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
        onOpenSearch={openSearch}
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
          onOpenSearch={openSearch}
        />
        {/* Enter-only CSS entrance; never an exit animation, which kept the old
            <main> mounted against the new route and rendered the page twice. */}
        <motion.main
          id="main-content"
          tabIndex={-1}
          key={pathname}
          layout="position"
          className={cn(
            'relative flex-1 min-h-0 overflow-y-auto overscroll-contain py-4 pb-[calc(var(--bottom-nav-height)+var(--safe-area-inset-bottom)+1rem)] scroll-pb-[calc(var(--bottom-nav-height)+var(--safe-area-inset-bottom)+1rem)] lg:py-6 lg:pb-6 lg:scroll-pb-6',
            navigated && !isExamOrLiveRoute(pathname) && 'page-enter',
            className,
          )}
        >
          <ScrollReset />
          <EmailVerificationBanner />
          {children}
        </motion.main>
      </div>
      <BottomNav items={mobileNavItems} />
    </div>
  );

  const shellWithTour = (
    <>
      {shell}
      {searchEnabled && searchRole ? (
        <GlobalSearch open={searchOpen} onOpenChange={setSearchOpen} sections={searchSections} workspaceRole={searchRole} />
      ) : null}
      {requireAuth ? <TourAutoTrigger workspaceRole={workspaceRole} /> : null}
    </>
  );

  const content = requireAuth ? (
    <Suspense fallback={<ShellFallback />}>
      <AuthGuard requiredRole={requiredRole}>{shellWithTour}</AuthGuard>
    </Suspense>
  ) : shellWithTour;

  return requireAuth && !hasAuthProvider ? <AuthProvider>{content}</AuthProvider> : content;
}
