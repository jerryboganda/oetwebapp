'use client';

import { SearchX } from 'lucide-react';

import { EmptyState } from '@/components/admin/ui/empty-state';

/**
 * The "nothing measured in this window" state, shared by the three companion
 * operator dashboards (SAMI §13.2 / F-127, F-128, F-129).
 *
 * It is deliberately distinct from the "not instrumented" panel: this state
 * means the tables exist and were queried, and the window simply contains no
 * rows. It says so, and it names the one thing an operator can change (the
 * window) rather than implying the platform has no data at all.
 */
export function CompanionReportEmptyState({
  subject,
  windowDays,
}: {
  /** What the window contains none of, e.g. "companion AI calls". */
  subject: string;
  windowDays: number;
}) {
  return (
    <EmptyState
      illustration={<SearchX className="h-8 w-8" />}
      title={`No ${subject} in the last ${windowDays} days`}
      description={`Every number on this page is counted from stored rows, and the last ${windowDays} days contain none. Widen the window to look further back. Nothing is shown as zero because a zero here would read as a measurement.`}
    />
  );
}
