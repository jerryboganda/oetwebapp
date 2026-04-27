'use client';

import { PrivilegedMfaBanner } from '@/components/auth/privileged-mfa-banner';
import { AdminDashboardShell, AppShell, LearnerWorkspaceContainer, type MobileMenuSection } from '@/components/layout';
import type { NavItem } from '@/components/layout/sidebar';
import { hasPermission, sidebarPermissionMap } from '@/lib/admin-permissions';
import { useAuth } from '@/contexts/auth-context';
import { useMemo } from 'react';
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
  ShieldCheck,
  Upload,
  Copy,
  Image as ImageIcon,
  Sparkles,
  Store,
  Snowflake,
  GitBranch,
  KeyRound,
  FileCheck2,
  Webhook,
  Scale,
  Mic,
  GraduationCap,
  MessageSquareText,
  BookOpenText,
} from 'lucide-react';
import { usePathname } from 'next/navigation';

const adminNavItems: NavItem[] = [
  { href: '/admin', label: 'Operations', icon: <LayoutDashboard className="w-5 h-5" />, exact: true },
  { href: '/admin/content', label: 'Content Library', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content' },
  { href: '/admin/content-papers', label: 'Content Papers', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content-papers' },
  { href: '/admin/taxonomy', label: 'Profession Taxonomy', icon: <ListTree className="w-5 h-5" />, matchPrefix: '/admin/taxonomy' },
  { href: '/admin/criteria', label: 'Rubrics & Criteria', icon: <Target className="w-5 h-5" />, matchPrefix: '/admin/criteria' },
  { href: '/admin/ai-config', label: 'AI Eval Config', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-config' },
  { href: '/admin/ai-usage', label: 'AI Usage & Budget', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-usage' },
  { href: '/admin/review-ops', label: 'Review Ops', icon: <Activity className="w-5 h-5" />, matchPrefix: '/admin/review-ops' },
  { href: '/admin/notifications', label: 'Notifications', icon: <Bell className="w-5 h-5" />, matchPrefix: '/admin/notifications' },
  { href: '/admin/analytics/quality', label: 'Quality Analytics', icon: <PieChart className="w-5 h-5" />, matchPrefix: '/admin/analytics' },
  { href: '/admin/users', label: 'User Ops', icon: <Users className="w-5 h-5" />, matchPrefix: '/admin/users' },
  { href: '/admin/experts', label: 'Expert Management', icon: <GraduationCap className="w-5 h-5" />, matchPrefix: '/admin/experts' },
  { href: '/admin/billing', label: 'Billing Ops', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/admin/billing' },
  { href: '/admin/flags', label: 'Feature Flags', icon: <Flag className="w-5 h-5" />, matchPrefix: '/admin/flags' },
  { href: '/admin/audit-logs', label: 'Audit Logs', icon: <ShieldCheck className="w-5 h-5" />, matchPrefix: '/admin/audit-logs' },
  { href: '/admin/content-import', label: 'Content Import', icon: <Upload className="w-5 h-5" />, matchPrefix: '/admin/content-import' },
  { href: '/admin/dedup', label: 'Deduplication', icon: <Copy className="w-5 h-5" />, matchPrefix: '/admin/dedup' },
  { href: '/admin/media', label: 'Media Assets', icon: <ImageIcon className="w-5 h-5" />, matchPrefix: '/admin/media' },
  { href: '/admin/content-generation', label: 'Content Generation', icon: <Sparkles className="w-5 h-5" />, matchPrefix: '/admin/content-generation' },
  { href: '/admin/grammar', label: 'Grammar CMS', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/grammar' },
  { href: '/admin/rulebooks', label: 'Rulebooks', icon: <BookOpenText className="w-5 h-5" />, matchPrefix: '/admin/rulebooks' },
  { href: '/admin/pronunciation', label: 'Pronunciation CMS', icon: <Mic className="w-5 h-5" />, matchPrefix: '/admin/pronunciation' },
  { href: '/admin/content/vocabulary', label: 'Vocabulary CMS', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content/vocabulary' },
  { href: '/admin/content/conversation', label: 'Conversation CMS', icon: <MessageSquareText className="w-5 h-5" />, matchPrefix: '/admin/content/conversation' },
  { href: '/admin/marketplace-review', label: 'Marketplace Review', icon: <Store className="w-5 h-5" />, matchPrefix: '/admin/marketplace-review' },
  { href: '/admin/freeze', label: 'Content Freeze', icon: <Snowflake className="w-5 h-5" />, matchPrefix: '/admin/freeze' },
  { href: '/admin/content-hierarchy', label: 'Content Hierarchy', icon: <GitBranch className="w-5 h-5" />, matchPrefix: '/admin/content-hierarchy' },
  { href: '/admin/permissions', label: 'Permissions', icon: <KeyRound className="w-5 h-5" />, matchPrefix: '/admin/permissions' },
  { href: '/admin/publish-requests', label: 'Publish Requests', icon: <FileCheck2 className="w-5 h-5" />, matchPrefix: '/admin/publish-requests' },
  { href: '/admin/webhooks', label: 'Webhooks', icon: <Webhook className="w-5 h-5" />, matchPrefix: '/admin/webhooks' },
  { href: '/admin/escalations', label: 'Escalations', icon: <Scale className="w-5 h-5" />, matchPrefix: '/admin/escalations' },
  { href: '/admin/private-speaking', label: 'Private Speaking', icon: <Mic className="w-5 h-5" />, matchPrefix: '/admin/private-speaking' },
  { href: '/admin/community', label: 'Community', icon: <MessageSquareText className="w-5 h-5" />, matchPrefix: '/admin/community' },
  { href: '/admin/strategies', label: 'Strategy Guides', icon: <BookOpenText className="w-5 h-5" />, matchPrefix: '/admin/strategies' },
];

const adminMobileNavItems: NavItem[] = [
  adminNavItems[0],
  adminNavItems[1],
  adminNavItems[5],
  adminNavItems[8],
  adminNavItems[9],
  adminNavItems[10],
];

const adminMobileMenuSections: MobileMenuSection[] = [
  {
    label: 'Operations',
    items: [adminNavItems[0], adminNavItems[5], adminNavItems[6], adminNavItems[7]],
  },
  {
    label: 'Content',
    items: [adminNavItems[1], adminNavItems[13], adminNavItems[14], adminNavItems[15], adminNavItems[16], adminNavItems[19], adminNavItems[20], adminNavItems[21], adminNavItems[30]],
  },
  {
    label: 'Governance',
    items: [adminNavItems[2], adminNavItems[3], adminNavItems[4], adminNavItems[11], adminNavItems[12], adminNavItems[17], adminNavItems[18]],
  },
  {
    label: 'People & billing',
    items: [adminNavItems[8], adminNavItems[9], adminNavItems[10], adminNavItems[25]],
  },
];

function isContentWorkspace(pathname: string | null) {
  if (!pathname) return false;
  if (pathname.startsWith('/admin/content/vocabulary')) return false;
  if (pathname.startsWith('/admin/content/conversation')) return false;
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

  if (pathname.startsWith('/admin/content/vocabulary')) {
    return 'Vocabulary CMS';
  }

  if (pathname.startsWith('/admin/content/conversation')) {
    return 'Conversation CMS';
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

  if (pathname.startsWith('/admin/experts')) {
    return 'Expert Management';
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

  if (pathname.startsWith('/admin/content-import')) {
    return 'Content Import';
  }

  if (pathname.startsWith('/admin/dedup')) {
    return 'Deduplication';
  }

  if (pathname.startsWith('/admin/media')) {
    return 'Media Assets';
  }

  if (pathname.startsWith('/admin/content-generation')) {
    return 'Content Generation';
  }

  if (pathname.startsWith('/admin/grammar')) {
    return 'Grammar CMS';
  }

  if (pathname.startsWith('/admin/pronunciation')) {
    return 'Pronunciation CMS';
  }

  if (pathname.startsWith('/admin/marketplace-review')) {
    return 'Marketplace Review';
  }

  if (pathname.startsWith('/admin/freeze')) {
    return 'Content Freeze';
  }

  if (pathname.startsWith('/admin/content-hierarchy')) {
    return 'Content Hierarchy';
  }

  if (pathname.startsWith('/admin/private-speaking')) {
    return 'Private Speaking';
  }

  if (pathname.startsWith('/admin/community')) {
    return 'Community Moderation';
  }

  if (pathname.startsWith('/admin/strategies')) {
    return 'Strategy Guides';
  }

  return undefined;
}

function filterNavByPermissions(items: NavItem[], perms: string[] | null | undefined): NavItem[] {
  return items.filter((item) => {
    const required = sidebarPermissionMap[item.href];
    if (!required) return true; // no permission required (e.g. dashboard)
    return hasPermission(perms, ...required);
  });
}

function filterSectionsByPermissions(
  sections: MobileMenuSection[],
  perms: string[] | null | undefined,
): MobileMenuSection[] {
  return sections
    .map((s) => ({ ...s, items: filterNavByPermissions(s.items, perms) }))
    .filter((s) => s.items.length > 0);
}

function AdminLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { user } = useAuth();
  const perms = user?.adminPermissions;

  const filteredNavItems = useMemo(() => filterNavByPermissions(adminNavItems, perms), [perms]);
  const filteredMobileNavItems = useMemo(() => filterNavByPermissions(adminMobileNavItems, perms), [perms]);
  const filteredMobileMenuSections = useMemo(() => filterSectionsByPermissions(adminMobileMenuSections, perms), [perms]);

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
        navItems={filteredNavItems}
        mobileNavItems={filteredMobileNavItems}
        mobileMenuSections={filteredMobileMenuSections}
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
      navItems={filteredNavItems}
      mobileNavItems={filteredMobileNavItems}
      mobileMenuSections={filteredMobileMenuSections}
      requiredRole="admin"
    >
      {bannerBlock}
    </AdminDashboardShell>
  );
}

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminLayoutContent>{children}</AdminLayoutContent>;
}
