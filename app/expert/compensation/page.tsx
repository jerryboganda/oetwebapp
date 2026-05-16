'use client';

import { useExpertCompensation } from '@/lib/hooks/use-expert-compensation';
import { ExpertRouteWorkspace, ExpertRouteHero, ExpertRouteSectionHeader, ExpertRouteSummaryCard } from '@/components/domain/expert-route-surface';
import { Skeleton } from '@/components/ui/skeleton';
import { ErrorState } from '@/components/ui/empty-error';

function formatCurrency(minorUnits: number, currency: string): string {
  return new Intl.NumberFormat('en-GB', { style: 'currency', currency }).format(minorUnits / 100);
}

export default function CompensationPage() {
  const { summary, earnings, payouts, loading, error, page, setPage, refresh } = useExpertCompensation();

  if (loading) {
    return (
      <ExpertRouteWorkspace>
        <ExpertRouteHero title="Compensation" description="Track your earnings and payouts." />
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          {[...Array(3)].map((_, i) => <Skeleton key={i} className="h-32 rounded-lg" />)}
        </div>
      </ExpertRouteWorkspace>
    );
  }

  if (error) {
    return (
      <ExpertRouteWorkspace>
        <ExpertRouteHero title="Compensation" description="Track your earnings and payouts." />
        <ErrorState message={error} onRetry={refresh} />
      </ExpertRouteWorkspace>
    );
  }

  return (
    <ExpertRouteWorkspace>
      <ExpertRouteHero title="Compensation" description="Track your earnings and payouts." />

      <section className="rounded-lg border bg-card p-4 text-sm text-muted-foreground">
        <ExpertRouteSectionHeader title="Launch payout model" />
        <p className="mt-2">
          Public launch uses fixed, tiered payouts by review type and promised turnaround SLA.
          Earnings are created only after a completed review is accepted by the platform workflow,
          then batched into payout runs for finance approval.
        </p>
        <ul className="mt-3 list-disc space-y-1 pl-5">
          <li>Writing and Speaking review payouts remain separate from learner wallet credits.</li>
          <li>Urgent-turnaround work is tracked through queue/SLA metadata before payout approval.</li>
          <li>Disputed, rework, or quality-held reviews stay pending until operations resolves them.</li>
        </ul>
      </section>

      {summary && (
        <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
          <ExpertRouteSummaryCard
            label="Pending Earnings"
            value={formatCurrency(summary.pendingEarningsMinorUnits, summary.currency)}
            hint={`${summary.completedReviewsThisMonth} reviews this month`}
          />
          <ExpertRouteSummaryCard
            label="Paid This Month"
            value={formatCurrency(summary.paidThisMonthMinorUnits, summary.currency)}
            hint={`${summary.pendingPayoutCount} pending payouts`}
          />
          <ExpertRouteSummaryCard
            label="Lifetime Earnings"
            value={formatCurrency(summary.lifetimeEarningsMinorUnits, summary.currency)}
            hint="All-time total"
          />
        </div>
      )}

      <ExpertRouteSectionHeader title="Recent Earnings" />
      {earnings.length === 0 ? (
        <p className="text-muted-foreground text-sm">No earnings recorded yet.</p>
      ) : (
        <div className="space-y-2">
          {earnings.map((item) => (
            <div key={item.id} className="flex items-center justify-between rounded-lg border p-3">
              <div>
                <p className="font-medium text-sm">{item.subtestCode} Review</p>
                <p className="text-xs text-muted-foreground">{new Date(item.earnedAt).toLocaleDateString()}</p>
              </div>
              <div className="text-right">
                <p className="font-semibold">{formatCurrency(item.amountMinorUnits, item.currency)}</p>
                <p className={`text-xs ${item.status === 'paid' ? 'text-green-600' : 'text-amber-600'}`}>
                  {item.status}
                </p>
              </div>
            </div>
          ))}
          {earnings.length >= 25 && (
            <button
              onClick={() => setPage(page + 1)}
              className="w-full text-sm text-primary hover:underline py-2"
            >
              Load More
            </button>
          )}
        </div>
      )}

      <ExpertRouteSectionHeader title="Payout History" />
      {payouts.length === 0 ? (
        <p className="text-muted-foreground text-sm">No payouts yet.</p>
      ) : (
        <div className="space-y-2">
          {payouts.map((payout) => (
            <div key={payout.id} className="flex items-center justify-between rounded-lg border p-3">
              <div>
                <p className="font-medium text-sm">Payout</p>
                <p className="text-xs text-muted-foreground">{new Date(payout.createdAt).toLocaleDateString()}</p>
              </div>
              <div className="text-right">
                <p className="font-semibold">{formatCurrency(payout.totalAmountMinorUnits, payout.currency)}</p>
                <p className={`text-xs ${payout.status === 'approved' ? 'text-green-600' : 'text-amber-600'}`}>
                  {payout.status}
                </p>
              </div>
            </div>
          ))}
        </div>
      )}
    </ExpertRouteWorkspace>
  );
}
