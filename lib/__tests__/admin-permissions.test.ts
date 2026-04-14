import { describe, it, expect } from 'vitest';
import { AdminPermission, hasPermission, sidebarPermissionMap } from '../admin-permissions';

describe('hasPermission', () => {
  it('returns false for null/undefined permissions', () => {
    expect(hasPermission(null, AdminPermission.ContentRead)).toBe(false);
    expect(hasPermission(undefined, AdminPermission.ContentRead)).toBe(false);
  });

  it('returns false for empty permissions array', () => {
    expect(hasPermission([], AdminPermission.ContentRead)).toBe(false);
  });

  it('returns true when user has the required permission', () => {
    expect(hasPermission([AdminPermission.ContentRead], AdminPermission.ContentRead)).toBe(true);
  });

  it('returns false when user lacks the required permission', () => {
    expect(hasPermission([AdminPermission.BillingRead], AdminPermission.ContentRead)).toBe(false);
  });

  it('returns true when user has any one of multiple required permissions', () => {
    expect(
      hasPermission(
        [AdminPermission.BillingRead],
        AdminPermission.ContentRead,
        AdminPermission.BillingRead,
      ),
    ).toBe(true);
  });

  it('system_admin satisfies any permission check', () => {
    expect(hasPermission([AdminPermission.SystemAdmin], AdminPermission.ContentRead)).toBe(true);
    expect(hasPermission([AdminPermission.SystemAdmin], AdminPermission.AuditLogs)).toBe(true);
  });
});

describe('sidebarPermissionMap', () => {
  it('does not require a permission for the dashboard', () => {
    expect(sidebarPermissionMap['/admin']).toBeUndefined();
  });

  it('requires content:read for the content library', () => {
    expect(sidebarPermissionMap['/admin/content']).toEqual([AdminPermission.ContentRead]);
  });

  it('requires system_admin for permissions page', () => {
    expect(sidebarPermissionMap['/admin/permissions']).toEqual([AdminPermission.SystemAdmin]);
  });

  it('maps every sidebar entry to a known permission value', () => {
    const allPerms = Object.values(AdminPermission);
    for (const [href, perms] of Object.entries(sidebarPermissionMap)) {
      for (const p of perms) {
        expect(allPerms).toContain(p);
      }
    }
  });
});
