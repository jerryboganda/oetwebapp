'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { useTranslations } from 'next-intl';
import { useReducedMotionConfig } from 'motion/react';
import { Sparkles, Send, FileText } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
import { MotionSection } from '@/components/ui/motion-primitives';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { prefersReducedMotion } from '@/lib/motion';
import { listWritingScenarios, requestWritingAsk, getWritingSubmission } from '@/lib/writing/api';
import type {
  WritingAskMessageDto,
  WritingScenarioDto,
  WritingSubmissionDto,
} from '@/lib/writing/types';

export default function WritingAskToolPage() {
  const t = useTranslations();
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

  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());

  useEffect(() => {
    // Only follow new messages: on the empty first render this jumped the page to the chat.
    if (messages.length > 0) {
      listEndRef.current?.scrollIntoView({ behavior: reducedMotion ? 'auto' : 'smooth', block: 'nearest' });
    }
  }, [messages.length, reducedMotion]);

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
      setError(err instanceof Error ? err.message : t('writing.tools.ask.error.loadSubmission'));
    }
  };

  const send = async () => {
    if (!draft.trim() || sending) return;
    if (!submission || !selectedScenarioId) {
      setError(t('writing.tools.ask.error.pickFirst'));
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
      setError(err instanceof Error ? err.message : t('writing.tools.ask.error.send'));
    } finally {
      setSending(false);
    }
  };

  const scenarioPreview = useMemo(() => scenarios.find((s) => s.id === selectedScenarioId)?.title ?? null, [scenarios, selectedScenarioId]);

  return (
    <>
      <LearnerPageHero
        eyebrow={t('writing.tools.ask.eyebrow')}
        icon={Sparkles}
        accent="writing"
        title={t('writing.tools.ask.title')}
        description={t('writing.tools.ask.description')}
      />

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <MotionSection delayIndex={0}>
        <Card padding="lg">
          <h2 className="text-base font-bold text-navy">{t('writing.tools.ask.loadHeading')}</h2>
          <div className="mt-2 flex flex-wrap items-end gap-2">
            <label className="flex min-w-0 flex-1 flex-col gap-1">
              <span className="eyebrow text-muted">{t('writing.tools.ask.submissionIdLabel')}</span>
              <input
                type="text"
                value={submissionInput}
                onChange={(e) => setSubmissionInput(e.target.value)}
                placeholder={t('writing.tools.ask.submissionIdPlaceholder')}
                className="min-h-11 rounded-control border border-border bg-background px-3 text-sm font-semibold text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                dir="ltr"
              />
            </label>
            <Button onClick={() => void onLoadSubmission()} disabled={!submissionInput.trim()}>{t('writing.tools.ask.loadButton')}</Button>
          </div>
          {submission ? (
            <div className="mt-3 rounded-xl bg-background-light p-3">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant="info" size="sm">{submission.mode}</Badge>
                <span className="text-xs text-muted">
                  <FileText className="me-1 inline h-3 w-3" aria-hidden="true" />
                  {/* Scenario title/id are OET-authored or system identifiers; force LTR. */}
                  <span dir="ltr">{scenarioPreview ?? submission.scenarioId}</span> · <span className="tabular-nums">{t('writing.tools.ask.wordCount', { count: submission.wordCount })}</span>
                </span>
              </div>
            </div>
          ) : null}
        </Card>
      </MotionSection>

      <MotionSection delayIndex={1}>
        <Card padding="lg">
          <h2 className="text-base font-bold text-navy">{t('writing.tools.ask.conversationHeading')}</h2>
          <ul className="mt-3 space-y-3" aria-live="polite" aria-label={t('writing.tools.ask.chatLabel')}>
            {messages.length === 0 ? (
              <li className="text-sm text-muted">{t('writing.tools.ask.empty')}</li>
            ) : null}
            {messages.map((m, idx) => (
              <li
                key={idx}
                className={`max-w-prose rounded-2xl px-3 py-2 text-sm ${m.role === 'learner' ? 'ms-auto bg-primary text-white dark:bg-primary-700' : 'bg-background-light text-navy'}`}
              >
                {/* Message content (learner question and AI answer) is English content per spec. */}
                <p className="whitespace-pre-wrap" dir="ltr">{m.content}</p>
              </li>
            ))}
          </ul>
          <div ref={listEndRef} aria-hidden="true" />
          <div className="mt-3 flex items-end gap-2">
            <label className="min-w-0 flex-1">
              <span className="sr-only">{t('writing.tools.ask.inputLabel')}</span>
              <textarea
                value={draft}
                onChange={(e) => setDraft(e.target.value)}
                rows={2}
                placeholder={t('writing.tools.ask.placeholder')}
                className="min-h-12 w-full rounded-control border border-border bg-background p-2 text-sm text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                onKeyDown={(e) => {
                  if (e.key === 'Enter' && !e.shiftKey) {
                    e.preventDefault();
                    void send();
                  }
                }}
                aria-label={t('writing.tools.ask.inputLabel')}
              />
            </label>
            <Button onClick={() => void send()} loading={sending} disabled={!draft.trim() || !submission}>
              <Send className="h-3 w-3 rtl:-scale-x-100" aria-hidden="true" /> {t('writing.tools.ask.send')}
            </Button>
          </div>
        </Card>
      </MotionSection>
    </>
  );
}
