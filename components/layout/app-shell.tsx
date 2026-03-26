'use client';

import { cn } from '@/lib/utils';
import { TopNav } from './top-nav';
import { Sidebar, BottomNav, type NavItem } from './sidebar';
import { type ReactNode } from 'react';

interface AppShellProps {
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
}

export function AppShell({ children, pageTitle, distractionFree = false, navActions, className, navItems, mobileNavItems }: AppShellProps) {
  if (distractionFree) {
    return (
      <div className="min-h-screen flex flex-col bg-background-light">
        <TopNav pageTitle={pageTitle} actions={navActions} />
        <main className={cn('flex-1 flex flex-col', className)}>
          {children}
        </main>
      </div>
    );
  }

  return (
    <div className="flex flex-col lg:flex-row min-h-screen bg-background-light">
      <TopNav className="lg:hidden" pageTitle={pageTitle} actions={navActions} />
      <Sidebar items={navItems} />
      <div className="flex flex-1 flex-col min-w-0">
        <TopNav className="hidden lg:flex" pageTitle={pageTitle} actions={navActions} />
        <main className={cn('flex-1 overflow-y-auto pb-20 lg:pb-0', className)}>
          {children}
        </main>
      </div>
      <BottomNav items={mobileNavItems} />
    </div>
  );
}
