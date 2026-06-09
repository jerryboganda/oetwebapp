'use client';

import { useState } from 'react';
import { Calendar, Loader2, Pause, Play, RefreshCw, Snowflake, Timer, XCircle } from 'lucide-react';

import type { SubscriptionMe, SubscriptionMeListItem } from '@/lib/api';
import {
  cancelSubscription,
  requestSubscriptionFreeze,
  resumeSubscriptionById,
} from '@/lib/api';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { formatMoney } from '@/lib/money';

import { BillingPortalLauncher } from './BillingPortalLauncher';

/**
 * Single-subscription summary card with pause / resume / cancel /
 * change-plan controls. Used on `/account/subscriptions`. Plan changes
 * are deferred to Stripe Customer Portal via `BillingPortalLauncher` so
 * we never run a parallel UI for plan switching.
 */

export interface SubscriptionCardProps {
  subscription: SubscriptionMe | SubscriptionMeListItem;
  onChanged?: (next: SubscriptionMe) => void;
}

export function SubscriptionCard({ subscription, onChanged }: SubscriptionCardProps) {
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function run(label: string, fn: () => Promise<SubscriptionMe>) {
    setError(null);
    setBusy(label);
    try {
      const next = await fn();
      onChanged?.(next);
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : `Failed to ${label}.`);
    } finally {
      setBusy(null);
    }
  }

  const isPaused = Boolean(subscription.pausedUntil);
  const isFreezeRequested = subscription.status === 'freeze_requested';
  const isFrozen = subscription.status === 'frozen';
  const isCancelled = subscription.status === 'cancelled' || subscription.status === 'canceled';
  const remainingDays = subscription.remainingDays ?? null;
  const durationDays = subscription.durationDays ?? null;
  const progress = remainingDays !== null && durationDays
    ? Math.max(0, Math.min(100, (remainingDays / durationDays) * 100))
    : null;

  return (
    <Card padding="none" className="p-5">
      <div className="flex items-start justify-between gap-3">
        <div>
          <h3 className="text-lg font-bold text-navy">{subscription.planName}</h3>
          <p className="text-xs uppercase tracking-wider text-muted">{subscription.planCode}</p>
        </div>
        <div className="text-right">
          <p className="text-lg font-semibold text-navy">
            {formatMoney(subscription.price, { currency: subscription.currency })}
            <span className="ml-1 text-xs font-normal text-muted">/ {subscription.interval}</span>
          </p>
          <StatusBadge status={subscription.status} paused={isPaused} />
        </div>
      </div>

      {remainingDays !== null ? (
        <div className="mt-4 rounded-lg bg-background-light px-3 py-3">
          <div className="flex items-center justify-between gap-3 text-sm">
            <span className="inline-flex items-center gap-1 font-medium text-navy">
              <Timer className="h-4 w-4" /> Access timer
            </span>
            <span className={subscription.expiringSoon ? 'font-semibold text-danger' : 'font-semibold text-navy'}>
              {remainingDays} day{remainingDays === 1 ? '' : 's'} remaining
            </span>
          </div>
          {progress !== null ? (
            <div className="mt-2 h-2 overflow-hidden rounded-full bg-border">
              <div className="h-full rounded-full bg-primary" style={{ width: `${progress}%` }} />
            </div>
          ) : null}
          <p className="mt-2 text-xs text-muted">
            {formatDate(subscription.startDate ?? subscription.startedAt)} to {formatDate(subscription.endDate)}
          </p>
        </div>
      ) : null}

      <dl className="mt-4 grid gap-3 text-sm sm:grid-cols-2">
        <Detail icon={<Calendar className="h-4 w-4" />} label="Next renewal">
          {formatDate(subscription.nextRenewalAt)}
        </Detail>
        {subscription.trialEndsAt ? (
          <Detail icon={<Calendar className="h-4 w-4" />} label="Trial ends">
            {formatDate(subscription.trialEndsAt)}
          </Detail>
        ) : null}
        {isPaused ? (
          <Detail icon={<Pause className="h-4 w-4" />} label="Paused until">
            {formatDate(subscription.pausedUntil)}
          </Detail>
        ) : null}
        {isFreezeRequested ? (
          <Detail icon={<Snowflake className="h-4 w-4" />} label="Freeze requested">
            {formatDate(subscription.pendingFreezeRequestDate)}
          </Detail>
        ) : null}
        {isFrozen ? (
          <Detail icon={<Snowflake className="h-4 w-4" />} label="Frozen since">
            {formatDate(subscription.frozenSince)}
          </Detail>
        ) : null}
        {subscription.maxFreezeDays ? (
          <Detail icon={<Snowflake className="h-4 w-4" />} label="Freeze allowance">
            {(subscription.freezeAllowanceRemaining ?? Math.max(0, subscription.maxFreezeDays - (subscription.totalFreezeDaysUsed ?? 0)))} days left
          </Detail>
        ) : null}
        {subscription.cancelledAt ? (
          <Detail icon={<XCircle className="h-4 w-4" />} label="Cancelled">
            {formatDate(subscription.cancelledAt)}
          </Detail>
        ) : null}
      </dl>

      {error ? (
        <InlineAlert variant="error" className="mt-4">
          {error}
        </InlineAlert>
      ) : null}

      <div className="mt-5 flex flex-wrap gap-2">
        <BillingPortalLauncher variant="primary">Manage subscription</BillingPortalLauncher>
        {!isCancelled && !isPaused && !isFreezeRequested && !isFrozen ? (
          <Button
            variant="outline"
            onClick={() => void run('freeze', () => requestSubscriptionFreeze(subscription.subscriptionId))}
            disabled={busy !== null}
          >
            {busy === 'freeze' ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <Pause className="mr-1 h-4 w-4" />}
            Request freeze
          </Button>
        ) : null}
        {!isCancelled && (isPaused || isFrozen) ? (
          <Button
            variant="outline"
            onClick={() => void run('resume', () => resumeSubscriptionById(subscription.subscriptionId))}
            disabled={busy !== null}
          >
            {busy === 'resume' ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <Play className="mr-1 h-4 w-4" />}
            Resume
          </Button>
        ) : null}
        <BillingPortalLauncher variant="outline">
          <RefreshCw className="mr-1 h-4 w-4" /> Change plan
        </BillingPortalLauncher>
        {!isCancelled ? (
          <Button
            variant="destructive"
            onClick={() => {
              if (
                typeof window !== 'undefined' &&
                window.confirm('Cancel this subscription? You will keep access until the renewal date.')
              ) {
                void run('cancel', () => cancelSubscription());
              }
            }}
            disabled={busy !== null}
          >
            {busy === 'cancel' ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <XCircle className="mr-1 h-4 w-4" />}
            Cancel
          </Button>
        ) : null}
      </div>
    </Card>
  );
}

