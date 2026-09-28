'use client';

import { useCallback, useEffect, useState } from 'react';
import { TrendingUp, Users, DollarSign } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { LearnerPageHero } from '@/components/domain';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { Badge } from '@/components/ui/badge';
import { Card } from '@/components/ui/card';

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
      const res = await fetch('/api/backend/v1/affiliates/me');
      if (res.status === 404) {
        setError('No affiliate record found. Contact partnerships@oet to apply.');
        return;
      }
      if (!res.ok) {
        throw new Error(`Failed to load (${res.status})`);
      }
      setStats(await res.json());
    } catch (err: any) {
      setError(err?.message ?? 'Failed to load affiliate stats.');
    }
  }, []);

  useEffect(() => { void load(); }, [load]);

  return (
    <LearnerDashboardShell>
      <LearnerPageHero
        icon={TrendingUp}
        eyebrow="Partner"
        title="Affiliate dashboard"
        description="Track clicks, conversions, and commission for your referral code."
      />

      {error && <InlineAlert variant="info">{error}</InlineAlert>}

      {stats === null && !error ? (
        <Skeleton className="h-48 w-full" />
      ) : stats ? (
        <div className="space-y-6">
          <div className="grid grid-cols-1 gap-4 sm:grid-cols-3">
            <AffiliateStat icon={<Users className="h-5 w-5" aria-hidden="true" />} label="Clicks" value={stats.totalClicks.toString()} />
            <AffiliateStat icon={<Users className="h-5 w-5" aria-hidden="true" />} label="Signups" value={stats.totalSignups.toString()} />
            <AffiliateStat icon={<DollarSign className="h-5 w-5" aria-hidden="true" />} label="Paid earnings" value={`${stats.paidPayoutAmount.toFixed(2)} ${stats.payoutCurrency}`} />
          </div>

          <Card padding="lg">
            <h2 className="mb-3 text-lg font-semibold text-navy">Your referral link</h2>
            <code className="block break-all rounded-lg border border-border bg-background-light px-3 py-2 text-sm text-navy">
              https://oet.example.com/?ref={stats.affiliateCode}
            </code>
          </Card>

          <section className="space-y-3">
            <h2 className="text-lg font-semibold text-navy">Recent commissions</h2>
            {stats.commissions.length === 0 ? (
              <p className="rounded-lg border border-dashed border-border bg-background-light px-4 py-6 text-center text-sm text-muted">No commissions yet.</p>
            ) : (
              <div className="space-y-2">
                {stats.commissions.map((c) => (
                  <div key={c.id} className="flex items-center justify-between gap-3 rounded-lg border border-border bg-surface p-3 text-sm">
                    <div className="min-w-0">
                      <p className="font-medium text-navy">{c.amountAmount.toFixed(2)} {c.currency}</p>
                      <p className="text-xs text-muted">{new Date(c.accruedAt).toLocaleDateString()} · payment {c.paymentTransactionId.slice(0, 12)}…</p>
                    </div>
                    <Badge variant={c.status === 'paid' ? 'success' : c.status === 'reversed' ? 'danger' : 'default'}>{c.status}</Badge>
                  </div>
                ))}
              </div>
            )}
          </section>
        </div>
      ) : null}
    </LearnerDashboardShell>
  );
}

function AffiliateStat({ icon, label, value }: { icon: React.ReactNode; label: string; value: string }) {
  return (
    <Card padding="lg">
      <div className="flex items-center gap-2 text-muted">
        {icon}
        <span className="text-sm">{label}</span>
      </div>
      <p className="mt-1 text-2xl font-semibold text-navy">{value}</p>
    </Card>
  );
}
