'use client';

import { useEffect, useRef } from 'react';
import { useAiAssistantContext as useAiAssistant } from '@/contexts/ai-assistant-context';

const ROLE_LABEL: Record<string, string> = {
  user: 'You',
  assistant: 'Assistant',
  system: 'System',
  tool: 'Tool',
};

const ROLE_CLASS: Record<string, string> = {
  user: 'bg-sky-50 border-sky-100',
  assistant: 'bg-white border-slate-200',
  system: 'bg-amber-50 border-amber-200',
  tool: 'bg-violet-50 border-violet-200',
};

export function ChatMessageList(): React.JSX.Element {
  const { messages, isStreaming, error, activeThread } = useAiAssistant();
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    const el = scrollRef.current;
    if (el) el.scrollTop = el.scrollHeight;
  }, [messages]);

  return (
    <div
      ref={scrollRef}
      className="flex-1 space-y-2 overflow-y-auto px-4 py-3 text-sm text-slate-700"
      data-testid="ai-assistant-message-list"
    >
      {!activeThread && messages.length === 0 ? (
        <p className="text-slate-400">Start a new conversation by typing a message below.</p>
      ) : null}
      {messages.map((m) => (
        <div
          key={m.id}
          className={`rounded border px-3 py-2 ${ROLE_CLASS[m.role] ?? 'bg-white border-slate-200'}`}
          data-role={m.role}
        >
          <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-500">
            {ROLE_LABEL[m.role] ?? m.role}
          </p>
          <p className="whitespace-pre-wrap break-words text-slate-800">{m.content}</p>
        </div>
      ))}
      {isStreaming ? (
        <p className="text-xs text-slate-400" data-testid="ai-assistant-streaming">Assistant is typing…</p>
      ) : null}
      {error ? (
        <p className="rounded border border-rose-200 bg-rose-50 px-3 py-2 text-xs text-rose-700" role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
