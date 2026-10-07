'use client';

import { useEffect } from 'react';
import { useQuery } from '@tanstack/react-query';
import { useTranslations } from 'next-intl';
import Link from 'next/link';
import { AlertCircle, Lock, Plus, Sparkles } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { AiAssistantInput, AiAssistantMessages } from '@/components/domain/ai-assistant';
import { CompanionMemoryPanel } from '@/components/domain/companion/CompanionMemoryPanel';
import { CompanionPreferencesPanel } from '@/components/domain/companion/CompanionPreferencesPanel';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { useAiAssistantContext } from '@/contexts/ai-assistant-context';
import { buildSurfaceContext } from '@/lib/ai-assistant/surface-context';
import { fetchCompanionSession, type CompanionAccessReason } from '@/lib/api/companion';

// Static: this page is always the same surface, so there is nothing to derive.
const COMPANION_SURFACE = buildSurfaceContext('/companion');

const SUGGESTION_KEYS = [
  'companion.suggestion.writing',
  'companion.suggestion.plan',
  'companion.suggestion.speaking',
] as const;

/**
 * Reason → message key. Every reason the server can return is listed, so a new
 * server reason shows a generic-but-honest message rather than a blank card.
 */
const REASON_KEYS: Record<CompanionAccessReason, string> = {
  ok: 'companion.paywall.reason.ok',
  companion_disabled: 'companion.paywall.reason.companionDisabled',
  ai_disabled: 'companion.paywall.reason.aiDisabled',
  kill_switch: 'companion.paywall.reason.killSwitch',
  policy_unavailable: 'companion.paywall.reason.policyUnavailable',
  package_required: 'companion.paywall.reason.packageRequired',
  plan_excludes_companion: 'companion.paywall.reason.planExcludes',
  monthly_cap_reached: 'companion.paywall.reason.monthlyCap',
  daily_cap_reached: 'companion.paywall.reason.dailyCap',
};

/**
 * Full-screen AI Learning Companion surface.
 *
 * The floating widget is for quick questions in context; this is the page for a
 * real study conversation. Both consume the same {@link useAiAssistantContext},
 * so a thread started in one continues in the other.
 *
 * <p>Capability comes from `GET /v1/companion/session` — one server call that
 * answers "may this learner chat, and if not why, and where do they go".
 * Deciding that here rather than from the chat stream is what lets the page show
 * an upgrade card instead of an input box that fails on the first message.</p>
 *
 * See docs/ai-learning-companion/.
 */
