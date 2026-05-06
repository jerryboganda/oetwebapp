'use client';

import { useEffect, useState } from 'react';
import { MotionItem, MotionPresence } from '@/components/ui/motion-primitives';
import { MessageSquare, Clock, ChevronRight, Mic, Zap, History, Sparkles } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader, ExamTypeBadge } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { analytics } from '@/lib/analytics';
import {
  createConversation,
  getConversationHistory,
  getConversationTaskTypes,
  getConversationEntitlement,
} from '@/lib/api';
import type {
  ConversationHistoryItem,
  ConversationTaskTypeCatalog,
  ConversationEntitlement,
} from '@/lib/types/conversation';
import { formatScaledScore } from '@/lib/scoring';

const TASK_ICONS: Record<string, LucideIcon> = {
  'oet-roleplay': Mic,
  'oet-handover': Zap,
};

const STATE_LABELS: Record<string, { label: string; color: string }> = {
  preparing: { label: 'Preparing', color: 'bg-background-light text-muted border border-border' },
  active: { label: 'In Progress', color: 'bg-info/10 text-info border border-info/20' },
  evaluating: { label: 'Evaluating…', color: 'bg-warning/10 text-warning border border-warning/20' },
  evaluated: { label: 'Completed', color: 'bg-success/10 text-success border border-success/20' },
  completed: { label: 'Completed', color: 'bg-success/10 text-success border border-success/20' },
  abandoned: { label: 'Abandoned', color: 'bg-danger/10 text-danger border border-danger/20' },
  failed: { label: 'Failed', color: 'bg-danger/10 text-danger border border-danger/20' },
};

