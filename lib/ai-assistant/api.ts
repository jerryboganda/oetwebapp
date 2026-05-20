/**
 * AI Assistant API client — CRUD for threads, messages, and assistant config.
 * Uses the shared apiClient from @/lib/api for consistent auth handling.
 */

import { apiClient } from '@/lib/api';
import type { AiAssistantMessage, AiAssistantThread, AssistantRole } from './types';

// Re-export legacy aliases for backward compat
export type { AiAssistantMessage as AiMessage, AiAssistantThread as AiThread } from './types';

// ─── Response Types ─────────────────────────────────────────────────────────

export interface ThreadListResponse {
  threads: AiAssistantThread[];
  total: number;
  page: number;
  pageSize: number;
}

export interface MessageListResponse {
  messages: AiAssistantMessage[];
  total: number;
}

// ─── Thread API ─────────────────────────────────────────────────────────────

export async function createThread(role?: AssistantRole, title?: string): Promise<AiAssistantThread> {
  return apiClient.post<AiAssistantThread>('/v1/ai-assistant/threads', {
    role: role ?? 'learner',
    title: title ?? null,
  });
}

export async function listThreads(page = 1, pageSize = 20): Promise<ThreadListResponse> {
  return apiClient.get<ThreadListResponse>(
    `/v1/ai-assistant/threads?page=${page}&pageSize=${pageSize}`,
  );
}

export async function getThread(threadId: string): Promise<AiAssistantThread> {
  return apiClient.get<AiAssistantThread>(`/v1/ai-assistant/threads/${threadId}`);
}

export async function archiveThread(threadId: string): Promise<void> {
  return apiClient.delete(`/v1/ai-assistant/threads/${threadId}`);
}

// ─── Messages API ───────────────────────────────────────────────────────────

export async function getMessages(threadId: string, before?: string): Promise<MessageListResponse> {
  const params = before ? `?before=${encodeURIComponent(before)}` : '';
  return apiClient.get<MessageListResponse>(
    `/v1/ai-assistant/threads/${threadId}/messages${params}`,
  );
}

export async function sendMessage(threadId: string, content: string): Promise<AiAssistantMessage> {
  return apiClient.post<AiAssistantMessage>(`/v1/ai-assistant/threads/${threadId}/messages`, {
    content,
  });
}
