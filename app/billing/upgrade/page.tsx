'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import {
  ArrowUpRight,
  Check,
  CreditCard,
  Layers,
  Sparkles,
  TrendingUp,
  Wallet,
} from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { cn } from '@/lib/utils';
import { analytics } from '@/lib/analytics';
// TODO(billing-impl-c): expose a typed `fetchBillingUpgradePath()` helper in
// lib/api.ts so this page no longer has to call apiClient.request directly.
import { apiClient, fetchFreezeStatus } from '@/lib/api';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';
import {
  BackToBillingLink,
  FREEZE_BLOCKED_MESSAGE,
  FREEZE_UNVERIFIED_MESSAGE,
  isFreezeEffective,
} from '@/components/domain/billing';

interface PlanInfo {
  planId: string;
  planCode: string;
  planName: string;
  description: string;
  price: number;
  currency: string;
  interval: string;
  includedCredits: number;
  trialDays: number;
  isCurrent: boolean;
  isUpgrade: boolean;
  isDowngrade: boolean;
  entitlements: Record<string, unknown>;
}

interface UpgradeData {
  currentPlan: { planId: string; planName: string; price: number; includedCredits: number } | null;
  usage: {
    reviewsUsedThisMonth: number;
    creditsRemaining: number;
    subscriptionStarted: string | null;
    subscriptionEnds: string | null;
  };
  plans: PlanInfo[];
  recommendation: string;
}

function formatPrice(amount: number, currency: string) {
  try {
    return new Intl.NumberFormat('en-AU', {
      style: 'currency',
      currency: currency || 'AUD',
      minimumFractionDigits: 0,
    }).format(amount);
  } catch {
    return `${currency || 'AUD'} ${amount.toFixed(0)}`;
  }
}

const linkButtonBase =
  'inline-flex items-center justify-center gap-2 rounded-lg px-4 py-2 text-sm font-medium transition-colors duration-200 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2';
const linkButtonStyles = {
  primary: 'bg-primary text-white hover:bg-primary/90 shadow-sm',
  outline: 'border border-border text-navy hover:bg-surface hover:border-border-hover',
  disabled: 'cursor-not-allowed border border-border bg-background-light text-muted',
} as const;

