'use client';

// React hook surface for the AI Assistant widget (Phase 1).
// Manages thread list, active thread, streaming SignalR connection,
// and message accumulation. Tool execution + approvals are deferred
// to Phase 3 — handlers exist but no-op safely.

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { aiAssistantClient } from '@/lib/ai-assistant/client';
import {
  createAiAssistantConnection,
  type AiAssistantConnection,
} from '@/lib/ai-assistant/signalr';
import type {
  ChatMessageDto,
  ChatThreadDto,
  StreamFrame,
  ToolInvocationDto,
} from '@/lib/ai-assistant/types';

export interface UseAiAssistantResult {
  threads: ReadonlyArray<ChatThreadDto>;
  activeThread: ChatThreadDto | null;
  messages: ReadonlyArray<ChatMessageDto>;
  pendingApproval: ToolInvocationDto | null;
  isConnected: boolean;
  isStreaming: boolean;
  error: string | null;
  liveFrames: ReadonlyArray<StreamFrame>;
  sendMessage: (content: string, attachmentPaths?: ReadonlyArray<string>) => Promise<void>;
  cancel: () => Promise<void>;
  selectThread: (threadId: string | null) => Promise<void>;
  createThread: (title?: string) => Promise<void>;
  deleteThread: (threadId: string) => Promise<void>;
  approveTool: (invocationId: string, decision: 'approve' | 'reject', reason?: string) => Promise<void>;
  reload: () => Promise<void>;
}

