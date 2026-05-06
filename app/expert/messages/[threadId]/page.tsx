'use client';

import { useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { useExpertMessageThread, usePostExpertReply } from '@/lib/hooks/use-expert-messages';
import { ExpertRouteWorkspace, ExpertRouteHero } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { ArrowLeft, Send } from 'lucide-react';

export default function MessageThreadPage() {
  const params = useParams();
  const router = useRouter();
  const threadId = (params as Record<string, string>)?.threadId || '';
  const { thread, loading, error, refresh } = useExpertMessageThread(threadId);
  const { reply, sending } = usePostExpertReply();
  const [body, setBody] = useState('');

  const handleReply = async () => {
    if (!body.trim()) return;
    await reply(threadId, body.trim());
    setBody('');
    refresh();
  };

  if (loading) {
    return (
      <ExpertRouteWorkspace>
        <Skeleton className="h-8 w-64 mb-4" />
        <div className="space-y-3">{[...Array(3)].map((_, i) => <Skeleton key={i} className="h-16 rounded-lg" />)}</div>
      </ExpertRouteWorkspace>
    );
  }

  if (error || !thread) {
    return (
      <ExpertRouteWorkspace>
        <ErrorState message={error ?? 'Thread not found'} onRetry={refresh} />
      </ExpertRouteWorkspace>
    );
  }

  return (
    <ExpertRouteWorkspace>
      <button onClick={() => router.push('/expert/messages')} className="text-sm text-muted-foreground hover:text-foreground mb-4 flex items-center gap-1">
        <ArrowLeft className="w-4 h-4" /> Back to Messages
      </button>

      <div className="flex items-center justify-between mb-4">
        <ExpertRouteHero title={thread.title} description={thread.status === 'open' ? 'Open' : 'Closed'} />
      </div>

      <div className="space-y-3 mb-6">
        {thread.replies.map((msg) => (
          <div key={msg.id} className={`rounded-lg border p-3 ${msg.authorRole === 'expert' ? 'bg-primary/5 border-primary/20' : ''}`}>
            <div className="flex items-center justify-between mb-1">
              <p className="text-sm font-medium">{msg.authorName}</p>
              <span className="text-xs text-muted-foreground">
                {msg.authorRole === 'expert' ? 'Expert' : 'Admin'} · {new Date(msg.createdAt).toLocaleString()}
              </span>
            </div>
            <p className="text-sm whitespace-pre-wrap">{msg.body}</p>
          </div>
        ))}
      </div>

      {thread.status === 'open' && (
        <div className="flex gap-2">
          <textarea
            value={body}
            onChange={e => setBody(e.target.value)}
            className="flex-1 rounded-md border px-3 py-2 text-sm min-h-[60px] resize-none"
            placeholder="Type your reply..."
            maxLength={4000}
            onKeyDown={e => {
              if (e.key === 'Enter' && (e.ctrlKey || e.metaKey)) handleReply();
            }}
          />
          <Button onClick={handleReply} disabled={sending || !body.trim()} className="self-end">
            <Send className="w-4 h-4" />
          </Button>
        </div>
      )}
    </ExpertRouteWorkspace>
  );
}
