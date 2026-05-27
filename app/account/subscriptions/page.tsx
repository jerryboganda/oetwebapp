'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { ArrowLeft, Layers } from 'lucide-react';

import {
  fetchSubscriptionsMe,
  type SubscriptionMe,
  type SubscriptionMeListItem,
} from '@/lib/api';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { SubscriptionCard } from '@/components/billing/SubscriptionCard';

/**
 * List of every subscription the learner owns. Most accounts will have
 * one, but premium / institutional accounts can hold many — render them
 * as a stack of `SubscriptionCard`s.
 */
export default function AccountSubscriptionsPage() {
  const [subscriptions, setSubscriptions] = useState<SubscriptionMeListItem[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      setSubscriptions(await fetchSubscriptionsMe());
    } catch (err) {
      console.error(err);
      setError(err instanceof Error ? err.message : 'Failed to load subscriptions.');
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    void refresh();
  }, [refresh]);

  const handleChanged = useCallback((next: SubscriptionMe) => {
    setSubscriptions((current) =>
      current.map((item) => (item.subscriptionId === next.subscriptionId ? { ...item, ...next } : item)),
    );
  }, []);

  return (
    <div className="mx-auto max-w-5xl space-y-6 px-4 py-10">
      <header className="flex flex-wrap items-end justify-between gap-3">
        <div>
          <Link
            href="/account"
            className="inline-flex items-center gap-1 text-xs text-muted hover:text-navy"
          >
            <ArrowLeft className="h-3.5 w-3.5" /> Account
          </Link>
          <h1 className="mt-2 text-3xl font-bold text-navy">Your subscriptions</h1>
          <p className="mt-1 text-sm text-muted">
            Pause, resume, change, or cancel any active subscription.
          </p>
        </div>
        <Button asChild variant="outline">
          <Link href="/catalog">Browse plans</Link>
        </Button>
      </header>

      {error ? (
        <InlineAlert variant="error" title="Could not load subscriptions">
          {error}
        </InlineAlert>
      ) : null}

      {loading ? (
        <p className="rounded-2xl border border-border bg-surface p-8 text-center text-muted">
          Loading subscriptions...
        </p>
      ) : subscriptions.length === 0 ? (
        <div className="rounded-2xl border border-border bg-surface p-10 text-center">
          <Layers className="mx-auto h-8 w-8 text-muted" aria-hidden="true" />
          <p className="mt-3 text-sm text-muted">
            You do not have any active subscriptions right now.
          </p>
          <Button asChild className="mt-4">
            <Link href="/catalog">Pick a plan</Link>
          </Button>
        </div>
      ) : (
        <div className="space-y-4">
          {subscriptions.map((subscription) => (
            <SubscriptionCard
              key={subscription.subscriptionId}
              subscription={subscription}
              onChanged={handleChanged}
            />
          ))}
        </div>
      )}
    </div>
  );
}
