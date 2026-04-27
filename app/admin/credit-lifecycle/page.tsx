'use client';

import { useEffect, useState } from 'react';
import { Coins, Wallet } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';

interface CreditPolicy {
  policy: { expiryDays: number; expiryEnabled: boolean; rolloverEnabled: boolean; rolloverPercentage: number; refundOnFailedReview: boolean; refundOnCancelledReview: boolean; proRataOnDowngrade: boolean; minimumCreditPurchase: number; maximumCreditBalance: number };
  systemStats: { totalCreditsInCirculation: number; walletsWithCredits: number; last30DaysTransactions: { type: string; count: number; totalAmount: number }[] };
  notes: string[];
}

const apiRequest = apiClient.request;

export default function CreditLifecyclePage() {
  const [data, setData] = useState<CreditPolicy | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('admin_credit_lifecycle_viewed');
    apiRequest<CreditPolicy>('/v1/admin/credit-lifecycle').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Credit Lifecycle Policy">
      <AdminRouteHero
        eyebrow="Admin Workspace"
        icon={Coins}
        accent="amber"
        title="Credit Lifecycle Policy"
        description="Manage review credit expiry, rollover, refund rules, and monitor credit circulation."
      />

      {loading ? (
        <div className="space-y-4">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}</div>
      ) : data ? (
        <MotionSection className="space-y-6">
          <MotionItem>
            <AdminRoutePanel title="Current Policy" description="Active rules governing credit issuance and lifecycle.">
              <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                <div className="text-center"><p className="text-lg font-bold text-navy">{data.policy.expiryEnabled ? `${data.policy.expiryDays}d` : 'Never'}</p><p className="text-xs text-muted">Credit Expiry</p></div>
                <div className="text-center"><p className="text-lg font-bold text-navy">{data.policy.rolloverEnabled ? `${data.policy.rolloverPercentage}%` : 'No'}</p><p className="text-xs text-muted">Rollover</p></div>
                <div className="text-center"><Badge variant={data.policy.refundOnFailedReview ? 'default' : 'outline'}>{data.policy.refundOnFailedReview ? 'Yes' : 'No'}</Badge><p className="text-xs text-muted mt-1">Refund on Failed</p></div>
                <div className="text-center"><Badge variant={data.policy.refundOnCancelledReview ? 'default' : 'outline'}>{data.policy.refundOnCancelledReview ? 'Yes' : 'No'}</Badge><p className="text-xs text-muted mt-1">Refund on Cancel</p></div>
                <div className="text-center"><Badge variant={data.policy.proRataOnDowngrade ? 'default' : 'outline'}>{data.policy.proRataOnDowngrade ? 'Yes' : 'No'}</Badge><p className="text-xs text-muted mt-1">Pro-rata Downgrade</p></div>
                <div className="text-center"><p className="text-lg font-bold text-navy">{data.policy.maximumCreditBalance}</p><p className="text-xs text-muted">Max Balance</p></div>
              </div>
            </AdminRoutePanel>
          </MotionItem>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <AdminRouteSummaryCard label="Total Credits in System" value={data.systemStats.totalCreditsInCirculation} icon={<Coins className="h-5 w-5" />} />
            <AdminRouteSummaryCard label="Wallets with Credits" value={data.systemStats.walletsWithCredits} icon={<Wallet className="h-5 w-5" />} />
          </div>

          {data.systemStats.last30DaysTransactions.length > 0 && (
            <AdminRoutePanel title="Last 30 Days by Type">
              <div className="space-y-1">{data.systemStats.last30DaysTransactions.map(t => (
                <div key={t.type} className="flex items-center justify-between text-sm"><span className="capitalize">{t.type.replace(/_/g, ' ')}</span><span className="text-muted">{t.count} txns • {t.totalAmount} credits</span></div>
              ))}</div>
            </AdminRoutePanel>
          )}

          <AdminRoutePanel title="Policy Notes">
            {data.notes.map((n, i) => <p key={i} className="text-sm text-muted">• {n}</p>)}
          </AdminRoutePanel>
        </MotionSection>
      ) : (
        <AdminRoutePanel><p className="text-center text-sm text-muted">Unable to load credit lifecycle data.</p></AdminRoutePanel>
      )}
    </AdminRouteWorkspace>
  );
}
