/**
 * AI Assistant API client — CRUD for threads, messages, and assistant config.
 * Uses the shared apiClient from @/lib/api for consistent auth handling.
 */

import { apiClient } from '@/lib/api';
import type { AiAssistantMessage, AiAssistantThread, AssistantRole, MessageCitation } from './types';

// Re-export legacy aliases for backward compat
export type { AiAssistantMessage as AiMessage, AiAssistantThread as AiThread } from './types';

// ─── Response Types ─────────────────────────────────────────────────────────

// NOTE: the learner endpoints return BARE ARRAYS and page with `skip`/`take`.
// This client previously declared `{threads,total,page,pageSize}` and
// `{messages,total}` wrappers and sent `?page=&pageSize=`, so `result.threads`
// and `result.messages` were always undefined and paging was ignored. The
// shapes below match `AiAssistantEndpoints` as deployed.

// ─── Thread API ─────────────────────────────────────────────────────────────

export async function createThread(role?: AssistantRole, title?: string): Promise<AiAssistantThread> {
  return apiClient.post<AiAssistantThread>('/v1/ai-assistant/threads', {
    role: role ?? 'learner',
    title: title ?? null,
  });
}

/** Rename a conversation (1–256 chars). Ownership-checked server-side. */
export async function renameThread(threadId: string, title: string): Promise<void> {
  await apiClient.patch(`/v1/ai-assistant/threads/${encodeURIComponent(threadId)}`, { title });
}

/** Pin a UBAG model override onto a conversation; null clears to default. */
export async function setThreadModel(threadId: string, model: string | null): Promise<void> {
  await apiClient.patch(`/v1/ai-assistant/threads/${encodeURIComponent(threadId)}/model`, {
    model: model ?? null,
  });
}

export interface AssistantModelOption {
  provider: string;
  models: string[];
}

/** Models the chat model-picker may offer (UBAG catalog + board composite). */
export async function listAssistantModels(): Promise<AssistantModelOption> {
  return apiClient.get<AssistantModelOption>('/v1/ai-assistant/models');
}

export async function listThreads(skip = 0, take = 20): Promise<AiAssistantThread[]> {
  const threads = await apiClient.get<AiAssistantThread[]>(
    `/v1/ai-assistant/threads?skip=${skip}&take=${take}`,
  );
  return Array.isArray(threads) ? threads : [];
}

export async function getThread(threadId: string): Promise<AiAssistantThread> {
  return apiClient.get<AiAssistantThread>(`/v1/ai-assistant/threads/${threadId}`);
}

export async function archiveThread(threadId: string): Promise<void> {
  return apiClient.delete(`/v1/ai-assistant/threads/${threadId}`);
}

// ─── Messages API ───────────────────────────────────────────────────────────

/** Raw message row as the server sends it. Citations arrive as a JSON string. */
interface ApiMessage extends Omit<AiAssistantMessage, 'threadId' | 'citations'> {
  citationsJson?: string | null;
}

export async function getMessages(
  threadId: string,
  skip = 0,
  take = 50,
): Promise<AiAssistantMessage[]> {
  const rows = await apiClient.get<ApiMessage[]>(
    `/v1/ai-assistant/threads/${threadId}/messages?skip=${skip}&take=${take}`,
  );
  if (!Array.isArray(rows)) return [];

  return rows.map((row) => ({
    ...row,
    threadId,
    citations: parseCitations(row.citationsJson),
  }));
}

/**
 * Companion citations are stored as JSON on the message. A malformed value must
 * never take the transcript down — an answer without its sources is still an
 * answer, so this degrades to undefined rather than throwing.
 */
function parseCitations(raw?: string | null): MessageCitation[] | undefined {
  if (!raw) return undefined;
  try {
    const parsed: unknown = JSON.parse(raw);
    return Array.isArray(parsed) ? (parsed as MessageCitation[]) : undefined;
  } catch {
    return undefined;
  }
}

// There is no POST /threads/{id}/messages route: a learner message is sent over
// SignalR via `StartTurn`, which is what starts the streamed turn. A REST
// `sendMessage` helper here would 404, so it deliberately does not exist.
