'use client';

import { useCallback, useState } from 'react';
import { Calendar, CreditCard, Globe2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { Price } from '@/components/ui/price';
import { openCustomerPortal } from '@/lib/native/billing-bridge';
import { useMobileBillingContext } from './use-mobile-billing-context';

/**
 * Status enum mirrors the backend `CustomerSubscriptionDto.status` field
 * so consumers can hand the raw value straight through.
 */
export type MobileSubscriptionStatus =
  | 'active'
  | 'trialing'
  | 'past_due'
  | 'paused'
  | 'cancelled'
  | 'incomplete'
  | 'incomplete_expired'
  | 'unpaid';

export interface MobileSubscriptionSummary {
  planName: string;
  planDescription?: string | null;
  status: MobileSubscriptionStatus;
  /** Amount in major units (e.g. 9.99). */
  priceAmount: number | null;
  currency: string | null;
  interval: 'month' | 'year' | 'one-time' | null;
  /** ISO 8601 date string for the next renewal, if any. */
  nextRenewal: string | null;
  /** Display-ready credits remaining for the period. */
  creditsRemaining?: number | null;
}

interface SubscriptionManagerProps {
  subscription: MobileSubscriptionSummary | null;
  /** Loading flag from the parent query. */
  loading?: boolean;
  /** Optional last-known billing-event push payload (e.g. payment.failed). */
  recentEvent?: MobileBillingEventBanner | null;
}

export interface MobileBillingEventBanner {
  kind: 'payment.success' | 'payment.failed' | 'subscription.renewing' | 'credits.low';
  message: string;
  /** Auto-dismissable after first paint. */
  dismissable?: boolean;
}

const STATUS_LABELS: Record<MobileSubscriptionStatus, string> = {
  active: 'Active',
  trialing: 'Trial',
  past_due: 'Payment past due',
  paused: 'Paused',
  cancelled: 'Cancelled',
  incomplete: 'Incomplete',
  incomplete_expired: 'Expired',
  unpaid: 'Unpaid',
};

const STATUS_VARIANTS: Record<MobileSubscriptionStatus, 'info' | 'success' | 'warning' | 'error'> = {
  active: 'success',
  trialing: 'info',
  past_due: 'error',
  paused: 'warning',
  cancelled: 'info',
  incomplete: 'warning',
  incomplete_expired: 'error',
  unpaid: 'error',
};

function formatRenewal(iso: string | null): string | null {
  if (!iso) return null;
  try {
    const date = new Date(iso);
    if (Number.isNaN(date.getTime())) return null;
    return date.toLocaleDateString(undefined, {
      year: 'numeric',
      month: 'short',
      day: 'numeric',
    });
  } catch {
    return null;
  }
}

export function SubscriptionManager({ subscription, loading, recentEvent }: SubscriptionManagerProps) {
  const context = useMobileBillingContext();
  const [portalLoading, setPortalLoading] = useState(false);
  const [portalError, setPortalError] = useState<string | null>(null);

  const handleOpenPortal = useCallback(async () => {
    if (portalLoading) return;
    setPortalLoading(true);
    setPortalError(null);
    try {
      await openCustomerPortal();
    } catch (err) {
      setPortalError(
        err instanceof Error
          ? err.message
          : 'We could not open the subscription manager. Please try again from the web.',
      );
    } finally {
      setPortalLoading(false);
    }
  }, [portalLoading]);

  return (
    <div className="flex flex-col gap-4 px-4 pb-24 pt-4" data-testid="mobile-subscription-manager">
      <header>
        <h1 className="text-xl font-bold text-navy">Your subscription</h1>
        <p className="mt-1 text-sm text-muted-foreground">
          Subscription changes happen on our website. Updates take effect across all your devices
          immediately.
        </p>
      </header>

      {recentEvent ? (
        <InlineAlert
          variant={
            recentEvent.kind === 'payment.success'
              ? 'success'
              : recentEvent.kind === 'payment.failed'
                ? 'error'
                : 'info'
          }
          dismissible={recentEvent.dismissable}
        >
          {recentEvent.message}
        </InlineAlert>
      ) : null}

      {loading ? (
        <Card padding="lg" aria-busy="true">
          <div className="h-32 animate-pulse rounded-lg bg-surface" />
        </Card>
      ) : subscription ? (
        <Card padding="lg" data-testid="mobile-subscription-card">
          <div className="flex items-start justify-between gap-3">
            <div className="min-w-0">
              <h2 className="text-base font-semibold text-navy">{subscription.planName}</h2>
              {subscription.planDescription ? (
                <p className="mt-1 text-sm text-muted-foreground">{subscription.planDescription}</p>
              ) : null}
            </div>
            <span
              className={`shrink-0 rounded-full px-2 py-1 text-xs font-medium ${
                STATUS_VARIANTS[subscription.status] === 'success'
                  ? 'bg-emerald-100 text-emerald-700'
                  : STATUS_VARIANTS[subscription.status] === 'error'
                    ? 'bg-red-100 text-red-700'
                    : STATUS_VARIANTS[subscription.status] === 'warning'
                      ? 'bg-amber-100 text-amber-700'
                      : 'bg-slate-100 text-slate-700'
              }`}
            >
              {STATUS_LABELS[subscription.status]}
            </span>
          </div>

          {subscription.priceAmount != null && subscription.currency ? (
            <div className="mt-4 flex items-center gap-2 text-sm text-navy">
              <CreditCard className="h-4 w-4 text-muted-foreground" aria-hidden="true" />
              <Price
                amount={subscription.priceAmount}
                currency={subscription.currency}
                className="font-semibold"
              />
              {subscription.interval && subscription.interval !== 'one-time' ? (
                <span className="text-muted-foreground">/ {subscription.interval}</span>
              ) : null}
            </div>
          ) : null}

          {formatRenewal(subscription.nextRenewal) ? (
            <div className="mt-2 flex items-center gap-2 text-sm text-muted-foreground">
              <Calendar className="h-4 w-4" aria-hidden="true" />
              <span>Renews on {formatRenewal(subscription.nextRenewal)}</span>
            </div>
          ) : null}

          {typeof subscription.creditsRemaining === 'number' ? (
            <div className="mt-2 text-sm text-muted-foreground">
              {subscription.creditsRemaining} grading credits remaining this period
            </div>
          ) : null}
        </Card>
      ) : (
        <Card padding="lg" data-testid="mobile-subscription-empty">
          <p className="text-sm text-muted-foreground">
            You do not have an active subscription. Browse our packages to get started.
          </p>
        </Card>
      )}

      <Card padding="lg">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-primary/10 text-primary">
            <Globe2 className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0">
            <h2 className="text-sm font-semibold text-navy">
              {context?.copy.messageTitle ?? 'Manage on the web'}
            </h2>
            <p className="mt-1 text-sm text-muted-foreground">
              {context?.copy.messageBody ??
                'Update payment methods, change plans, or cancel from our secure portal.'}
            </p>
            <Button
              variant="primary"
              size="md"
              className="mt-3"
              onClick={() => void handleOpenPortal()}
              loading={portalLoading}
            >
              {context?.copy.ctaLabel ?? 'Manage on website'}
            </Button>
            {portalError ? (
              <p role="alert" className="mt-2 text-xs text-red-600">
                {portalError}
              </p>
            ) : null}
          </div>
        </div>
      </Card>
    </div>
  );
}