export function useAiAssistant(): UseAiAssistantResult {
  const [threads, setThreads] = useState<ChatThreadDto[]>([]);
  const [activeThread, setActiveThread] = useState<ChatThreadDto | null>(null);
  const [messages, setMessages] = useState<ChatMessageDto[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [isConnected, setIsConnected] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [liveFrames, setLiveFrames] = useState<StreamFrame[]>([]);

  const connectionRef = useRef<AiAssistantConnection | null>(null);
  const streamingMessageIdRef = useRef<string | null>(null);
  const activeThreadIdRef = useRef<string | null>(null);
  const subscribedThreadIdRef = useRef<string | null>(null);

  function frameBelongsToActiveThread(frame: StreamFrame): boolean {
    if (!frame.threadId && frame.type === 'Error') {
      return true;
    }

    return frame.threadId === activeThreadIdRef.current;
  }

  // Connect lazily on first mount.
  useEffect(() => {
    let cancelled = false;
    const conn = createAiAssistantConnection();
    connectionRef.current = conn;

    const unsubFrame = conn.onFrame((frame) => {
      if (cancelled) return;
      if (!frameBelongsToActiveThread(frame)) return;
      setLiveFrames((prev) => (prev.length >= 200 ? [...prev.slice(-199), frame] : [...prev, frame]));
      applyFrame(frame);
    });
    const unsubState = conn.onStateChange((connected) => {
      if (!cancelled) setIsConnected(connected);
    });

    void conn.start().catch((err) => {
      if (!cancelled) {
        const message = err instanceof Error ? err.message : String(err);
        setError(`Cannot connect to AI Assistant: ${message}`);
      }
    });

    return () => {
      cancelled = true;
      unsubFrame();
      unsubState();
      void conn.stop();
      connectionRef.current = null;
    };
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const applyFrame = useCallback((frame: StreamFrame) => {
    if (frame.type === 'MessageStart') {
      streamingMessageIdRef.current = frame.messageId;
      setIsStreaming(true);
      setMessages((prev) => {
        if (prev.some((m) => m.id === frame.messageId)) return prev;
        const placeholder: ChatMessageDto = {
          id: frame.messageId,
          threadId: (frame.threadId || activeThreadIdRef.current) ?? '',
          role: frame.role,
          content: '',
          createdAt: new Date().toISOString(),
        };
        return [...prev, placeholder];
      });
    } else if (frame.type === 'TokenDelta') {
      setMessages((prev) =>
        prev.map((m) => (m.id === frame.messageId ? { ...m, content: m.content + frame.delta } : m)),
      );
    } else if (frame.type === 'MessageEnd') {
      setMessages((prev) =>
        prev.map((m) =>
          m.id === frame.messageId
            ? { ...m, promptTokens: frame.promptTokens ?? null, completionTokens: frame.completionTokens ?? null }
            : m,
        ),
      );
      setIsStreaming(false);
      streamingMessageIdRef.current = null;
    } else if (frame.type === 'Error') {
      setError(frame.message);
      setIsStreaming(false);
      streamingMessageIdRef.current = null;
    }
    // ToolCall*/ApprovalRequest frames are surfaced via liveFrames; Phase 3 wires UI.
  }, []);

  const reload = useCallback(async () => {
    setError(null);
    try {
      const list = await aiAssistantClient.listThreads();
      setThreads(list);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(`Failed to load conversations: ${message}`);
    }
  }, []);

  useEffect(() => {
    void reload();
  }, [reload]);

  const selectThread = useCallback(async (threadId: string | null) => {
    setError(null);
    setLiveFrames([]);
    const conn = connectionRef.current;
    const previousThreadId = subscribedThreadIdRef.current;
    if (!threadId) {
      if (conn && previousThreadId) {
        try { await conn.unsubscribeThread(previousThreadId); } catch { /* best effort */ }
      }
      subscribedThreadIdRef.current = null;
      activeThreadIdRef.current = null;
      setActiveThread(null);
      setMessages([]);
      return;
    }
    activeThreadIdRef.current = threadId;
    try {
      if (conn && previousThreadId && previousThreadId !== threadId) {
        try { await conn.unsubscribeThread(previousThreadId); } catch { /* best effort */ }
      }
      const detail = await aiAssistantClient.getThread(threadId);
      setActiveThread(detail.thread);
      setMessages(detail.messages);
      if (conn) {
        await conn.subscribeThread(threadId);
        subscribedThreadIdRef.current = threadId;
      }
    } catch (err) {
      if (activeThreadIdRef.current === threadId) {
        activeThreadIdRef.current = null;
      }
      const message = err instanceof Error ? err.message : String(err);
      setError(`Failed to open conversation: ${message}`);
    }
  }, []);

  const createThread = useCallback(async (title?: string) => {
    setError(null);
    try {
      const t = await aiAssistantClient.createThread({ title });
      setThreads((prev) => [t, ...prev]);
      activeThreadIdRef.current = t.id;
      await selectThread(t.id);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(`Failed to start conversation: ${message}`);
    }
  }, [selectThread]);

  const deleteThread = useCallback(async (threadId: string) => {
    setError(null);
    try {
      await aiAssistantClient.deleteThread(threadId);
      setThreads((prev) => prev.filter((t) => t.id !== threadId));
      if (activeThread?.id === threadId) {
        activeThreadIdRef.current = null;
        if (subscribedThreadIdRef.current === threadId) {
          const conn = connectionRef.current;
          if (conn) {
            try { await conn.unsubscribeThread(threadId); } catch { /* best effort */ }
          }
          subscribedThreadIdRef.current = null;
        }
        setActiveThread(null);
        setMessages([]);
      }
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(`Failed to delete conversation: ${message}`);
    }
  }, [activeThread]);

  const sendMessage = useCallback(async (content: string, _attachmentPaths?: ReadonlyArray<string>) => {
    setError(null);
    if (!content.trim()) return;
    let thread = activeThread;
    if (!thread) {
      // Create a thread on the fly using the first 60 chars as title.
      try {
        thread = await aiAssistantClient.createThread({ title: content.slice(0, 60) });
        setThreads((prev) => [thread!, ...prev]);
        setActiveThread(thread);
        activeThreadIdRef.current = thread.id;
        const conn = connectionRef.current;
        if (conn) {
          const previousThreadId = subscribedThreadIdRef.current;
          if (previousThreadId && previousThreadId !== thread.id) {
            try { await conn.unsubscribeThread(previousThreadId); } catch { /* best effort */ }
          }
          await conn.subscribeThread(thread.id);
          subscribedThreadIdRef.current = thread.id;
        }
      } catch (err) {
        const message = err instanceof Error ? err.message : String(err);
        setError(`Failed to start conversation: ${message}`);
        return;
      }
    }
    const conn = connectionRef.current;
    if (!conn) {
      setError('AI Assistant connection not ready.');
      return;
    }
    // Optimistic user message — server persists the authoritative copy.
    const optimistic: ChatMessageDto = {
      id: `optimistic-${Date.now()}`,
      threadId: thread.id,
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    };
    setMessages((prev) => [...prev, optimistic]);
    try {
      await conn.startTurn(thread.id, content);
    } catch (err) {
      const message = err instanceof Error ? err.message : String(err);
      setError(`Send failed: ${message}`);
      setMessages((prev) => prev.filter((m) => m.id !== optimistic.id));
    }
  }, [activeThread]);

  const cancel = useCallback(async () => {
    const id = streamingMessageIdRef.current;
    if (!id) return;
    try {
      await aiAssistantClient.cancelMessage(id);
    } catch {
      // ignore — server may have already finished.
    }
  }, []);

  const approveTool = useCallback(async () => {
    // Phase 3 — endpoint not yet implemented.
    setError('Tool approvals will be available in a later release.');
  }, []);

  return useMemo<UseAiAssistantResult>(
    () => ({
      threads,
      activeThread,
      messages,
      pendingApproval: null,
      isConnected,
      isStreaming,
      error,
      liveFrames,
      sendMessage,
      cancel,
      selectThread,
      createThread,
      deleteThread,
      approveTool,
      reload,
    }),
    [threads, activeThread, messages, isConnected, isStreaming, error, liveFrames, sendMessage, cancel, selectThread, createThread, deleteThread, approveTool, reload],
  );
}
