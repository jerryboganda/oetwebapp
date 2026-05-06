'use client';

import { useState } from 'react';
import { useRouter } from 'next/navigation';
import { useExpertMessageThreads, useCreateExpertThread } from '@/lib/hooks/use-expert-messages';
import { ExpertRouteWorkspace, ExpertRouteHero, ExpertRouteSectionHeader } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
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
        <div className="text-center py-12 rounded-lg border border-dashed">
          <MessageSquare className="w-8 h-8 mx-auto text-muted-foreground mb-2" />
          <p className="text-muted-foreground">No messages yet.</p>
          <p className="text-xs text-muted-foreground mt-1">Start a new thread to contact the admin team.</p>
        </div>
      ) : (
        <div className="space-y-2">
          {threads.map((thread) => (
            <button
              key={thread.id}
              onClick={() => router.push(`/expert/messages/${thread.id}`)}
              className="w-full text-left rounded-lg border p-4 hover:bg-accent transition-colors"
            >
              <div className="flex items-center justify-between">
                <p className="font-medium">{thread.title}</p>
                <span className={`text-xs px-2 py-0.5 rounded-full ${thread.status === 'open' ? 'bg-green-100 text-green-700' : 'bg-muted text-muted-foreground'}`}>
                  {thread.status}
                </span>
              </div>
              <p className="text-xs text-muted-foreground mt-1">{thread.replyCount} replies · {new Date(thread.updatedAt).toLocaleDateString()}</p>
            </button>
          ))}
        </div>
      )}

      {showNew && (
        <Modal open onClose={() => setShowNew(false)} title="New Message">
          <div className="space-y-4">
            <div>
              <label className="text-sm font-medium">Title</label>
              <input
                value={title}
                onChange={e => setTitle(e.target.value)}
                className="w-full mt-1 rounded-md border px-3 py-2 text-sm"
                placeholder="Brief subject"
                maxLength={200}
              />
            </div>
            <div>
              <label className="text-sm font-medium">Message</label>
              <textarea
                value={body}
                onChange={e => setBody(e.target.value)}
                className="w-full mt-1 rounded-md border px-3 py-2 text-sm min-h-[120px]"
                placeholder="Describe your issue..."
                maxLength={4000}
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setShowNew(false)} disabled={saving}>Cancel</Button>
              <Button onClick={handleCreate} disabled={saving || !title.trim() || !body.trim()}>
                {saving ? 'Sending...' : 'Send'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </ExpertRouteWorkspace>
  );
}
