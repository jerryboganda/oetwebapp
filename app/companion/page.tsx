'use client';

import { useQuery } from '@tanstack/react-query';
import { useTranslations } from 'next-intl';
import Link from 'next/link';
import { AlertCircle, Coins, Lock, Plus, Sparkles } from 'lucide-react';
import { AiAssistantInput, AiAssistantMessages } from '@/components/domain/ai-assistant';
import { CompanionMemoryPanel } from '@/components/domain/companion/CompanionMemoryPanel';
import { LearnerDashboardShell } from '@/components/layout';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { useAiAssistantContext } from '@/contexts/ai-assistant-context';
import { fetchCompanionSession, type CompanionAccessReason } from '@/lib/api/companion';

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
    isConnected,
    connectionState,
    error,
    clearError,
    sendMessage,
    cancelTurn,
    selectThread,
    createNewThread,
    hasAccess,
  } = useAiAssistantContext();

  const statusLabel = isConnected
    ? t('companion.status.connected')
    : connectionState === 'connecting' || connectionState === 'reconnecting'
      ? t('companion.status.connecting')
      : t('companion.status.offline');

  if (session.isLoading) {
    return (
      <LearnerDashboardShell>
        <Skeleton className="h-64 w-full" />
      </LearnerDashboardShell>
    );
  }

  // Fail closed: an unreadable session is treated exactly like a disabled one.
  if (session.isError || !session.data?.enabled) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="info" title={t('companion.disabled.title')}>
          {t('companion.disabled.body')}
        </InlineAlert>
      </LearnerDashboardShell>
    );
  }

  if (!hasAccess) {
    return (
      <LearnerDashboardShell>
        <InlineAlert variant="info" title={t('companion.noAccess.title')}>
          {t('companion.noAccess.body')}
        </InlineAlert>
      </LearnerDashboardShell>
    );
  }

  const data = session.data;
  const canChat = data.access.canChat;

  return (
    <LearnerDashboardShell>
      <div className="flex flex-col gap-4">
        <header className="flex flex-wrap items-start justify-between gap-3">
          <div>
            <p className="flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide text-primary">
              <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
              {t('companion.page.eyebrow')}
            </p>
            <h1 className="mt-1 text-2xl font-bold text-navy">
              {t('companion.page.title', { persona: data.persona })}
            </h1>
            <p className="mt-1 text-sm text-muted">{t('companion.page.subtitle')}</p>
          </div>

          <div className="flex items-center gap-2">
            {/* Credit chip — the single candidate-facing wallet, never a
                companion-specific balance. */}
            <Link
              href={data.topUpUrl ?? '/ai-packages'}
              className="inline-flex items-center gap-1.5 rounded-full border border-border px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-background-light focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
            >
              <Coins className="h-3.5 w-3.5" aria-hidden="true" />
              {t('companion.credits.label')}: {data.aiCreditsRemaining}
            </Link>
            {canChat && (
              <button
                type="button"
                onClick={() => void createNewThread()}
                className="inline-flex items-center gap-1.5 rounded-full border border-border px-3 py-1.5 text-xs font-semibold text-navy transition-colors hover:bg-background-light focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
              >
                <Plus className="h-3.5 w-3.5" aria-hidden="true" />
                {t('companion.thread.new')}
              </button>
            )}
          </div>
        </header>

        <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_280px]">
          {canChat ? (
            <Card padding="none" className="flex h-[min(70vh,640px)] flex-col overflow-hidden">
              <div className="flex items-center gap-2 border-b border-border px-4 py-2">
                <span
                  data-testid="companion-connection-state"
                  data-state={connectionState}
                  className={`h-2 w-2 rounded-full ${isConnected ? 'bg-green-500' : 'bg-amber-500'}`}
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
                  className="flex items-start gap-2 border-b border-border bg-red-50 px-4 py-2 text-xs text-red-800 dark:bg-red-950/40 dark:text-red-200"
                >
                  <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                  <span className="flex-1">{error}</span>
                  <button type="button" onClick={clearError} className="underline">
                    {t('companion.error.dismiss')}
                  </button>
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
                            onClick={() => void sendMessage(t(key))}
                            disabled={!isConnected}
                            className="w-full rounded-lg border border-border px-3 py-2 text-sm text-navy transition-colors hover:bg-background-light disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary"
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
                  />
                )}
              </div>

              <AiAssistantInput
                onSend={(content) => void sendMessage(content)}
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
              <ul className="mt-4 space-y-1.5 text-sm text-navy">
                <li>· {t('companion.paywall.benefit.grounded')}</li>
                <li>· {t('companion.paywall.benefit.plan')}</li>
                <li>· {t('companion.paywall.benefit.actions')}</li>
              </ul>
              {data.access.upgradeUrl && (
                <Link
                  href={data.access.upgradeUrl}
                  className="mt-5 inline-flex items-center gap-1.5 rounded-lg bg-primary px-4 py-2 text-sm font-semibold text-white transition-colors hover:bg-primary/90 focus:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2"
                >
                  {t('companion.paywall.cta')}
                </Link>
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
                          className={`w-full truncate rounded px-2 py-1 text-start text-xs transition-colors hover:bg-background-light ${
                            activeThread?.id === thread.id ? 'bg-background-light font-semibold' : ''
                          }`}
                        >
                          {thread.title ?? t('companion.thread.untitled')}
                        </button>
                      </li>
                    ))}
                  </ul>
                )}
              </Card>
            )}

            <CompanionMemoryPanel />

            <Card padding="md">
              <p className="text-xs leading-relaxed text-muted">{t('companion.grounding.note')}</p>
              {canChat && !isConnected && (
                <p className="mt-2 text-xs text-muted">{t('companion.status.offlineHelp')}</p>
              )}
            </Card>
          </aside>
        </div>
      </div>
    </LearnerDashboardShell>
  );
}
