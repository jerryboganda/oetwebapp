'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useExpertMessageThreads, useCreateExpertThread } from '@/lib/hooks/use-expert-messages';
import { ExpertRouteWorkspace, ExpertRouteHero, ExpertRouteSectionHeader } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { Badge } from '@/components/ui/badge';
import { Input, Textarea } from '@/components/ui/form-controls';
import { MessageSquare, Plus } from 'lucide-react';

export default function MessagesPage() {
  const router = useRouter();
  const { threads, loading, error, refresh } = useExpertMessageThreads();
  const { create, saving } = useCreateExpertThread();
  const [showNew, setShowNew] = useState(false);
  const [title, setTitle] = useState('');
  const [body, setBody] = useState('');

  const handleCreate = async () => {
    if (!title.trim() || !body.trim()) return;
    const thread = await create({ title: title.trim(), body: body.trim() });
    setShowNew(false);
    setTitle('');
    setBody('');
    router.push(`/expert/messages/${thread.id}`);
  };

  if (loading) {
    return (
      <ExpertRouteWorkspace>
        <ExpertRouteHero title="Messages" description="Communicate with the admin team." />
        <div className="space-y-3">{[...Array(3)].map((_, i) => <Skeleton key={i} className="h-20 rounded-lg" />)}</div>
      </ExpertRouteWorkspace>
    );
  }

  if (error) {
    return (
      <ExpertRouteWorkspace>
        <ExpertRouteHero title="Messages" description="Communicate with the admin team." />
        <ErrorState message={error} onRetry={refresh} />
      </ExpertRouteWorkspace>
    );
  }

  return (
    <ExpertRouteWorkspace>
      <ExpertRouteHero
        title="Messages"
        description="Communicate with the admin team."
      />

      <div className="flex justify-end">
        <Button size="sm" onClick={() => setShowNew(true)}>
          <Plus className="w-4 h-4 mr-1" />New Thread
        </Button>
      </div>

      {threads.length === 0 ? (
        <div className="rounded-2xl border border-dashed border-border bg-surface py-12 text-center">
          <MessageSquare className="mx-auto mb-2 h-8 w-8 text-muted" />
          <p className="font-medium text-navy dark:text-foreground">No messages yet.</p>
          <p className="mt-1 text-xs text-muted">Start a new thread to contact the admin team.</p>
        </div>
      ) : (
        <div className="space-y-2">
          {threads.map((thread) => (
            <button
              key={thread.id}
              onClick={() => router.push(`/expert/messages/${thread.id}`)}
              className="w-full rounded-2xl border border-border bg-surface p-4 text-left shadow-sm transition-all hover:border-primary/40 hover:shadow-md"
            >
              <div className="flex items-center justify-between gap-3">
                <p className="font-semibold text-navy dark:text-foreground">{thread.title}</p>
                <Badge variant={thread.status === 'open' ? 'success' : 'muted'} size="sm">
                  {thread.status}
                </Badge>
              </div>
              <p className="mt-1 text-xs text-muted">{thread.replyCount} replies · {new Date(thread.updatedAt).toLocaleDateString()}</p>
            </button>
          ))}
        </div>
      )}

      {showNew && (
        <Modal open onClose={() => setShowNew(false)} title="New Message">
          <div className="space-y-4">
            <Input
              label="Title"
              value={title}
              onChange={(e) => setTitle(e.target.value)}
              placeholder="Brief subject"
              maxLength={200}
              required
            />
            <Textarea
              label="Message"
              value={body}
              onChange={(e) => setBody(e.target.value)}
              placeholder="Describe your issue..."
              maxLength={4000}
              required
            />
            <div className="flex justify-end gap-2 pt-2">
              <Button variant="outline" onClick={() => setShowNew(false)} disabled={saving}>Cancel</Button>
              <Button onClick={handleCreate} disabled={saving || !title.trim() || !body.trim()} loading={saving}>
                Send
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </ExpertRouteWorkspace>
  );
}