export default function BillingUpgradePage() {
  const [data, setData] = useState<UpgradeData | null>(null);
  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [freezeLoadFailed, setFreezeLoadFailed] = useState(false);
  const [loading, setLoading] = useState(true);
  const [loadError, setLoadError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('billing_upgrade_path_viewed');
    Promise.allSettled([
      apiClient.request<UpgradeData>('/v1/learner/billing/upgrade-path'),
      fetchFreezeStatus(),
    ])
      .then(([upgradeResult, freezeResult]) => {
        if (upgradeResult.status === 'fulfilled') {
          setData(upgradeResult.value);
        } else {
          setLoadError(
            upgradeResult.reason instanceof Error
              ? upgradeResult.reason.message
              : 'Unable to load plan information.',
          );
        }
        if (freezeResult.status === 'fulfilled') {
          setFreezeState(freezeResult.value as LearnerFreezeStatus);
          setFreezeLoadFailed(false);
        } else {
          setFreezeState(null);
          setFreezeLoadFailed(true);
        }
      })
      .finally(() => setLoading(false));
  }, []);

  const isFrozen = isFreezeEffective(freezeState);
  const mutationsBlocked = freezeLoadFailed || isFrozen;
  const blockedMessage = freezeLoadFailed ? FREEZE_UNVERIFIED_MESSAGE : FREEZE_BLOCKED_MESSAGE;

  const heroHighlights = useMemo(() => {
    if (!data) return [];
    const planLabel = data.currentPlan?.planName ?? 'No active plan';
    const monthlyCost = data.currentPlan
      ? formatPrice(data.currentPlan.price, data.plans[0]?.currency ?? 'AUD')
      : '—';
    return [
      { icon: Layers, label: 'Current plan', value: planLabel },
      { icon: CreditCard, label: 'Monthly cost', value: monthlyCost },
      { icon: Wallet, label: 'Credits remaining', value: `${data.usage.creditsRemaining}` },
      { icon: TrendingUp, label: 'Reviews this month', value: `${data.usage.reviewsUsedThisMonth}` },
    ];
  }, [data]);

  return (
    <LearnerDashboardShell pageTitle="Compare plans" backHref="/billing">
      <div className="space-y-6">
        <BackToBillingLink />

        <LearnerPageHero
          eyebrow="Billing"
          icon={Layers}
          accent="navy"
          title="Compare plans"
          description="Find the plan that matches the way you study. Current usage and recommendations are pulled live from your account so the comparison is always up to date."
          highlights={heroHighlights}
        />

        {isFrozen ? (
          <InlineAlert variant="warning">
            Your account is frozen, so plan changes are paused. Billing history and the comparison stay visible.
          </InlineAlert>
        ) : null}
        {freezeLoadFailed ? (
          <InlineAlert variant="error">{FREEZE_UNVERIFIED_MESSAGE}</InlineAlert>
        ) : null}
        {loadError ? <InlineAlert variant="error">{loadError}</InlineAlert> : null}

        {loading ? (
          <div className="grid gap-4 md:grid-cols-3">
            {[0, 1, 2].map((i) => (
              <Skeleton key={i} className="h-64 rounded-2xl" />
            ))}
          </div>
        ) : data ? (
          <>
            <section
              aria-labelledby="upgrade-usage-heading"
              className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
            >
              <LearnerSurfaceSectionHeader
                eyebrow="Snapshot"
                icon={Sparkles}
                title="Your usage"
                description="What this month looks like compared to what each plan includes."
              />
              <h2 id="upgrade-usage-heading" className="sr-only">
                Your usage snapshot
              </h2>
              <dl className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Reviews this month
                  </dt>
                  <dd className="mt-1 text-2xl font-black text-navy">
                    {data.usage.reviewsUsedThisMonth}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Credits remaining
                  </dt>
                  <dd className="mt-1 text-2xl font-black text-navy">
                    {data.usage.creditsRemaining}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Current plan
                  </dt>
                  <dd className="mt-1 text-sm font-bold text-navy">
                    {data.currentPlan?.planName ?? 'No active plan'}
                  </dd>
                </div>
                <div className="rounded-2xl border border-border bg-background-light p-4">
                  <dt className="text-[11px] font-black uppercase tracking-widest text-muted">
                    Monthly cost
                  </dt>
                  <dd className="mt-1 text-sm font-bold text-navy">
                    {data.currentPlan
                      ? formatPrice(
                          data.currentPlan.price,
                          data.plans[0]?.currency ?? 'AUD',
                        )
                      : '—'}
                  </dd>
                </div>
              </dl>
              {data.recommendation ? (
                <p className="mt-5 inline-flex items-start gap-2 rounded-xl bg-primary/5 px-3 py-2 text-sm text-navy">
                  <Sparkles className="mt-0.5 h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                  <span>{data.recommendation}</span>
                </p>
              ) : null}
            </section>

            <section
              aria-labelledby="upgrade-plans-heading"
              className="rounded-2xl border border-border bg-surface p-6 shadow-sm"
            >
              <LearnerSurfaceSectionHeader
                eyebrow="Plans"
                icon={Layers}
                title="Available plans"
                description="Switch any time. Plan-change previews and validated quotes are handled in the billing center."
                action={
                  <Link
                    href="/billing?tab=plans"
                    className={cn(linkButtonBase, linkButtonStyles.outline)}
                    aria-label="Open the plans tab in the billing center"
                  >
                    Open plans tab
                    <ArrowUpRight className="h-3.5 w-3.5" aria-hidden="true" />
                  </Link>
                }
              />
              <h2 id="upgrade-plans-heading" className="sr-only">
                Available plans
              </h2>
              {data.plans.length === 0 ? (
                <p className="mt-4 text-sm text-muted">No plans are available right now.</p>
              ) : (
                <ul className="mt-5 grid gap-4 md:grid-cols-3" role="list">
                  {data.plans.map((plan) => {
                    const ctaLabel = plan.isUpgrade ? 'Upgrade' : 'Switch plan';
                    const ctaAriaLabel = mutationsBlocked
                      ? `${ctaLabel} to ${plan.planName} (unavailable: ${blockedMessage})`
                      : `${ctaLabel} to ${plan.planName} via the billing plans tab`;
                    return (
                      <li
                        key={plan.planId}
                        className={cn(
                          'relative rounded-2xl border bg-background-light p-5 transition-shadow hover:shadow-sm',
                          plan.isCurrent ? 'border-primary ring-1 ring-primary/40' : 'border-border',
                        )}
                      >
                        {plan.isCurrent ? (
                          <Badge variant="default" className="absolute -top-2 left-4">
                            Current
                          </Badge>
                        ) : null}
                        <h3 className="mt-1 text-lg font-bold text-navy">{plan.planName}</h3>
                        <p className="mt-2 text-3xl font-black text-navy">
                          {formatPrice(plan.price, plan.currency)}
                          <span className="ml-1 text-sm font-normal text-muted">
                            /{plan.interval}
                          </span>
                        </p>
                        {plan.description ? (
                          <p className="mt-2 text-sm text-muted">{plan.description}</p>
                        ) : null}
                        <ul className="mt-4 space-y-2 text-sm text-navy" role="list">
                          <li className="flex items-center gap-2">
                            <Check className="h-4 w-4 text-success" aria-hidden="true" />
                            {plan.includedCredits} review credits
                          </li>
                          {plan.trialDays > 0 ? (
                            <li className="flex items-center gap-2">
                              <Check className="h-4 w-4 text-success" aria-hidden="true" />
                              {plan.trialDays}-day free trial
                            </li>
                          ) : null}
                        </ul>
                        {plan.isCurrent ? null : mutationsBlocked ? (
                          <button
                            type="button"
                            disabled
                            aria-label={ctaAriaLabel}
                            className={cn(linkButtonBase, linkButtonStyles.disabled, 'mt-5 w-full')}
                          >
                            {ctaLabel}
                          </button>
                        ) : (
                          <Link
                            href={`/billing?tab=plans&planId=${encodeURIComponent(plan.planCode || plan.planId)}`}
                            aria-label={ctaAriaLabel}
                            className={cn(
                              linkButtonBase,
                              plan.isUpgrade ? linkButtonStyles.primary : linkButtonStyles.outline,
                              'mt-5 w-full',
                            )}
                          >
                            {plan.isUpgrade ? (
                              <>
                                <ArrowUpRight className="h-4 w-4" aria-hidden="true" />
                                Upgrade
                              </>
                            ) : (
                              ctaLabel
                            )}
                          </Link>
                        )}
                      </li>
                    );
                  })}
                </ul>
              )}
            </section>
          </>
        ) : (
          <section className="rounded-2xl border border-border bg-surface p-8 text-center">
            <p className="text-sm text-muted">Unable to load plan information.</p>
            <Link
              href="/billing"
              className={cn(linkButtonBase, linkButtonStyles.outline, 'mt-4')}
            >
              Return to billing center
            </Link>
          </section>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
