'use client';

import type { NavItem } from '@/components/layout/sidebar';
import { SponsorDashboardShell } from "@/components/layout/sponsor-dashboard-shell";
import { MobileMenuSection } from "@/components/layout/top-nav";
import { useSponsorAuth } from '@/lib/hooks/use-sponsor-auth';
import { CreditCard, LayoutDashboard, Users } from 'lucide-react';
import { usePathname } from 'next/navigation';

const sponsorNavItems: NavItem[] = [
  { href: '/sponsor', label: 'Dashboard', icon: <LayoutDashboard className="w-5 h-5" />, matchPrefix: '/sponsor', exact: true },
  { href: '/sponsor/learners', label: 'Learners', icon: <Users className="w-5 h-5" />, matchPrefix: '/sponsor/learners' },
  { href: '/sponsor/billing', label: 'Billing', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/sponsor/billing' },
];

const sponsorMobileNavItems: NavItem[] = [
  sponsorNavItems[0],
  sponsorNavItems[1],
  sponsorNavItems[2],
];

const sponsorMobileMenuSections: MobileMenuSection[] = [
  {
    label: 'Sponsor',
    items: [sponsorNavItems[0], sponsorNavItems[1], sponsorNavItems[2]],
  },
];

function getSponsorPageTitle(pathname: string | null): string | undefined {
  if (!pathname || pathname === '/sponsor') {
    return 'Dashboard';
  }

  if (pathname.startsWith('/sponsor/learners')) {
    return 'Sponsored Learners';
  }

  if (pathname.startsWith('/sponsor/billing')) {
    return 'Billing';
  }

  return undefined;
}

function SponsorLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { isLoading } = useSponsorAuth();
  const pageTitle = getSponsorPageTitle(pathname);

  if (isLoading) {
    return null;
  }

  return (
    <SponsorDashboardShell
      pageTitle={pageTitle}
      navItems={sponsorNavItems}
      mobileNavItems={sponsorMobileNavItems}
      mobileMenuSections={sponsorMobileMenuSections}
      requiredRole="sponsor"
    >
      {children}
    </SponsorDashboardShell>
  );
}

export default function SponsorLayout({ children }: { children: React.ReactNode }) {
  return <SponsorLayoutContent>{children}</SponsorLayoutContent>;
}