export default function CompanionPage() {
  const t = useTranslations();

  const session = useQuery({
    queryKey: ['companion', 'session'],
    queryFn: fetchCompanionSession,
    staleTime: 60_000,
  });

  const {
    messages,
    threads,
    activeThread,
    isStreaming,
    streamingContent,
    citations,
    isConnected,
    connectionState,
    error,
    clearError,
    sendMessage,
    cancelTurn,
    selectThread,
    createNewThread,
    hasAccess,
    activate,
  } = useAiAssistantContext();

  // The assistant hub connects lazily: only once this page can actually chat.
  const companionEnabled = session.data?.enabled === true;
  useEffect(() => {
    if (companionEnabled && hasAccess) activate();
  }, [activate, companionEnabled, hasAccess]);

  const statusLabel = isConnected
    ? t('companion.status.connected')
    : connectionState === 'connecting' || connectionState === 'reconnecting'
      ? t('companion.status.connecting')
      : t('companion.status.offline');

  if (session.isLoading) {
    return <Skeleton className="h-64 w-full rounded-2xl" />;
  }

  // Fail closed: an unreadable session is treated exactly like a disabled one.
  // Either way the page keeps one header: the reason is the hero, not a bare alert.
  if (session.isError || !session.data?.enabled) {
    return (
      <LearnerPageHero
        eyebrow={t('companion.page.eyebrow')}
        icon={Sparkles}
        title={t('companion.disabled.title')}
        description={t('companion.disabled.body')}
      />
    );
  }

  if (!hasAccess) {
    return (
      <LearnerPageHero
        eyebrow={t('companion.page.eyebrow')}
        icon={Lock}
        title={t('companion.noAccess.title')}
        description={t('companion.noAccess.body')}
      />
    );
  }

  const data = session.data;
  const canChat = data.access.canChat;

  return (
    <>
      <LearnerPageHero
        eyebrow={t('companion.page.eyebrow')}
        icon={Sparkles}
        title={t('companion.page.title', { persona: data.persona })}
        description={t('companion.page.subtitle')}
        aside={(
          <div className="flex flex-wrap items-center gap-2">
            {canChat && (
              <Button variant="outline" size="sm" onClick={() => void createNewThread()}>
                <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                {t('companion.thread.new')}
              </Button>
            )}
          </div>
        )}
      />

      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,1fr)_280px]">
        {canChat ? (
          <Card padding="none" className="flex h-[min(70vh,640px)] flex-col overflow-hidden">
            <div className="flex items-center gap-2 border-b border-border px-4 py-2">
              <span
                data-testid="companion-connection-state"
                data-state={connectionState}
                className={`h-2 w-2 rounded-full ${isConnected ? 'bg-success' : 'bg-warning'}`}
                aria-hidden="true"
              />
              <span aria-live="polite" className="text-xs text-muted">
                {statusLabel}
              </span>
            </div>

            {error && (
              <div
                role="alert"
                data-testid="companion-error"
                className="flex items-start gap-2 border-b border-border bg-danger/10 px-4 py-2 text-xs text-navy"
              >
                <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0 text-danger-strong" aria-hidden="true" />
                <span className="flex-1">{error}</span>
                <Button type="button" variant="ghost" size="xs" onClick={clearError} className="-my-1.5 underline">
                  {t('companion.error.dismiss')}
                </Button>
              </div>
            )}

            <div className="flex-1 overflow-y-auto p-4">
              {messages.length === 0 && !isStreaming ? (
                <div className="mx-auto max-w-md py-8 text-center">
                  <h2 className="text-base font-semibold text-navy">{t('companion.empty.title')}</h2>
                  <p className="mt-2 text-sm text-muted">{t('companion.empty.body')}</p>
                  <ul className="mt-4 space-y-2 text-start">
                    {SUGGESTION_KEYS.map((key) => (
                      <li key={key}>
                        <button
                          type="button"
                          onClick={() => void sendMessage(t(key), COMPANION_SURFACE)}
                          disabled={!isConnected}
                          className="hover-primary min-h-11 w-full rounded-control border border-border px-3 py-2 text-sm text-navy transition-colors disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
                        >
                          {t(key)}
                        </button>
                      </li>
                    ))}
                  </ul>
                </div>
              ) : (
                <AiAssistantMessages
                  messages={messages}
                  streamingContent={isStreaming ? streamingContent : undefined}
                  streamingCitations={isStreaming ? citations : undefined}
                />
              )}
            </div>

            <AiAssistantInput
              onSend={(content) => void sendMessage(content, COMPANION_SURFACE)}
              onCancel={() => void cancelTurn()}
              isStreaming={isStreaming}
              disabled={!isConnected}
            />
          </Card>
        ) : (
          <Card padding="lg" data-testid="companion-paywall">
            <span className="inline-flex h-10 w-10 items-center justify-center rounded-xl bg-background-light text-primary">
              <Lock className="h-5 w-5" aria-hidden="true" />
            </span>
            <h2 className="mt-3 text-lg font-bold text-navy">{t('companion.paywall.title')}</h2>
            <p className="mt-2 text-sm text-muted">{t(REASON_KEYS[data.access.reason])}</p>
            {data.access.planName && (
              <p className="mt-1 text-xs text-muted">
                {t('companion.paywall.currentPlan', { plan: data.access.planName })}
              </p>
            )}
            <ul className="mt-4 list-disc space-y-1.5 ps-5 text-sm text-navy marker:text-primary">
              <li>{t('companion.paywall.benefit.grounded')}</li>
              <li>{t('companion.paywall.benefit.plan')}</li>
              <li>{t('companion.paywall.benefit.actions')}</li>
            </ul>
            {data.access.upgradeUrl && (
              <Button asChild className="mt-5">
                <Link href={data.access.upgradeUrl}>
                  {t('companion.paywall.cta')}
                </Link>
              </Button>
            )}
          </Card>
        )}

        <aside className="flex flex-col gap-4">
          {canChat && (
            <Card padding="md">
              <h2 className="text-sm font-semibold text-navy">{t('companion.thread.previous')}</h2>
              {threads.length === 0 ? (
                <p className="mt-2 text-xs text-muted">{t('companion.thread.none')}</p>
              ) : (
                <ul className="mt-2 max-h-64 space-y-1 overflow-y-auto">
                  {threads.map((thread) => (
                    <li key={thread.id}>
                      <button
                        type="button"
                        onClick={() => void selectThread(thread.id)}
                        aria-current={activeThread?.id === thread.id}
                        className={cn(
                          'hover-primary min-h-11 w-full truncate rounded-control px-2 py-1 text-start text-xs text-navy transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary lg:min-h-8',
                          activeThread?.id === thread.id && 'bg-primary/10 font-semibold text-primary',
                        )}
                      >
                        {thread.title ?? t('companion.thread.untitled')}
                      </button>
                    </li>
                  ))}
                </ul>
              )}
            </Card>
          )}

          {canChat && <CompanionPreferencesPanel />}

          <CompanionMemoryPanel />

          <Card padding="md">
            <p className="text-xs leading-relaxed text-muted">{t('companion.grounding.note')}</p>
            {canChat && !isConnected && (
              <p className="mt-2 text-xs text-muted">{t('companion.status.offlineHelp')}</p>
            )}
          </Card>
        </aside>
      </div>
    </>
  );
}
