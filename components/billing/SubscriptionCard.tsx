'use client';

import { useState } from 'react';
import { Calendar, Loader2, Pause, Play, RefreshCw, XCircle } from 'lucide-react';

import type { SubscriptionMe, SubscriptionMeListItem } from '@/lib/api';
import {
  cancelSubscription,
  pauseSubscriptionSelf,
  resumeSubscriptionSelf,
} from '@/lib/api';
import { Button } from '@/components/ui/button';
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
  const isCancelled = subscription.status === 'cancelled' || subscription.status === 'canceled';

  return (
    <article className="rounded-2xl border border-border bg-surface p-5 shadow-sm">
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
        {!isCancelled && !isPaused ? (
          <Button
            variant="outline"
            onClick={() => void run('pause', () => pauseSubscriptionSelf())}
            disabled={busy !== null}
          >
            {busy === 'pause' ? <Loader2 className="mr-1 h-4 w-4 animate-spin" /> : <Pause className="mr-1 h-4 w-4" />}
            Pause
          </Button>
        ) : null}
        {!isCancelled && isPaused ? (
          <Button
            variant="outline"
            onClick={() => void run('resume', () => resumeSubscriptionSelf())}
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
    </article>
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
      : status === 'cancelled' || status === 'canceled' || status === 'expired'
        ? 'bg-danger/10 text-danger'
        : 'bg-background-light text-muted';
  return (
    <span
      className={`mt-1 inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-semibold uppercase tracking-wider ${tone}`}
    >
      {status}
    </span>
  );
}

function formatDate(value: string | null | undefined): string {
  if (!value) return 'Not scheduled';
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? 'Not scheduled' : date.toLocaleDateString();
}
