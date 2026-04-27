/** Backend admin permission constants — must match AdminPermissions in AuthEntities.cs */
export const AdminPermission = {
  ContentRead: 'content:read',
  ContentWrite: 'content:write',
  ContentPublish: 'content:publish',
  ContentEditorReview: 'content:editor_review',
  ContentPublisherApproval: 'content:publisher_approval',
  BillingRead: 'billing:read',
  BillingWrite: 'billing:write',
  UsersRead: 'users:read',
  UsersWrite: 'users:write',
  ReviewOps: 'review_ops',
  QualityAnalytics: 'quality_analytics',
  AiConfig: 'ai_config',
  FeatureFlags: 'feature_flags',
  AuditLogs: 'audit_logs',
  SystemAdmin: 'system_admin',
} as const;

/**
 * Check whether the user has at least one of the required permissions.
 * `system_admin` is an implicit super-permission that satisfies any check.
 */
export function hasPermission(
  userPermissions: string[] | null | undefined,
  ...required: string[]
): boolean {
  if (!userPermissions || userPermissions.length === 0) return false;
  if (userPermissions.includes(AdminPermission.SystemAdmin)) return true;
  return required.some((p) => userPermissions.includes(p));
}

/**
 * Map from admin sidebar `href` to the permission(s) required to see it.
 * Items not listed here are always visible (e.g. the dashboard).
 */
export const sidebarPermissionMap: Record<string, string[]> = {
  '/admin/content': [AdminPermission.ContentRead],
  '/admin/content/mocks': [AdminPermission.ContentRead],
  '/admin/content/papers': [AdminPermission.ContentRead],
  '/admin/taxonomy': [AdminPermission.ContentRead],
  '/admin/criteria': [AdminPermission.ContentRead],
  '/admin/ai-config': [AdminPermission.AiConfig],
  '/admin/ai-usage': [AdminPermission.AiConfig],
  '/admin/review-ops': [AdminPermission.ReviewOps],
  '/admin/notifications': [AdminPermission.SystemAdmin],
  '/admin/analytics/quality': [AdminPermission.QualityAnalytics],
  '/admin/users': [AdminPermission.UsersRead],
  '/admin/experts': [AdminPermission.UsersRead],
  '/admin/billing': [AdminPermission.BillingRead],
  '/admin/flags': [AdminPermission.FeatureFlags],
  '/admin/audit-logs': [AdminPermission.AuditLogs],
  '/admin/content/import': [AdminPermission.ContentWrite],
  '/admin/content/dedup': [AdminPermission.ContentWrite],
  '/admin/content/media': [AdminPermission.ContentRead],
  '/admin/content/generation': [AdminPermission.ContentWrite],
  '/admin/content/strategies': [AdminPermission.ContentRead],
  '/admin/marketplace-review': [AdminPermission.ContentPublish],
  '/admin/freeze': [AdminPermission.ContentPublish],
  '/admin/content/hierarchy': [AdminPermission.ContentRead],
  '/admin/permissions': [AdminPermission.SystemAdmin],
  '/admin/content/publish-requests': [AdminPermission.ContentPublish],
  '/admin/pending-review': [AdminPermission.ContentEditorReview, AdminPermission.ContentPublisherApproval, AdminPermission.ContentPublish],
  '/admin/webhooks': [AdminPermission.SystemAdmin],
  '/admin/escalations': [AdminPermission.SystemAdmin],
  '/admin/private-speaking': [AdminPermission.ReviewOps],
};
