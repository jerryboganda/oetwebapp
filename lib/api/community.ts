/**
 * Engagement cluster: adaptive difficulty, predictions, community forum,
 * study groups, community moderation — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export async function fetchSkillProfile(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/adaptive/skill-profile${p}`);
}

export async function fetchAdaptiveContent(examTypeCode: string, subtestCode: string, count = 5) {
  return apiRequest(`/v1/adaptive/content?examTypeCode=${examTypeCode}&subtestCode=${subtestCode}&count=${count}`);
}

export async function fetchPredictions(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/predictions${p}`);
}

export async function fetchPrediction(examTypeCode: string, subtestCode: string) {
  return apiRequest(`/v1/predictions/${encodeURIComponent(examTypeCode)}/${encodeURIComponent(subtestCode)}`);
}

export async function requestPredictionComputation(examTypeCode: string, subtestCode: string) {
  return apiRequest('/v1/predictions/compute', {
    method: 'POST',
    body: JSON.stringify({ examTypeCode, subtestCode }),
  });
}

export async function fetchForumCategories(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/community/categories${p}`);
}

export async function fetchForumThreads(categoryId?: string, page = 1, pageSize = 20) {
  const p = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (categoryId) p.set('categoryId', categoryId);
  return apiRequest(`/v1/community/threads?${p}`);
}

export async function fetchAdminCommunityThreads(categoryId?: string, page = 1, pageSize = 20) {
  const p = new URLSearchParams({ page: String(page), pageSize: String(pageSize) });
  if (categoryId) p.set('categoryId', categoryId);
  return apiRequest(`/v1/admin/community/threads?${p}`);
}

export async function fetchForumThread(threadId: string) {
  return apiRequest(`/v1/community/threads/${encodeURIComponent(threadId)}`);
}

export async function createForumThread(payload: { categoryId: string; title: string; body: string }) {
  return apiRequest('/v1/community/threads', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function fetchThreadReplies(threadId: string, page = 1, pageSize = 20) {
  return apiRequest(`/v1/community/threads/${encodeURIComponent(threadId)}/replies?page=${page}&pageSize=${pageSize}`);
}

export async function createReply(threadId: string, body: string) {
  return apiRequest(`/v1/community/threads/${encodeURIComponent(threadId)}/replies`, {
    method: 'POST',
    body: JSON.stringify({ body }),
  });
}

export async function fetchStudyGroups(examTypeCode?: string) {
  const p = examTypeCode ? `?examTypeCode=${examTypeCode}` : '';
  return apiRequest(`/v1/community/study-groups${p}`);
}

export async function createStudyGroup(payload: { name: string; description: string; examTypeCode: string; isPublic: boolean }) {
  return apiRequest('/v1/community/study-groups', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function joinStudyGroup(groupId: string) {
  return apiRequest(`/v1/community/study-groups/${encodeURIComponent(groupId)}/join`, { method: 'POST' });
}

export async function pinCommunityThread(threadId: string, isPinned: boolean) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}/pin`, {
    method: 'PATCH',
    body: JSON.stringify({ isPinned }),
  });
}

export async function lockCommunityThread(threadId: string, isLocked: boolean) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}/lock`, {
    method: 'PATCH',
    body: JSON.stringify({ isLocked }),
  });
}

export async function adminDeleteCommunityThread(threadId: string) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}`, {
    method: 'DELETE',
  });
}

export async function adminDeleteCommunityReply(threadId: string, replyId: string) {
  return apiRequest(`/v1/admin/community/threads/${encodeURIComponent(threadId)}/replies/${encodeURIComponent(replyId)}`, {
    method: 'DELETE',
  });
}
