'use client';

import { AppShell } from '@/components/layout/app-shell';
import { NavItem } from '@/components/layout/sidebar';
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

export default function ExpertLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { expert } = useExpertAuth();
  
  // Hide nav for specific review workspaces
  const isReviewWorkspace = pathname?.includes('/expert/review/') ?? false;

  if (isReviewWorkspace) {
    return (
      <AppShell distractionFree pageTitle="Review Workspace" navItems={expertNavItems} userSummary={{ displayName: expert?.displayName, email: expert?.email }} requiredRole="expert">
        {children}
      </AppShell>
    );
  }

  return (
    <AppShell 
      navItems={expertNavItems} 
      mobileNavItems={expertNavItems} 
      userSummary={{ displayName: expert?.displayName, email: expert?.email }}
      requiredRole="expert"
    >
      {children}
    </AppShell>
  );
}
