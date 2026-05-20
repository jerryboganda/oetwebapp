'use client';

/**
 * Main React hook for the AI Assistant.
 * Manages SignalR connection lifecycle, streaming state, message history, and thread management.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
import { HubConnectionState } from '@microsoft/signalr';
import type { HubConnection } from '@microsoft/signalr';
import type {
  AiAssistantMessage,
  AiAssistantThread,
  AssistantRole,
  StreamingStatus,
  ToolCallInfo,
} from '@/lib/ai-assistant/types';
import {
  createAssistantConnection,
  registerHubCallbacks,
  invokeStartTurn,
  invokeCancelTurn,
  mapHubState,
  type AssistantConnectionState,
} from '@/lib/ai-assistant/signalr';
import {
  createThread as apiCreateThread,
  listThreads as apiListThreads,
  getMessages as apiGetMessages,
  archiveThread as apiArchiveThread,
} from '@/lib/ai-assistant/api';
import { getAssistantRole } from '@/lib/ai-assistant/permissions';
import type { UserRole } from '@/lib/types/auth';

// ─── Legacy exports for backward compatibility ──────────────────────────────

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'error';

// ─── Hook State ─────────────────────────────────────────────────────────────

export interface UseAiAssistantReturn {
  // Connection
  connectionState: AssistantConnectionState;
  isConnected: boolean;

  // Streaming
  streamingStatus: StreamingStatus;
  streamingText: string;
  activeToolCalls: ToolCallInfo[];

  // Legacy compat
  isStreaming: boolean;
  streamingContent: string;

  // Messages & Threads
  messages: AiAssistantMessage[];
  threads: AiAssistantThread[];
  activeThread: AiAssistantThread | null;
  /** @deprecated Use activeThread */
  thread: AiAssistantThread | null;

  // Actions
  sendMessage: (content: string) => Promise<void>;
  cancelTurn: () => Promise<void>;
  /** @deprecated Use cancelTurn */
  cancelStream: () => void;
  selectThread: (threadId: string) => Promise<void>;
  createNewThread: (title?: string) => Promise<AiAssistantThread | undefined>;
  archiveThread: (threadId: string) => Promise<void>;
  refreshThreads: () => Promise<void>;

  // Connection actions (legacy compat)
  connect: () => Promise<void>;
  disconnect: () => void;

  // Error
  error: string | null;
  clearError: () => void;
}

