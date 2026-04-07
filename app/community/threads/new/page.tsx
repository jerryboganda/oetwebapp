'use client';

import { useState, useEffect } from 'react';
import { motion } from 'motion/react';
import { MessageSquare, ArrowLeft, Send } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { fetchForumCategories, createForumThread } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ForumCategory = { id: string; name: string };

export default function NewThreadPage() {
  const router = useRouter();
  const [categories, setCategories] = useState<ForumCategory[]>([]);
  const [form, setForm] = useState({ categoryId: '', title: '', body: '' });
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('new_thread_page_viewed');
    fetchForumCategories().then(data => {
      const cats = data as ForumCategory[];
      setCategories(cats);
      if (cats.length > 0) setForm(p => ({ ...p, categoryId: cats[0].id }));
    }).catch(() => {});
  }, []);

  async function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (!form.categoryId || !form.title.trim() || !form.body.trim() || submitting) return;
    setSubmitting(true);
    setError(null);
    try {
      const res = await createForumThread({ categoryId: form.categoryId, title: form.title.trim(), body: form.body.trim() }) as { id: string };
      router.push(`/community/threads/${res.id}`);
    } catch {
      setError('Could not post thread. Please try again.');
      setSubmitting(false);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="flex items-center gap-3 mb-6">
        <Link href="/community" className="text-gray-400 hover:text-gray-600 dark:hover:text-gray-300">
          <ArrowLeft className="w-5 h-5" />
        </Link>
        <LearnerPageHero title="New Discussion" description="Start a new thread in the community forum" icon={MessageSquare} />
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="max-w-2xl mx-auto">
        <motion.form initial={{ opacity: 0, y: 10 }} animate={{ opacity: 1, y: 0 }} onSubmit={handleSubmit} className="space-y-4">
          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Category</label>
            <select
              value={form.categoryId}
              onChange={e => setForm(p => ({ ...p, categoryId: e.target.value }))}
              required
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
            >
              {categories.map(cat => <option key={cat.id} value={cat.id}>{cat.name}</option>)}
            </select>
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Title</label>
            <input
              type="text"
              value={form.title}
              onChange={e => setForm(p => ({ ...p, title: e.target.value }))}
              required
              placeholder="What's your question or topic?"
              maxLength={200}
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 text-sm focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
          </div>

          <div>
            <label className="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5">Body</label>
            <textarea
              value={form.body}
              onChange={e => setForm(p => ({ ...p, body: e.target.value }))}
              required
              placeholder="Share your question, experience, or discussion topic in detail..."
              rows={8}
              className="w-full px-3 py-2.5 border border-gray-200 dark:border-gray-700 rounded-xl bg-white dark:bg-gray-800 text-gray-800 dark:text-gray-200 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-indigo-400"
            />
          </div>

          <div className="flex justify-end gap-3">
            <Link href="/community" className="px-5 py-2.5 border border-gray-300 dark:border-gray-600 rounded-xl text-sm text-gray-600 dark:text-gray-400 hover:bg-gray-50 dark:hover:bg-gray-700">
              Cancel
            </Link>
            <button
              type="submit"
              disabled={submitting || !form.title.trim() || !form.body.trim()}
              className="flex items-center gap-2 px-6 py-2.5 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-sm font-medium disabled:opacity-50 transition-colors"
            >
              <Send className="w-4 h-4" />
              {submitting ? 'Posting...' : 'Post Thread'}
            </button>
          </div>
        </motion.form>
      </div>
    </LearnerDashboardShell>
  );
}
