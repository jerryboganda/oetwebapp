'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Sparkles, Send, FileText } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { listWritingScenarios, requestWritingAsk, getWritingSubmission } from '@/lib/writing/api';
import type {
  WritingAskMessageDto,
  WritingScenarioDto,
  WritingSubmissionDto,
} from '@/lib/writing/types';

export default function WritingAskToolPage() {
  const [scenarios, setScenarios] = useState<WritingScenarioDto[]>([]);
  const [selectedScenarioId, setSelectedScenarioId] = useState<string | null>(null);
  const [submissionInput, setSubmissionInput] = useState('');
  const [submission, setSubmission] = useState<WritingSubmissionDto | null>(null);
  const [threadId, setThreadId] = useState<string | undefined>(undefined);
  const [messages, setMessages] = useState<WritingAskMessageDto[]>([]);
  const [draft, setDraft] = useState('');
  const [sending, setSending] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const listEndRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    let cancelled = false;
    void listWritingScenarios({ pageSize: 100 })
      .then((r) => {
        if (cancelled) return;
        setScenarios(r.items);
        if (r.items[0]) setSelectedScenarioId(r.items[0].id);
      })
      .catch(() => {
        /* not fatal */
      });
    return () => {
      cancelled = true;
    };
  }, []);

  useEffect(() => {
    listEndRef.current?.scrollIntoView({ behavior: 'smooth' });
  }, [messages.length]);

  const onLoadSubmission = async () => {
    if (!submissionInput.trim()) return;
    setError(null);
    try {
      const s = await getWritingSubmission(submissionInput.trim());
      setSubmission(s);
      setSelectedScenarioId(s.scenarioId);
      setMessages([]);
      setThreadId(undefined);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load submission.');
    }
  };

  const send = async () => {
    if (!draft.trim() || sending) return;
    if (!submission || !selectedScenarioId) {
      setError('Pick a submission first so the assistant has your letter to read.');
      return;
    }
    const turn: WritingAskMessageDto = {
      role: 'learner',
      content: draft.trim(),
      createdAt: new Date().toISOString(),
    };
    setMessages((prev) => [...prev, turn]);
    setDraft('');
    setSending(true);
    try {
      const r = await requestWritingAsk({
        threadId,
        letterContent: submission.letterContent,
        scenarioId: selectedScenarioId,
        question: turn.content,
      });
      setThreadId(r.threadId);
      setMessages((prev) => [...prev, r.reply]);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ask request failed.');
    } finally {
      setSending(false);
    }
  };

  const scenarioPreview = useMemo(() => scenarios.find((s) => s.id === selectedScenarioId)?.title ?? null, [scenarios, selectedScenarioId]);

  return (
    <LearnerDashboardShell pageTitle="Ask about your letter">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Tools"
          icon={Sparkles}
          accent="amber"
          title="Chat with the coach about a past submission"
          description="Paste a submission ID, then ask anything — why was C4 low, how would Dr Ahmed open this letter, etc."
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <Card padding="md">
          <CardContent>
            <h2 className="text-base font-bold text-navy">Load a submission</h2>
            <div className="mt-2 flex flex-wrap items-end gap-2">
              <label className="flex flex-1 flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">
                Submission ID
                <input
                  type="text"
                  value={submissionInput}
                  onChange={(e) => setSubmissionInput(e.target.value)}
                  placeholder="e.g. 1f3b48a0-…"
                  className="min-h-11 rounded-lg border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                />
              </label>
              <Button onClick={() => void onLoadSubmission()} disabled={!submissionInput.trim()}>Load</Button>
            </div>
            {submission ? (
              <div className="mt-3 rounded-xl border border-border bg-background p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant="info" size="sm">{submission.mode}</Badge>
                  <span className="text-xs text-muted">
                    <FileText className="mr-1 inline h-3 w-3" aria-hidden="true" />
                    {scenarioPreview ?? submission.scenarioId} · {submission.wordCount} words
                  </span>
                </div>
              </div>
            ) : null}
          </CardContent>
        </Card>

        <Card padding="md">
          <CardContent>
            <h2 className="text-base font-bold text-navy">Conversation</h2>
            <ul className="mt-3 space-y-3" aria-live="polite" aria-label="Chat history">
              {messages.length === 0 ? (
                <li className="text-sm text-muted">Ask something to begin. For example: "Why did C4 drop on this letter?"</li>
              ) : null}
              {messages.map((m, idx) => (
                <li
                  key={idx}
                  className={`max-w-prose rounded-2xl px-3 py-2 text-sm ${m.role === 'learner' ? 'ml-auto bg-primary text-white' : 'bg-slate-100 text-navy'}`}
                >
                  <p className="whitespace-pre-wrap">{m.content}</p>
                </li>
              ))}
              <div ref={listEndRef} />
            </ul>
            <div className="mt-3 flex items-end gap-2">
              <label className="flex-1">
                <span className="sr-only">Type your question</span>
                <textarea
                  value={draft}
                  onChange={(e) => setDraft(e.target.value)}
                  rows={2}
                  placeholder="Type a question about the letter…"
                  className="min-h-12 w-full rounded-lg border border-border bg-background p-2 text-sm focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  onKeyDown={(e) => {
                    if (e.key === 'Enter' && !e.shiftKey) {
                      e.preventDefault();
                      void send();
                    }
                  }}
                  aria-label="Your question"
                />
              </label>
              <Button onClick={() => void send()} loading={sending} disabled={!draft.trim() || !submission}>
                <Send className="h-3 w-3" aria-hidden="true" /> Send
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </LearnerDashboardShell>
  );
}