export function useAiAssistant(
  tokenOrOptions?: string | null | { token: string | null; autoConnect?: boolean },
  userRole?: UserRole | null,
): UseAiAssistantReturn {
  // Handle both call signatures for backward compatibility
  const token = typeof tokenOrOptions === 'object' && tokenOrOptions !== null
    ? tokenOrOptions.token
    : (tokenOrOptions ?? null);
  const resolvedRole = userRole ?? 'learner';

  const [connectionState, setConnectionState] = useState<AssistantConnectionState>('disconnected');
  const [streamingStatus, setStreamingStatus] = useState<StreamingStatus>('idle');
  const [streamingText, setStreamingText] = useState('');
  const [activeToolCalls, setActiveToolCalls] = useState<ToolCallInfo[]>([]);
  const [messages, setMessages] = useState<AiAssistantMessage[]>([]);
  const [threads, setThreads] = useState<AiAssistantThread[]>([]);
  const [activeThread, setActiveThread] = useState<AiAssistantThread | null>(null);
  const [error, setError] = useState<string | null>(null);

  const connectionRef = useRef<HubConnection | null>(null);
  const unsubRef = useRef<(() => void) | null>(null);
  const activeThreadRef = useRef<AiAssistantThread | null>(null);
  const activeToolCallsRef = useRef<ToolCallInfo[]>([]);

  // Keep refs in sync
  useEffect(() => { activeThreadRef.current = activeThread; }, [activeThread]);
  useEffect(() => { activeToolCallsRef.current = activeToolCalls; }, [activeToolCalls]);

  const assistantRole: AssistantRole = getAssistantRole(resolvedRole);

  // ─── Connect ────────────────────────────────────────────────────────────

  const connect = useCallback(async () => {
    if (!token) {
      setError('No authentication token');
      return;
    }

    // Cleanup previous connection
    if (unsubRef.current) {
      unsubRef.current();
      unsubRef.current = null;
    }
    if (connectionRef.current && connectionRef.current.state !== HubConnectionState.Disconnected) {
      await connectionRef.current.stop().catch(() => {});
    }

    const connection = createAssistantConnection(token, {
      onReconnecting: () => setConnectionState('reconnecting'),
      onReconnected: () => setConnectionState('connected'),
      onClose: () => setConnectionState('disconnected'),
    });

    connectionRef.current = connection;

    // Register hub callbacks
    const unsub = registerHubCallbacks(connection, {
      onTextDelta: (text: string) => {
        setStreamingStatus('streaming');
        setStreamingText((prev: string) => prev + text);
      },
      onToolCallStart: (toolCallId: string, toolName: string, args: string) => {
        setStreamingStatus('tool-calling');
        setActiveToolCalls((prev: ToolCallInfo[]) => [
          ...prev,
          { id: toolCallId, toolName, arguments: args },
        ]);
      },
      onToolCallResult: (toolCallId: string, result: string, isError: boolean) => {
        setActiveToolCalls((prev: ToolCallInfo[]) =>
          prev.map((tc) =>
            tc.id === toolCallId ? { ...tc, result, isError } : tc,
          ),
        );
        setStreamingStatus('streaming');
      },
      onTurnComplete: (messageId: string, fullText: string) => {
        const assistantMsg: AiAssistantMessage = {
          id: messageId,
          threadId: activeThreadRef.current?.id ?? '',
          role: 'assistant',
          content: fullText,
          createdAt: new Date().toISOString(),
          toolCalls: activeToolCallsRef.current.length > 0
            ? [...activeToolCallsRef.current]
            : undefined,
        };
        setMessages((prev) => [...prev, assistantMsg]);
        setStreamingStatus('idle');
        setStreamingText('');
        setActiveToolCalls([]);
      },
      onTurnError: (code: string, message: string) => {
        setError(`[${code}] ${message}`);
        setStreamingStatus('idle');
        setStreamingText('');
        setActiveToolCalls([]);
      },
    });

    unsubRef.current = unsub;

    // Start connection
    setConnectionState('connecting');
    try {
      await connection.start();
      setConnectionState('connected');
      setError(null);
    } catch (err) {
      console.error('[AI Assistant] Connection failed:', err);
      setConnectionState('disconnected');
      setError('Failed to connect to AI assistant');
    }
  }, [token]);

  const disconnect = useCallback(() => {
    if (unsubRef.current) {
      unsubRef.current();
      unsubRef.current = null;
    }
    if (connectionRef.current && connectionRef.current.state !== HubConnectionState.Disconnected) {
      connectionRef.current.stop().catch(() => {});
    }
    connectionRef.current = null;
    setConnectionState('disconnected');
  }, []);

  // ─── Auto-connect on token change ──────────────────────────────────────

  useEffect(() => {
    if (token) {
      void connect();
    } else {
      disconnect();
    }
    return () => {
      disconnect();
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [token]);

  // ─── Load threads on connect ────────────────────────────────────────────

  useEffect(() => {
    if (connectionState === 'connected') {
      void refreshThreads();
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [connectionState]);

  // ─── Actions ────────────────────────────────────────────────────────────

  const refreshThreads = useCallback(async () => {
    try {
      const result = await apiListThreads();
      setThreads(result.threads);
    } catch (err) {
      console.error('[AI Assistant] Failed to load threads:', err);
    }
  }, []);

  const selectThread = useCallback(async (threadId: string) => {
    try {
      const result = await apiGetMessages(threadId);
      setMessages(result.messages);
      setActiveThread((prev: AiAssistantThread | null) => {
        const found = threads.find((t) => t.id === threadId);
        return found ?? prev;
      });
      setStreamingText('');
      setStreamingStatus('idle');
      setActiveToolCalls([]);
      setError(null);
    } catch (err) {
      console.error('[AI Assistant] Failed to load messages:', err);
      setError('Failed to load messages');
    }
  }, [threads]);

  const createNewThread = useCallback(async (title?: string): Promise<AiAssistantThread | undefined> => {
    try {
      const thread = await apiCreateThread(assistantRole, title);
      setThreads((prev) => [thread, ...prev]);
      setActiveThread(thread);
      setMessages([]);
      setStreamingText('');
      setStreamingStatus('idle');
      setActiveToolCalls([]);
      setError(null);
      return thread;
    } catch (err) {
      console.error('[AI Assistant] Failed to create thread:', err);
      setError('Failed to create thread');
      return undefined;
    }
  }, [assistantRole]);

  const sendMessage = useCallback(
    async (content: string) => {
      const connection = connectionRef.current;
      if (!connection || connection.state !== HubConnectionState.Connected) {
        setError('Not connected to assistant');
        return;
      }

      // Ensure we have an active thread
      let threadId = activeThreadRef.current?.id;
      if (!threadId) {
        try {
          const thread = await apiCreateThread(assistantRole);
          setThreads((prev) => [thread, ...prev]);
          setActiveThread(thread);
          threadId = thread.id;
        } catch (err) {
          console.error('[AI Assistant] Failed to create thread:', err);
          setError('Failed to create thread');
          return;
        }
      }

      // Add user message to local state
      const userMsg: AiAssistantMessage = {
        id: `temp-${Date.now()}`,
        threadId,
        role: 'user',
        content,
        createdAt: new Date().toISOString(),
      };
      setMessages((prev) => [...prev, userMsg]);
      setStreamingStatus('thinking');
      setStreamingText('');
      setActiveToolCalls([]);
      setError(null);

      try {
        await invokeStartTurn(connection, threadId, content);
      } catch (err) {
        console.error('[AI Assistant] Failed to start turn:', err);
        setError('Failed to send message');
        setStreamingStatus('idle');
      }
    },
    [assistantRole],
  );

  const cancelTurn = useCallback(async () => {
    const connection = connectionRef.current;
    const thread = activeThreadRef.current;
    if (!connection || !thread) return;

    try {
      await invokeCancelTurn(connection, thread.id);
    } catch (err) {
      console.error('[AI Assistant] Failed to cancel turn:', err);
    }
    setStreamingStatus('idle');
    setStreamingText('');
    setActiveToolCalls([]);
  }, []);

  const archiveThreadAction = useCallback(
    async (threadId: string) => {
      try {
        await apiArchiveThread(threadId);
        setThreads((prev) => prev.filter((t) => t.id !== threadId));
        if (activeThreadRef.current?.id === threadId) {
          setActiveThread(null);
          setMessages([]);
        }
      } catch (err) {
        console.error('[AI Assistant] Failed to archive thread:', err);
        setError('Failed to archive thread');
      }
    },
    [],
  );

  const clearError = useCallback(() => setError(null), []);

  const isStreaming = streamingStatus === 'streaming' || streamingStatus === 'thinking' || streamingStatus === 'tool-calling';

  return {
    connectionState,
    isConnected: connectionState === 'connected',
    streamingStatus,
    streamingText,
    activeToolCalls,
    isStreaming,
    streamingContent: streamingText,
    messages,
    threads,
    activeThread,
    thread: activeThread,
    sendMessage,
    cancelTurn,
    cancelStream: () => void cancelTurn(),
    selectThread,
    createNewThread,
    archiveThread: archiveThreadAction,
    refreshThreads,
    connect,
    disconnect,
    error,
    clearError,
  };
}
