'use client';

import { Sparkles } from 'lucide-react';
import { Card } from '@/components/ui/card';

/**
 * Non-interactive first-paint surface shared by the auth gate and dashboard.
 * Keeping the same copy and footprint across the session-hydration boundary
 * prevents the centered auth checker from becoming a large mobile CLS event.
 */
export function LearnerDashboardLoadingCard() {
  return (
    <Card
      className="min-h-[260px]"
      role="status"
      aria-busy="true"
      aria-label="Preparing your next study step"
    >
      <div className="flex h-full flex-col justify-between gap-4 sm:gap-6">
        <div>
          <div className="inline-flex items-center gap-1.5 rounded-full border border-primary/20 bg-primary/10 px-2.5 py-1 text-[11px] font-bold uppercase tracking-wider text-primary sm:text-xs">
            <Sparkles className="h-3.5 w-3.5" aria-hidden="true" />
            Next action
          </div>
          <h3 className="mt-2.5 text-base font-bold text-navy sm:mt-4 sm:text-xl">
            Preparing your next study step
          </h3>
          <p className="mt-1.5 max-w-2xl text-[13px] leading-relaxed text-muted sm:mt-2 sm:text-sm">
            We&apos;re loading your live study plan so you can start the right practice without losing your place.
          </p>
        </div>
        <div aria-hidden="true" className="space-y-2">
          <div className="h-10 w-full rounded-lg bg-border/60 motion-safe:animate-pulse" />
          <div className="h-10 w-full rounded-lg bg-border/60 motion-safe:animate-pulse" />
        </div>
      </div>
    </Card>
  );
}
