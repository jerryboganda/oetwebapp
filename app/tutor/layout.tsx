'use client';

/**
 * Tutor console layout — wave B1 (frontend mirror of /v1/tutor/me/*).
 *
 * The tutor area lives under /tutor/* and is gated by the same ExpertOnly
 * policy on the backend. We re-use the ExpertDashboardShell for visual
 * consistency.
 */

import { LayoutDashboard, CalendarClock, BookOpen, DollarSign, User, Video, ClipboardList, Gauge, PenSquare } from 'lucide-react';
import { useMemo } from 'react';
import { usePathname } from 'next/navigation';

import { AppShell, ExpertDashboardShell, type MobileMenuSection } from '@/components/layout';
import type { NavItem } from '@/components/layout/sidebar';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';

const tutorNavItems: NavItem[] = [
  { href: '/tutor/dashboard', label: 'Dashboard', icon: <LayoutDashboard className="w-5 h-5" />, matchPrefix: '/tutor/dashboard' },
  { href: '/tutor/classes', label: 'Classes', icon: <Video className="w-5 h-5" />, matchPrefix: '/tutor/classes' },
  { href: '/tutor/writing/queue', label: 'Writing Queue', icon: <ClipboardList className="w-5 h-5" />, matchPrefix: '/tutor/writing/queue' },
  { href: '/tutor/writing/calibration', label: 'Writing Calibration', icon: <Gauge className="w-5 h-5" />, matchPrefix: '/tutor/writing/calibration' },
  { href: '/tutor/availability', label: 'Availability', icon: <CalendarClock className="w-5 h-5" />, matchPrefix: '/tutor/availability' },
  { href: '/tutor/earnings', label: 'Earnings', icon: <DollarSign className="w-5 h-5" />, matchPrefix: '/tutor/earnings' },
  { href: '/tutor/profile', label: 'Profile', icon: <User className="w-5 h-5" />, matchPrefix: '/tutor/profile' },
];

function getTutorPageTitle(pathname: string | null): string | undefined {
  if (!pathname) return 'Tutor Console';
  if (pathname.startsWith('/tutor/dashboard')) return 'Tutor Dashboard';
  if (pathname.startsWith('/tutor/classes/new')) return 'New Class';
  if (pathname.startsWith('/tutor/classes')) return 'Classes';
  if (pathname.startsWith('/tutor/writing/queue')) return 'Writing Review Queue';
  if (pathname.startsWith('/tutor/writing/reviews')) return 'Writing Review';
  if (pathname.startsWith('/tutor/writing/calibration')) return 'Writing Calibration';
  if (pathname.startsWith('/tutor/availability')) return 'Availability';
  if (pathname.startsWith('/tutor/earnings')) return 'Earnings';
  if (pathname.startsWith('/tutor/profile')) return 'Profile';
  return 'Tutor Console';
}

function TutorLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { expert } = useExpertAuth();
  const pageTitle = getTutorPageTitle(pathname);

  const navItems = tutorNavItems;

  const mobileNavItems = useMemo(
    () => [tutorNavItems[0], tutorNavItems[1], tutorNavItems[2], tutorNavItems[3], tutorNavItems[4]],
    [],
  );

  const mobileMenuSections: MobileMenuSection[] = useMemo(
    () => [
      {
        label: 'Teach',
        items: [tutorNavItems[0], tutorNavItems[1], tutorNavItems[2]],
      },
      {
        label: 'Account',
        items: [tutorNavItems[3], tutorNavItems[4]],
      },
    ],
    [],
  );

  // Class detail editor pages have lots of vertical content; render them
  // through the same chrome but allow a fluid container.
  if (pathname?.startsWith('/tutor/classes/new')) {
    return (
      <AppShell
        pageTitle="New Class"
        navItems={navItems}
        mobileNavItems={mobileNavItems}
        mobileMenuSections={mobileMenuSections}
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
      navItems={navItems}
      mobileNavItems={mobileNavItems}
      mobileMenuSections={mobileMenuSections}
      userSummary={{ displayName: expert?.displayName, email: expert?.email }}
      requiredRole="expert"
    >
      {children}
    </ExpertDashboardShell>
  );
}

export default function TutorLayout({ children }: { children: React.ReactNode }) {
  return <TutorLayoutContent>{children}</TutorLayoutContent>;
}
