'use client';

import { useState, useEffect, useCallback } from 'react';
import { useRouter } from 'next/navigation';
import { PenLine, ArrowLeft } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Input, Textarea, Select } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { fetchForumCategories, createForumThread } from '@/lib/api';
import { analytics } from '@/lib/analytics';

interface ForumCategory {
  id: string;
  name: string;
  description: string;
}

export default function NewThreadPage() {
  const router = useRouter();

  const [categories, setCategories] = useState<ForumCategory[]>([]);
  const [title, setTitle] = useState('');
  const [categoryId, setCategoryId] = useState('');
  const [body, setBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});

  const loadCategories = useCallback(async () => {
    try {
      const cats = await fetchForumCategories() as ForumCategory[];
      setCategories(Array.isArray(cats) ? cats : []);
    } catch {
      // Non-blocking
    }
  }, []);

  useEffect(() => {
    loadCategories();
    analytics.track('community_new_thread_viewed');
  }, [loadCategories]);

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
      const res = await createForumThread({ categoryId, title: title.trim(), body: body.trim() }) as { id: string };
      analytics.track('community_thread_created', { threadId: res.id, categoryId });
      router.push(`/community/threads/${res.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to create thread');
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell pageTitle="New Thread">
      <LearnerPageHero
        title="Start a Discussion"
        description="Share your question or insight with the community."
        icon={PenLine}
      />

      <MotionSection className="mx-auto max-w-2xl space-y-4">
        <Button variant="outline" size="sm" onClick={() => router.push('/community')}>
          <ArrowLeft className="mr-1.5 h-4 w-4" /> Back to Threads
        </Button>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        <Card className="p-6 shadow-sm">
          <form onSubmit={handleSubmit} className="space-y-5">
            <Input
              label="Title"
              placeholder="What do you want to discuss?"
              value={title}
              onChange={e => setTitle(e.target.value)}
              error={fieldErrors.title}
              maxLength={200}
            />

            <Select
              label="Category"
              placeholder="Select a category"
              options={categories.map(c => ({ value: c.id, label: c.name }))}
              value={categoryId}
              onChange={e => setCategoryId(e.target.value)}
              error={fieldErrors.categoryId}
            />

            <Textarea
              label="Body"
              placeholder="Write your thread content here..."
              value={body}
              onChange={e => setBody(e.target.value)}
              error={fieldErrors.body}
              rows={8}
            />

            <div className="flex items-center justify-end gap-3 pt-2">
              <Button
                type="button"
                variant="outline"
                onClick={() => router.push('/community')}
                disabled={submitting}
              >
                Cancel
              </Button>
              <Button type="submit" disabled={submitting}>
                {submitting ? 'Creating…' : 'Create Thread'}
              </Button>
            </div>
          </form>
        </Card>
      </MotionSection>
    </LearnerDashboardShell>
  );
}
