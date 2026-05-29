'use client';

/**
 * Writing Module V2 — Buddy System page (spec §23.5).
 *
 * Three states the user can be in:
 *   1. Not opted in    → explanation + opt-in CTA.
 *   2. Opted in / queued (no pair yet)
 *                       → "Looking for a buddy" panel + Find buddy button.
 *   3. Paired           → anonymised partner card, chat thread, send
 *                         message form, weekly check-in widget,
 *                         option to end the pair.
 */

import { useCallback, useEffect, useMemo, useState } from 'react';
import { Users, MessageSquare, CalendarCheck, X, Loader2, RefreshCcw } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { LearnerPageHero } from '@/components/domain/learner-surface';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Card, CardContent } from '@/components/ui/card';
import {
  endWritingBuddyPair,
  getWritingBuddyPair,
  listWritingBuddyMessages,
  optInWritingBuddy,
  requestWritingBuddyMatch,
  sendWritingBuddyMessage,
  submitWritingBuddyCheckIn,
  type WritingBuddyMessageDto,
  type WritingBuddyPairDto,
} from '@/lib/writing/buddy';

const MESSAGE_MAX = 500;
const MESSAGES_PER_DAY = 10;

interface CheckInDraft {
  highlight: string;
  challenge: string;
  goalNextWeek: string;
}

const EMPTY_CHECK_IN: CheckInDraft = {
  highlight: '',
  challenge: '',
  goalNextWeek: '',
};

