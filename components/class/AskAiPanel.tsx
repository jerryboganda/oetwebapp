'use client';

import { useEffect, useRef, useState, type FormEvent } from 'react';
import { Bot, Send, Sparkles } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { apiClient, isApiError } from '@/lib/api';

interface AskAiPanelProps {
  sessionId: string;
}

interface ChatMessage {
  id: string;
  role: 'user' | 'assistant';
  content: string;
}

interface AssistantApiResponse {
  answer?: string;
  message?: string;
  content?: string;
}

/**
 * Class AI Q&A panel — calls /v1/me/classes/sessions/{sessionId}/ai-qa.
 *
 * The endpoint is gated by feature code `class.assistant.qna.v1` wired in
 * wave A2 but not yet exposed in wave B1. Until the endpoint is live we
 * surface a friendly notice on the first 404 instead of breaking the page.
 */
export function AskAiPanel({ sessionId }: AskAiPanelProps) {
  const [messages, setMessages] = useState<ChatMessage[]>([]);
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [unavailable, setUnavailable] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const scrollRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (scrollRef.current) {
      scrollRef.current.scrollTop = scrollRef.current.scrollHeight;
    }
  }, [messages]);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    const trimmed = draft.trim();
    if (!trimmed || sending || unavailable) return;

    const userMsg: ChatMessage = { id: `u-${Date.now()}`, role: 'user', content: trimmed };
    setMessages((prev) => [...prev, userMsg]);
    setDraft('');
    setSending(true);
    setError(null);

    try {
      const response = await apiClient.post<AssistantApiResponse>(
        `/v1/me/classes/sessions/${encodeURIComponent(sessionId)}/ai-qa`,
        { question: trimmed },
      );
      const answer = response?.answer ?? response?.message ?? response?.content ?? 'No answer.';
      setMessages((prev) => [...prev, { id: `a-${Date.now()}`, role: 'assistant', content: answer }]);
    } catch (err: unknown) {
      if (isApiError(err) && err.status === 404) {
        setUnavailable(true);
      } else {
        setError(err instanceof Error ? err.message : 'Could not contact the AI assistant.');
      }
    } finally {
      setSending(false);
    }
  }

  return (
    <aside className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4 shadow-sm">
      <header className="flex items-center gap-2">
        <Sparkles className="h-4 w-4 text-primary" />
        <h3 className="text-sm font-semibold text-navy">Ask AI about this class</h3>
      </header>

      {unavailable ? (
        <InlineAlert variant="info">
          AI Q&amp;A isn’t enabled yet for this class. Please check back later.
        </InlineAlert>
      ) : (
        <>
          <div
            ref={scrollRef}
            className="flex max-h-96 min-h-[160px] flex-col gap-2 overflow-y-auto rounded-xl bg-background-light p-3"
            aria-live="polite"
          >
            {messages.length === 0 ? (
              <div className="flex flex-col items-center justify-center py-6 text-center text-xs text-muted">
                <Bot className="mb-2 h-6 w-6 text-muted/60" />
                Ask anything about today’s class. Examples: <em>“Summarise the speaking section.”</em>
              </div>
            ) : (
              messages.map((m) => (
                <div
                  key={m.id}
                  className={
                    m.role === 'user'
                      ? 'ml-auto max-w-[85%] rounded-2xl bg-primary px-3 py-2 text-sm text-white'
                      : 'mr-auto max-w-[85%] rounded-2xl bg-surface px-3 py-2 text-sm text-navy shadow-sm'
                  }
                >
                  {m.content}
                </div>
              ))
            )}
          </div>

          {error ? <InlineAlert variant="warning">{error}</InlineAlert> : null}

          <form onSubmit={(e) => void handleSubmit(e)} className="flex flex-col gap-2">
            <Textarea
              value={draft}
              onChange={(e) => setDraft(e.target.value)}
              placeholder="Ask a question about this class..."
              rows={2}
              onKeyDown={(e) => {
                if (e.key === 'Enter' && !e.shiftKey) {
                  e.preventDefault();
                  void handleSubmit(e as unknown as FormEvent);
                }
              }}
            />
            <div className="flex justify-end">
              <Button type="submit" variant="primary" size="sm" loading={sending} disabled={!draft.trim()}>
                <Send className="h-3.5 w-3.5" /> Send
              </Button>
            </div>
          </form>
        </>
      )}
    </aside>
  );
}
