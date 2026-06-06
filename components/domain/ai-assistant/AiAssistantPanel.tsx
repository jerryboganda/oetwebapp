'use client';

import { useState } from 'react';
import { X, List } from 'lucide-react';
import { AiAssistantMessages } from './AiAssistantMessages';
import { AiAssistantInput } from './AiAssistantInput';
import type { AiMessage } from '@/lib/ai-assistant/types';

export interface AiAssistantPanelProps {
  onClose: () => void;
}

export function AiAssistantPanel({ onClose }: AiAssistantPanelProps) {
  const [messages, setMessages] = useState<AiMessage[]>([]);
  const [isStreaming, setIsStreaming] = useState(false);
  const [streamingContent, setStreamingContent] = useState('');
  const [showThreadList, setShowThreadList] = useState(false);

  const handleSend = (content: string) => {
    const msg: AiMessage = {
      id: `msg-${Date.now()}`,
      threadId: 'current',
      role: 'user',
      content,
      createdAt: new Date().toISOString(),
    };
    setMessages((prev) => [...prev, msg]);
    setIsStreaming(true);
    // In real implementation, this would trigger SignalR send
  };

  const handleCancel = () => {
    setIsStreaming(false);
    setStreamingContent('');
  };

  return (
    <div
      className="fixed bottom-24 right-6 z-50 flex h-[600px] max-h-[calc(100dvh-8rem)] w-[400px] max-w-[calc(100vw-3rem)] flex-col rounded-2xl border border-border bg-surface shadow-xl md:w-[450px]"
      role="dialog"
      aria-label="AI Assistant"
    >
      {/* Header */}
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <h2 className="text-sm font-semibold">AI Assistant</h2>
        <div className="flex items-center gap-2">
          <button
            onClick={() => setShowThreadList((prev) => !prev)}
            aria-label="Thread list"
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
          <p className="text-xs text-muted">Previous conversations</p>
        </div>
      )}

      {/* Messages */}
      <div className="flex-1 overflow-y-auto p-4">
        <AiAssistantMessages
          messages={messages}
          streamingContent={isStreaming ? streamingContent : undefined}
        />
      </div>

      {/* Input */}
      <AiAssistantInput
        onSend={handleSend}
        onCancel={handleCancel}
        isStreaming={isStreaming}
        disabled={false}
      />
    </div>
  );
}
