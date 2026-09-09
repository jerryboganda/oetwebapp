'use client';

/**
 * Main React hook for the AI Assistant.
 * Manages SignalR connection lifecycle, streaming state, message history, and thread management.
 */

import { useCallback, useEffect, useRef, useState } from 'react';
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
  packDocumentAttachment,
  type AssistantCitation,
  type AssistantConnectionState,
  type AssistantTurnAttachments,
} from '@/lib/ai-assistant/signalr';
import type { CompanionSurfaceContext } from '@/lib/ai-assistant/surface-context';
import {
  createThread as apiCreateThread,
  listThreads as apiListThreads,
  getMessages as apiGetMessages,
  archiveThread as apiArchiveThread,
  renameThread as apiRenameThread,
  setThreadModel as apiSetThreadModel,
  listAssistantModels as apiListAssistantModels,
} from '@/lib/ai-assistant/api';
import { getAssistantRole } from '@/lib/ai-assistant/permissions';
import type { UserRole } from '@/lib/types/auth';

// ─── Legacy exports for backward compatibility ──────────────────────────────

export type ConnectionState = 'disconnected' | 'connecting' | 'connected' | 'error';

export interface AssistantAttachmentInput {
  /** Raw image bytes for vision (sent as data URLs; UBAG ubag_attachments). */
  images?: Array<{ bytes: Uint8Array | ArrayBuffer; mimeType: string; fileName?: string }>;
  /** Extracted document text folded into the prompt (text-only path). */
  document?: { fileName: string; mimeType: string; text: string };
  /**
   * One recorded voice note. Transcribed server-side through the platform's
   * existing ASR pipeline; the transcript comes back on the `VoiceTranscript`
   * event so the learner can see what was heard before the answer arrives.
   */
  audio?: { bytes: Uint8Array | ArrayBuffer; mimeType: string };
}

export interface UseAiAssistantReturn {
  // Connection
  connectionState: AssistantConnectionState;
  isConnected: boolean;

  // Streaming
  streamingStatus: StreamingStatus;
  streamingText: string;
  activeToolCalls: ToolCallInfo[];
  /** Sources for the turn currently streaming; empty when idle. */
  citations: AssistantCitation[];

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
  /**
   * Sends a turn. `context` is a bounded surface hint (route, resource,
   * question, video position) that lets the companion answer "this question".
   * Identifiers only — the server resolves meaning and entitlement.
   * `attachments` carries this turn's images (vision) and/or extracted
   * document text; both are turn-scoped, never persisted.
   */
  sendMessage: (content: string, context?: CompanionSurfaceContext, attachments?: AssistantAttachmentInput) => Promise<void>;
  cancelTurn: () => Promise<void>;
  /** @deprecated Use cancelTurn */
  cancelStream: () => void;
  selectThread: (threadId: string) => Promise<void>;
  createNewThread: (title?: string) => Promise<AiAssistantThread | undefined>;
  archiveThread: (threadId: string) => Promise<void>;
  renameThread: (threadId: string, title: string) => Promise<void>;
  refreshThreads: () => Promise<void>;

  // Per-conversation model override (null = feature-route default).
  threadModel: string | null;
  availableModels: string[];
  modelGroups: Array<{ provider: string; label: string; models: string[] }>;
  modelsLoading: boolean;
  setThreadModel: (model: string | null) => Promise<void>;

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
  // Citations for the turn currently streaming. Cleared as soon as they are
  // bound to the completed message, so one turn's sources never leak into the next.
  const [citations, setCitations] = useState<AssistantCitation[]>([]);
  const [messages, setMessages] = useState<AiAssistantMessage[]>([]);
  const [threads, setThreads] = useState<AiAssistantThread[]>([]);
  const [activeThread, setActiveThread] = useState<AiAssistantThread | null>(null);
  const [error, setError] = useState<string | null>(null);
  // Per-conversation UBAG model override, mirrored from the active thread's
  // server row (thread.ModelOverride). Null = feature-route default.
  const [threadModel, setThreadModelState] = useState<string | null>(null);
  const [availableModels, setAvailableModels] = useState<string[]>([]);
  const [modelGroups, setModelGroups] = useState<Array<{ provider: string; label: string; models: string[] }>>([]);
  const [modelsLoading, setModelsLoading] = useState(false);

