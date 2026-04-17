'use client';

import { useEffect, useState } from 'react';
import { Coins, RefreshCw, ShieldCheck, Info } from 'lucide-react';
import { MotionSection, MotionItem } from '@/components/ui/motion-primitives';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { analytics } from '@/lib/analytics';

interface CreditPolicy {
  policy: { expiryDays: number; expiryEnabled: boolean; rolloverEnabled: boolean; rolloverPercentage: number; refundOnFailedReview: boolean; refundOnCancelledReview: boolean; proRataOnDowngrade: boolean; minimumCreditPurchase: number; maximumCreditBalance: number };
  systemStats: { totalCreditsInCirculation: number; walletsWithCredits: number; last30DaysTransactions: { type: string; count: number; totalAmount: number }[] };
  notes: string[];
}

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, { ...init, headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json', ...init?.headers } });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function CreditLifecyclePage() {
  const [data, setData] = useState<CreditPolicy | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    analytics.track('admin_credit_lifecycle_viewed');
    apiRequest<CreditPolicy>('/v1/admin/credit-lifecycle').then(setData).catch(() => {}).finally(() => setLoading(false));
  }, []);

  return (
    <div className="min-h-screen bg-background-light">
      <div className="max-w-4xl mx-auto px-4 py-8">
        <h1 className="text-2xl font-bold mb-1">Credit Lifecycle Policy</h1>
        <p className="text-muted mb-6">Manage review credit expiry, rollover, refund rules, and monitor credit circulation.</p>

        {loading ? <div className="space-y-4">{Array.from({ length: 3 }).map((_, i) => <Skeleton key={i} className="h-32 rounded-xl" />)}</div> : data ? (
          <MotionSection className="space-y-6">
            {/* Policy rules */}
            <MotionItem><Card className="p-5"><h3 className="font-semibold mb-4 flex items-center gap-2"><ShieldCheck className="w-5 h-5 text-primary" /> Current Policy</h3>
              <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
                <div className="text-center"><p className="text-lg font-bold">{data.policy.expiryEnabled ? `${data.policy.expiryDays}d` : 'Never'}</p><p className="text-xs text-muted">Credit Expiry</p></div>
                <div className="text-center"><p className="text-lg font-bold">{data.policy.rolloverEnabled ? `${data.policy.rolloverPercentage}%` : 'No'}</p><p className="text-xs text-muted">Rollover</p></div>
                <div className="text-center"><Badge variant={data.policy.refundOnFailedReview ? 'default' : 'outline'}>{data.policy.refundOnFailedReview ? 'Yes' : 'No'}</Badge><p className="text-xs text-muted mt-1">Refund on Failed</p></div>
                <div className="text-center"><Badge variant={data.policy.refundOnCancelledReview ? 'default' : 'outline'}>{data.policy.refundOnCancelledReview ? 'Yes' : 'No'}</Badge><p className="text-xs text-muted mt-1">Refund on Cancel</p></div>
                <div className="text-center"><Badge variant={data.policy.proRataOnDowngrade ? 'default' : 'outline'}>{data.policy.proRataOnDowngrade ? 'Yes' : 'No'}</Badge><p className="text-xs text-muted mt-1">Pro-rata Downgrade</p></div>
                <div className="text-center"><p className="text-lg font-bold">{data.policy.maximumCreditBalance}</p><p className="text-xs text-muted">Max Balance</p></div>
              </div>
            </Card></MotionItem>

            {/* System stats */}
            <MotionItem><Card className="p-5"><h3 className="font-semibold mb-4 flex items-center gap-2"><Coins className="w-5 h-5 text-amber-500" /> Credit Circulation</h3>
              <div className="grid grid-cols-1 sm:grid-cols-2 gap-4 mb-4">
                <div className="text-center"><p className="text-2xl font-bold">{data.systemStats.totalCreditsInCirculation}</p><p className="text-xs text-muted">Total Credits in System</p></div>
                <div className="text-center"><p className="text-2xl font-bold">{data.systemStats.walletsWithCredits}</p><p className="text-xs text-muted">Wallets with Credits</p></div>
              </div>
              {data.systemStats.last30DaysTransactions.length > 0 && (
                <div><h4 className="text-sm font-medium mb-2">Last 30 Days by Type</h4>
                  <div className="space-y-1">{data.systemStats.last30DaysTransactions.map(t => (
                    <div key={t.type} className="flex items-center justify-between text-sm"><span className="capitalize">{t.type.replace(/_/g, ' ')}</span><span className="text-muted">{t.count} txns • {t.totalAmount} credits</span></div>
                  ))}</div>
                </div>
              )}
            </Card></MotionItem>

            {/* Notes */}
            <Card className="p-4 bg-muted/50"><h4 className="font-medium mb-2 flex items-center gap-2"><Info className="w-4 h-4" /> Policy Notes</h4>
              {data.notes.map((n, i) => <p key={i} className="text-sm text-muted">• {n}</p>)}
            </Card>
          </MotionSection>
        ) : <Card className="p-8 text-center text-muted"><p>Unable to load credit lifecycle data.</p></Card>}
      </div>
    </div>
  );
}
