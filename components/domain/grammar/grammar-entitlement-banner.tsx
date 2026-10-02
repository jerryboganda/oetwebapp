'use client';

import { useEffect } from 'react';
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
  const isBlocked = entitlement.tier === 'free' && !entitlement.allowed;
  // One paywall impression per blocked view; tracking during render fired it on every re-render.
  useEffect(() => {
    if (isBlocked) analytics.track('grammar_paywall_shown', { lessonId: lessonId ?? null, resetAt: entitlement.resetAt });
  }, [isBlocked, lessonId, entitlement.resetAt]);

  if (entitlement.tier !== 'free') return null;
  const resetLabel = entitlement.resetAt
    ? new Date(entitlement.resetAt).toLocaleDateString(undefined, { day: 'numeric', month: 'short' })
    : null;

  if (isBlocked) {
    return (
      <Card className="border-warning/30 bg-warning/5">
        <div className="flex items-start gap-4">
          <div className="flex h-11 w-11 shrink-0 items-center justify-center rounded-lg bg-warning/10 text-warning-strong">
            <Lock className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="min-w-0 flex-1">
            <p className="eyebrow text-warning-strong">Free tier limit reached</p>
            <h2 className="mt-0.5 text-base font-bold text-navy">You&apos;ve reached your free grammar lessons for this week.</h2>
            <p className="mt-1 text-sm leading-6 text-muted">
              {entitlement.reason}
              {resetLabel ? <> Your allowance resets on <strong className="text-navy">{resetLabel}</strong>.</> : null}
            </p>
            <div className="mt-4 flex flex-wrap gap-2">
              <Button variant="primary" size="sm" asChild>
                <Link href="/billing" onClick={() => analytics.track('grammar_paywall_upgrade_clicked', { lessonId: lessonId ?? null })}>
                  <Sparkles className="h-3.5 w-3.5" aria-hidden="true" /> Upgrade for unlimited
                </Link>
              </Button>
              <Button variant="outline" size="sm" asChild>
                <Link href="/grammar">Back to grammar home</Link>
              </Button>
            </div>
          </div>
        </div>
      </Card>
    );
  }

  if (typeof entitlement.remaining === 'number' && entitlement.remaining <= 1) {
    return (
      <Card className="border-primary/20 bg-primary/5 p-3 text-xs text-navy">
        <p>
          <strong>{entitlement.remaining}</strong> of {entitlement.limitPerWindow} free grammar lessons left this week.
          {' '}
          <Link href="/billing" className="font-semibold text-primary underline-offset-2 hover:underline">Upgrade</Link> for unlimited practice.
        </p>
      </Card>
    );
  }

  return null;
}
