'use client';

import { useMemo } from 'react';
import { AppShell, ExpertDashboardShell, type MobileMenuSection } from '@/components/layout';
import type { NavGroup, NavItem } from '@/components/layout/sidebar';
import { LayoutDashboard, Inbox, CheckCircle, BarChart3, CalendarClock, Users, Mic, Rocket, MessageSquare, DollarSign, Headphones, Video, ClipboardList, BookOpen, AudioLines, Scale, Gauge, StickyNote, BookMarked, MessagesSquare, GraduationCap } from 'lucide-react';
import { usePathname } from 'next/navigation';
import { useExpertAuth } from '@/lib/hooks/use-expert-auth';

const expertNavItems: NavItem[] = [
  { href: '/expert', label: 'Dashboard', icon: <LayoutDashboard className="w-5 h-5" />, matchPrefix: '/expert', exact: true },
  { href: '/expert/queue', label: 'Review Queue', icon: <Inbox className="w-5 h-5" />, matchPrefix: '/expert/queue', exact: true },
  { href: '/expert/queue/assigned', label: 'Writing Reviews', icon: <ClipboardList className="w-5 h-5" />, matchPrefix: '/expert/queue/assigned' },
  { href: '/expert/listening', label: 'Listening Reviews', icon: <Headphones className="w-5 h-5" />, matchPrefix: '/expert/listening' },
  { href: '/expert/calibration', label: 'Calibration', icon: <CheckCircle className="w-5 h-5" />, matchPrefix: '/expert/calibration' },
  { href: '/expert/metrics', label: 'Metrics', icon: <BarChart3 className="w-5 h-5" />, matchPrefix: '/expert/metrics' },
  { href: '/expert/schedule', label: 'Schedule', icon: <CalendarClock className="w-5 h-5" />, matchPrefix: '/expert/schedule' },
  { href: '/expert/live-classes', label: 'Live Classes', icon: <Video className="w-5 h-5" />, matchPrefix: '/expert/live-classes' },
  { href: '/expert/learners', label: 'Learners', icon: <Users className="w-5 h-5" />, matchPrefix: '/expert/learners' },
  { href: '/expert/private-speaking', label: 'Private Speaking', icon: <Mic className="w-5 h-5" />, matchPrefix: '/expert/private-speaking' },
  { href: '/expert/messages', label: 'Messages', icon: <MessageSquare className="w-5 h-5" />, matchPrefix: '/expert/messages' },
  { href: '/expert/compensation', label: 'Compensation', icon: <DollarSign className="w-5 h-5" />, matchPrefix: '/expert/compensation' },
  // New review surfaces appended AFTER the original 12 items so the index-based
  // mobile nav slices above stay stable.
  { href: '/expert/reading', label: 'Reading Queue', icon: <BookOpen className="w-5 h-5" />, matchPrefix: '/expert/reading' },
  { href: '/expert/speaking/queue', label: 'Speaking Reviews', icon: <AudioLines className="w-5 h-5" />, matchPrefix: '/expert/speaking/queue' },
  { href: '/expert/speaking/moderation', label: 'Moderation', icon: <Scale className="w-5 h-5" />, matchPrefix: '/expert/speaking/moderation' },
  { href: '/expert/scoring-quality', label: 'Scoring Quality', icon: <Gauge className="w-5 h-5" />, matchPrefix: '/expert/scoring-quality' },
];

const expertToolsNavItems: NavItem[] = [
  { href: '/expert/annotation-templates', label: 'Annotation Templates', icon: <StickyNote className="w-5 h-5" />, matchPrefix: '/expert/annotation-templates' },
  { href: '/expert/rubric-reference', label: 'Rubric Guide', icon: <BookMarked className="w-5 h-5" />, matchPrefix: '/expert/rubric-reference' },
  { href: '/expert/ask-an-expert', label: 'Ask a Tutor', icon: <MessagesSquare className="w-5 h-5" />, matchPrefix: '/expert/ask-an-expert' },
  { href: '/expert/training', label: 'Training', icon: <GraduationCap className="w-5 h-5" />, matchPrefix: '/expert/training' },
];

const onboardingNavItem: NavItem = {
  href: '/expert/onboarding',
  label: 'Onboarding',
  icon: <Rocket className="w-5 h-5" />,
  matchPrefix: '/expert/onboarding',
};

