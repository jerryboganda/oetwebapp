'use client';

import { PrivilegedMfaBanner } from '@/components/auth/privileged-mfa-banner';
import { AdminDashboardShell, AppShell, LearnerWorkspaceContainer, type MobileMenuSection } from '@/components/layout';
import type { NavItem, SidebarSection } from '@/components/layout/sidebar';
import { hasPermission, sidebarPermissionMap } from '@/lib/admin-permissions';
import { useAuth } from '@/contexts/auth-context';
import { useMemo } from 'react';
import { CommandPalette, type CommandItem } from '@/components/ui/command-palette';
import type { BreadcrumbItem } from '@/components/ui/breadcrumbs';
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
} from 'lucide-react';
import { usePathname } from 'next/navigation';

// ─────────────────────────────────────────────────────────────────────────────
// Admin navigation catalog
// Flat list (used by permission filter + flat mobile bottom nav).
// ─────────────────────────────────────────────────────────────────────────────

const adminNavItems: NavItem[] = [
  { href: '/admin', label: 'Operations', icon: <LayoutDashboard className="w-5 h-5" />, exact: true },
  { href: '/admin/review-ops', label: 'Review Ops', icon: <Activity className="w-5 h-5" />, matchPrefix: '/admin/review-ops' },
  { href: '/admin/notifications', label: 'Notifications', icon: <Bell className="w-5 h-5" />, matchPrefix: '/admin/notifications' },
  { href: '/admin/escalations', label: 'Escalations', icon: <Scale className="w-5 h-5" />, matchPrefix: '/admin/escalations' },

  { href: '/admin/content', label: 'Content Library', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content' },
  { href: '/admin/content-papers', label: 'Content Papers', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content-papers' },
  { href: '/admin/taxonomy', label: 'Profession Taxonomy', icon: <ListTree className="w-5 h-5" />, matchPrefix: '/admin/taxonomy' },
  { href: '/admin/criteria', label: 'Rubrics & Criteria', icon: <Target className="w-5 h-5" />, matchPrefix: '/admin/criteria' },
  { href: '/admin/content-hierarchy', label: 'Content Hierarchy', icon: <GitBranch className="w-5 h-5" />, matchPrefix: '/admin/content-hierarchy' },
  { href: '/admin/content-import', label: 'Content Import', icon: <Upload className="w-5 h-5" />, matchPrefix: '/admin/content-import' },
  { href: '/admin/media', label: 'Media Assets', icon: <ImageIcon className="w-5 h-5" />, matchPrefix: '/admin/media' },
  { href: '/admin/content-generation', label: 'Content Generation', icon: <Sparkles className="w-5 h-5" />, matchPrefix: '/admin/content-generation' },
  { href: '/admin/freeze', label: 'Content Freeze', icon: <Snowflake className="w-5 h-5" />, matchPrefix: '/admin/freeze' },
  { href: '/admin/dedup', label: 'Deduplication', icon: <Copy className="w-5 h-5" />, matchPrefix: '/admin/dedup' },
  { href: '/admin/marketplace-review', label: 'Marketplace Review', icon: <Store className="w-5 h-5" />, matchPrefix: '/admin/marketplace-review' },
  { href: '/admin/publish-requests', label: 'Publish Requests', icon: <FileCheck2 className="w-5 h-5" />, matchPrefix: '/admin/publish-requests' },

  { href: '/admin/ai-config', label: 'AI Eval Config', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-config' },
  { href: '/admin/ai-usage', label: 'AI Usage & Budget', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-usage' },
  { href: '/admin/analytics/quality', label: 'Quality Analytics', icon: <PieChart className="w-5 h-5" />, matchPrefix: '/admin/analytics' },
  { href: '/admin/progress-policy', label: 'Progress Policy', icon: <PieChart className="w-5 h-5" />, matchPrefix: '/admin/progress-policy' },
  { href: '/admin/community', label: 'Community', icon: <MessageSquareText className="w-5 h-5" />, matchPrefix: '/admin/community' },
  { href: '/admin/permissions', label: 'Permissions', icon: <KeyRound className="w-5 h-5" />, matchPrefix: '/admin/permissions' },
  { href: '/admin/audit-logs', label: 'Audit Logs', icon: <ShieldCheck className="w-5 h-5" />, matchPrefix: '/admin/audit-logs' },

  { href: '/admin/users', label: 'User Ops', icon: <Users className="w-5 h-5" />, matchPrefix: '/admin/users' },
  { href: '/admin/experts', label: 'Expert Management', icon: <GraduationCap className="w-5 h-5" />, matchPrefix: '/admin/experts' },
  { href: '/admin/billing', label: 'Billing Ops', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/admin/billing' },
  { href: '/admin/private-speaking', label: 'Private Speaking', icon: <Mic className="w-5 h-5" />, matchPrefix: '/admin/private-speaking' },

  { href: '/admin/flags', label: 'Feature Flags', icon: <Flag className="w-5 h-5" />, matchPrefix: '/admin/flags' },
  { href: '/admin/webhooks', label: 'Webhooks', icon: <Webhook className="w-5 h-5" />, matchPrefix: '/admin/webhooks' },
];

function findItem(href: string) {
  return adminNavItems.find((item) => item.href === href)!;
}

const adminNavSections: SidebarSection[] = [
  {
    label: 'Operations',
    defaultOpen: true,
    items: [
      findItem('/admin'),
      findItem('/admin/review-ops'),
      findItem('/admin/notifications'),
      findItem('/admin/escalations'),
    ],
  },
  {
    label: 'Content',
    defaultOpen: true,
    items: [
      findItem('/admin/content'),
      findItem('/admin/content-papers'),
      findItem('/admin/taxonomy'),
      findItem('/admin/criteria'),
      findItem('/admin/content-hierarchy'),
      findItem('/admin/content-import'),
      findItem('/admin/media'),
      findItem('/admin/content-generation'),
      findItem('/admin/freeze'),
      findItem('/admin/dedup'),
      findItem('/admin/marketplace-review'),
      findItem('/admin/publish-requests'),
    ],
  },
  {
    label: 'Governance',
    defaultOpen: false,
    items: [
      findItem('/admin/ai-config'),
      findItem('/admin/ai-usage'),
      findItem('/admin/analytics/quality'),
      findItem('/admin/community'),
      findItem('/admin/permissions'),
      findItem('/admin/audit-logs'),
    ],
  },
  {
    label: 'People & Billing',
    defaultOpen: false,
    items: [
      findItem('/admin/users'),
      findItem('/admin/experts'),
      findItem('/admin/billing'),
      findItem('/admin/private-speaking'),
    ],
  },
  {
    label: 'System',
    defaultOpen: false,
    items: [
      findItem('/admin/flags'),
      findItem('/admin/webhooks'),
    ],
  },
];

const adminMobileNavItems: NavItem[] = [
  findItem('/admin'),
  findItem('/admin/content'),
  findItem('/admin/review-ops'),
  findItem('/admin/analytics/quality'),
  findItem('/admin/users'),
];

const adminMobileMenuSections: MobileMenuSection[] = adminNavSections.map((section) => ({
  label: section.label,
  items: section.items,
}));

// ─────────────────────────────────────────────────────────────────────────────
// Page title + breadcrumb derivation
// ─────────────────────────────────────────────────────────────────────────────

function isContentWorkspace(pathname: string | null) {
  return pathname === '/admin/content/new' || Boolean(pathname?.match(/^\/admin\/content\/[^/]+$/));
}

interface PageMeta {
  title: string | undefined;
  section: string;
  breadcrumbs: BreadcrumbItem[];
}

function getPageMeta(pathname: string | null): PageMeta {
  if (!pathname || pathname === '/admin') {
    return { title: 'Operations', section: 'Operations', breadcrumbs: [{ label: 'Operations' }] };
  }

  if (isContentWorkspace(pathname)) {
    return {
      title: 'Content Workspace',
      section: 'Content',
      breadcrumbs: [
        { label: 'Content Library', href: '/admin/content' },
        { label: pathname === '/admin/content/new' ? 'New paper' : 'Workspace' },
      ],
    };
  }

  if (pathname.startsWith('/admin/content/') && pathname.endsWith('/revisions')) {
    return {
      title: 'Revision History',
      section: 'Content',
      breadcrumbs: [
        { label: 'Content Library', href: '/admin/content' },
        { label: 'Revisions' },
      ],
    };
  }

  const prefixMap: Array<{ match: string; title: string; section: string }> = [
    { match: '/admin/content-papers', title: 'Content Papers', section: 'Content' },
    { match: '/admin/content-hierarchy', title: 'Content Hierarchy', section: 'Content' },
    { match: '/admin/content-import', title: 'Content Import', section: 'Content' },
    { match: '/admin/content-generation', title: 'Content Generation', section: 'Content' },
    { match: '/admin/content-analytics', title: 'Content Analytics', section: 'Content' },
    { match: '/admin/content-quality', title: 'Content Quality', section: 'Content' },
    { match: '/admin/content', title: 'Content Library', section: 'Content' },
    { match: '/admin/taxonomy', title: 'Profession Taxonomy', section: 'Content' },
    { match: '/admin/criteria', title: 'Rubrics & Criteria', section: 'Content' },
    { match: '/admin/media', title: 'Media Assets', section: 'Content' },
    { match: '/admin/marketplace-review', title: 'Marketplace Review', section: 'Content' },
    { match: '/admin/publish-requests', title: 'Publish Requests', section: 'Content' },
    { match: '/admin/freeze', title: 'Content Freeze', section: 'Content' },
    { match: '/admin/dedup', title: 'Deduplication', section: 'Content' },

    { match: '/admin/review-ops', title: 'Review Ops', section: 'Operations' },
    { match: '/admin/notifications', title: 'Notifications', section: 'Operations' },
    { match: '/admin/escalations', title: 'Escalations', section: 'Operations' },
    { match: '/admin/playbook', title: 'Playbook', section: 'Operations' },
    { match: '/admin/sla-health', title: 'SLA Health', section: 'Operations' },
    { match: '/admin/bulk-operations', title: 'Bulk Operations', section: 'Operations' },

    { match: '/admin/ai-config', title: 'AI Eval Config', section: 'Governance' },
    { match: '/admin/ai-usage', title: 'AI Usage & Budget', section: 'Governance' },
    { match: '/admin/analytics/cohort', title: 'Cohort Analytics', section: 'Governance' },
    { match: '/admin/analytics/content-effectiveness', title: 'Content Effectiveness', section: 'Governance' },
    { match: '/admin/analytics/expert-efficiency', title: 'Expert Efficiency', section: 'Governance' },
    { match: '/admin/analytics/subscription-health', title: 'Subscription Health', section: 'Governance' },
    { match: '/admin/analytics', title: 'Quality Analytics', section: 'Governance' },
    { match: '/admin/progress-policy', title: 'Progress Policy', section: 'Governance' },
    { match: '/admin/audit-logs', title: 'Audit Logs', section: 'Governance' },
    { match: '/admin/permissions', title: 'Permissions', section: 'Governance' },
    { match: '/admin/community', title: 'Community Moderation', section: 'Governance' },
    { match: '/admin/roles', title: 'Roles', section: 'Governance' },
    { match: '/admin/business-intelligence', title: 'Business Intelligence', section: 'Governance' },

    { match: '/admin/users', title: 'User Ops', section: 'People & Billing' },
    { match: '/admin/experts', title: 'Expert Management', section: 'People & Billing' },
    { match: '/admin/billing', title: 'Billing Ops', section: 'People & Billing' },
    { match: '/admin/private-speaking', title: 'Private Speaking', section: 'People & Billing' },
    { match: '/admin/credit-lifecycle', title: 'Credit Lifecycle', section: 'People & Billing' },
    { match: '/admin/score-guarantee-claims', title: 'Score Guarantee Claims', section: 'People & Billing' },
    { match: '/admin/free-tier', title: 'Free Tier Limits', section: 'People & Billing' },
    { match: '/admin/enterprise', title: 'Enterprise', section: 'People & Billing' },

    { match: '/admin/flags', title: 'Feature Flags', section: 'System' },
    { match: '/admin/webhooks', title: 'Webhooks', section: 'System' },
  ];

  const hit = prefixMap.find((entry) => pathname.startsWith(entry.match));
  if (!hit) {
    return { title: undefined, section: 'Admin', breadcrumbs: [{ label: 'Admin' }] };
  }

  // Detail page heuristic: if there is a path segment after the prefix with
  // actual content (not `/import`, `/new`, etc.), emit a two-level breadcrumb.
  const suffix = pathname.slice(hit.match.length);
  const segments = suffix.split('/').filter(Boolean);
  const breadcrumbs: BreadcrumbItem[] = [
    { label: hit.section },
    { label: hit.title, href: hit.match },
  ];
  if (segments.length === 1) {
    const seg = segments[0];
    const human = seg === 'new' ? 'New' : seg === 'import' ? 'Import' : 'Detail';
    breadcrumbs.push({ label: human });
  } else if (segments.length > 1) {
    breadcrumbs.push({ label: 'Detail' });
  } else {
    // Root of this prefix — drop the trailing duplicate.
    breadcrumbs.pop();
    breadcrumbs.push({ label: hit.title });
  }

  return { title: hit.title, section: hit.section, breadcrumbs };
}

function filterNavByPermissions(items: NavItem[], perms: string[] | null | undefined): NavItem[] {
  return items.filter((item) => {
    const required = sidebarPermissionMap[item.href];
    if (!required) return true;
    return hasPermission(perms, ...required);
  });
}

function filterSectionsByPermissions<T extends { label: string; items: NavItem[] }>(
  sections: T[],
  perms: string[] | null | undefined,
): T[] {
  return sections
    .map((section) => ({ ...section, items: filterNavByPermissions(section.items, perms) }))
    .filter((section) => section.items.length > 0);
}

// ─────────────────────────────────────────────────────────────────────────────
// Command palette catalog
// ─────────────────────────────────────────────────────────────────────────────

function buildCommandItems(items: NavItem[]): CommandItem[] {
  return items.map((item) => ({
    id: item.href,
    label: item.label,
    href: item.href,
    icon: item.icon,
    section: adminNavSections.find((s) => s.items.some((i) => i.href === item.href))?.label,
    keywords: `${item.label} ${item.href}`,
  }));
}

function AdminLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { user } = useAuth();
  const perms = user?.adminPermissions;

  const filteredNavItems = useMemo(() => filterNavByPermissions(adminNavItems, perms), [perms]);
  const filteredSections = useMemo(() => filterSectionsByPermissions(adminNavSections, perms), [perms]);
  const filteredMobileNavItems = useMemo(() => filterNavByPermissions(adminMobileNavItems, perms), [perms]);
  const filteredMobileMenuSections = useMemo(() => filterSectionsByPermissions(adminMobileMenuSections, perms), [perms]);

  const meta = getPageMeta(pathname);

  const commandItems = useMemo(() => buildCommandItems(filteredNavItems), [filteredNavItems]);

  const bannerBlock = (
    <div className="space-y-6">
      <PrivilegedMfaBanner />
      {children}
    </div>
  );

  if (isContentWorkspace(pathname)) {
    return (
      <CommandPalette items={commandItems} recentsKey="admin.command-palette.recents">
        <AppShell
          distractionFree
          pageTitle={meta.title}
          navItems={filteredNavItems}
          mobileNavItems={filteredMobileNavItems}
          mobileMenuSections={filteredMobileMenuSections}
          requiredRole="admin"
          workspaceRole="admin"
          breadcrumbs={meta.breadcrumbs}
          showCommandPalette
        >
          <LearnerWorkspaceContainer maxWidth="admin">{bannerBlock}</LearnerWorkspaceContainer>
        </AppShell>
      </CommandPalette>
    );
  }

  return (
    <CommandPalette items={commandItems} recentsKey="admin.command-palette.recents">
      <AdminDashboardShell
        pageTitle={meta.title}
        navItems={filteredNavItems}
        navSections={filteredSections}
        mobileNavItems={filteredMobileNavItems}
        mobileMenuSections={filteredMobileMenuSections}
        requiredRole="admin"
        breadcrumbs={meta.breadcrumbs}
        showCommandPalette
      >
        {bannerBlock}
      </AdminDashboardShell>
    </CommandPalette>
  );
}

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return <AdminLayoutContent>{children}</AdminLayoutContent>;
}
