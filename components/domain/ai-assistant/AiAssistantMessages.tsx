'use client';

import type { AiMessage } from '@/lib/ai-assistant/types';

export interface AiAssistantMessagesProps {
  messages: AiMessage[];
  streamingContent?: string;
}

export function AiAssistantMessages({ messages, streamingContent }: AiAssistantMessagesProps) {
  const isStreaming = streamingContent !== undefined;

  if (messages.length === 0 && !isStreaming) {
    return (
      <div className="flex h-full items-center justify-center text-sm text-muted">
        Start a conversation with the AI assistant
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {messages.map((msg) => (
        <MessageBubble key={msg.id} message={msg} />
      ))}
      {streamingContent !== undefined && (
        <div className="rounded-lg bg-background-light p-3" data-testid="streaming-message">
          <div className="prose prose-sm max-w-none">{streamingContent}</div>
          <span className="inline-block h-4 w-1 animate-pulse bg-primary" data-testid="streaming-cursor" />
        </div>
      )}
    </div>
  );
}

function MessageBubble({ message }: { message: AiMessage }) {
  const isUser = message.role === 'user';
  const isTool = message.role === 'tool';

  if (isTool) {
    const firstTool = message.toolCalls?.[0];
    return (
      <div className="rounded-lg border border-border bg-background-light p-3" data-testid="tool-call-card">
        <div className="text-xs font-medium text-muted mb-1">
          Tool: {firstTool?.toolName ?? 'unknown'}
        </div>
        <pre className="text-xs overflow-x-auto">{message.content}</pre>
      </div>
    );
  }

  return (
    <div
      className={`rounded-lg p-3 ${isUser ? 'ml-8 bg-primary/10' : 'mr-8 bg-background-light'}`}
      data-testid={isUser ? 'user-message' : 'assistant-message'}
    >
      <div className="prose prose-sm max-w-none">{message.content}</div>
    </div>
  );
}
