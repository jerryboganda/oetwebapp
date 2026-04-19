'use client';

import Link from 'next/link';
import { Lock, Sparkles } from 'lucide-react';
import { Card } from '@/components/ui/card';
import { Button } from '@/components/ui/button';
import { analytics } from '@/lib/analytics';
import type { GrammarEntitlement } from '@/lib/api';

/**
 * Learner-facing entitlement banner for grammar lessons.
 * Surfaces the free-tier weekly cap with an upgrade CTA when the learner
 * has exhausted their allowance. Paid/trial learners never see this.
 */
export function GrammarEntitlementBanner({ entitlement, lessonId }: { entitlement: GrammarEntitlement; lessonId?: string }) {
  if (entitlement.tier !== 'free') return null;

  const isBlocked = !entitlement.allowed;
  const resetLabel = entitlement.resetAt
    ? new Date(entitlement.resetAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
    : null;

  if (isBlocked) {
    // Fire a single analytics event on render so we can measure paywall impressions.
    if (typeof window !== 'undefined') {
      // Debounce: only once per mount
      queueMicrotask(() => analytics.track('grammar_paywall_shown', { lessonId: lessonId ?? null, resetAt: entitlement.resetAt }));
    }
    return (
      <Card className="border-amber-200 bg-amber-50 p-4 text-amber-900">
        <div className="flex items-start gap-3">
          <Lock className="mt-0.5 h-5 w-5 flex-none" />
          <div className="min-w-0 flex-1">
            <h2 className="text-sm font-semibold">You&apos;ve reached your free grammar lessons for this week.</h2>
            <p className="mt-1 text-sm">
              {entitlement.reason}
              {resetLabel ? <> Your allowance resets on <strong>{resetLabel}</strong>.</> : null}
            </p>
            <div className="mt-3 flex flex-wrap gap-2">
              <Link href="/billing" onClick={() => analytics.track('grammar_paywall_upgrade_clicked', { lessonId: lessonId ?? null })}>
                <Button size="sm" className="inline-flex items-center gap-1">
                  <Sparkles className="h-3.5 w-3.5" /> Upgrade for unlimited
                </Button>
              </Link>
              <Link href="/grammar">
                <Button size="sm" variant="outline">Back to grammar home</Button>
              </Link>
            </div>
          </div>
        </div>
      </Card>
    );
  }

  if (typeof entitlement.remaining === 'number' && entitlement.remaining <= 1) {
    return (
      <Card className="border-primary/20 bg-primary/5 p-3 text-xs text-navy dark:text-white">
        <p>
          <strong>{entitlement.remaining}</strong> of {entitlement.limitPerWindow} free grammar lessons left this week.
          {' '}
          <Link href="/billing" className="underline">Upgrade</Link> for unlimited practice.
        </p>
      </Card>
    );
  }

  return null;
}
