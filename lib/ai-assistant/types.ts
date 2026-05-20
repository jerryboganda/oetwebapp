/**
 * AI Assistant types — shared between frontend hooks, API layer, and components.
 */

import type { UserRole } from '@/lib/types/auth';

// ─── Assistant Role ─────────────────────────────────────────────────────────

export type AssistantRole = 'admin' | 'expert' | 'learner';

// ─── Message Types ──────────────────────────────────────────────────────────

export type MessageRole = 'user' | 'assistant' | 'system' | 'tool';

export interface AiAssistantMessage {
  id: string;
  threadId: string;
  role: MessageRole;
  content: string;
  createdAt: string;
  toolCalls?: ToolCallInfo[];
  metadata?: Record<string, unknown>;
}

/** Backwards-compatible alias */
export type AiMessage = AiAssistantMessage;

export interface ToolCallInfo {
  id: string;
  toolName: string;
  arguments: string;
  result?: string;
  isError?: boolean;
}

export interface AiAssistantThread {
  id: string;
  title: string | null;
  role: AssistantRole;
  createdAt: string;
  updatedAt: string;
  archived: boolean;
  messageCount: number;
}

/** Backwards-compatible alias */
export type AiThread = AiAssistantThread;

// ─── Stream Events (SignalR hub events) ─────────────────────────────────────

export interface TextDeltaEvent {
  type: 'TextDelta';
  text: string;
}

export interface ToolCallStartEvent {
  type: 'ToolCallStart';
  toolCallId: string;
  toolName: string;
  args: string;
}

export interface ToolCallResultEvent {
  type: 'ToolCallResult';
  toolCallId: string;
  result: string;
  isError: boolean;
}

export interface TurnCompleteEvent {
  type: 'TurnComplete';
  messageId: string;
  fullText: string;
}

export interface TurnErrorEvent {
  type: 'TurnError';
  code: string;
  message: string;
}

export type StreamEvent =
  | TextDeltaEvent
  | ToolCallStartEvent
  | ToolCallResultEvent
  | TurnCompleteEvent
  | TurnErrorEvent;

// ─── Legacy stream event types (kept for backward compat with tests) ────────

export type StreamEventType =
  | 'message_start'
  | 'content_delta'
  | 'content_done'
  | 'tool_call_start'
  | 'tool_call_done'
  | 'error'
  | 'done';

export interface MessageStartEvent {
  type: 'message_start';
  messageId: string;
  threadId: string;
}

export interface ContentDeltaEvent {
  type: 'content_delta';
  messageId: string;
  delta: string;
}

export interface ContentDoneEvent {
  type: 'content_done';
  messageId: string;
  content: string;
}

export interface LegacyToolCallStartEvent {
  type: 'tool_call_start';
  messageId: string;
  toolCallId: string;
  toolName: string;
  arguments: string;
}

export interface ToolCallDoneEvent {
  type: 'tool_call_done';
  messageId: string;
  toolCallId: string;
  result: string;
}

export interface StreamErrorEvent {
  type: 'error';
  code: string;
  message: string;
}

export interface StreamDoneEvent {
  type: 'done';
  messageId: string;
}

// ─── Streaming State ────────────────────────────────────────────────────────

export type StreamingStatus = 'idle' | 'thinking' | 'streaming' | 'tool-calling';

// ─── Type Guards ────────────────────────────────────────────────────────────

export function isTextDelta(event: StreamEvent): event is TextDeltaEvent {
  return event.type === 'TextDelta';
}

export function isToolCallStartEvent(event: StreamEvent): event is ToolCallStartEvent {
  return event.type === 'ToolCallStart';
}

export function isToolCallResultEvent(event: StreamEvent): event is ToolCallResultEvent {
  return event.type === 'ToolCallResult';
}

export function isTurnComplete(event: StreamEvent): event is TurnCompleteEvent {
  return event.type === 'TurnComplete';
}

export function isTurnError(event: StreamEvent): event is TurnErrorEvent {
  return event.type === 'TurnError';
}

export function isContentDelta(event: { type: string }): boolean {
  return event.type === 'content_delta';
}

export function isToolCallStart(event: { type: string }): boolean {
  return event.type === 'tool_call_start' || event.type === 'ToolCallStart';
}

export function isStreamError(event: { type: string }): boolean {
  return event.type === 'error' || event.type === 'TurnError';
}

export function isStreamDone(event: { type: string }): boolean {
  return event.type === 'done' || event.type === 'TurnComplete';
}

export function isUserMessage(message: AiAssistantMessage): boolean {
  return message.role === 'user';
}

export function isAssistantMessage(message: AiAssistantMessage): boolean {
  return message.role === 'assistant';
}

export function isToolMessage(message: AiAssistantMessage): boolean {
  return message.role === 'tool';
}

// ─── Permissions ────────────────────────────────────────────────────────────

export type AiAssistantPermission =
  | 'ai_assistant:chat'
  | 'ai_assistant:threads'
  | 'ai_assistant:tool_access'
  | 'ai_assistant:admin_config';

export interface AiAssistantAccess {
  canChat: boolean;
  canListThreads: boolean;
  canUseTools: boolean;
  canConfigureAssistant: boolean;
}

// ─── Role mapping helper type ───────────────────────────────────────────────

export type { UserRole };
