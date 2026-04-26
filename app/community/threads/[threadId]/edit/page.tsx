'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter, useParams } from 'next/navigation';
import { PenLine, ArrowLeft } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui';
import { useAuth } from '@/contexts/auth-context';
import { apiClient, fetchForumThread, fetchForumCategories } from '@/lib/api';
import { analytics } from '@/lib/analytics';

interface ForumThread {
  id: string;
  categoryId: string;
  authorUserId: string;
  authorDisplayName: string;
  title: string;
  body: string;
}

interface ForumCategory {
  id: string;
  name: string;
}

export default function EditThreadPage() {
  const router = useRouter();
  const params = useParams();
  const threadId = params?.threadId as string;
  const { user } = useAuth();

  const [categories, setCategories] = useState<ForumCategory[]>([]);
  const [title, setTitle] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [body, setBody] = useState('');
  const [loading, setLoading] = useState(true);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const loadData = useCallback(async () => {
    if (!threadId) return;
    setLoading(true);
    setError(null);
    try {
      const [thread, cats] = await Promise.all([
        fetchForumThread(threadId) as Promise<ForumThread>,
        fetchForumCategories() as Promise<ForumCategory[]>,
      ]);

      if (user && thread.authorUserId !== user.userId) {
        setError('You can only edit your own threads.');
        setLoading(false);
        return;
      }

      setTitle(thread.title);
      setCategoryId(thread.categoryId);
      setBody(thread.body);
      setCategories(Array.isArray(cats) ? cats : []);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Failed to load thread');
    } finally {
      setLoading(false);
    }
  }, [threadId, user]);

  useEffect(() => {
    loadData();
    analytics.track('community_edit_thread_viewed', { threadId });
  }, [loadData, threadId]);

  function validate(): boolean {
    const errors: Record<string, string> = {};
    if (!title.trim()) errors.title = 'Title is required';
    else if (title.trim().length < 5) errors.title = 'Title must be at least 5 characters';
    if (!categoryId) errors.categoryId = 'Please select a category';
    if (!body.trim()) errors.body = 'Body is required';
    else if (body.trim().length < 10) errors.body = 'Body must be at least 10 characters';
    setFieldErrors(errors);
    return Object.keys(errors).length === 0;
  }

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!validate()) return;

    setSubmitting(true);
    setError(null);
    try {
      await apiClient.put(`/v1/community/threads/${encodeURIComponent(threadId)}`, {
        categoryId,
        title: title.trim(),
        body: body.trim(),
      });
      analytics.track('community_thread_updated', { threadId });
      router.push(`/community/threads/${threadId}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to update thread');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="Edit Thread">
      <LearnerPageHero
        title="Edit Thread"
        description="Update your thread content."
        icon={PenLine}
      />

      <MotionSection className="mx-auto max-w-2xl space-y-4">
        <Button variant="outline" size="sm" onClick={() => router.push(`/community/threads/${threadId}`)}>
          <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Thread
        </Button>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {loading ? (
          <div className="space-y-3">
            <Skeleton className="h-12 w-full rounded-2xl" />
            <Skeleton className="h-12 w-full rounded-2xl" />
            <Skeleton className="h-40 w-full rounded-2xl" />
          </div>
        ) : (
          <Card className="p-6 shadow-sm">
            <form onSubmit={handleSubmit} className="space-y-5">
              <Input
                label="Title"
                value={title}
                onChange={e => setTitle(e.target.value)}
                error={fieldErrors.title}
                maxLength={200}
              />

              <Select
                label="Category"
                options={categories.map(c => ({ value: c.id, label: c.name }))}
                value={categoryId}
                onChange={e => setCategoryId(e.target.value)}
                error={fieldErrors.categoryId}
              />

              <Textarea
                label="Body"
                value={body}
                onChange={e => setBody(e.target.value)}
                error={fieldErrors.body}
                rows={8}
              />

              <div className="flex items-center justify-end gap-3 pt-2">
                <Button
                  type="button"
                  variant="outline"
                  onClick={() => router.push(`/community/threads/${threadId}`)}
                  disabled={submitting}
                >
                  Cancel
                </Button>
                <Button type="submit" disabled={submitting}>
                  {submitting ? 'Saving…' : 'Save Changes'}
                </Button>
              </div>
            </form>
          </Card>
        )}
      </MotionSection>
    </LearnerDashboardShell>
  );
}
