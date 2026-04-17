'use client';

import { useEffect, useState } from 'react';
import { MotionItem, MotionPresence } from '@/components/ui/motion-primitives';
import { MessageSquare, Plus, Clock, ChevronRight, Mic, Zap, History } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader, ExamTypeBadge } from '@/components/domain';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Card } from '@/components/ui/card';
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
  const heroHighlights = [
    { icon: MessageSquare, label: 'Sessions', value: `${history.length}` },
    { icon: Mic, label: 'Mode', value: 'AI partner' },
    { icon: History, label: 'Status', value: 'Live history' },
  ];

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
      <LearnerPageHero
        eyebrow="Practice"
        title="AI Conversation Practice"
        description="Practise speaking with an AI partner. Get real-time transcription and detailed evaluation."
        icon={MessageSquare}
        accent="purple"
        highlights={heroHighlights}
      />

      {error && <InlineAlert variant="warning" className="mb-4">{error}</InlineAlert>}

      {/* Start New Session */}
      <section className="mb-8">
        <LearnerSurfaceSectionHeader
          eyebrow="Session builder"
          title="Start a new conversation"
          description="Choose a conversation type to begin practising."
          className="mb-4"
        />
        <div className="grid grid-cols-1 gap-4 md:grid-cols-3">
          {TASK_TYPES.map((task, i) => (
            <MotionItem
              key={task.code}
              delayIndex={i}
            >
              <button
                onClick={() => handleStart(task.code)}
                disabled={creating}
                className="group w-full rounded-3xl border border-border bg-surface p-5 text-left shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical disabled:opacity-50"
              >
              <div className="flex items-start gap-3">
                <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-primary/10 text-primary transition-colors group-hover:bg-primary/15">
                  <task.icon className="w-5 h-5" />
                </div>
                <div className="flex-1 min-w-0">
                  <h3 className="text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">
                    {task.label}
                  </h3>
                  <p className="mt-1 text-xs text-muted">{task.description}</p>
                </div>
                <ChevronRight className="mt-1 h-4 w-4 text-muted transition-colors group-hover:text-primary" />
              </div>
              </button>
            </MotionItem>
          ))}
        </div>
      </section>

      {/* History */}
      <section>
        <LearnerSurfaceSectionHeader
          eyebrow="History"
          title="Recent conversations"
          description="Review and continue your previous practice sessions."
          className="mb-4"
        />

        {loading ? (
          <div className="space-y-3">
            {Array.from({ length: 3 }).map((_, i) => (
              <Skeleton key={i} className="h-20 rounded-3xl" />
            ))}
          </div>
        ) : history.length === 0 ? (
          <Card className="border-dashed border-border p-8 text-center shadow-sm">
            <MessageSquare className="mx-auto mb-3 h-10 w-10 text-gray-300" />
            <p className="text-sm font-semibold text-navy">No conversations yet</p>
            <p className="mt-1 text-xs text-muted">Start your first AI conversation above</p>
          </Card>
        ) : (
          <MotionPresence>
            <div className="space-y-3">
              {history.map((session, i) => {
                const stateInfo = STATE_LABELS[session.state] ?? STATE_LABELS.preparing;
                const isViewable = session.state === 'evaluated' || session.state === 'completed';
                return (
                  <MotionItem
                    key={session.id}
                    delayIndex={i}
                  >
                    <Link
                      href={isViewable ? `/conversation/${session.id}/results` : `/conversation/${session.id}`}
                      className="block rounded-3xl border border-border bg-surface p-4 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical"
                    >
                      <div className="flex items-center justify-between gap-3">
                        <div className="flex items-center gap-3 min-w-0">
                          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                            <MessageSquare className="h-4 w-4" />
                          </div>
                          <div className="min-w-0">
                            <div className="flex items-center gap-2 mb-0.5">
                              <span className="truncate text-sm font-semibold text-navy">
                                {session.taskTypeCode.replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase())}
                              </span>
                              <ExamTypeBadge examType={session.examTypeCode} size="sm" />
                            </div>
                            <div className="flex items-center gap-3 text-xs text-muted">
                              <span>{dateFormatter.format(new Date(session.createdAt))}</span>
                              <span className="flex items-center gap-1">
                                <Clock className="h-3 w-3" />{formatDuration(session.durationSeconds)}
                              </span>
                              <span>{session.turnCount} turns</span>
                            </div>
                          </div>
                        </div>
                        <div className="flex items-center gap-2">
                          <span className={`rounded-full px-2 py-0.5 text-xs font-medium ${stateInfo.color}`}>
                            {stateInfo.label}
                          </span>
                          <ChevronRight className="h-4 w-4 text-muted" />
                        </div>
                      </div>
                    </Link>
                  </MotionItem>
                );
              })}
            </div>
          </MotionPresence>
        )}
      </section>
      </div>
    </LearnerDashboardShell>
  );
}