function Detail({
  icon,
  label,
  children,
}: {
  icon: React.ReactNode;
  label: string;
  children: React.ReactNode;
}) {
  return (
    <div className="rounded-lg bg-background-light px-3 py-2">
      <dt className="flex items-center gap-1 text-[11px] uppercase tracking-wider text-muted">
        <span aria-hidden="true">{icon}</span> {label}
      </dt>
      <dd className="mt-0.5 text-sm font-medium text-navy">{children}</dd>
    </div>
  );
}

function StatusBadge({ status, paused }: { status: string; paused: boolean }) {
  if (paused) {
    return (
      <span className="mt-1 inline-flex items-center rounded-full bg-warning/10 px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wider text-warning">
        Paused
      </span>
    );
  }
  const tone =
    status === 'active' || status === 'trial'
      ? 'bg-success/10 text-success'
      : status === 'freeze_requested'
        ? 'bg-warning/10 text-warning'
        : status === 'frozen'
          ? 'bg-info/10 text-info'
      : status === 'cancelled' || status === 'canceled' || status === 'expired'
        ? 'bg-danger/10 text-danger'
        : 'bg-background-light text-muted';
  return (
    <span
      className={`mt-1 inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wider ${tone}`}
    >
      {status.replaceAll('_', ' ')}
    </span>
  );
}

function formatDate(value: string | null | undefined): string {
  if (!value) return 'Not scheduled';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not scheduled' : date.toLocaleDateString();
}
