'use client';

import { useCallback, useEffect, useState } from 'react';
import { TrendingUp, Users, DollarSign, Link2 } from 'lucide-react';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';
import { CountUp } from '@/components/ui/count-up';
import { EmptyState } from '@/components/ui/empty-error';
import { MotionItem, MotionSection } from '@/components/ui/motion-primitives';
import { apiClient, isApiError } from '@/lib/api';

interface AffiliateStats {
  affiliateCode: string;
  ownerName: string;
  totalClicks: number;
  totalSignups: number;
  totalConversions: number;
  totalEarningsAmount: number;
  payoutCurrency: string;
  pendingPayoutAmount: number;
  paidPayoutAmount: number;
  commissions: Array<{
    id: string;
    paymentTransactionId: string;
    amountAmount: number;
    currency: string;
    status: string;
    accruedAt: string;
    paidAt: string | null;
  }>;
}

/**
 * Phase 8 — affiliate self-serve portal. Reads /v1/affiliates/me which
 * the BillingExpansion endpoint surface exposes for the authenticated
 * affiliate (or admin viewing on their behalf via query param).
 *
 * If no affiliate record exists for the authenticated user, the page shows
 * a sign-up CTA. Production wires this to the affiliate-onboarding form.
 */
export default function AffiliatePortalPage() {
  const [stats, setStats] = useState<AffiliateStats | null>(null);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    try {
      setStats(await apiClient.get<AffiliateStats>('/v1/affiliates/me'));
    } catch (err: any) {
      if (isApiError(err) && err.status === 404) {
        setError('No affiliate record found. Contact partnerships@oet to apply.');
        return;
      }
      setError(err?.message ?? 'Failed to load affiliate stats.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  return (
    <>
      <LearnerPageHero
        icon={TrendingUp}
        eyebrow="Partner"
        title="Affiliate dashboard"
        description="Track clicks, conversions, and commission for your referral code."
        highlights={stats ? [{ icon: Link2, label: 'Referral code', value: stats.affiliateCode }] : undefined}
      />

      {error && <InlineAlert variant="info">{error}</InlineAlert>}

      {stats === null && !error ? (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            {[0, 1, 2].map((i) => <Skeleton key={i} className="h-28 rounded-2xl" />)}
          </div>
          <Skeleton className="h-48 w-full rounded-2xl" />
        </>
      ) : stats ? (
        <>
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <MotionItem delayIndex={0}>
              <AffiliateStat icon={<Users className="h-5 w-5" aria-hidden="true" />} label="Clicks" value={<CountUp value={stats.totalClicks} />} />
            </MotionItem>
            <MotionItem delayIndex={1}>
              <AffiliateStat icon={<Users className="h-5 w-5" aria-hidden="true" />} label="Signups" value={<CountUp value={stats.totalSignups} />} />
            </MotionItem>
            <MotionItem delayIndex={2}>
              <AffiliateStat icon={<DollarSign className="h-5 w-5" aria-hidden="true" />} label="Paid earnings" value={`${stats.paidPayoutAmount.toFixed(2)} ${stats.payoutCurrency}`} />
            </MotionItem>
          </div>

          <MotionSection delayIndex={1} className="space-y-4">
            <LearnerSurfaceSectionHeader title="Recent commissions" />
            {stats.commissions.length === 0 ? (
              <EmptyState icon={<DollarSign className="h-8 w-8" />} title="No commissions yet." />
            ) : (
              <Card padding="none" className="divide-y divide-border overflow-hidden">
                {stats.commissions.map((c) => (
                  <div key={c.id} className="flex items-center justify-between gap-3 p-4 text-sm sm:px-5">
                    <div className="min-w-0">
                      <p className="font-semibold tabular-nums text-navy">{c.amountAmount.toFixed(2)} {c.currency}</p>
                      <p className="text-xs tabular-nums text-muted">{new Date(c.accruedAt).toLocaleDateString()} · payment {c.paymentTransactionId.slice(0, 12)}…</p>
                    </div>
                    <Badge variant={c.status === 'paid' ? 'success' : c.status === 'reversed' ? 'danger' : 'default'} className="shrink-0">{c.status}</Badge>
                  </div>
                ))}
              </Card>
            )}
          </MotionSection>
        </>
      ) : null}
    </>
  );
}

function AffiliateStat({ icon, label, value }: { icon: React.ReactNode; label: string; value: React.ReactNode }) {
  return (
    <Card padding="lg" className="h-full">
      <div className="flex items-center gap-2 text-muted">
        {icon}
        <span className="tile-label">{label}</span>
      </div>
      <p className="mt-1 text-2xl font-semibold tabular-nums text-navy">{value}</p>
    </Card>
  );
}
