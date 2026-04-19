'use client';

import { useEffect, useState } from 'react';
import { Coins, ShieldCheck, Info } from 'lucide-react';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRoutePanelFooter,
  AdminRouteStatRow,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { analytics } from '@/lib/analytics';

interface CreditPolicy {
  policy: {
    expiryDays: number;
    expiryEnabled: boolean;
    rolloverEnabled: boolean;
    rolloverPercentage: number;
    refundOnFailedReview: boolean;
    refundOnCancelledReview: boolean;
    proRataOnDowngrade: boolean;
    minimumCreditPurchase: number;
    maximumCreditBalance: number;
  };
  systemStats: {
    totalCreditsInCirculation: number;
    walletsWithCredits: number;
    last30DaysTransactions: { type: string; count: number; totalAmount: number }[];
  };
  notes: string[];
}

type Status = 'loading' | 'error' | 'success';

async function apiRequest<T = unknown>(path: string, init?: RequestInit): Promise<T> {
  const { ensureFreshAccessToken } = await import('@/lib/auth-client');
  const { env } = await import('@/lib/env');
  const token = await ensureFreshAccessToken();
  const res = await fetch(`${env.apiBaseUrl}${path}`, {
    ...init,
    headers: {
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
      ...init?.headers,
    },
  });
  if (!res.ok) throw new Error(`API error ${res.status}`);
  return res.json();
}

export default function CreditLifecyclePage() {
  const [data, setData] = useState<CreditPolicy | null>(null);
  const [status, setStatus] = useState<Status>('loading');

  useEffect(() => {
    analytics.track('admin_credit_lifecycle_viewed');
    apiRequest<CreditPolicy>('/v1/admin/credit-lifecycle')
      .then((res) => {
        setData(res);
        setStatus('success');
      })
      .catch(() => setStatus('error'));
  }, []);

  return (
    <AdminRouteWorkspace role="main" aria-label="Credit lifecycle policy">
      <AdminRouteHero
        eyebrow="People & Billing"
        icon={Coins}
        accent="amber"
        title="Credit Lifecycle Policy"
        description="Manage review credit expiry, rollover, refund rules, and monitor credit circulation."
        highlights={
          data
            ? [
                {
                  label: 'In circulation',
                  value: data.systemStats.totalCreditsInCirculation.toLocaleString(),
                },
                {
                  label: 'Wallets',
                  value: data.systemStats.walletsWithCredits.toLocaleString(),
                },
                {
                  label: 'Expiry',
                  value: data.policy.expiryEnabled ? `${data.policy.expiryDays}d` : 'Never',
                },
              ]
            : undefined
        }
      />

      <AsyncStateWrapper status={status} onRetry={() => window.location.reload()}>
        {data ? (
          <>
            <AdminRoutePanel
              eyebrow="Policy"
              title="Current credit lifecycle policy"
              description="Active rules for expiry, rollover, refunds, and balance ceilings."
              actions={<ShieldCheck className="h-5 w-5 text-primary" aria-hidden />}
            >
              <AdminRouteStatRow
                items={[
                  {
                    label: 'Credit expiry',
                    value: data.policy.expiryEnabled ? `${data.policy.expiryDays}d` : 'Never',
                  },
                  {
                    label: 'Rollover',
                    value: data.policy.rolloverEnabled ? `${data.policy.rolloverPercentage}%` : 'No',
                  },
                  { label: 'Max balance', value: data.policy.maximumCreditBalance },
                  { label: 'Min purchase', value: data.policy.minimumCreditPurchase },
                ]}
              />
              <div className="flex flex-wrap gap-2 border-t border-border pt-3">
                <Badge variant={data.policy.refundOnFailedReview ? 'success' : 'muted'}>
                  Refund on failed review · {data.policy.refundOnFailedReview ? 'Yes' : 'No'}
                </Badge>
                <Badge variant={data.policy.refundOnCancelledReview ? 'success' : 'muted'}>
                  Refund on cancelled · {data.policy.refundOnCancelledReview ? 'Yes' : 'No'}
                </Badge>
                <Badge variant={data.policy.proRataOnDowngrade ? 'success' : 'muted'}>
                  Pro-rata on downgrade · {data.policy.proRataOnDowngrade ? 'Yes' : 'No'}
                </Badge>
              </div>
            </AdminRoutePanel>

            <AdminRoutePanel
              eyebrow="Circulation"
              title="Credit circulation"
              description="Live credit totals and recent transaction mix."
            >
              <AdminRouteStatRow
                items={[
                  {
                    label: 'Total credits',
                    value: data.systemStats.totalCreditsInCirculation.toLocaleString(),
                  },
                  {
                    label: 'Wallets with credits',
                    value: data.systemStats.walletsWithCredits.toLocaleString(),
                  },
                ]}
              />
              {data.systemStats.last30DaysTransactions.length > 0 ? (
                <div className="space-y-2 border-t border-border pt-3">
                  <p className="text-sm font-semibold text-navy">Last 30 days by type</p>
                  <div className="space-y-1">
                    {data.systemStats.last30DaysTransactions.map((t) => (
                      <div
                        key={t.type}
                        className="flex items-center justify-between rounded-lg bg-background-light px-3 py-1.5 text-sm"
                      >
                        <span className="capitalize text-navy">{t.type.replace(/_/g, ' ')}</span>
                        <span className="text-muted">
                          {t.count} txns · {t.totalAmount.toLocaleString()} credits
                        </span>
                      </div>
                    ))}
                  </div>
                </div>
              ) : null}
              <AdminRoutePanelFooter window="30d" source="Credit ledger" />
            </AdminRoutePanel>

            {data.notes.length > 0 ? (
              <InlineAlert variant="info" title="Policy notes">
                <ul className="mt-1 space-y-1">
                  {data.notes.map((n, i) => (
                    <li key={i} className="flex items-start gap-2">
                      <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden />
                      <span>{n}</span>
                    </li>
                  ))}
                </ul>
              </InlineAlert>
            ) : null}
          </>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