export default function WritingBuddyPage() {
  const [pair, setPair] = useState<WritingBuddyPairDto | null>(null);
  const [optedIn, setOptedIn] = useState<boolean | null>(null);
  const [messages, setMessages] = useState<WritingBuddyMessageDto[]>([]);
  const [draftMessage, setDraftMessage] = useState('');
  const [matchStatus, setMatchStatus] = useState<'idle' | 'queued' | 'matched'>('idle');
  const [checkInDraft, setCheckInDraft] = useState<CheckInDraft>(EMPTY_CHECK_IN);
  const [busy, setBusy] = useState<'opt-in' | 'match' | 'send' | 'check-in' | 'end' | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const loadPair = useCallback(async () => {
    try {
      const p = await getWritingBuddyPair();
      setPair(p);
      if (p) {
        setOptedIn(true);
        setMatchStatus('matched');
        const msgs = await listWritingBuddyMessages(p.id, 50);
        setMessages(msgs);
      }
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load your buddy.');
    }
  }, []);

  useEffect(() => {
    void loadPair();
  }, [loadPair]);

  const onOptIn = async () => {
    setBusy('opt-in');
    setError(null);
    try {
      const r = await optInWritingBuddy();
      setOptedIn(r.optedIn);
      setNotice('You are now opted into the Buddy System.');
      if (r.activePairId) {
        await loadPair();
      } else {
        // Auto-attempt a match immediately so the learner sees progress.
        const m = await requestWritingBuddyMatch();
        setMatchStatus(m.status);
        if (m.status === 'matched') {
          await loadPair();
        }
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not opt in.');
    } finally {
      setBusy(null);
    }
  };

  const onFindBuddy = async () => {
    setBusy('match');
    setError(null);
    try {
      const r = await requestWritingBuddyMatch();
      setMatchStatus(r.status);
      if (r.status === 'matched') {
        await loadPair();
        setNotice(`Paired with ${r.partnerDisplayName ?? 'a buddy'}!`);
      } else {
        setNotice('No buddy available yet. We will keep looking.');
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not find a buddy.');
    } finally {
      setBusy(null);
    }
  };

  const onSendMessage = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!pair || !draftMessage.trim()) return;
    setBusy('send');
    setError(null);
    try {
      const msg = await sendWritingBuddyMessage(pair.id, draftMessage.trim());
      setMessages((prev) => [...prev, msg]);
      setDraftMessage('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not send message.');
    } finally {
      setBusy(null);
    }
  };

  const onSubmitCheckIn = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!pair) return;
    setBusy('check-in');
    setError(null);
    try {
      await submitWritingBuddyCheckIn(pair.id, checkInDraft);
      setNotice('Weekly check-in saved.');
      setCheckInDraft(EMPTY_CHECK_IN);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not save check-in.');
    } finally {
      setBusy(null);
    }
  };

  const onEndPair = async () => {
    if (!pair) return;
    if (typeof window !== 'undefined' && !window.confirm('End this buddy pairing? You can request a new buddy afterwards.')) return;
    setBusy('end');
    setError(null);
    try {
      await endWritingBuddyPair(pair.id, 'user_ended');
      setPair(null);
      setMessages([]);
      setMatchStatus('idle');
      setNotice('Buddy pairing ended.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not end pair.');
    } finally {
      setBusy(null);
    }
  };

  const charsRemaining = useMemo(() => MESSAGE_MAX - draftMessage.length, [draftMessage]);
  const sentToday = useMemo(() => {
    const since = Date.now() - 24 * 60 * 60 * 1000;
    return messages.filter((m) => m.mineMessage && new Date(m.sentAt).getTime() >= since).length;
  }, [messages]);

  return (
    <LearnerDashboardShell pageTitle="Buddy System">
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="Community"
          icon={Users}
          accent="purple"
          title="Find a Writing buddy"
          description="Pair anonymously with someone at your level. Send short check-ins and keep each other accountable on the way to your target band."
          highlights={[]}
        />

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {notice ? <InlineAlert variant="success">{notice}</InlineAlert> : null}

        {/* State 1 — not opted in */}
        {optedIn === false || optedIn === null ? (
          <Card>
            <CardContent className="space-y-4 p-6">
              <h2 className="text-lg font-bold text-navy">How the Buddy System works</h2>
              <ul className="ml-5 list-disc space-y-2 text-sm text-navy/80">
                <li>We pair you anonymously with another learner in your profession at ±1 band.</li>
                <li>You can swap short messages (max {MESSAGE_MAX} characters, {MESSAGES_PER_DAY}/day).</li>
                <li>Each week you both submit a short check-in: highlight, challenge, next goal.</li>
                <li>No personal details are shared. You can end the pairing at any time.</li>
              </ul>
              <Button onClick={onOptIn} disabled={busy === 'opt-in'}>
                {busy === 'opt-in' ? (
                  <span className="inline-flex items-center gap-2"><Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" /> Opting in…</span>
                ) : (
                  'Opt in to the Buddy System'
                )}
              </Button>
            </CardContent>
          </Card>
        ) : null}

        {/* State 2 — opted in but no pair yet */}
        {optedIn && !pair ? (
          <Card>
            <CardContent className="space-y-3 p-6 text-center">
              <Loader2 className="mx-auto h-8 w-8 animate-spin text-violet-500" aria-hidden="true" />
              <h2 className="text-lg font-bold text-navy">Looking for a buddy…</h2>
              <p className="text-sm text-navy/80">
                {matchStatus === 'queued'
                  ? 'No match yet. We will keep looking; tap the button to retry now.'
                  : 'You are in the matching pool.'}
              </p>
              <Button onClick={onFindBuddy} disabled={busy === 'match'}>
                <RefreshCcw className="mr-2 h-4 w-4" aria-hidden="true" /> Find buddy now
              </Button>
            </CardContent>
          </Card>
        ) : null}

        {/* State 3 — paired */}
        {pair ? (
          <div className="grid gap-6 lg:grid-cols-3">
            {/* Partner card + chat */}
            <Card className="lg:col-span-2">
              <CardContent className="space-y-4 p-6">
                <header className="flex items-start justify-between gap-4">
                  <div>
                    <h2 className="text-lg font-bold text-navy">{pair.partnerDisplayName}</h2>
                    <p className="text-xs uppercase tracking-wider text-muted">
                      {pair.profession} · matched at band {pair.matchedAtBand}
                    </p>
                  </div>
                  <Button variant="outline" size="sm" onClick={onEndPair} disabled={busy === 'end'}>
                    <X className="mr-1 h-3.5 w-3.5" aria-hidden="true" /> End pair
                  </Button>
                </header>

                <section aria-label="Conversation" className="rounded-2xl border border-border bg-background p-3">
                  <div className="mb-2 flex items-center gap-2 text-xs font-bold uppercase tracking-wider text-muted">
                    <MessageSquare className="h-3.5 w-3.5" aria-hidden="true" /> Conversation
                  </div>
                  <div className="space-y-2 max-h-80 overflow-y-auto" role="log" aria-live="polite">
                    {messages.length === 0 ? (
                      <p className="text-sm text-muted">No messages yet. Say hi.</p>
                    ) : null}
                    {messages.map((m) => (
                      <article
                        key={m.id}
                        className={`max-w-[80%] rounded-2xl px-3 py-2 text-sm ${m.mineMessage ? 'ml-auto bg-primary text-white dark:bg-violet-700' : 'bg-surface text-navy'}`}
                      >
                        <p className="whitespace-pre-wrap">{m.bodyMarkdown}</p>
                        <time
                          className={`mt-1 block text-[10px] uppercase tracking-wider ${m.mineMessage ? 'text-white/70' : 'text-muted'}`}
                          dateTime={m.sentAt}
                        >
                          {new Date(m.sentAt).toLocaleString()}
                        </time>
                      </article>
                    ))}
                  </div>
                </section>

                <form onSubmit={onSendMessage} className="space-y-2" aria-label="Send a buddy message">
                  <label htmlFor="buddy-message" className="block text-xs font-bold uppercase tracking-wider text-muted">
                    Message · {sentToday}/{MESSAGES_PER_DAY} sent today
                  </label>
                  <textarea
                    id="buddy-message"
                    value={draftMessage}
                    onChange={(e) => setDraftMessage(e.target.value)}
                    maxLength={MESSAGE_MAX}
                    rows={3}
                    placeholder="Keep it short and supportive. Maximum 500 characters."
                    className="w-full rounded-xl border border-border bg-background p-3 text-sm text-navy focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                  />
                  <div className="flex items-center justify-between">
                    <span className={`text-xs ${charsRemaining < 0 ? 'text-red-600' : 'text-muted'}`}>
                      {charsRemaining} characters remaining
                    </span>
                    <Button type="submit" disabled={busy === 'send' || !draftMessage.trim() || charsRemaining < 0}>
                      {busy === 'send' ? 'Sending…' : 'Send'}
                    </Button>
                  </div>
                </form>
              </CardContent>
            </Card>

            {/* Weekly check-in */}
            <Card>
              <CardContent className="space-y-3 p-6">
                <h2 className="flex items-center gap-2 text-lg font-bold text-navy">
                  <CalendarCheck className="h-5 w-5" aria-hidden="true" /> Weekly check-in
                </h2>
                <p className="text-xs text-muted">
                  Both of you submit one short reflection per week. We mark the week complete when
                  both halves are in.
                </p>
                <form onSubmit={onSubmitCheckIn} className="space-y-3">
                  <label className="block text-xs font-bold uppercase tracking-wider text-muted">
                    Highlight
                    <textarea
                      value={checkInDraft.highlight}
                      onChange={(e) => setCheckInDraft({ ...checkInDraft, highlight: e.target.value })}
                      rows={2}
                      className="mt-1 w-full rounded-xl border border-border bg-background p-2 text-sm text-navy"
                    />
                  </label>
                  <label className="block text-xs font-bold uppercase tracking-wider text-muted">
                    Challenge
                    <textarea
                      value={checkInDraft.challenge}
                      onChange={(e) => setCheckInDraft({ ...checkInDraft, challenge: e.target.value })}
                      rows={2}
                      className="mt-1 w-full rounded-xl border border-border bg-background p-2 text-sm text-navy"
                    />
                  </label>
                  <label className="block text-xs font-bold uppercase tracking-wider text-muted">
                    Goal for next week
                    <textarea
                      value={checkInDraft.goalNextWeek}
                      onChange={(e) => setCheckInDraft({ ...checkInDraft, goalNextWeek: e.target.value })}
                      rows={2}
                      className="mt-1 w-full rounded-xl border border-border bg-background p-2 text-sm text-navy"
                    />
                  </label>
                  <Button type="submit" disabled={busy === 'check-in'}>
                    {busy === 'check-in' ? 'Saving…' : 'Submit check-in'}
                  </Button>
                </form>
              </CardContent>
            </Card>
          </div>
        ) : null}
      </div>
    </LearnerDashboardShell>
  );
}
