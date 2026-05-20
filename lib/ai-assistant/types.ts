// Mirror of backend AI Assistant contracts (see
// backend/src/OetLearner.Api/Contracts/AiAssistant/*).
// TODO Phase 1: keep in sync; consider zod schemas for runtime validation
// at the apiClient boundary.

export type AiProviderKind =
  | 'GitHubCopilot'
  | 'OpenAi'
  | 'Anthropic'
  | 'AzureOpenAi'
  | 'GitHubModels'
  | 'OpenAiCompatible'
  | 'Cloudflare'
  | 'Copilot'
  | 'Mock';

export type AiChatMessageRole = 'system' | 'user' | 'assistant' | 'tool';

export type AiToolApprovalPolicy = 'Auto' | 'RequireAdmin' | 'Never';

export interface ChatThreadDto {
  id: string;
  title: string;
  providerConfigId?: string | null;
  model?: string | null;
  isArchived: boolean;
  createdAt: string;
  updatedAt: string;
  messageCount: number;
}

export interface ChatMessageDto {
  id: string;
  threadId: string;
  role: AiChatMessageRole;
  content: string;
  toolPayloadJson?: string | null;
  promptTokens?: number | null;
  completionTokens?: number | null;
  createdAt: string;
}

export interface ProviderConfigDto {
  id: string;
  code?: string | null;
  kind: AiProviderKind;
  displayName: string;
  endpoint?: string | null;
  defaultModel?: string | null;
  allowedModelsCsv?: string | null;
  isEnabled: boolean;
  isDefault: boolean;
  hasSecret: boolean;
}

export interface ToolInvocationDto {
  id: string;
  threadId: string;
  messageId: string;
  toolName: string;
  argsJson: string;
  approvalPolicy: AiToolApprovalPolicy;
  approvalDecision?: boolean | null;
  rejectionReason?: string | null;
  didSucceed: boolean;
  resultJson?: string | null;
  createdAt: string;
  completedAt?: string | null;
}

export type StreamFrame =
  | { type: 'MessageStart'; threadId: string; messageId: string; role: AiChatMessageRole }
  | { type: 'TokenDelta'; threadId: string; messageId: string; delta: string }
  | { type: 'ToolCallStart'; threadId: string; messageId: string; invocationId: string; toolName: string }
  | { type: 'ToolCallDelta'; threadId: string; messageId: string; invocationId: string; delta: string }
  | { type: 'ToolCallResult'; threadId: string; messageId: string; invocationId: string; success: boolean; resultJson?: string }
  | { type: 'ApprovalRequest'; threadId: string; messageId: string; invocationId: string; toolName: string; argsJson: string }
  | { type: 'MessageEnd'; threadId: string; messageId: string; promptTokens?: number; completionTokens?: number }
  | { type: 'Error'; threadId: string; messageId: string; message: string; code?: string };
