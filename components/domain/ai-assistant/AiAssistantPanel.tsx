'use client';

import { useEffect, useRef, useState } from 'react';
import { usePathname } from 'next/navigation';
import { X, List, AlertCircle, Plus, Pencil, Trash2, Check } from 'lucide-react';
import { AiAssistantMessages } from './AiAssistantMessages';
import { AiAssistantInput } from './AiAssistantInput';
import { useAiAssistantContext } from '@/contexts/ai-assistant-context';
import { buildSurfaceContext } from '@/lib/ai-assistant/surface-context';

export interface AiAssistantPanelProps {
  onClose: () => void;
}

/**
 * Chat panel for the AI Learning Companion.
 *
 * Until the companion programme this component held its own `useState` message
 * list and `handleSend` was a no-op comment, so nothing it displayed ever
 * reached the server. It now consumes {@link useAiAssistantContext}, which
 * wraps the SignalR streaming state machine in `hooks/use-ai-assistant.ts` —
 * that hook already handled connect, stream, cancel and thread management and
 * was simply not wired to any rendered component.
 *
 * See docs/ai-learning-companion/.
 */
export function AiAssistantPanel({ onClose }: AiAssistantPanelProps) {
  const {
    messages,
    threads,
    activeThread,
    isStreaming,
    streamingContent,
    citations,
    isConnected,
    connectionState,
    error,
    clearError,
    sendMessage,
    cancelTurn,
    selectThread,
    createNewThread,
    archiveThread,
    renameThread,
    threadModel,
    availableModels,
    modelGroups,
    modelsLoading,
    setThreadModel,
  } = useAiAssistantContext();

  const [showThreadList, setShowThreadList] = useState(false);
  const [renamingId, setRenamingId] = useState<string | null>(null);
  const [renameDraft, setRenameDraft] = useState('');
  const [confirmDeleteId, setConfirmDeleteId] = useState<string | null>(null);
  const renameInputRef = useRef<HTMLInputElement>(null);
  const pathname = usePathname();

  useEffect(() => {
    if (renamingId) renameInputRef.current?.focus();
  }, [renamingId]);

  const handleSend = (content: string, attachments?: Parameters<typeof sendMessage>[2]) => {
    // The hook owns optimistic echo, streaming and persistence; fire and forget
    // here so a send failure surfaces through `error` rather than as an
    // unhandled rejection.
    // The floating panel opens on top of whatever the learner is doing, so the
    // route is the single most useful thing we can tell the companion.
    void sendMessage(content, buildSurfaceContext(pathname), attachments);
  };

  const handleCancel = () => {
    void cancelTurn();
  };

  const startRename = (threadId: string, title: string | null) => {
    setConfirmDeleteId(null);
    setRenamingId(threadId);
    setRenameDraft(title ?? '');
  };

  const commitRename = () => {
    if (!renamingId) return;
    const trimmed = renameDraft.trim();
    if (trimmed) void renameThread(renamingId, trimmed);
    setRenamingId(null);
  };

  return (
    <div
      className="fixed bottom-24 right-6 z-50 flex h-[600px] max-h-[calc(100dvh-8rem)] w-[400px] max-w-[calc(100vw-3rem)] flex-col rounded-2xl border border-border bg-surface shadow-xl md:w-[450px]"
      role="dialog"
      aria-label="AI Assistant"
    >
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div className="flex items-center gap-2">
          <h2 className="text-sm font-semibold">AI Assistant</h2>
          <span
            data-testid="connection-state"
            data-state={connectionState}
            aria-live="polite"
            className={`h-2 w-2 rounded-full ${isConnected ? 'bg-green-500' : 'bg-amber-500'}`}
          >
            <span className="sr-only">
              {isConnected ? 'Connected' : `Connection ${connectionState}`}
            </span>
          </span>
        </div>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setShowThreadList((prev) => !prev)}
            aria-label="Thread list"
            aria-expanded={showThreadList}
            className="rounded p-1 hover:bg-background-light"
          >
            <List className="h-4 w-4" />
          </button>
          <button
            onClick={onClose}
            aria-label="Close"
            className="rounded p-1 hover:bg-background-light"
          >
            <X className="h-4 w-4" />
          </button>
        </div>
      </div>

      {/* Thread list — conversations are server-authoritative: select
          reloads the row (title + model) so renames and picks survive
          reconnects, and the actions stay visible on touch (no hover). */}
      {showThreadList && (
        <div className="border-b border-border p-3" data-testid="thread-list">
          <div className="mb-2 flex items-center justify-between">
            <p className="text-xs text-muted">Previous conversations</p>
            <button
              onClick={() => {
                setRenamingId(null);
                setConfirmDeleteId(null);
                void createNewThread();
              }}
              aria-label="New conversation"
              className="flex items-center gap-1 rounded px-1.5 py-0.5 text-xs hover:bg-background-light"
            >
              <Plus className="h-3 w-3" />
              New
            </button>
          </div>
          {threads.length === 0 ? (
            <p className="text-xs text-muted">No previous conversations yet.</p>
          ) : (
            <ul className="max-h-40 space-y-1 overflow-y-auto">
              {threads.map((thread) => {
                const isActive = activeThread?.id === thread.id;
                const isRenaming = renamingId === thread.id;
                const isConfirmingDelete = confirmDeleteId === thread.id;
                return (
                  <li key={thread.id} className={`group rounded px-1 py-0.5 ${isActive ? 'bg-background-light' : 'hover:bg-background-light'}`}>
                    {isRenaming ? (
                      <div className="flex items-center gap-1">
                        <input
                          ref={renameInputRef}
                          value={renameDraft}
                          maxLength={256}
                          onChange={(e) => setRenameDraft(e.target.value)}
                          onKeyDown={(e) => {
                            if (e.key === 'Enter') commitRename();
                            if (e.key === 'Escape') setRenamingId(null);
                          }}
                          aria-label="Conversation name"
                          className="min-w-0 flex-1 rounded border border-border bg-surface px-1.5 py-0.5 text-xs"
                        />
                        <button
                          onClick={commitRename}
                          aria-label="Save conversation name"
                          className="rounded p-1 hover:bg-background"
                        >
                          <Check className="h-3.5 w-3.5" />
                        </button>
                      </div>
                    ) : isConfirmingDelete ? (
                      <div className="flex items-center justify-between gap-1 px-1 py-0.5">
                        <span className="truncate text-xs text-muted">Delete this conversation?</span>
                        <span className="flex shrink-0 items-center gap-1">
                          <button
                            onClick={() => {
                              setConfirmDeleteId(null);
                              void archiveThread(thread.id);
                            }}
                            aria-label={`Confirm delete ${thread.title ?? 'Untitled conversation'}`}
                            className="rounded bg-red-500 px-1.5 py-0.5 text-[11px] font-medium text-white hover:bg-red-600"
                          >
                            Delete
                          </button>
                          <button
                            onClick={() => setConfirmDeleteId(null)}
                            aria-label="Cancel delete"
                            className="rounded px-1.5 py-0.5 text-[11px] hover:bg-background"
                          >
                            Keep
                          </button>
                        </span>
                      </div>
                    ) : (
                      <div className="flex items-center gap-0.5">
                        <button
                          onClick={() => void selectThread(thread.id)}
                          aria-current={isActive}
                          className={`min-w-0 flex-1 truncate rounded px-2 py-1 text-left text-xs ${isActive ? 'font-medium' : ''}`}
                        >
                          {thread.title ?? 'Untitled conversation'}
                        </button>
                        <span className="flex shrink-0 items-center group-hover:flex group-focus-within:flex max-md:flex">
                          <button
                            onClick={() => startRename(thread.id, thread.title)}
                            aria-label={`Rename ${thread.title ?? 'Untitled conversation'}`}
                            className="rounded p-1 text-muted hover:bg-background hover:text-navy"
                          >
                            <Pencil className="h-3 w-3" />
                          </button>
                          <button
                            onClick={() => setConfirmDeleteId(thread.id)}
                            aria-label={`Delete ${thread.title ?? 'Untitled conversation'}`}
                            className="rounded p-1 text-muted hover:bg-background hover:text-red-600"
                          >
                            <Trash2 className="h-3 w-3" />
                          </button>
                        </span>
                      </div>
                    )}
                  </li>
                );
              })}
            </ul>
          )}
        </div>
      )}

      {/* Model picker — Claude API and UBAG browser as separate groups. */}
      <div className="flex items-center gap-2 border-b border-border px-4 py-2">
        <label htmlFor="assistant-model" className="shrink-0 text-xs font-medium text-muted">
          Model
        </label>
        <select
          id="assistant-model"
          aria-label="AI model for this conversation"
          value={threadModel ?? ''}
          disabled={modelsLoading || isStreaming}
          onChange={(e) => void setThreadModel(e.target.value === '' ? null : e.target.value)}
          className="min-w-0 flex-1 truncate rounded-lg border border-border bg-background px-2 py-1.5 text-xs focus:outline-none focus:ring-2 focus:ring-primary disabled:opacity-50"
        >
          <option value="">Default</option>
          {modelGroups.length > 0
            ? (
                <>
                  {modelGroups.map((group) => (
                    <optgroup key={group.provider || group.label} label={group.label}>
                      {group.models.map((model) => (
                        <option key={`${group.provider}:${model}`} value={model}>
                          {model}
                        </option>
                      ))}
                    </optgroup>
                  ))}
                  {threadModel && !modelGroups.some((group) => group.models.includes(threadModel)) && (
                    <option value={threadModel}>{threadModel} (unavailable)</option>
                  )}
                </>
              )
            : (availableModels.length > 0 ? availableModels : threadModel ? [threadModel] : []).map((model) => (
                <option key={model} value={model}>
                  {model}
                </option>
              ))}
        </select>
      </div>

      {/* Error */}
      {error && (
        <div
          role="alert"
          data-testid="assistant-error"
          className="flex items-start gap-2 border-b border-border bg-red-50 px-4 py-2 text-xs text-red-800 dark:bg-red-950/40 dark:text-red-200"
        >
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          <span className="flex-1">{error}</span>
          <button onClick={clearError} aria-label="Dismiss error" className="underline">
            Dismiss
          </button>
        </div>
      )}

      {/* Messages */}
      <div className="flex-1 overflow-y-auto p-4">
        <AiAssistantMessages
          messages={messages}
          streamingContent={isStreaming ? streamingContent : undefined}
          streamingCitations={isStreaming ? citations : undefined}
        />
      </div>

      {/* Input */}
      <AiAssistantInput
        onSend={handleSend}
        onCancel={handleCancel}
        isStreaming={isStreaming}
        disabled={!isConnected}
      />
    </div>
  );
}
