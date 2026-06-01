import { describe, expect, it } from 'vitest';
import {
  adminMobileMenuSections,
  adminMobileNavItems,
  adminNavGroups,
  adminNavItems,
  getAdminPageTitle,
} from '../admin-navigation';
import { AdminPermission, sidebarPermissionMap } from '../admin-permissions';

describe('admin navigation registry', () => {
  it('uses the selected six top-level admin workspaces', () => {
    expect(adminNavGroups.map((group) => group.label)).toEqual([
      'Command Center',
      'Content & Exams',
      'Quality & Analytics',
      'People & Access',
      'AI & Automation',
      'Commerce & Settings',
    ]);
  });

  it('keeps OET subtest hubs together under Content & Exams', () => {
    const contentGroup = adminNavGroups.find((group) => group.label === 'Content & Exams');

    expect(contentGroup?.items.map((item) => [item.label, item.href])).toEqual([
      ['Content Home', '/admin/content'],
      ['Reading', '/admin/content/reading'],
      ['Listening', '/admin/content/listening'],
      ['Writing', '/admin/writing'],
      ['Speaking', '/admin/speaking'],
      ['Mocks', '/admin/content/mocks'],
      ['Recalls', '/admin/recalls'],
    ]);
  });

  it('derives mobile menus and bottom navigation from the same registry', () => {
    expect(adminMobileMenuSections.map((section) => section.label)).toEqual(adminNavGroups.map((group) => group.label));
    expect(adminMobileNavItems.map((item) => item.href)).toEqual([
      '/admin',
      '/admin/content',
      '/admin/review-ops',
      '/admin/users',
      '/admin/ai-usage',
      '/admin/billing',
    ]);
  });

  it('keeps nav permissions explicit and aligned with the sidebar permission map', () => {
    const navHrefs = new Set<string>();

    for (const item of adminNavItems) {
      expect(navHrefs.has(item.href)).toBe(false);
      navHrefs.add(item.href);

      const registryPermissions = item.requiredPermissions ?? [];
      const sidebarPermissions = sidebarPermissionMap[item.href] ?? [];
      expect(registryPermissions).toEqual(sidebarPermissions);
    }
  });

  it('maps consolidated page titles from the registry', () => {
    expect(getAdminPageTitle('/admin')).toBe('Operations');
    expect(getAdminPageTitle('/admin/content/reading/paper-1/questions')).toBe('Reading');
    expect(getAdminPageTitle('/admin/content/listening/paper-1/structure')).toBe('Listening');
    expect(getAdminPageTitle('/admin/writing/options')).toBe('Writing AI Options');
    expect(getAdminPageTitle('/admin/speaking/recordings/audit')).toBe('Speaking Recording Audit');
    expect(getAdminPageTitle('/admin/content/mocks/wizard/bundle-1/reading')).toBe('Mocks');
  });

  it('uses known admin permission constants in registry metadata', () => {
    const allPermissions = Object.values(AdminPermission);

    for (const item of adminNavItems) {
      for (const permission of item.requiredPermissions ?? []) {
        expect(allPermissions).toContain(permission);
      }
    }
  });
});