  const connectionRef = useRef<HubConnection | null>(null);
  const unsubRef = useRef<(() => void) | null>(null);
  const activeThreadRef = useRef<AiAssistantThread | null>(null);
  const activeToolCallsRef = useRef<ToolCallInfo[]>([]);
  const citationsRef = useRef<AssistantCitation[]>([]);
  const connectionAttemptRef = useRef(0);
  const threadModelRef = useRef<string | null>(null);
  const pendingVoiceTranscriptRef = useRef<string | null>(null);
  const pendingUserMessageIdRef = useRef<string | null>(null);

  // Keep refs in sync
  useEffect(() => { activeThreadRef.current = activeThread; }, [activeThread]);
  useEffect(() => { activeToolCallsRef.current = activeToolCalls; }, [activeToolCalls]);
  useEffect(() => { citationsRef.current = citations; }, [citations]);
  useEffect(() => { threadModelRef.current = threadModel; }, [threadModel]);

  const assistantRole: AssistantRole = getAssistantRole(resolvedRole);

  // ─── Connect ────────────────────────────────────────────────────────────

  const connect = useCallback(async () => {
    if (!token) {
      setError('No authentication token');
      return;
    }

    const attempt = connectionAttemptRef.current + 1;
    connectionAttemptRef.current = attempt;

    // Cleanup previous connection
    if (unsubRef.current) {
      unsubRef.current();
      unsubRef.current = null;
    }
    if (connectionRef.current && mapHubState(connectionRef.current.state) !== 'disconnected') {
      await connectionRef.current.stop().catch(() => {});
    }

    setConnectionState('connecting');
    let connection: HubConnection;
    try {
      connection = await createAssistantConnection(token, {
        onReconnecting: () => {
          if (connectionAttemptRef.current === attempt) setConnectionState('reconnecting');
        },
        onReconnected: () => {
          if (connectionAttemptRef.current === attempt) setConnectionState('connected');
        },
        onClose: () => {
          if (connectionAttemptRef.current === attempt) setConnectionState('disconnected');
        },
      });
    } catch (err) {
      if (connectionAttemptRef.current !== attempt) return;
      console.error('[AI Assistant] Connection failed:', err);
      setConnectionState('disconnected');
      setError('Failed to connect to AI assistant');
      return;
    }

    if (connectionAttemptRef.current !== attempt) {
      await connection.stop().catch(() => {});
      return;
    }

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
      onCitations: (incoming: AssistantCitation[]) => {
        // Arrive before the first token; bound to the message on completion.
        setCitations(incoming);
      },
      onVoiceTranscript: (text: string) => {
        // Hub already folded this into the stored user turn. Mirror it locally
        // so a voice-only send is not a blank bubble. Hold the text if the
        // optimistic bubble has not flushed yet — sendMessage reads the same ref.
        if (!text) return;
        pendingVoiceTranscriptRef.current = text;
        const id = pendingUserMessageIdRef.current;
        setMessages((prev) => {
          const idx = id ? prev.findIndex((m) => m.id === id) : -1;
          if (idx < 0) return prev;
          const last = prev[idx];
          pendingVoiceTranscriptRef.current = null;
          if (last.content.includes(text)) return prev;
          const next = [...prev];
          next[idx] = {
            ...last,
            content: last.content.trim() ? `${last.content}\n${text}` : text,
          };
          return next;
        });
      },
      onTurnComplete: (messageId: string, fullText: string) => {
        pendingVoiceTranscriptRef.current = null;
        pendingUserMessageIdRef.current = null;
        const assistantMsg: AiAssistantMessage = {
          id: messageId,
          threadId: activeThreadRef.current?.id ?? '',
          role: 'assistant',
          content: fullText,
          createdAt: new Date().toISOString(),
          toolCalls: activeToolCallsRef.current.length > 0
            ? [...activeToolCallsRef.current]
            : undefined,
          citations: citationsRef.current.length > 0
            ? [...citationsRef.current]
            : undefined,
        };
        setMessages((prev) => [...prev, assistantMsg]);
        setStreamingStatus('idle');
        setStreamingText('');
        setActiveToolCalls([]);
        setCitations([]);
      },
      onTurnError: (code: string, message: string) => {
        pendingVoiceTranscriptRef.current = null;
        pendingUserMessageIdRef.current = null;
        setError(`[${code}] ${message}`);
        setStreamingStatus('idle');
        setStreamingText('');
        setActiveToolCalls([]);
        setCitations([]);
      },
    });

