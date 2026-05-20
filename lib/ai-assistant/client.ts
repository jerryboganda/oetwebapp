// Thin apiClient wrapper for the admin-only AI Assistant feature.
// All requests are admin-gated server-side (policies "AiAssistantUse" and
// "AiAssistantManage" in Program.cs); this client is also gated client-side
// by hasAiAssistantAccess in ./permissions.

import { apiClient } from '@/lib/api';
import type {
  ChatMessageDto,
  ChatThreadDto,
  ProviderConfigDto,
  ToolInvocationDto,
} from './types';

interface AssistantSettingsDto {
  globalEnabled: boolean;
  requireApprovalAlways: boolean;
  defaultProvider: string;
  defaultModel: string;
  lastKillSwitchAt: string | null;
  lastKillSwitchActor: string | null;
}

interface ThreadDetailDto {
  thread: ChatThreadDto;
  messages: ChatMessageDto[];
}

const NOT_IMPLEMENTED_PHASE_3 = 'Not implemented in Phase 1 (deferred to Phase 3+).';

export const aiAssistantClient = {
  // ── Chat (uses "AiAssistantUse" policy)
  listThreads: (take = 50, skip = 0) =>
    apiClient.get<ChatThreadDto[]>(`/v1/ai-assistant/threads?take=${take}&skip=${skip}`),

  getThread: (id: string) =>
    apiClient.get<ThreadDetailDto>(`/v1/ai-assistant/threads/${id}`),

  createThread: (input: { title?: string }) =>
    apiClient.post<ChatThreadDto>('/v1/ai-assistant/threads', input ?? {}),

  deleteThread: (id: string) =>
    apiClient.delete<void>(`/v1/ai-assistant/threads/${id}`),

  cancelMessage: (messageId: string) =>
    apiClient.post<{ cancelled: boolean }>(`/v1/ai-assistant/messages/${messageId}/cancel`, {}),

  // ── Tool approvals (Phase 3 — endpoint stub returns 501)
  approveToolInvocation: async (
    _invocationId: string,
    _decision: 'approve' | 'reject',
    _reason?: string,
  ): Promise<ToolInvocationDto> => {
    throw new Error(NOT_IMPLEMENTED_PHASE_3);
  },

  // ── Admin surfaces (uses "AiAssistantManage" policy)
  getSettings: () =>
    apiClient.get<AssistantSettingsDto>('/v1/admin/ai-assistant/settings'),

  toggleKillSwitch: (enabled: boolean) =>
    apiClient.post<AssistantSettingsDto>('/v1/admin/ai-assistant/kill-switch', { enabled }),

  listAllThreads: (take = 50, skip = 0) =>
    apiClient.get<ChatThreadDto[]>(`/v1/admin/ai-assistant/threads?take=${take}&skip=${skip}`),

  getAuditLog: (take = 100, skip = 0) =>
    apiClient.get<unknown[]>(`/v1/admin/ai-assistant/audit?take=${take}&skip=${skip}`),

  getUsage: (take = 100, skip = 0) =>
    apiClient.get<unknown[]>(`/v1/admin/ai-assistant/usage?take=${take}&skip=${skip}`),

  listProviders: () =>
    apiClient.get<ProviderConfigDto[]>('/v1/admin/ai-assistant/providers'),

  getIndexingStatus: () =>
    apiClient.get<{ state: string; indexedChunkCount: number }>('/v1/admin/ai-assistant/indexing/status'),

  // ── Phase 2+ — endpoints stubbed server-side as 501.
  upsertProvider: async (_input: Partial<ProviderConfigDto> & { secret?: string }): Promise<ProviderConfigDto> => {
    throw new Error(NOT_IMPLEMENTED_PHASE_3);
  },
  deleteProvider: async (_id: string): Promise<void> => {
    throw new Error(NOT_IMPLEMENTED_PHASE_3);
  },
  triggerReindex: async (_full: boolean): Promise<void> => {
    throw new Error(NOT_IMPLEMENTED_PHASE_3);
  },
};
