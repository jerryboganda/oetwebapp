'use client';

import { PrivilegedMfaBanner } from '@/components/auth/privileged-mfa-banner';
import { AdminDashboardShell, AppShell, LearnerWorkspaceContainer, type MobileMenuSection } from '@/components/layout';
import type { NavItem } from '@/components/layout/sidebar';
import { 
  LayoutDashboard,
  Library, 
  ListTree, 
  Target, 
  Cpu, 
  Activity, 
  PieChart, 
  Users, 
  CreditCard, 
  Bell,
  Flag, 
  ShieldCheck 
} from 'lucide-react';
import { usePathname } from 'next/navigation';

const adminNavItems: NavItem[] = [
  { href: '/admin', label: 'Operations', icon: <LayoutDashboard className="w-5 h-5" />, exact: true },
  { href: '/admin/content', label: 'Content Library', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content' },
  { href: '/admin/taxonomy', label: 'Profession Taxonomy', icon: <ListTree className="w-5 h-5" />, matchPrefix: '/admin/taxonomy' },
  { href: '/admin/criteria', label: 'Rubrics & Criteria', icon: <Target className="w-5 h-5" />, matchPrefix: '/admin/criteria' },
  { href: '/admin/ai-config', label: 'AI Eval Config', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-config' },
  { href: '/admin/review-ops', label: 'Review Ops', icon: <Activity className="w-5 h-5" />, matchPrefix: '/admin/review-ops' },
  { href: '/admin/notifications', label: 'Notifications', icon: <Bell className="w-5 h-5" />, matchPrefix: '/admin/notifications' },
  { href: '/admin/analytics/quality', label: 'Quality Analytics', icon: <PieChart className="w-5 h-5" />, matchPrefix: '/admin/analytics' },
  { href: '/admin/users', label: 'User Ops', icon: <Users className="w-5 h-5" />, matchPrefix: '/admin/users' },
  { href: '/admin/billing', label: 'Billing Ops', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/admin/billing' },
  { href: '/admin/flags', label: 'Feature Flags', icon: <Flag className="w-5 h-5" />, matchPrefix: '/admin/flags' },
  { href: '/admin/audit-logs', label: 'Audit Logs', icon: <ShieldCheck className="w-5 h-5" />, matchPrefix: '/admin/audit-logs' },
];

const adminMobileNavItems: NavItem[] = [
  adminNavItems[0],
  adminNavItems[1],
  adminNavItems[5],
  adminNavItems[8],
  adminNavItems[9],
];

const adminMobileMenuSections: MobileMenuSection[] = [
  {
    label: 'Operations',
    items: [adminNavItems[0], adminNavItems[5], adminNavItems[6], adminNavItems[7]],
  },
  {
    label: 'Governance',
    items: [adminNavItems[1], adminNavItems[2], adminNavItems[3], adminNavItems[4], adminNavItems[10], adminNavItems[11]],
  },
  {
    label: 'People & billing',
    items: [adminNavItems[8], adminNavItems[9]],
  },
];

function isContentWorkspace(pathname: string | null) {
  return pathname === '/admin/content/new' || Boolean(pathname?.match(/^\/admin\/content\/[^/]+$/));
}

function getAdminPageTitle(pathname: string | null) {
  if (!pathname || pathname === '/admin') {
    return 'Operations';
  }

  if (isContentWorkspace(pathname)) {
    return 'Content Workspace';
  }

  if (pathname.startsWith('/admin/content/') && pathname.endsWith('/revisions')) {
    return 'Revision History';
  }

  if (pathname.startsWith('/admin/content')) {
    return 'Content Library';
  }

  if (pathname.startsWith('/admin/taxonomy')) {
    return 'Profession Taxonomy';
  }

  if (pathname.startsWith('/admin/criteria')) {
    return 'Rubrics & Criteria';
  }

  if (pathname.startsWith('/admin/ai-config')) {
    return 'AI Eval Config';
  }

  if (pathname.startsWith('/admin/review-ops')) {
    return 'Review Ops';
  }

  if (pathname.startsWith('/admin/notifications')) {
    return 'Notifications';
  }

  if (pathname.startsWith('/admin/analytics')) {
    return 'Quality Analytics';
  }

  if (pathname.startsWith('/admin/users')) {
    return 'User Ops';
  }

  if (pathname.startsWith('/admin/billing')) {
    return 'Billing Ops';
  }

  if (pathname.startsWith('/admin/flags')) {
    return 'Feature Flags';
  }

  if (pathname.startsWith('/admin/audit-logs')) {
    return 'Audit Logs';
  }

  return undefined;
}

function AdminLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const pageTitle = getAdminPageTitle(pathname);
  const bannerBlock = (
    <div className="space-y-6">
      <PrivilegedMfaBanner />
      {children}
    </div>
  );

  if (isContentWorkspace(pathname)) {
    return (
      <AppShell
        distractionFree
        pageTitle={pageTitle}
        navItems={adminNavItems}
        mobileNavItems={adminMobileNavItems}
        mobileMenuSections={adminMobileMenuSections}
        requiredRole="admin"
        workspaceRole="admin"
      >
        <LearnerWorkspaceContainer>{bannerBlock}</LearnerWorkspaceContainer>
      </AppShell>
    );
  }

  return (
    <AdminDashboardShell
      pageTitle={pageTitle}
      navItems={adminNavItems}
      mobileNavItems={adminMobileNavItems}
      mobileMenuSections={adminMobileMenuSections}
      requiredRole="admin"
    >
      {bannerBlock}
    </AdminDashboardShell>
  );
}

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminLayoutContent>{children}</AdminLayoutContent>;
}
