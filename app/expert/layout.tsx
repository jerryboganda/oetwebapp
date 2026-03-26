'use client';

import { AppShell } from '@/components/layout/app-shell';
import { NavItem } from '@/components/layout/sidebar';
import { Inbox, CheckCircle, BarChart3, CalendarClock, Users, Lock } from 'lucide-react';
import { usePathname } from 'next/navigation';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';
import { useEffect } from 'react';

const expertNavItems: NavItem[] = [
  { href: '/expert/queue', label: 'Review Queue', icon: <Inbox className="w-5 h-5" />, matchPrefix: '/expert/queue' },
  { href: '/expert/calibration', label: 'Calibration', icon: <CheckCircle className="w-5 h-5" />, matchPrefix: '/expert/calibration' },
  { href: '/expert/metrics', label: 'Metrics', icon: <BarChart3 className="w-5 h-5" />, matchPrefix: '/expert/metrics' },
  { href: '/expert/schedule', label: 'Schedule', icon: <CalendarClock className="w-5 h-5" />, matchPrefix: '/expert/schedule' },
  { href: '/expert/learners', label: 'Learners', icon: <Users className="w-5 h-5" />, matchPrefix: '/expert/learners' },
];

export default function ExpertLayout({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { role, isAuthenticated, isLoading } = useExpertAuth();
  
  // Hide nav for specific review workspaces
  const isReviewWorkspace = pathname?.includes('/expert/review/') ?? false;

  if (isLoading) {
    return (
      <div className="flex h-screen w-full items-center justify-center bg-gray-50">
        <div className="flex flex-col items-center gap-4 text-gray-500 font-sans">
          <Lock className="w-8 h-8 animate-pulse text-primary" />
          <p>Verifying Expert Credentials...</p>
        </div>
        {/* Next.js layouts MUST render children to prevent hydration and routing failures */}
        <div className="hidden">{children}</div>
      </div>
    );
  }

  // Basic RBAC Simulation - Render nothing if not an expert (the hook handles redirecting, but this acts as an extra visual guard)
  if (!isAuthenticated || role !== 'expert') {
    return (
      <div className="flex h-screen w-full items-center justify-center bg-gray-50 text-red-600 font-sans">
        <p>Access Denied. You do not have permission to view this console.</p>
        <div className="hidden">{children}</div>
      </div>
    );
  }

  if (isReviewWorkspace) {
    return (
      <AppShell distractionFree pageTitle="Review Workspace">
        {children}
      </AppShell>
    );
  }

  return (
    <AppShell 
      navItems={expertNavItems} 
      mobileNavItems={expertNavItems} 
    >
      {children}
    </AppShell>
  );
}
