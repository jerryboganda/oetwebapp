'use client';

import { AppShell, ExpertDashboardShell, type MobileMenuSection } from '@/components/layout';
import type { NavItem } from '@/components/layout/sidebar';
import { LayoutDashboard, Inbox, CheckCircle, BarChart3, CalendarClock, Users } from 'lucide-react';
import { usePathname } from 'next/navigation';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';

const expertNavItems: NavItem[] = [
  { href: '/expert', label: 'Dashboard', icon: <LayoutDashboard className="w-5 h-5" />, matchPrefix: '/expert', exact: true },
  { href: '/expert/queue', label: 'Review Queue', icon: <Inbox className="w-5 h-5" />, matchPrefix: '/expert/queue' },
  { href: '/expert/calibration', label: 'Calibration', icon: <CheckCircle className="w-5 h-5" />, matchPrefix: '/expert/calibration' },
  { href: '/expert/metrics', label: 'Metrics', icon: <BarChart3 className="w-5 h-5" />, matchPrefix: '/expert/metrics' },
  { href: '/expert/schedule', label: 'Schedule', icon: <CalendarClock className="w-5 h-5" />, matchPrefix: '/expert/schedule' },
  { href: '/expert/learners', label: 'Learners', icon: <Users className="w-5 h-5" />, matchPrefix: '/expert/learners' },
];

const expertMobileNavItems: NavItem[] = [
  expertNavItems[0],
  expertNavItems[1],
  expertNavItems[2],
  expertNavItems[3],
  expertNavItems[4],
];

const expertMobileMenuSections: MobileMenuSection[] = [
  {
    label: 'Review',
    items: [expertNavItems[0], expertNavItems[1], expertNavItems[2]],
  },
  {
    label: 'Performance',
    items: [expertNavItems[3], expertNavItems[4], expertNavItems[5]],
  },
];

function getExpertPageTitle(pathname: string | null): string | undefined {
  if (!pathname || pathname === '/expert') {
    return 'Dashboard';
  }

  if (pathname.startsWith('/expert/queue')) {
    return 'Review Queue';
  }

  if (pathname.startsWith('/expert/calibration')) {
    return 'Calibration';
  }

  if (pathname.startsWith('/expert/learners')) {
    return 'Learners';
  }

  if (pathname.startsWith('/expert/metrics')) {
    return 'Metrics';
  }

  if (pathname.startsWith('/expert/schedule')) {
    return 'Schedule';
  }

  return undefined;
}

function ExpertLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { expert } = useExpertAuth();
  const pageTitle = getExpertPageTitle(pathname);

  // Hide nav for specific review workspaces
  const isReviewWorkspace = pathname?.includes('/expert/review/') ?? false;

  if (isReviewWorkspace) {
    return (
      <AppShell
        distractionFree
        pageTitle="Review Workspace"
        navItems={expertNavItems}
        mobileNavItems={expertMobileNavItems}
        mobileMenuSections={expertMobileMenuSections}
        userSummary={{ displayName: expert?.displayName, email: expert?.email }}
        requiredRole="expert"
        workspaceRole="expert"
      >
        {children}
      </AppShell>
    );
  }

  return (
    <ExpertDashboardShell
      pageTitle={pageTitle}
      navItems={expertNavItems}
      mobileNavItems={expertMobileNavItems}
      mobileMenuSections={expertMobileMenuSections}
      userSummary={{ displayName: expert?.displayName, email: expert?.email }}
      requiredRole="expert"
    >
      {children}
    </ExpertDashboardShell>
  );
}

export default function ExpertLayout({ children }: { children: React.ReactNode }) {
  return <ExpertLayoutContent>{children}</ExpertLayoutContent>;
}
