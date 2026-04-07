'use client';

import { useEffect, useState } from 'react';
import { motion, AnimatePresence } from 'motion/react';
import { MessageSquare, Plus, Clock, ChevronRight, Mic, Zap, History } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, ExamTypeBadge } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { analytics } from '@/lib/analytics';
import {
  createConversation,
  getConversationHistory,
} from '@/lib/api';

type SessionSummary = {
  id: string;
  taskTypeCode: string;
  examTypeCode: string;
  state: string;
  turnCount: number;
  durationSeconds: number;
  createdAt: string;
  completedAt: string | null;
};

const TASK_TYPES = [
  { code: 'oet-roleplay', label: 'OET Clinical Role Play', description: 'Practise 5-minute clinical scenarios', icon: Mic },
  { code: 'ielts-part2', label: 'IELTS Part 2 Long Turn', description: '1–2 minute individual talk', icon: MessageSquare },
  { code: 'oet-handover', label: 'OET Handover', description: 'Practise structured clinical handovers', icon: Zap },
];

const STATE_LABELS: Record<string, { label: string; color: string }> = {
  preparing: { label: 'Preparing', color: 'bg-gray-100 text-gray-600 dark:bg-gray-800 dark:text-gray-400' },
  active: { label: 'In Progress', color: 'bg-blue-100 text-blue-700 dark:bg-blue-900/40 dark:text-blue-300' },
  evaluating: { label: 'Evaluating...', color: 'bg-yellow-100 text-yellow-700 dark:bg-yellow-900/40 dark:text-yellow-300' },
  evaluated: { label: 'Completed', color: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300' },
  completed: { label: 'Completed', color: 'bg-green-100 text-green-700 dark:bg-green-900/40 dark:text-green-300' },
  abandoned: { label: 'Abandoned', color: 'bg-red-100 text-red-700 dark:bg-red-900/40 dark:text-red-300' },
};

export default function ConversationPage() {
  const router = useRouter();
  const [history, setHistory] = useState<SessionSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('conversation_page_viewed');
    getConversationHistory(1, 10)
      .then((data: Record<string, unknown>) => {
        const items = Array.isArray(data?.items) ? data.items : (Array.isArray(data) ? data : []);
        setHistory(items as SessionSummary[]);
      })
      .catch(() => setError('Could not load conversation history.'))
      .finally(() => setLoading(false));
  }, []);

  async function handleStart(taskTypeCode: string) {
    if (creating) return;
    setCreating(true);
    setError(null);
    try {
      const session = await createConversation({ examFamilyCode: 'oet', taskTypeCode }) as Record<string, unknown>;
      analytics.track('conversation_started', { taskTypeCode, sessionId: String(session.id) });
      router.push(`/conversation/${session.id}`);
    } catch {
      setError('Failed to start conversation. Please try again.');
      setCreating(false);
    }
  }

  const dateFormatter = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  const formatDuration = (s: number) => s >= 60 ? `${Math.floor(s / 60)}m ${s % 60}s` : `${s}s`;

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        title="AI Conversation Practice"
        description="Practise speaking with an AI partner. Get real-time transcription and detailed evaluation."
        icon={MessageSquare}
        accent="purple"
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Start New Session */}
      <section className="mb-8">
        <h2 className="text-lg font-bold text-gray-900 dark:text-white mb-4 flex items-center gap-2">
          <Plus className="w-5 h-5 text-purple-500" />
          Start a New Conversation
        </h2>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {TASK_TYPES.map((task, i) => (
            <motion.button
              key={task.code}
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: i * 0.06 }}
              onClick={() => handleStart(task.code)}
              disabled={creating}
              className="group text-left bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700 p-5 hover:border-purple-300 dark:hover:border-purple-600 hover:shadow-lg hover:shadow-purple-500/5 transition-all disabled:opacity-50"
            >
              <div className="flex items-start gap-3">
                <div className="p-2.5 rounded-xl bg-purple-100 dark:bg-purple-900/30 text-purple-600 dark:text-purple-400 group-hover:bg-purple-200 dark:group-hover:bg-purple-900/50 transition-colors">
                  <task.icon className="w-5 h-5" />
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="font-semibold text-gray-900 dark:text-white text-sm group-hover:text-purple-600 dark:group-hover:text-purple-400 transition-colors">
                    {task.label}
                  </h3>
                  <p className="text-xs text-gray-500 mt-1">{task.description}</p>
                </div>
                <ChevronRight className="w-4 h-4 mt-1 text-gray-300 group-hover:text-purple-400 transition-colors" />
              </div>
            </motion.button>
          ))}
        </div>
      </section>

      {/* History */}
      <section>
        <h2 className="text-lg font-bold text-gray-900 dark:text-white mb-4 flex items-center gap-2">
          <History className="w-5 h-5 text-gray-400" />
          Recent Conversations
        </h2>

        {loading ? (
          <div className="space-y-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-20 rounded-xl" />
            ))}
          </div>
        ) : history.length === 0 ? (
          <div className="text-center py-12 bg-gray-50 dark:bg-gray-800/50 rounded-2xl border border-dashed border-gray-200 dark:border-gray-700">
            <MessageSquare className="w-10 h-10 text-gray-300 mx-auto mb-3" />
            <p className="text-sm font-semibold text-gray-600 dark:text-gray-400">No conversations yet</p>
            <p className="text-xs text-gray-400 mt-1">Start your first AI conversation above</p>
          </div>
        ) : (
          <AnimatePresence>
            <div className="space-y-3">
              {history.map((session, i) => {
                const stateInfo = STATE_LABELS[session.state] ?? STATE_LABELS.preparing;
                const isViewable = session.state === 'evaluated' || session.state === 'completed';
                return (
                  <motion.div
                    key={session.id}
                    initial={{ opacity: 0, y: 8 }}
                    animate={{ opacity: 1, y: 0 }}
                    transition={{ delay: i * 0.04 }}
                  >
                    <Link
                      href={isViewable ? `/conversation/${session.id}/results` : `/conversation/${session.id}`}
                      className="block bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700 p-4 hover:border-purple-300 dark:hover:border-purple-600 hover:shadow-sm transition-all"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-3 min-w-0">
                          <div className="p-2 rounded-lg bg-purple-50 dark:bg-purple-900/20">
                            <MessageSquare className="w-4 h-4 text-purple-500" />
                          </div>
                          <div className="min-w-0">
                            <div className="flex items-center gap-2 mb-0.5">
                              <span className="text-sm font-semibold text-gray-900 dark:text-white truncate">
                                {session.taskTypeCode.replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase())}
                              </span>
                              <ExamTypeBadge examType={session.examTypeCode} size="sm" />
                            </div>
                            <div className="flex items-center gap-3 text-xs text-gray-400">
                              <span>{dateFormatter.format(new Date(session.createdAt))}</span>
                              <span className="flex items-center gap-1">
                                <Clock className="w-3 h-3" />{formatDuration(session.durationSeconds)}
                              </span>
                              <span>{session.turnCount} turns</span>
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          <span className={`text-xs font-medium px-2 py-0.5 rounded-full ${stateInfo.color}`}>
                            {stateInfo.label}
                          </span>
                          <ChevronRight className="w-4 h-4 text-gray-300" />
                        </div>
                      </div>
                    </Link>
                  </motion.div>
                );
              })}
            </div>
          </AnimatePresence>
        )}
      </section>
    </LearnerDashboardShell>
  );
}
