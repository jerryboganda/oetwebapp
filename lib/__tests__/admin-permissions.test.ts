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

  it('allows content workflow permissions to open the content hub', () => {
    expect(sidebarPermissionMap['/admin/content']).toEqual([
      AdminPermission.ContentRead,
      AdminPermission.ContentWrite,
      AdminPermission.ContentPublish,
      AdminPermission.ContentEditorReview,
      AdminPermission.ContentPublisherApproval,
    ]);

    const required = sidebarPermissionMap['/admin/content'];
    expect(hasPermission([AdminPermission.ContentRead], ...required)).toBe(true);
    expect(hasPermission([AdminPermission.ContentWrite], ...required)).toBe(true);
    expect(hasPermission([AdminPermission.ContentPublish], ...required)).toBe(true);
    expect(hasPermission([AdminPermission.ContentEditorReview], ...required)).toBe(true);
    expect(hasPermission([AdminPermission.ContentPublisherApproval], ...required)).toBe(true);
    expect(hasPermission([AdminPermission.BillingRead], ...required)).toBe(false);
  });

  it('requires content:read for the content library', () => {
    expect(sidebarPermissionMap['/admin/content/library']).toEqual([AdminPermission.ContentRead]);
  });

  it('keeps consolidated content hub child workflows mapped to granular permissions', () => {
    expect(sidebarPermissionMap['/admin/content/import']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/papers/import']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/generation']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/dedup']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/analytics']).toEqual([AdminPermission.ContentRead]);
    expect(sidebarPermissionMap['/admin/content/quality']).toEqual([AdminPermission.ContentRead]);
    expect(sidebarPermissionMap['/admin/content/grammar/topics']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/grammar/ai-draft']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/grammar/lessons/new']).toEqual([AdminPermission.ContentWrite]);
    expect(sidebarPermissionMap['/admin/content/publish-requests']).toEqual([
      AdminPermission.ContentEditorReview,
      AdminPermission.ContentPublisherApproval,
      AdminPermission.ContentPublish,
    ]);
  });

  it('requires manage_permissions for permissions page', () => {
    expect(sidebarPermissionMap['/admin/permissions']).toEqual([AdminPermission.ManagePermissions]);
  });

  it('requires system_admin for launch readiness', () => {
    expect(sidebarPermissionMap['/admin/launch-readiness']).toEqual([AdminPermission.SystemAdmin]);
  });

  it('maps every sidebar entry to a known permission value', () => {
    const allPerms = Object.values(AdminPermission);
    for (const [, perms] of Object.entries(sidebarPermissionMap)) {
      for (const p of perms) {
        expect(allPerms).toContain(p);
      }
    }
  });
});
