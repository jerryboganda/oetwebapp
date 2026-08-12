'use client';

import { useState } from 'react';
import { Button } from '@/components/ui/button';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  askAiAboutPassage,
  type ChatMessage,
  type PassageQnaResponse,
} from '@/lib/reading-pathway-api';

export function GroundedReadingPassageQna({
  attemptId,
  passageId,
}: {
  attemptId: string;
  passageId: string;
}) {
  const [message, setMessage] = useState('');
  const [history, setHistory] = useState<ChatMessage[]>([]);
  const [result, setResult] = useState<PassageQnaResponse | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const visibleHistory = result ? history.slice(0, -2) : history;

  const ask = async () => {
    const trimmed = message.trim();
    if (!trimmed || loading) return;
    setLoading(true);
    setError(null);
    try {
      const response = await askAiAboutPassage(attemptId, passageId, trimmed, history);
      setResult(response);
      setHistory(response.history);
      setMessage('');
    } catch (err) {
      setError(readErrorMessage(err, 'Grounded Reading Q&A is not available for this passage yet.'));
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="mt-4 rounded-xl border border-sky-200 bg-sky-50/70 p-4 dark:border-sky-400/30 dark:bg-sky-950/20" data-testid="reading-grounded-qna">
      <div>
        <p className="text-[11px] font-black uppercase tracking-[0.14em] text-sky-700 dark:text-sky-300">Ask about this Reading passage</p>
        <p className="mt-1 text-xs text-muted">Grounded advisory only; it cannot change your marks.</p>
      </div>
      {visibleHistory.length > 0 ? (
        <div className="mt-3 space-y-2 text-sm leading-6 text-navy dark:text-white/90" aria-live="polite">
          {visibleHistory.slice(-6).map((item, index) => (
            <p key={`${item.role}-${index}`}>
              <strong>{item.role === 'user' ? 'You' : 'AI'}:</strong> {item.content}
            </p>
          ))}
        </div>
      ) : null}
      <div className="mt-3 flex flex-col gap-2 sm:flex-row sm:items-end">
        <label className="flex-1">
          <span className="sr-only">Ask a question about this Reading passage</span>
          <textarea
            value={message}
            onChange={(event) => setMessage(event.target.value)}
            onKeyDown={(event) => {
              if ((event.ctrlKey || event.metaKey) && event.key === 'Enter') void ask();
            }}
            maxLength={2000}
            rows={2}
            placeholder="What does the passage say about this point?"
            className="w-full resize-y rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy outline-none ring-primary/30 placeholder:text-muted focus:ring-2 dark:text-white"
          />
        </label>
        <Button type="button" size="sm" variant="outline" onClick={() => void ask()} disabled={loading || !message.trim()}>
          {loading ? 'Thinking…' : 'Ask AI'}
        </Button>
      </div>
      {error ? <p className="mt-3 text-sm font-semibold text-danger" role="alert">{error}</p> : null}
      {result ? (
        <div className="mt-3 rounded-lg border border-sky-200 bg-white/70 p-3 text-sm leading-6 text-navy dark:border-sky-400/30 dark:bg-sky-950/30 dark:text-white/90" role="status" aria-live="polite">
          <p>{result.reply}</p>
          <p className="mt-2 text-[11px] text-muted">Answer grounded in the stored passage; advisory only and marks are unaffected.</p>
        </div>
      ) : null}
    </div>
  );
}