export default function ConversationPage() {
  const router = useRouter();
  const [history, setHistory] = useState<ConversationHistoryItem[]>([]);
  const [catalog, setCatalog] = useState<ConversationTaskTypeCatalog | null>(null);
  const [entitlement, setEntitlement] = useState<ConversationEntitlement | null>(null);
  const [loading, setLoading] = useState(true);
  const [creating, setCreating] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('conversation_page_viewed');
    Promise.allSettled([
      getConversationTaskTypes() as Promise<ConversationTaskTypeCatalog>,
      getConversationEntitlement() as Promise<ConversationEntitlement>,
      getConversationHistory(1, 10) as Promise<{ items?: ConversationHistoryItem[] }>,
    ])
      .then(([catRes, entRes, histRes]) => {
        if (catRes.status === 'fulfilled') setCatalog(catRes.value);
        if (entRes.status === 'fulfilled') setEntitlement(entRes.value);
        if (histRes.status === 'fulfilled') setHistory(Array.isArray(histRes.value?.items) ? histRes.value.items : []);
        else setError('Could not load conversation history.');
      })
      .finally(() => setLoading(false));
  }, []);

  async function handleStart(taskTypeCode: string) {
    if (creating) return;
    if (entitlement && !entitlement.allowed) { setError(entitlement.reason); return; }
    setCreating(taskTypeCode);
    setError(null);
    try {
      const session = (await createConversation({ taskTypeCode })) as { id?: string };
      if (!session?.id) throw new Error('no session id');
      analytics.track('conversation_started', { taskTypeCode, sessionId: session.id });
      router.push(`/conversation/${session.id}`);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to start conversation.');
      setCreating(null);
    }
  }

  const dateFormatter = new Intl.DateTimeFormat('en-US', { month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit' });
  const formatDuration = (s: number) => (s >= 60 ? `${Math.floor(s / 60)}m ${s % 60}s` : `${s}s`);

  const remainingLabel = (() => {
    if (!entitlement) return 'Loading…';
    if (entitlement.limit === -1 || entitlement.remaining === -1) return 'Unlimited';
    return `${entitlement.remaining}/${entitlement.limit} left`;
  })();

  const heroHighlights = [
    { icon: MessageSquare, label: 'Sessions', value: `${history.length}` },
    { icon: Mic, label: 'Mode', value: 'AI partner' },
    { icon: Sparkles, label: 'Entitlement', value: remainingLabel },
    { icon: History, label: 'Status', value: 'Live history' },
  ];

  const entitlementVariant: 'default' | 'success' | 'warning' | 'danger' =
    !entitlement ? 'default'
      : !entitlement.allowed ? 'danger'
      : entitlement.tier === 'paid' || entitlement.tier === 'trial' ? 'success'
      : entitlement.remaining <= 1 ? 'warning' : 'default';

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <LearnerPageHero
          eyebrow="AI Conversation"
          title="Rehearse a real OET roleplay before exam day"
          description="Speak with an AI patient partner — every session is graded against the OET Speaking rubric and projected to the 0–500 scale (pass = 350). Advisory only."
          icon={MessageSquare}
          accent="purple"
          highlights={heroHighlights}
        />

        <LearnerSkillSwitcher compact />

        {entitlement && !entitlement.allowed && (
          <InlineAlert variant="warning">
            {entitlement.reason}
            {entitlement.resetAt && (<> {' '}Quota resets at <strong>{new Date(entitlement.resetAt).toLocaleString()}</strong>.</>)}
          </InlineAlert>
        )}

        {error && <InlineAlert variant="warning">{error}</InlineAlert>}

        <section>
          <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
            <LearnerSurfaceSectionHeader
              eyebrow="Session builder"
              title="Start a new conversation"
              description="Choose a scenario type to begin practising. Sessions are ~5 minutes and graded automatically."
            />
            {entitlement && (
              <Badge variant={entitlementVariant}>
                {entitlement.tier.toUpperCase()} · {remainingLabel}
              </Badge>
            )}
          </div>

          {loading && !catalog ? (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {Array.from({ length: 2 }).map((_, i) => (<Skeleton key={i} className="h-24 rounded-[24px]" />))}
            </div>
          ) : catalog && catalog.taskTypes.length > 0 ? (
            <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
              {catalog.taskTypes.map((task, i) => {
                const Icon = TASK_ICONS[task.code] ?? MessageSquare;
                const isCreatingThis = creating === task.code;
                const disabled = !!creating || (entitlement ? !entitlement.allowed : false);
                return (
                  <MotionItem key={task.code} delayIndex={i}>
                    <button onClick={() => handleStart(task.code)} disabled={disabled}
                      className="group w-full rounded-[24px] border border-border bg-surface p-5 text-left shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical disabled:opacity-50">
                      <div className="flex items-start gap-3">
                        <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-primary/10 text-primary transition-colors group-hover:bg-primary/15">
                          <Icon className="w-5 h-5" />
                        </div>
                        <div className="flex-1 min-w-0">
                          <h3 className="text-sm font-semibold text-navy transition-colors group-hover:text-primary-dark">
                            {task.label}
                          </h3>
                          <p className="mt-1 text-xs text-muted">{task.description}</p>
                          {isCreatingThis && (<p className="mt-2 text-xs text-primary">Creating session…</p>)}
                        </div>
                        <ChevronRight className="mt-1 h-4 w-4 text-muted transition-colors group-hover:text-primary" />
                      </div>
                    </button>
                  </MotionItem>
                );
              })}
            </div>
          ) : (
            <LearnerEmptyState
              icon={MessageSquare}
              title="No scenario types are currently enabled"
              description="AI conversation scenarios will appear here once they are published. Use speaking role plays or mocks while this module is being prepared."
              primaryAction={{ label: 'Open Speaking', href: '/speaking' }}
              secondaryAction={{ label: 'Open Mocks', href: '/mocks' }}
            />
          )}
        </section>

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="History"
            title="Recent conversations"
            description="Review and continue your previous practice sessions."
            className="mb-4"
          />

          {loading ? (
            <div className="space-y-3">
              {Array.from({ length: 3 }).map((_, i) => (<Skeleton key={i} className="h-20 rounded-[24px]" />))}
            </div>
          ) : history.length === 0 ? (
            <LearnerEmptyState
              compact
              icon={MessageSquare}
              title="No conversations yet"
              description="Start your first AI conversation above, or use a speaking role play if you need a structured exam-style prompt."
              primaryAction={{ label: 'Open Speaking', href: '/speaking' }}
              secondaryAction={{ label: 'Track Progress', href: '/progress' }}
            />
          ) : (
            <MotionPresence>
              <div className="space-y-3">
                {history.map((session, i) => {
                  const stateInfo = STATE_LABELS[session.state] ?? STATE_LABELS.preparing;
                  const isViewable = session.state === 'evaluated' || session.state === 'completed';
                  return (
                    <MotionItem key={session.id} delayIndex={i}>
                      <Link href={isViewable ? `/conversation/${session.id}/results` : `/conversation/${session.id}`}
                        className="block rounded-[24px] border border-border bg-surface p-4 shadow-sm transition-[border-color,box-shadow,transform] duration-200 hover:border-border-hover hover:shadow-clinical">
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
                              <div className="flex flex-wrap items-center gap-3 text-xs text-muted">
                                <span>{dateFormatter.format(new Date(session.createdAt))}</span>
                                <span className="flex items-center gap-1">
                                  <Clock className="h-3 w-3" />{formatDuration(session.durationSeconds)}
                                </span>
                                <span>{session.turnCount} turns</span>
                                {session.scaledScore != null && (
                                  <span className="font-medium text-navy">
                                    {formatScaledScore(session.scaledScore)}
                                    {session.overallGrade ? ` · Grade ${session.overallGrade}` : ''}
                                  </span>
                                )}
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
