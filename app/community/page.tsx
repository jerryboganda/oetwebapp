'use client';

import { useEffect, useState } from 'react';
import { motion } from 'motion/react';
import { Users, MessageSquare, Plus, ChevronRight, Pin } from 'lucide-react';
import Link from 'next/link';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { fetchForumCategories, fetchForumThreads } from '@/lib/api';
import { analytics } from '@/lib/analytics';

type ForumCategory = { id: string; name: string; description: string | null; sortOrder: number };
type ForumThread = { id: string; categoryId: string; title: string; authorDisplayName: string; authorRole: string; isPinned: boolean; isLocked: boolean; replyCount: number; viewCount: number; likeCount: number; lastActivityAt: string };

export default function CommunityPage() {
  const [categories, setCategories] = useState<ForumCategory[]>([]);
  const [threads, setThreads] = useState<ForumThread[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [selectedCategory, setSelectedCategory] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('community_page_viewed');
    Promise.allSettled([fetchForumCategories(), fetchForumThreads(undefined, 1, 20)]).then(([catsR, threadsR]) => {
      if (catsR.status === 'fulfilled') setCategories(catsR.value as ForumCategory[]);
      if (threadsR.status === 'fulfilled') {
        const data = threadsR.value as { threads: ForumThread[] };
        setThreads(data.threads ?? []);
      }
      if (catsR.status === 'rejected') setError('Could not load community data.');
      setLoading(false);
    });
  }, []);

  const filtered = selectedCategory ? threads.filter(t => t.categoryId === selectedCategory) : threads;

  return (
    <LearnerDashboardShell>
      <div className="flex items-center justify-between mb-6">
        <LearnerPageHero
          title="Community"
          description="Connect with other learners, share tips, and ask questions"
          icon={Users}
        />
        <Link href="/community/threads/new">
          <button className="flex items-center gap-2 px-4 py-2 bg-indigo-600 hover:bg-indigo-700 text-white rounded-xl text-sm font-medium transition-colors">
            <Plus className="w-4 h-4" /> New Thread
          </button>
        </Link>
      </div>

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      <div className="grid grid-cols-1 lg:grid-cols-4 gap-6">
        {/* Sidebar: categories */}
        <div className="lg:col-span-1">
          <LearnerSurfaceSectionHeader title="Categories" />
          {loading ? (
            <div className="space-y-2">
              {Array.from({ length: 5 }).map((_, i) => <Skeleton key={i} className="h-10 rounded-lg" />)}
            </div>
          ) : (
            <div className="space-y-1">
              <button
                onClick={() => setSelectedCategory(null)}
                className={`w-full text-left px-3 py-2 rounded-lg text-sm transition-colors ${!selectedCategory ? 'bg-indigo-50 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-300 font-medium' : 'text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700'}`}
              >
                All Discussions
              </button>
              {categories.map(cat => (
                <button
                  key={cat.id}
                  onClick={() => setSelectedCategory(cat.id)}
                  className={`w-full text-left px-3 py-2 rounded-lg text-sm transition-colors ${selectedCategory === cat.id ? 'bg-indigo-50 dark:bg-indigo-900/20 text-indigo-700 dark:text-indigo-300 font-medium' : 'text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700'}`}
                >
                  {cat.name}
                </button>
              ))}
              <Link href="/community/groups" className="block px-3 py-2 rounded-lg text-sm text-gray-600 dark:text-gray-400 hover:bg-gray-100 dark:hover:bg-gray-700 mt-2 border-t border-gray-200 dark:border-gray-700 pt-3">
                <div className="flex items-center gap-2"><Users className="w-4 h-4" />Study Groups</div>
              </Link>
            </div>
          )}
        </div>

        {/* Main: threads */}
        <div className="lg:col-span-3">
          <LearnerSurfaceSectionHeader title={selectedCategory ? (categories.find(c => c.id === selectedCategory)?.name ?? 'Threads') : 'Recent Threads'} />
          {loading ? (
            <div className="space-y-3">
              {Array.from({ length: 6 }).map((_, i) => <Skeleton key={i} className="h-20 rounded-xl" />)}
            </div>
          ) : filtered.length === 0 ? (
            <div className="text-center py-12 text-gray-400">No threads yet. Be the first to start a discussion!</div>
          ) : (
            <div className="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 overflow-hidden">
              {filtered.map((thread, i) => (
                <motion.div
                  key={thread.id}
                  initial={{ opacity: 0 }}
                  animate={{ opacity: 1 }}
                  transition={{ delay: i * 0.03 }}
                >
                  <Link href={`/community/threads/${thread.id}`}>
                    <div className="flex items-start gap-3 px-5 py-4 border-b border-gray-100 dark:border-gray-700 last:border-0 hover:bg-gray-50 dark:hover:bg-gray-700/30 transition-colors cursor-pointer group">
                      <div className="p-2 bg-gray-100 dark:bg-gray-700 rounded-lg flex-shrink-0 group-hover:bg-indigo-100 dark:group-hover:bg-indigo-900/30 transition-colors">
                        <MessageSquare className="w-4 h-4 text-gray-500 dark:text-gray-400 group-hover:text-indigo-500" />
                      </div>
                      <div className="flex-1 min-w-0">
                        <div className="flex items-center gap-2 mb-0.5">
                          {thread.isPinned && <Pin className="w-3.5 h-3.5 text-indigo-400" />}
                          <span className="font-medium text-gray-900 dark:text-white text-sm group-hover:text-indigo-600 dark:group-hover:text-indigo-400 transition-colors line-clamp-1">{thread.title}</span>
                        </div>
                        <div className="text-xs text-gray-400">
                          by {thread.authorDisplayName} · {thread.replyCount} replies · {thread.viewCount} views
                        </div>
                      </div>
                      <ChevronRight className="w-4 h-4 text-gray-300 group-hover:text-indigo-400 flex-shrink-0 mt-1 transition-colors" />
                    </div>
                  </Link>
                </motion.div>
              ))}
            </div>
          )}
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
