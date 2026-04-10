'use client';

import { useState, useEffect, useCallback } from 'react';
import { MessageSquareText, CheckCircle2, Clock, Plus, Send } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/inline-alert';
import { analytics } from '@/lib/analytics';

type AskThread = {
  id: string;
  categoryId: string;
  title: string;
  authorDisplayName: string;
  authorRole: string;
  replyCount: number;
  viewCount: number;
  isPinned: boolean;
  createdAt: string;
  lastActivityAt: string;
};

type ThreadsResponse = {
  total: number;
  threads: AskThread[];
};

type Category = { id: string; name: string };

async function apiRequest<T>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth/token-manager');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { ...init?.headers, Authorization: `Bearer ${token}` } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function AskAnExpertLearnerPage() {
  const [threads, setThreads] = useState<AskThread[]>([]);
  const [total, setTotal] = useState(0);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [showNew, setShowNew] = useState(false);
  const [newTitle, setNewTitle] = useState('');
  const [newBody, setNewBody] = useState('');
  const [posting, setPosting] = useState(false);
  const [categoryId, setCategoryId] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      // First get the "Ask an Expert" category ID
      const categories = await apiRequest<Category[]>('/v1/community/categories');
      const askCat = categories.find(c => c.name === 'Ask an Expert');
      if (!askCat) {
        setError('Ask an Expert category not found');
        setLoading(false);
        return;
      }
      setCategoryId(askCat.id);
      const data = await apiRequest<ThreadsResponse>(`/v1/community/threads?categoryId=${askCat.id}&page=1&pageSize=50`);
      setThreads(data.threads);
      setTotal(data.total);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => { load(); analytics.track('learner_ask_an_expert_viewed'); }, [load]);

  const postQuestion = async () => {
    if (!newTitle.trim() || !newBody.trim() || !categoryId) return;
    setPosting(true);
    try {
      await apiRequest<{ id: string }>('/v1/community/threads', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ categoryId, title: newTitle, body: newBody }),
      });
      analytics.track('learner_expert_question_posted');
      setNewTitle('');
      setNewBody('');
      setShowNew(false);
      await load();
    } catch {
      setError('Failed to post question');
    } finally {
      setPosting(false);
    }
  };

  return (
    <LearnerDashboardShell>
      <LearnerPageHero title="Ask an Expert" subtitle="Get verified answers from certified OET expert reviewers." />

      <MotionSection className="px-4 py-6 max-w-4xl mx-auto space-y-6">
        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <div className="flex items-center justify-between">
          <LearnerSurfaceSectionHeader title={`${total} Questions`} />
          <Button onClick={() => setShowNew(!showNew)} variant={showNew ? 'outline' : 'default'} size="sm">
            <Plus className="w-4 h-4 mr-1" /> {showNew ? 'Cancel' : 'Ask a Question'}
          </Button>
        </div>

        {showNew && (
          <Card className="p-5 space-y-3 border-primary/30 bg-primary/5">
            <input
              type="text"
              className="w-full rounded-md border bg-background p-3 text-sm focus:ring-2 focus:ring-primary focus:outline-none"
              placeholder="Your question title..."
              value={newTitle}
              onChange={(e) => setNewTitle(e.target.value)}
              maxLength={128}
            />
            <textarea
              className="w-full rounded-md border bg-background p-3 text-sm resize-none focus:ring-2 focus:ring-primary focus:outline-none"
              rows={4}
              placeholder="Describe your question in detail..."
              value={newBody}
              onChange={(e) => setNewBody(e.target.value)}
            />
            <div className="flex justify-end">
              <Button onClick={postQuestion} disabled={posting || !newTitle.trim() || !newBody.trim()}>
                <Send className="w-4 h-4 mr-1" /> {posting ? 'Posting...' : 'Post Question'}
              </Button>
            </div>
          </Card>
        )}

        {loading && !threads.length && (
          <div className="space-y-3">{Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-16 rounded-lg" />)}</div>
        )}

        <div className="space-y-3">
          {threads.map(thread => {
            const hasExpertReply = thread.authorRole === 'expert';
            return (
              <MotionItem key={thread.id}>
                <Card className="p-4">
                  <div className="flex items-center gap-2 mb-1">
                    {thread.isPinned && <Badge variant="default">Pinned</Badge>}
                    {hasExpertReply ? (
                      <Badge variant="default" className="gap-1"><CheckCircle2 className="w-3 h-3" /> Expert Reply</Badge>
                    ) : (
                      <Badge variant="outline" className="gap-1"><Clock className="w-3 h-3" /> Awaiting Expert</Badge>
                    )}
                    <span className="text-xs text-muted-foreground">{thread.replyCount} replies · {thread.viewCount} views</span>
                  </div>
                  <h3 className="font-semibold">{thread.title}</h3>
                  <p className="text-xs text-muted-foreground mt-1">
                    by {thread.authorDisplayName} · {new Date(thread.createdAt).toLocaleDateString()}
                  </p>
                </Card>
              </MotionItem>
            );
          })}
        </div>

        {!loading && threads.length === 0 && (
          <Card className="p-8 text-center text-muted-foreground">
            <MessageSquareText className="w-12 h-12 mx-auto mb-3 opacity-30" />
            <p>No questions yet. Be the first to ask an expert!</p>
          </Card>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
