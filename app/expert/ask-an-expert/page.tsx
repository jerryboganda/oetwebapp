'use client';

import { useState, useEffect, useCallback } from 'react';
import { MessageSquareText, CheckCircle2, Clock, Send, ChevronRight, RefreshCw } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';

type AskThread = {
  id: string;
  title: string;
  authorDisplayName: string;
  replyCount: number;
  viewCount: number;
  hasExpertAnswer: boolean;
  createdAt: string;
  lastActivityAt: string;
};

type ThreadsResponse = {
  total: number;
  categoryId: string;
  threads: AskThread[];
};

async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { ...init?.headers, Authorization: `Bearer ${token}` } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function AskAnExpertPage() {
  const [data, setData] = useState<ThreadsResponse | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [replyingTo, setReplyingTo] = useState<string | null>(null);
  const [replyBody, setReplyBody] = useState('');
  const [submitting, setSubmitting] = useState(false);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const d = await apiRequest<ThreadsResponse>('/v1/expert/community/ask-an-expert?page=1&pageSize=50');
      setData(d);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load threads');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); analytics.track('expert_ask_an_expert_viewed'); }, [load]);

  const submitReply = async (threadId: string) => {
    if (!replyBody.trim()) return;
    setSubmitting(true);
    try {
      await apiRequest<{ id: string }>(`/v1/expert/community/threads/${threadId}/verified-reply`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ body: replyBody }),
      });
      analytics.track('expert_verified_reply_posted', { threadId });
      setReplyBody('');
      setReplyingTo(null);
      await load();
    } catch {
      setError('Failed to post reply');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="min-h-screen bg-background">
      <div className="max-w-4xl mx-auto px-4 py-8 space-y-6">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="text-3xl font-bold flex items-center gap-2">
              <MessageSquareText className="w-8 h-8" /> Ask an Expert
            </h1>
            <p className="text-muted-foreground mt-1">
              Answer learner questions with verified expert responses.
            </p>
          </div>
          <Button variant="outline" size="sm" onClick={load} disabled={loading}>
            <RefreshCw className={`w-4 h-4 mr-1 ${loading ? 'animate-spin' : ''}`} /> Refresh
          </Button>
        </div>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading && !data && (
          <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-lg" />)}</div>
        )}

        {data && (
          <MotionSection className="space-y-3">
            <div className="flex gap-3 mb-4">
              <Badge variant="secondary">{data.total} questions</Badge>
              <Badge variant="outline">{data.threads.filter(t => !t.hasExpertAnswer).length} unanswered</Badge>
            </div>

            {/* Unanswered first */}
            {[...data.threads].sort((a, b) => (a.hasExpertAnswer === b.hasExpertAnswer ? 0 : a.hasExpertAnswer ? 1 : -1)).map(thread => (
              <MotionItem key={thread.id}>
                <Card className={`p-4 ${!thread.hasExpertAnswer ? 'border-amber-300 dark:border-amber-700 bg-amber-50/50 dark:bg-amber-900/10' : ''}`}>
                  <div className="flex items-start justify-between gap-3">
                    <div className="flex-1">
                      <div className="flex items-center gap-2 mb-1">
                        {thread.hasExpertAnswer ? (
                          <Badge variant="default" className="gap-1"><CheckCircle2 className="w-3 h-3" /> Answered</Badge>
                        ) : (
                          <Badge variant="outline" className="gap-1 border-amber-500 text-amber-700 dark:text-amber-400"><Clock className="w-3 h-3" /> Needs Answer</Badge>
                        )}
                        <span className="text-xs text-muted-foreground">{thread.replyCount} replies · {thread.viewCount} views</span>
                      </div>
                      <h3 className="font-semibold">{thread.title}</h3>
                      <p className="text-xs text-muted-foreground mt-1">
                        by {thread.authorDisplayName} · {new Date(thread.createdAt).toLocaleDateString()}
                      </p>
                    </div>
                    {!thread.hasExpertAnswer && (
                      <Button size="sm" variant="primary" onClick={() => { setReplyingTo(replyingTo === thread.id ? null : thread.id); setReplyBody(''); }}>
                        <Send className="w-4 h-4 mr-1" /> Reply
                      </Button>
                    )}
                    {thread.hasExpertAnswer && (
                      <ChevronRight className="w-5 h-5 text-muted-foreground flex-shrink-0" />
                    )}
                  </div>

                  {replyingTo === thread.id && (
                    <div className="mt-4 space-y-3 border-t pt-3">
                      <textarea
                        className="w-full rounded-md border bg-background p-3 text-sm resize-none focus:ring-2 focus:ring-primary focus:outline-none"
                        rows={4}
                        placeholder="Write your expert-verified answer..."
                        value={replyBody}
                        onChange={(e) => setReplyBody(e.target.value)}
                      />
                      <div className="flex items-center justify-between">
                        <p className="text-xs text-muted-foreground">Your reply will be marked as &quot;Expert Verified&quot;</p>
                        <div className="flex gap-2">
                          <Button variant="ghost" size="sm" onClick={() => setReplyingTo(null)}>Cancel</Button>
                          <Button size="sm" onClick={() => submitReply(thread.id)} disabled={submitting || !replyBody.trim()}>
                            {submitting ? 'Posting...' : 'Post Verified Reply'}
                          </Button>
                        </div>
                      </div>
                    </div>
                  )}
                </Card>
              </MotionItem>
            ))}

            {data.threads.length === 0 && (
              <Card className="p-8 text-center text-muted-foreground">
                <MessageSquareText className="w-12 h-12 mx-auto mb-3 opacity-30" />
                <p>No questions yet. Learners will post questions in the &quot;Ask an Expert&quot; forum category.</p>
              </Card>
            )}
          </MotionSection>
        )}
      </div>
    </div>
  );
}
