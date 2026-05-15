'use client';

import { PrivilegedMfaBanner } from '@/components/auth/privileged-mfa-banner';
import { AdminDashboardShell, type MobileMenuSection } from '@/components/layout';
import type { NavGroup, NavItem } from '@/components/layout/sidebar';
import {
  canAccessAdminRoute,
  getAdminRoutePermissions,
  hasPermission,
  sidebarPermissionMap,
} from '@/lib/admin-permissions';
import { useAuth } from '@/contexts/auth-context';
import { Children, isValidElement, cloneElement, useMemo } from 'react';
import { 
  LayoutDashboard,
  Library, 
  ClipboardList,
  Target, 
  Cpu, 
  Activity, 
  PieChart, 
  Users, 
  CreditCard,
  Bell,
  Flag,
  ShieldCheck,
  Server,
  Upload,
  Copy,
  Image as ImageIcon,
  Sparkles,
  Store,
  Snowflake,
  GitBranch,
  FileCheck2,
  Webhook,
  Scale,
  Mic,
  MessageSquareText,
  BookOpenText,
  Rocket,
} from 'lucide-react';
import { usePathname } from 'next/navigation';

// Grouped admin nav: each section maps to a workstream so the sidebar reads as
// "what do I want to do" rather than as a flat dump of 35 routes.
const adminNavGroups: NavGroup[] = [
  {
    label: 'Overview',
    items: [
      { href: '/admin', label: 'Operations', icon: <LayoutDashboard className="w-5 h-5" />, exact: true },
      { href: '/admin/launch-readiness', label: 'Launch Readiness', icon: <Rocket className="w-5 h-5" />, matchPrefix: '/admin/launch-readiness' },
      { href: '/admin/alerts', label: 'Alerts', icon: <Bell className="w-5 h-5" />, matchPrefix: '/admin/alerts' },
      { href: '/admin/analytics/quality', label: 'Quality Analytics', icon: <PieChart className="w-5 h-5" />, matchPrefix: '/admin/analytics' },
      { href: '/admin/audit-logs', label: 'Audit Logs', icon: <ShieldCheck className="w-5 h-5" />, matchPrefix: '/admin/audit-logs' },
    ],
  },
  {
    label: 'Content',
    items: [
      { href: '/admin/content', label: 'Content Hub', icon: <Library className="w-5 h-5" />, matchPrefix: '/admin/content' },
    ],
  },
  {
    label: 'Governance & rubrics',
    items: [
      // Profession registry is now managed via /admin/signup-catalog (single source of truth);
      // writes there are mirrored into the legacy Professions taxonomy table by the backend.
      { href: '/admin/signup-catalog', label: 'Signup Catalog', icon: <ClipboardList className="w-5 h-5" />, matchPrefix: '/admin/signup-catalog' },
      { href: '/admin/criteria', label: 'Rubrics & Criteria', icon: <Target className="w-5 h-5" />, matchPrefix: '/admin/criteria' },
      { href: '/admin/rulebooks', label: 'Rulebooks', icon: <BookOpenText className="w-5 h-5" />, matchPrefix: '/admin/rulebooks' },
    ],
  },
  {
    label: 'Reviews & quality',
    items: [
      { href: '/admin/review-ops', label: 'Review Ops', icon: <Activity className="w-5 h-5" />, matchPrefix: '/admin/review-ops' },
      { href: '/admin/escalations', label: 'Escalations', icon: <Scale className="w-5 h-5" />, matchPrefix: '/admin/escalations' },
      { href: '/admin/marketplace-review', label: 'Marketplace Review', icon: <Store className="w-5 h-5" />, matchPrefix: '/admin/marketplace-review' },
      { href: '/admin/private-speaking', label: 'Private Speaking', icon: <Mic className="w-5 h-5" />, matchPrefix: '/admin/private-speaking' },
    ],
  },
  {
    label: 'AI & automation',
    items: [
      { href: '/admin/ai-config', label: 'AI Eval Config', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-config' },
      { href: '/admin/ai-providers', label: 'AI Providers', icon: <Server className="w-5 h-5" />, matchPrefix: '/admin/ai-providers' },
      { href: '/admin/ai-usage', label: 'AI Usage & Budget', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/ai-usage' },
      { href: '/admin/writing/options', label: 'Writing AI Options', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/writing/options' },
      { href: '/admin/writing/ai-draft', label: 'Writing AI Draft', icon: <Cpu className="w-5 h-5" />, matchPrefix: '/admin/writing/ai-draft' },
    ],
  },
  {
    label: 'People & access',
    items: [
      { href: '/admin/users', label: 'User Operations', icon: <Users className="w-5 h-5" />, matchPrefix: '/admin/users' },
      { href: '/admin/community', label: 'Community', icon: <MessageSquareText className="w-5 h-5" />, matchPrefix: '/admin/community' },
    ],
  },
  {
    label: 'Billing & growth',
    items: [
      { href: '/admin/billing', label: 'Billing Ops', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/admin/billing' },
      { href: '/admin/billing/wallet-tiers', label: 'Wallet Tiers', icon: <CreditCard className="w-5 h-5" />, matchPrefix: '/admin/billing/wallet-tiers' },
      { href: '/admin/freeze', label: 'Subscription Freezes', icon: <Snowflake className="w-5 h-5" />, matchPrefix: '/admin/freeze' },
      { href: '/admin/flags', label: 'Feature Flags', icon: <Flag className="w-5 h-5" />, matchPrefix: '/admin/flags' },
      { href: '/admin/notifications', label: 'Notifications', icon: <Bell className="w-5 h-5" />, matchPrefix: '/admin/notifications' },
      { href: '/admin/webhooks', label: 'Webhooks', icon: <Webhook className="w-5 h-5" />, matchPrefix: '/admin/webhooks' },
    ],
  },
];

const adminNavItems: NavItem[] = adminNavGroups.flatMap((g) => g.items);

const adminMobileNavItems: NavItem[] = [
  adminNavItems.find((i) => i.href === '/admin')!,
  adminNavItems.find((i) => i.href === '/admin/content')!,
  adminNavItems.find((i) => i.href === '/admin/review-ops')!,
  adminNavItems.find((i) => i.href === '/admin/users')!,
  adminNavItems.find((i) => i.href === '/admin/billing')!,
];

const adminMobileMenuSections: MobileMenuSection[] = adminNavGroups.map((g) => ({
  label: g.label,
  items: g.items,
}));

function isContentWorkspace(pathname: string | null) {
  if (!pathname) return false;
  // The "content workspace" editor lives at /admin/content/[id]; it must NOT
  // match the new hub sub-routes (papers, import, hierarchy, grammar, etc.).
  const hubSubRoutes = [
    'vocabulary',
    'conversation',
    'library',
    'mocks',
    'analytics',
    'quality',
    'papers',
    'import',
    'generation',
    'hierarchy',
    'media',
    'dedup',
    'grammar',
    'pronunciation',
    'listening',
    'writing',
    'strategies',
    'publish-requests',
  ];
  for (const seg of hubSubRoutes) {
    if (pathname.startsWith(`/admin/content/${seg}`)) return false;
  }
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

  if (pathname.startsWith('/admin/content/library')) {
    return 'Content Library';
  }

  if (pathname.startsWith('/admin/content/mocks')) {
    return 'Mock Bundles';
  }

  if (pathname.startsWith('/admin/content/analytics')) {
    return 'Content Analytics';
  }

  if (pathname.startsWith('/admin/content/quality')) {
    return 'Content Quality';
  }

  if (pathname.startsWith('/admin/content/papers')) {
    return 'Content Papers';
  }

  if (pathname.startsWith('/admin/content/import')) {
    return 'Content Import';
  }

  if (pathname.startsWith('/admin/content/generation')) {
    return 'Content Generation';
  }

  if (pathname.startsWith('/admin/content/hierarchy')) {
    return 'Content Hierarchy';
  }

  if (pathname.startsWith('/admin/content/media')) {
    return 'Media Assets';
  }

  if (pathname.startsWith('/admin/content/dedup')) {
    return 'Deduplication';
  }

  if (pathname.startsWith('/admin/content/listening')) {
    return 'Listening Authoring';
  }

  if (pathname.startsWith('/admin/content/grammar')) {
    return 'Grammar CMS';
  }

  if (pathname.startsWith('/admin/content/pronunciation')) {
    return 'Pronunciation CMS';
  }

  if (pathname.startsWith('/admin/content/strategies')) {
    return 'Strategy Guides';
  }

  if (pathname.startsWith('/admin/content/publish-requests')) {
    return 'Publish Requests';
  }

  if (pathname.startsWith('/admin/content/writing')) {
    return 'Writing Authoring';
  }

  if (pathname.startsWith('/admin/content')) {
    return 'Content Hub';
  }

  if (pathname.startsWith('/admin/taxonomy')) {
    return 'Professions';
  }

  if (pathname.startsWith('/admin/criteria')) {
    return 'Rubrics & Criteria';
  }

  if (pathname.startsWith('/admin/ai-config')) {
    return 'AI Eval Config';
  }

  if (pathname.startsWith('/admin/ai-providers')) {
    return 'AI Providers';
  }

  if (pathname.startsWith('/admin/ai-usage')) {
    return 'AI Usage & Budget';
  }

  if (pathname.startsWith('/admin/launch-readiness')) {
    return 'Launch Readiness';
  }

  if (pathname.startsWith('/admin/review-ops')) {
    return 'Review Ops';
  }

  if (pathname.startsWith('/admin/notifications')) {
    return 'Notifications';
  }

  if (pathname.startsWith('/admin/analytics/reading')) {
    return 'Reading Analytics';
  }

  if (pathname.startsWith('/admin/analytics/listening')) {
    return 'Listening Analytics';
  }

  if (pathname.startsWith('/admin/business-intelligence')) {
    return 'Business Intelligence';
  }

  if (pathname.startsWith('/admin/analytics/subscription-health')) {
    return 'Subscription Health';
  }

  if (pathname.startsWith('/admin/analytics')) {
    return 'Quality Analytics';
  }

  if (pathname.startsWith('/admin/users')) {
    return 'User Operations';
  }

  if (pathname.startsWith('/admin/institutions')) {
    return 'Institutions';
  }

  // Legacy routes — kept for redirect stubs only.
  if (pathname.startsWith('/admin/experts') || pathname.startsWith('/admin/permissions') || pathname.startsWith('/admin/roles')) {
    return 'User Operations';
  }

  if (pathname.startsWith('/admin/billing')) {
    return 'Billing Ops';
  }

  if (pathname.startsWith('/admin/free-tier')) {
    return 'Free Tier';
  }

  if (pathname.startsWith('/admin/credit-lifecycle')) {
    return 'Credit Lifecycle';
  }

  if (pathname.startsWith('/admin/bulk-operations')) {
    return 'Bulk Operations';
  }

  if (pathname.startsWith('/admin/flags')) {
    return 'Feature Flags';
  }

  if (pathname.startsWith('/admin/audit-logs')) {
    return 'Audit Logs';
  }

  if (pathname.startsWith('/admin/marketplace-review')) {
    return 'Marketplace Review';
  }

  if (pathname.startsWith('/admin/freeze')) {
    return 'Subscription Freezes';
  }

  if (pathname.startsWith('/admin/private-speaking')) {
    return 'Private Speaking';
  }

  if (pathname.startsWith('/admin/signup-catalog')) {
    return 'Signup Catalog';
  }

  if (pathname.startsWith('/admin/rulebooks')) {
    return 'Rulebooks';
  }

  if (pathname.startsWith('/admin/webhooks')) {
    return 'Webhooks';
  }

  if (pathname.startsWith('/admin/escalations')) {
    return 'Escalations';
  }

  if (pathname.startsWith('/admin/enterprise')) {
    return 'Enterprise';
  }

  if (pathname.startsWith('/admin/playbook')) {
    return 'Playbook';
  }

  if (pathname.startsWith('/admin/community')) {
    return 'Community Moderation';
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

function filterGroupsByPermissions(
  groups: NavGroup[],
  perms: string[] | null | undefined,
): NavGroup[] {
  return groups
    .map((g) => ({ ...g, items: filterNavByPermissions(g.items, perms) }))
    .filter((g) => g.items.length > 0);
}

function AdminPermissionDenied({ required }: { required: string[] }) {
  return (
    <section className="mx-auto flex min-h-[50vh] w-full max-w-2xl items-center justify-center px-4 py-12">
      <div className="rounded-[2rem] border border-red-100 bg-white p-8 text-center shadow-sm">
        <ShieldCheck className="mx-auto h-10 w-10 text-red-500" aria-hidden="true" />
        <h1 className="mt-4 text-xl font-semibold text-navy">Admin permission required</h1>
        <p className="mt-2 text-sm text-muted">
          Your admin role does not include the permission needed to view this workspace.
        </p>
        {required.length > 0 ? (
          <p className="mt-4 rounded-2xl bg-red-50 px-4 py-3 text-xs font-medium text-red-700">
            Required: {required.join(' or ')}
          </p>
        ) : null}
      </div>
    </section>
  );
}

function AdminPermissionChecking() {
  return (
    <section className="mx-auto flex min-h-[50vh] w-full max-w-sm items-center justify-center px-4 py-12">
      <div className="rounded-[2rem] border border-slate-100 bg-white p-8 text-center shadow-sm">
        <div className="mx-auto h-8 w-8 animate-spin rounded-full border-2 border-primary border-t-transparent" aria-hidden="true" />
        <p className="mt-4 text-sm font-semibold text-navy">Checking admin permissions</p>
      </div>
    </section>
  );
}

function AdminLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const { loading, user } = useAuth();
  const perms = user?.adminPermissions;

  const filteredNavItems = useMemo(() => filterNavByPermissions(adminNavItems, perms), [perms]);
  const filteredNavGroups = useMemo(() => filterGroupsByPermissions(adminNavGroups, perms), [perms]);
  const filteredMobileNavItems = useMemo(() => filterNavByPermissions(adminMobileNavItems, perms), [perms]);
  const filteredMobileMenuSections = useMemo(() => filterSectionsByPermissions(adminMobileMenuSections, perms), [perms]);

  const pageTitle = getAdminPageTitle(pathname);
  const requiredRoutePermissions = useMemo(() => getAdminRoutePermissions(pathname), [pathname]);
  const canAccessRoute = useMemo(() => canAccessAdminRoute(perms, pathname), [perms, pathname]);

  return (
    <AdminDashboardShell
      pageTitle={pageTitle}
      navItems={filteredNavItems}
      navGroups={filteredNavGroups}
      mobileNavItems={filteredMobileNavItems}
      mobileMenuSections={filteredMobileMenuSections}
      requiredRole="admin"
    >
      <PrivilegedMfaBanner key="privileged-mfa-banner" />
      {loading ? (
        <AdminPermissionChecking />
      ) : canAccessRoute ? (
        Children.map(children, (child, index) =>
          isValidElement(child) ? cloneElement(child, { key: child.key ?? `admin-children-${index}` }) : child,
        )
      ) : (
        <AdminPermissionDenied required={requiredRoutePermissions} />
      )}
    </AdminDashboardShell>
  );
}

export default function AdminLayout({ children }: { children: React.ReactNode }) {
  return (
    <div className="admin-compact-theme text-sm leading-snug">
      <AdminLayoutContent>{children}</AdminLayoutContent>
    </div>
  );
}
