'use client';

import { useState } from 'react';
import { usePathname } from 'next/navigation';
import { X, List, AlertCircle, Plus } from 'lucide-react';
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
  } = useAiAssistantContext();

  const [showThreadList, setShowThreadList] = useState(false);
  const pathname = usePathname();

  const handleSend = (content: string) => {
    // The hook owns optimistic echo, streaming and persistence; fire and forget
    // here so a send failure surfaces through `error` rather than as an
    // unhandled rejection.
    // The floating panel opens on top of whatever the learner is doing, so the
    // route is the single most useful thing we can tell the companion.
    void sendMessage(content, buildSurfaceContext(pathname));
  };

  const handleCancel = () => {
    void cancelTurn();
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

      {/* Thread list */}
      {showThreadList && (
        <div className="border-b border-border p-3" data-testid="thread-list">
          <div className="mb-2 flex items-center justify-between">
            <p className="text-xs text-muted">Previous conversations</p>
            <button
              onClick={() => void createNewThread()}
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
              {threads.map((thread) => (
                <li key={thread.id}>
                  <button
                    onClick={() => void selectThread(thread.id)}
                    aria-current={activeThread?.id === thread.id}
                    className={`w-full truncate rounded px-2 py-1 text-left text-xs hover:bg-background-light ${
                      activeThread?.id === thread.id ? 'bg-background-light font-medium' : ''
                    }`}
                  >
                    {thread.title ?? 'Untitled conversation'}
                  </button>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}

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
