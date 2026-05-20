'use client';

import { useState, type KeyboardEvent } from 'react';
import { useAiAssistantContext as useAiAssistant } from '@/contexts/ai-assistant-context';

export function MessageComposer(): React.JSX.Element {
  const { sendMessage, cancel, isStreaming, isConnected } = useAiAssistant();
  const [value, setValue] = useState('');

  const submit = async () => {
    const text = value.trim();
    if (!text || isStreaming) return;
    setValue('');
    await sendMessage(text);
  };

  const handleKey = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    if (e.key === 'Enter' && !e.shiftKey) {
      e.preventDefault();
      void submit();
    }
  };

  return (
    <div className="border-t border-slate-200 px-4 py-3">
      <div className="flex items-end gap-2">
        <textarea
          value={value}
          onChange={(e) => setValue(e.target.value)}
          onKeyDown={handleKey}
          className="min-h-[44px] flex-1 resize-none rounded border border-slate-300 px-2 py-1.5 text-sm focus:border-sky-500 focus:outline-none disabled:bg-slate-50"
          placeholder={isConnected ? 'Ask the assistant…' : 'Connecting…'}
          aria-label="Message"
          disabled={!isConnected || isStreaming}
          rows={2}
          data-testid="ai-assistant-composer-input"
        />
        {isStreaming ? (
          <button
            type="button"
            onClick={() => { void cancel(); }}
            className="rounded bg-rose-600 px-3 py-2 text-sm font-medium text-white hover:bg-rose-700"
            data-testid="ai-assistant-cancel"
          >
            Stop
          </button>
        ) : (
          <button
            type="button"
            onClick={() => { void submit(); }}
            disabled={!isConnected || !value.trim()}
            className="rounded bg-slate-900 px-3 py-2 text-sm font-medium text-white hover:bg-slate-800 disabled:cursor-not-allowed disabled:bg-slate-300"
            data-testid="ai-assistant-send"
          >
            Send
          </button>
        )}
      </div>
    </div>
  );
}
