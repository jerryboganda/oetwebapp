/**
 * Admin governance: RBAC permissions, permission templates, content
 * publishing workflow, webhook monitoring — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export async function fetchAllPermissions() {
  return apiRequest('/v1/admin/permissions');
}

export async function fetchAdminPermissions(userId: string) {
  return apiRequest(`/v1/admin/permissions/${encodeURIComponent(userId)}`);
}

export async function updateAdminPermissions(userId: string, permissions: string[]) {
  return apiRequest(`/v1/admin/permissions/${encodeURIComponent(userId)}`, {
    method: 'PUT',
    body: JSON.stringify({ permissions }),
  });
}

export async function fetchPermissionTemplates() {
  return apiRequest('/v1/admin/permission-templates');
}

export async function createPermissionTemplate(name: string, description: string, permissions: string[]) {
  return apiRequest('/v1/admin/permission-templates', {
    method: 'POST',
    body: JSON.stringify({ name, description, permissions }),
  });
}

export async function deletePermissionTemplate(id: string) {
  return apiRequest(`/v1/admin/permission-templates/${encodeURIComponent(id)}`, {
    method: 'DELETE',
  });
}

export async function applyPermissionTemplate(userId: string, templateId: string) {
  return apiRequest(`/v1/admin/users/${encodeURIComponent(userId)}/apply-template/${encodeURIComponent(templateId)}`, {
    method: 'POST',
  });
}

export async function requestContentPublish(contentId: string, note?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/request-publish`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function submitContentForReview(contentId: string, note?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/submit-for-review`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function editorApproveContent(contentId: string, notes?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/editor-approve`, {
    method: 'POST',
    body: JSON.stringify({ notes }),
  });
}

export async function editorRejectContent(contentId: string, reason: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/editor-reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function publisherApproveContent(contentId: string, notes?: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/publisher-approve`, {
    method: 'POST',
    body: JSON.stringify({ notes }),
  });
}

export async function publisherRejectContent(contentId: string, reason: string) {
  return apiRequest(`/v1/admin/content/${encodeURIComponent(contentId)}/publisher-reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });
}

export async function fetchPendingReviewContent(params?: { stage?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.stage) qs.set('stage', params.stage);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/content/pending-review?${qs}`);
}

export async function fetchPublishRequests(params?: { status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/publish-requests?${qs}`);
}

export async function approvePublishRequest(requestId: string, note?: string) {
  return apiRequest(`/v1/admin/publish-requests/${encodeURIComponent(requestId)}/approve`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function rejectPublishRequest(requestId: string, note?: string) {
  return apiRequest(`/v1/admin/publish-requests/${encodeURIComponent(requestId)}/reject`, {
    method: 'POST',
    body: JSON.stringify({ note }),
  });
}

export async function fetchWebhookEvents(params?: { gateway?: string; status?: string; page?: number; pageSize?: number }) {
  const qs = new URLSearchParams();
  if (params?.gateway) qs.set('gateway', params.gateway);
  if (params?.status) qs.set('status', params.status);
  qs.set('page', String(params?.page ?? 1));
  qs.set('pageSize', String(params?.pageSize ?? 20));
  return apiRequest(`/v1/admin/webhooks?${qs}`);
}

export async function fetchWebhookSummary() {
  return apiRequest('/v1/admin/webhooks/summary');
}

export async function retryWebhook(eventId: string) {
  return apiRequest(`/v1/admin/webhooks/${encodeURIComponent(eventId)}/retry`, {
    method: 'POST',
  });
}
