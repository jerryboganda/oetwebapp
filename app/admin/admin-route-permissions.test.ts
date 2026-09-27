import { readdirSync, statSync } from 'node:fs';
import path from 'node:path';
import {
  AdminPermission,
  canAccessAdminRoute,
  getAdminRoutePermissions,
  hasExplicitAdminRoutePermission,
} from '@/lib/admin-permissions';

function walk(dir: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const fullPath = path.join(dir, entry);
    if (statSync(fullPath).isDirectory()) {
      return walk(fullPath);
    }
    return fullPath;
  });
}

function routeFromPage(file: string): string {
  const relative = path.relative(path.join(process.cwd(), 'app'), file).replaceAll(path.sep, '/');
  const route = `/${relative.replace(/\/page\.tsx$/, '')}`
    .replace(/\([^)]+\)\//g, '')
    .replace(/\/\[[^\]]+\]/g, '/:param');
  return route === '/admin/page.tsx' ? '/admin' : route;
}

describe('admin route permission metadata', () => {
  it('maps every concrete admin page to an explicit permission rule', () => {
    const adminDir = path.join(process.cwd(), 'app', 'admin');
    const missing = walk(adminDir)
      .filter((file) => file.endsWith(`${path.sep}page.tsx`))
      .map(routeFromPage)
      .filter((route) => !hasExplicitAdminRoutePermission(route))
      .sort();

    expect(missing).toEqual([]);
  });

  it('denies deep-linked admin pages when the user lacks the route permission', () => {
    expect(canAccessAdminRoute([AdminPermission.UsersRead], '/admin/billing')).toBe(false);
    expect(canAccessAdminRoute([AdminPermission.BillingRead], '/admin/billing')).toBe(true);
    expect(canAccessAdminRoute([AdminPermission.SystemAdmin], '/admin/flags')).toBe(true);
  });

  it('limits every Agent Console route to system_admin (the API adds the owner + unlock checks)', () => {
    for (const route of [
      '/admin/agent-console',
      '/admin/agent-console/settings',
      '/admin/agent-console/history',
      '/admin/agent-console/01J9ZQ3V4W5X6Y7Z8A9B0C1D2E',
    ]) {
      expect(hasExplicitAdminRoutePermission(route)).toBe(true);
      expect(getAdminRoutePermissions(route)).toEqual([AdminPermission.SystemAdmin]);
      expect(canAccessAdminRoute([AdminPermission.AiConfig, AdminPermission.UsersWrite], route)).toBe(false);
      expect(canAccessAdminRoute([AdminPermission.SystemAdmin], route)).toBe(true);
    }
  });
});