function getExpertPageTitle(pathname: string | null): string | undefined {
  if (!pathname || pathname === '/expert') {
    return 'Dashboard';
  }

  if (pathname.startsWith('/expert/queue/assigned')) {
    return 'Writing Reviews';
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

  if (pathname.startsWith('/expert/live-classes')) {
    return 'Live Classes';
  }

  if (pathname.startsWith('/expert/private-speaking')) {
    return 'Private Speaking';
  }

  if (pathname.startsWith('/expert/messages')) {
    return 'Messages';
  }

  if (pathname.startsWith('/expert/compensation')) {
    return 'Compensation';
  }

  if (pathname.startsWith('/expert/onboarding')) {
    return 'Onboarding';
  }

  if (pathname.startsWith('/expert/listening')) {
    return 'Listening Reviews';
  }

  if (pathname.startsWith('/expert/reading')) {
    return 'Reading Queue';
  }

  if (pathname.startsWith('/expert/speaking/moderation')) {
    return 'Moderation';
  }

  if (pathname.startsWith('/expert/speaking/queue')) {
    return 'Speaking Reviews';
  }

  if (pathname.startsWith('/expert/scoring-quality')) {
    return 'Scoring Quality';
  }

  if (pathname.startsWith('/expert/annotation-templates')) {
    return 'Annotation Templates';
  }

  if (pathname.startsWith('/expert/rubric-reference')) {
    return 'Rubric Guide';
  }

  if (pathname.startsWith('/expert/ask-an-expert')) {
    return 'Ask a Tutor';
  }

  if (pathname.startsWith('/expert/training')) {
    return 'Training';
  }

  if (pathname.startsWith('/expert/settings')) {
    return 'Settings';
  }

  return undefined;
}

function ExpertLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { expert } = useExpertAuth();
  const pageTitle = getExpertPageTitle(pathname);

  const showOnboarding = expert?.isOnboardingComplete === false;

  const navItems = useMemo(() => {
    const base = showOnboarding
      ? [expertNavItems[0], onboardingNavItem, ...expertNavItems.slice(1)]
      : expertNavItems;
    return [...base, ...expertToolsNavItems];
  }, [showOnboarding]);

  // Desktop sidebar renders grouped: the main workspace cluster plus a Tools
  // cluster (templates, rubric guide, community answers).
  const navGroups = useMemo<NavGroup[]>(() => {
    const workspaceItems = showOnboarding
      ? [expertNavItems[0], onboardingNavItem, ...expertNavItems.slice(1)]
      : expertNavItems;
    return [
      { label: 'Workspace', items: workspaceItems },
      { label: 'Tools', items: expertToolsNavItems },
    ];
  }, [showOnboarding]);

  const mobileNavItems = useMemo(() => {
    const base = [expertNavItems[0], expertNavItems[1], expertNavItems[2], expertNavItems[3], expertNavItems[7]];
    if (!showOnboarding) return base;
    return [expertNavItems[0], onboardingNavItem, ...base.slice(1)];
  }, [showOnboarding]);

  const mobileMenuSections: MobileMenuSection[] = useMemo(() => {
    const sections: MobileMenuSection[] = [
      {
        label: 'Review',
        items: [expertNavItems[0], expertNavItems[1], expertNavItems[2], expertNavItems[3], expertNavItems[12], expertNavItems[13], expertNavItems[14]],
      },
      {
        label: 'Performance',
        items: [expertNavItems[4], expertNavItems[5], expertNavItems[6], expertNavItems[7], expertNavItems[15]],
      },
      {
        label: 'Tools',
        items: expertToolsNavItems,
      },
    ];
    if (showOnboarding) {
      sections[0] = {
        label: 'Review',
        items: [expertNavItems[0], onboardingNavItem, expertNavItems[1], expertNavItems[2], expertNavItems[3], expertNavItems[12], expertNavItems[13], expertNavItems[14]],
      };
    }
    return sections;
  }, [showOnboarding]);

  // Hide nav for specific review workspaces
  const isReviewWorkspace = pathname?.includes('/expert/review/') ?? false;
  const isOnboardingPage = pathname?.startsWith('/expert/onboarding') ?? false;

  if (isOnboardingPage) {
    return (
      <AppShell
        distractionFree
        pageTitle="Expert Onboarding"
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

  if (isReviewWorkspace) {
    return (
      <AppShell
        distractionFree
        pageTitle="Review Workspace"
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
      navGroups={navGroups}
      mobileNavItems={mobileNavItems}
      mobileMenuSections={mobileMenuSections}
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
