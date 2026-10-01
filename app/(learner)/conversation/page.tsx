'use client';

import { useEffect, useState } from 'react';
import { MotionItem } from '@/components/ui/motion-primitives';
import { MessageSquare, Clock, ChevronRight, Mic, Zap, Sparkles } from 'lucide-react';
import type { LucideIcon } from 'lucide-react';
import { useRouter } from 'next/navigation';
import { LearnerPageHero, LearnerSurfaceSectionHeader, ExamTypeBadge } from '@/components/domain';
import { LearnerEmptyState } from '@/components/domain/learner-empty-state';
import { LearnerSkillSwitcher } from '@/components/domain/learner-skill-switcher';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import { Badge } from '@/components/ui/badge';
import { cardClassName } from '@/components/ui/card';
import { CardLink } from '@/components/ui/card-link';
import { analytics } from '@/lib/analytics';
import { cn } from '@/lib/utils';
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

const STATE_LABELS: Record<string, { label: string; variant: 'slate' | 'info' | 'warning' | 'success' | 'danger' }> = {
  preparing: { label: 'Preparing', variant: 'slate' },
  active: { label: 'In Progress', variant: 'info' },
  evaluating: { label: 'Evaluating…', variant: 'warning' },
  evaluated: { label: 'Completed', variant: 'success' },
  completed: { label: 'Completed', variant: 'success' },
  abandoned: { label: 'Abandoned', variant: 'danger' },
  failed: { label: 'Failed', variant: 'danger' },
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

  // Real values only: the fixed "Mode" and "Status" chips were labels, not data.
  const heroHighlights = [
    { icon: MessageSquare, label: 'Sessions', value: `${history.length}` },
    { icon: Sparkles, label: 'Entitlement', value: remainingLabel },
  ];

  const entitlementVariant: 'default' | 'success' | 'warning' | 'danger' =
    !entitlement ? 'default'
      : !entitlement.allowed ? 'danger'
      : entitlement.tier === 'paid' || entitlement.tier === 'trial' ? 'success'
      : entitlement.remaining <= 1 ? 'warning' : 'default';

  return (
    <>
      <LearnerPageHero
        eyebrow="AI Conversation"
        title="Rehearse a real OET roleplay before exam day"
        description="Speak with an AI patient partner. Every session is graded against the OET Speaking rubric and projected to the 0–500 scale (pass = 350). Advisory only."
        icon={MessageSquare}
        accent="primary"
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

      <section className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="Session builder"
          title="Start a new conversation"
          description="Choose a scenario type to begin practising. Sessions are ~5 minutes and graded automatically."
          action={entitlement ? (
            <Badge variant={entitlementVariant} className="w-fit shrink-0">
              {entitlement.tier.toUpperCase()} · {remainingLabel}
            </Badge>
          ) : undefined}
        />

        {loading && !catalog ? (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2" role="status" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 2 }).map((_, i) => (<Skeleton aria-hidden key={i} className="h-24 rounded-2xl" />))}
          </div>
        ) : catalog && catalog.taskTypes.length > 0 ? (
          <div className="grid grid-cols-1 gap-4 md:grid-cols-2">
            {catalog.taskTypes.map((task, i) => {
              const Icon = TASK_ICONS[task.code] ?? MessageSquare;
              const isCreatingThis = creating === task.code;
              const disabled = !!creating || (entitlement ? !entitlement.allowed : false);
              return (
                <MotionItem key={task.code} delayIndex={Math.min(i, 5)}>
                  <button type="button" onClick={() => handleStart(task.code)} disabled={disabled}
                    className={cn(cardClassName({ hoverable: true, interactive: true }), 'group h-full w-full text-start disabled:pointer-events-none disabled:opacity-50')}>
                    <div className="flex items-start gap-3">
                      <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary transition-colors group-hover:bg-primary/15">
                        <Icon className="h-5 w-5" aria-hidden="true" />
                      </div>
                      <div className="min-w-0 flex-1">
                        <h3 className="text-sm font-bold text-navy transition-colors group-hover:text-primary-dark">
                          {task.label}
                        </h3>
                        <p className="mt-1 text-xs text-muted">{task.description}</p>
                        {isCreatingThis && (<p className="mt-2 text-xs text-primary" role="status">Creating session…</p>)}
                      </div>
                      <ChevronRight className="mt-1 h-4 w-4 shrink-0 text-muted transition-colors group-hover:text-primary rtl:rotate-180" aria-hidden="true" />
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

      <section className="space-y-4">
        <LearnerSurfaceSectionHeader
          eyebrow="History"
          title="Recent conversations"
          description="Review and continue your previous practice sessions."
        />

        {loading ? (
          <div className="space-y-3" role="status" aria-busy="true" aria-label="Loading">
            {Array.from({ length: 3 }).map((_, i) => (<Skeleton aria-hidden key={i} className="h-20 rounded-2xl" />))}
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
          <div className="space-y-3">
            {history.map((session, i) => {
              const stateInfo = STATE_LABELS[session.state] ?? STATE_LABELS.preparing;
              const isViewable = session.state === 'evaluated' || session.state === 'completed';
              return (
                <MotionItem key={session.id} delayIndex={Math.min(i, 5)}>
                  <CardLink href={isViewable ? `/conversation/${session.id}/results` : `/conversation/${session.id}`} prefetch={false}>
                    <div className="flex items-center justify-between gap-3">
                      <div className="flex min-w-0 items-center gap-3">
                        <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
                          <MessageSquare className="h-4 w-4" aria-hidden="true" />
                        </div>
                        <div className="min-w-0">
                          <div className="mb-0.5 flex items-center gap-2">
                            <span className="truncate text-sm font-semibold text-navy">
                              {session.taskTypeCode.replace(/-/g, ' ').replace(/\b\w/g, c => c.toUpperCase())}
                            </span>
                            <ExamTypeBadge examType={session.examTypeCode} size="sm" />
                          </div>
                          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs text-muted tabular-nums">
                            <span>{dateFormatter.format(new Date(session.createdAt))}</span>
                            <span className="flex items-center gap-1">
                              <Clock className="h-3 w-3" aria-hidden="true" />{formatDuration(session.durationSeconds)}
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
                      <div className="flex shrink-0 items-center gap-2">
                        <Badge variant={stateInfo.variant}>{stateInfo.label}</Badge>
                        <ChevronRight className="h-4 w-4 text-muted rtl:rotate-180" aria-hidden="true" />
                      </div>
                    </div>
                  </CardLink>
                </MotionItem>
              );
            })}
          </div>
        )}
      </section>
    </>
  );
}