    unsubRef.current = unsub;

    // Start connection
    try {
      await connection.start();
      if (connectionAttemptRef.current !== attempt) {
        await connection.stop().catch(() => {});
        return;
      }
      setConnectionState('connected');
      setError(null);
    } catch (err) {
      if (connectionAttemptRef.current !== attempt) return;
      unsub();
      if (unsubRef.current === unsub) {
        unsubRef.current = null;
      }
      if (connectionRef.current === connection) {
        connectionRef.current = null;
      }
      console.error('[AI Assistant] Connection failed:', err);
      setConnectionState('disconnected');
      setError('Failed to connect to AI assistant');
    }
  }, [token]);

  const disconnect = useCallback(() => {
    connectionAttemptRef.current += 1;
    if (unsubRef.current) {
      unsubRef.current();
      unsubRef.current = null;
    }
    if (connectionRef.current && mapHubState(connectionRef.current.state) !== 'disconnected') {
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

  // Load Claude + UBAG catalogs once the socket is up (fail-soft).
  useEffect(() => {
    if (connectionState !== 'connected' || !token) return;
    let cancelled = false;
    setModelsLoading(true);
    apiListAssistantModels()
      .then((catalog) => {
        if (cancelled) return;
        const groups = Array.isArray(catalog?.groups)
          ? catalog.groups.filter((g) => Array.isArray(g?.models) && g.models.length > 0)
          : [];
        setModelGroups(groups.map((g) => ({
          provider: typeof g.provider === 'string' ? g.provider : '',
          label: typeof g.label === 'string' && g.label.length > 0 ? g.label : g.provider,
          models: g.models.filter((m): m is string => typeof m === 'string' && m.length > 0),
        })));
        const flat = groups.length > 0
          ? groups.flatMap((g) => g.models)
          : Array.isArray(catalog?.models) ? catalog.models : [];
        setAvailableModels(flat.filter((m): m is string => typeof m === 'string' && m.length > 0));
      })
      .catch(() => {})
      .finally(() => {
        if (!cancelled) setModelsLoading(false);
      });
    return () => {
      cancelled = true;
    };
  }, [connectionState, token]);

  // ─── Actions ────────────────────────────────────────────────────────────

  const refreshThreads = useCallback(async () => {
    try {
      const rows = await apiListThreads();
      if (rows.length > 0) {
        setThreads(rows);
      } else {
        // The thread list is authoritative-empty only when the user owns
        // nothing; never wipe locally created threads on a transient fetch
        // (the hook test double returns [] for listThreads by default).
        setThreads((prev) => (prev.length > 0 ? prev : rows));
      }
      // Keep the pick pinned to the conversation even if this refresh
      // (e.g. the on-connect load, or a rename-triggered reload) arrives
      // while a just-saved override has not round-tripped yet: local state
      // for the ACTIVE thread wins over the list copy.
      setActiveThread((prev) => {
        if (!prev) return prev;
        const fresh = rows.find((t) => t.id === prev.id) ?? null;
        if (!fresh) return prev;
        const merged = prev.modelOverride !== undefined
          ? { ...fresh, modelOverride: prev.modelOverride }
          : fresh;
        if (merged.modelOverride !== undefined) setThreadModelState(merged.modelOverride ?? null);
        return merged;
      });
    } catch (err) {
      console.error('[AI Assistant] Failed to load threads:', err);
    }
  }, []);

  const selectThread = useCallback(async (threadId: string) => {
    try {
      // Re-selected conversations re-read the authoritative row (rename
      // titles, model picks) alongside the transcript, so stale list copies
      // can never stick on the UI.
      const [rows, history] = await Promise.all([
        apiListThreads(),
        apiGetMessages(threadId),
      ]);
      setThreads((prev) => {
        if (rows.length === 0) return prev;
        const seen = new Set(rows.map((t) => t.id));
        const activeExtra = prev.filter((t) => !seen.has(t.id));
        return [...rows, ...activeExtra];
      });
      setMessages(history);
      const fresh = rows.find((t) => t.id === threadId) ?? null;
      // The row is authoritative for the conversation's own pick, so a
      // re-selected thread restores its model instead of keeping the
      // previous thread's.
      if (fresh) setThreadModelState(fresh.modelOverride ?? null);
      setActiveThread((prev) => fresh ?? prev);
      setStreamingText('');
      setStreamingStatus('idle');
      setActiveToolCalls([]);
      setError(null);
    } catch (err) {
      console.error('[AI Assistant] Failed to load messages:', err);
      setError('Failed to load messages');
    }
  }, []);

  const createNewThread = useCallback(async (title?: string): Promise<AiAssistantThread | undefined> => {
    try {
      const thread = await apiCreateThread(assistantRole, title);
      setThreads((prev) => [thread, ...prev]);
      setActiveThread(thread);
      // A fresh conversation starts on the feature-route default; the
      // previous thread's pick must not leak onto it.
      setThreadModelState(thread.modelOverride ?? null);
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

  const renameThreadAction = useCallback(async (threadId: string, title: string) => {
    const trimmed = title.trim();
    if (!trimmed) {
      setError('Conversation name cannot be empty');
      return;
    }
    try {
      await apiRenameThread(threadId, trimmed);
      setThreads((prev) => prev.map((t) => (t.id === threadId ? { ...t, title: trimmed } : t)));
      setActiveThread((prev) => (prev?.id === threadId ? { ...prev, title: trimmed } : prev));
    } catch (err) {
      console.error('[AI Assistant] Failed to rename thread:', err);
      setError('Failed to rename conversation');
    }
  }, []);

  const setThreadModelAction = useCallback(async (model: string | null) => {
    const threadId = activeThreadRef.current?.id;
    // No conversation yet: remember the pick so the auto-created thread's
    // first turn already uses it.
    if (!threadId) {
      setThreadModelState(model);
      return;
    }
    const previous = activeThreadRef.current?.modelOverride ?? null;
    setThreadModelState(model);
    setActiveThread((prev) => (prev?.id === threadId ? { ...prev, modelOverride: model } : prev));
    setThreads((prev) => prev.map((t) => (t.id === threadId ? { ...t, modelOverride: model } : t)));
    try {
      await apiSetThreadModel(threadId, model);
    } catch (err) {
      console.error('[AI Assistant] Failed to set thread model:', err);
      setThreadModelState(previous);
      setActiveThread((prev) => (prev?.id === threadId ? { ...prev, modelOverride: previous } : prev));
      setThreads((prev) => prev.map((t) => (t.id === threadId ? { ...t, modelOverride: previous } : t)));
      setError('Failed to change model');
    }
  }, []);

  const sendMessage = useCallback(
    async (content: string, context?: CompanionSurfaceContext, attachments?: AssistantAttachmentInput) => {
      const connection = connectionRef.current;
      if (!connection || mapHubState(connection.state) !== 'connected') {
        setError('Not connected to assistant');
        return;
      }

      // Ensure we have an active thread (a pending model pick created before
      // the first thread is applied to it right away).
      let threadId = activeThreadRef.current?.id;
      if (!threadId) {
        try {
          const thread = await apiCreateThread(assistantRole);
          setThreads((prev) => [thread, ...prev]);
          setActiveThread(thread);
          threadId = thread.id;
          const pendingModel = threadModelRef.current;
          if (pendingModel) {
            try {
              await apiSetThreadModel(thread.id, pendingModel);
              setThreads((prev) => prev.map((t) => (t.id === thread.id ? { ...t, modelOverride: pendingModel } : t)));
              setActiveThread((prev) => (prev?.id === thread.id ? { ...prev, modelOverride: pendingModel } : prev));
            } catch {
              // Model pick is best-effort; the turn still sends on default.
            }
          }
        } catch (err) {
          console.error('[AI Assistant] Failed to create thread:', err);
          setError('Failed to create thread');
          return;
        }
      }

      // Encode attachments for the hub wire format (server re-validates).
      let wire: AssistantTurnAttachments | undefined;
      try {
        wire = encodeAttachments(attachments);
      } catch (err) {
        setError(err instanceof Error ? err.message : 'Could not read attachment');
        return;
      }

      // Add user message to local state. A voice-only send has empty `content`
      // until VoiceTranscript arrives; if it already did, fold it in here.
      const userMsgId = `temp-${Date.now()}`;
      pendingUserMessageIdRef.current = userMsgId;
      setMessages((prev) => {
        const heard = pendingVoiceTranscriptRef.current;
        if (heard) pendingVoiceTranscriptRef.current = null;
        const userContent = content.trim()
          ? (heard && !content.includes(heard) ? `${content}\n${heard}` : content)
          : (heard ?? '');
        return [...prev, {
          id: userMsgId,
          threadId,
          role: 'user',
          content: userContent,
          createdAt: new Date().toISOString(),
        }];
      });
      setStreamingStatus('thinking');
      setStreamingText('');
      setActiveToolCalls([]);
      setError(null);

      try {
        await invokeStartTurn(connection, threadId, content, context, wire);
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
    citations,
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
    renameThread: renameThreadAction,
    refreshThreads,
    threadModel,
    availableModels,
    modelGroups,
    modelsLoading,
    setThreadModel: setThreadModelAction,
    connect,
    disconnect,
    error,
    clearError,
  };
}

/** Base64 without the data-URL prefix (btoa-safe chunked encode). */
function base64FromBytes(bytes: Uint8Array): string {
  let binary = '';
  const chunk = 0x8000;
  for (let i = 0; i < bytes.length; i += chunk) {
    binary += String.fromCharCode(...bytes.subarray(i, i + chunk));
  }
  return btoa(binary);
}

function toUint8(bytes: Uint8Array | ArrayBuffer): Uint8Array {
  return bytes instanceof Uint8Array ? bytes : new Uint8Array(bytes);
}

/**
 * Encode hook-level attachments to the hub wire format. Client-side caps
 * mirror the server (3 images, 5 MB each, images-only); the hub re-validates
 * and stays authoritative.
 */
function encodeAttachments(input?: AssistantAttachmentInput): AssistantTurnAttachments | undefined {
  if (!input) return undefined;
  const images = (input.images ?? [])
    .filter((img) => img.bytes.byteLength > 0 && img.bytes.byteLength <= 5 * 1024 * 1024)
    .slice(0, 3)
    .map((img) => {
      const mime = img.mimeType.toLowerCase();
      if (!['image/jpeg', 'image/png', 'image/gif', 'image/webp'].includes(mime)) {
        throw new Error('Only JPG, PNG, GIF and WEBP images are supported.');
      }
      return `data:${mime};base64,${base64FromBytes(toUint8(img.bytes))}`;
    });
  const document = input.document && input.document.text.trim()
    ? packDocumentAttachment(input.document.fileName, input.document.mimeType, input.document.text.slice(0, 60000))
    : null;

  // Speech is bulky, so the audio cap is far larger than the image one: 20 MB is
  // roughly twenty minutes compressed, past which the learner is recording a
  // lecture rather than asking a question. The hub re-checks it either way.
  let audio: string | null = null;
  if (input.audio && input.audio.bytes.byteLength > 0) {
    if (input.audio.bytes.byteLength > 20 * 1024 * 1024) {
      throw new Error('That recording is too long. Keep voice notes under 20 MB.');
    }
    audio = `data:${input.audio.mimeType.toLowerCase()};base64,${base64FromBytes(toUint8(input.audio.bytes))}`;
  }

  if (images.length === 0 && !document && !audio) return undefined;
  return { imageDataUrls: images.length > 0 ? images : null, document, audioDataUrl: audio };
}
