'use client';

import { PrivilegedMfaBanner } from '@/components/auth/privileged-mfa-banner';
import { AdminDashboardShell, type MobileMenuSection } from '@/components/layout';
import type { NavGroup } from '@/components/layout/sidebar';
import {
  adminMobileMenuSections,
  adminMobileNavItems,
  adminNavGroups,
  adminNavItems,
  getAdminPageTitle,
  type AdminNavItem,
} from '@/lib/admin-navigation';
import {
  canAccessAdminRoute,
  getAdminRoutePermissions,
  hasPermission,
  sidebarPermissionMap,
} from '@/lib/admin-permissions';
import { useAuth } from '@/contexts/auth-context';
import { Children, isValidElement, cloneElement, useMemo } from 'react';
import { ShieldCheck } from 'lucide-react';
import { usePathname, useSearchParams } from 'next/navigation';

function filterNavByPermissions(items: readonly AdminNavItem[], perms: string[] | null | undefined): AdminNavItem[] {
  return items.filter((item) => {
    const required = item.requiredPermissions ?? sidebarPermissionMap[item.href];
    if (!required) return true; // no permission required (e.g. dashboard)
    return hasPermission(perms, ...required);
  });
}

function filterSectionsByPermissions(
  sections: MobileMenuSection[],
  perms: string[] | null | undefined,
): MobileMenuSection[] {
  return sections
    .map((s) => ({ ...s, items: filterNavByPermissions(s.items as AdminNavItem[], perms) }))
    .filter((s) => s.items.length > 0);
}

function filterGroupsByPermissions(
  groups: readonly NavGroup[],
  perms: string[] | null | undefined,
): NavGroup[] {
  return groups
    .map((g) => ({ ...g, items: filterNavByPermissions(g.items as AdminNavItem[], perms) }))
    .filter((g) => g.items.length > 0);
}

function AdminPermissionDenied({ required }: { required: string[] }) {
  return (
    <section className="mx-auto flex min-h-[50vh] w-full max-w-2xl items-center justify-center px-4 py-12">
      <div className="rounded-[2rem] border border-danger/20 bg-surface p-8 text-center shadow-sm">
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
      <div className="rounded-[2rem] border border-border bg-surface p-8 text-center shadow-sm">
        <div className="mx-auto h-8 w-8 animate-spin rounded-full border-2 border-primary border-t-transparent" aria-hidden="true" />
        <p className="mt-4 text-sm font-semibold text-navy">Checking admin permissions</p>
      </div>
    </section>
  );
}

function AdminLayoutContent({ children }: { children: React.ReactNode }) {
  const pathname = usePathname();
  const searchParams = useSearchParams();
  const { loading, user } = useAuth();
  const perms = user?.adminPermissions;

  const routePath = useMemo(() => {
    const query = searchParams?.toString();
    return query ? `${pathname ?? '/admin'}?${query}` : pathname;
  }, [pathname, searchParams]);

  const filteredNavItems = useMemo(() => filterNavByPermissions(adminNavItems, perms), [perms]);
  const filteredNavGroups = useMemo(() => filterGroupsByPermissions(adminNavGroups, perms), [perms]);
  const filteredMobileNavItems = useMemo(() => filterNavByPermissions(adminMobileNavItems, perms), [perms]);
  const filteredMobileMenuSections = useMemo(() => filterSectionsByPermissions(adminMobileMenuSections, perms), [perms]);

  const pageTitle = getAdminPageTitle(pathname);
  const requiredRoutePermissions = useMemo(() => getAdminRoutePermissions(routePath), [routePath]);
  const canAccessRoute = useMemo(() => canAccessAdminRoute(perms, routePath), [perms, routePath]);

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
