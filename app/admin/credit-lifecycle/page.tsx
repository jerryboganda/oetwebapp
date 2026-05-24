'use client';

import { useEffect, useState } from 'react';
import { Coins, Wallet, AlertOctagon } from 'lucide-react';

import { analytics } from '@/lib/analytics';
import { apiClient } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';

import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Card, CardContent, CardHeader, CardTitle, CardDescription } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

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

const apiRequest = apiClient.request;

export default function CreditLifecyclePage() {
  useAdminAuth();
  const [data, setData] = useState<CreditPolicy | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    analytics.track('admin_credit_lifecycle_viewed');
    apiRequest<CreditPolicy>('/v1/admin/credit-lifecycle')
      .then(setData)
      .catch((e) =>
        setError(e instanceof Error ? e.message : 'Failed to load credit lifecycle data'),
      )
      .finally(() => setLoading(false));
  }, []);

  return (
    <AdminOperationsLayout
      title="Credit lifecycle policy"
      description="Manage review credit expiry, rollover, refund rules, and monitor credit circulation."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Credit lifecycle' },
      ]}
    >
      {loading ? (
        <div className="space-y-4">
          {Array.from({ length: 3 }).map((_, i) => (
            <Skeleton key={i} className="h-32 rounded-admin" />
          ))}
        </div>
      ) : error ? (
        <Card>
          <CardContent>
            <EmptyState
              variant="error"
              size="lg"
              illustration={<AlertOctagon aria-hidden="true" />}
              title="Unable to load credit policy"
              description={error}
            />
          </CardContent>
        </Card>
      ) : data ? (
        <>
          <Card>
            <CardHeader>
              <div>
                <CardTitle>Current policy</CardTitle>
                <CardDescription>Active rules governing credit issuance and lifecycle.</CardDescription>
              </div>
            </CardHeader>
            <CardContent>
              <div className="grid grid-cols-2 gap-4 md:grid-cols-3">
                <PolicyStat
                  label="Credit expiry"
                  value={data.policy.expiryEnabled ? `${data.policy.expiryDays}d` : 'Never'}
                />
                <PolicyStat
                  label="Rollover"
                  value={data.policy.rolloverEnabled ? `${data.policy.rolloverPercentage}%` : 'No'}
                />
                <PolicyBadgeStat
                  label="Refund on failed"
                  yes={data.policy.refundOnFailedReview}
                />
                <PolicyBadgeStat
                  label="Refund on cancel"
                  yes={data.policy.refundOnCancelledReview}
                />
                <PolicyBadgeStat
                  label="Pro-rata downgrade"
                  yes={data.policy.proRataOnDowngrade}
                />
                <PolicyStat
                  label="Max balance"
                  value={String(data.policy.maximumCreditBalance)}
                />
              </div>
            </CardContent>
          </Card>

          <div className="grid grid-cols-1 gap-4 sm:grid-cols-2">
            <KpiTile
              label="Total credits in system"
              value={data.systemStats.totalCreditsInCirculation.toLocaleString()}
              tone="primary"
              icon={<Coins className="h-4 w-4" />}
            />
            <KpiTile
              label="Wallets with credits"
              value={data.systemStats.walletsWithCredits.toLocaleString()}
              tone="info"
              icon={<Wallet className="h-4 w-4" />}
            />
          </div>

          {data.systemStats.last30DaysTransactions.length > 0 && (
            <Card>
              <CardHeader>
                <CardTitle>Last 30 days by type</CardTitle>
              </CardHeader>
              <CardContent>
                <ul className="divide-y divide-admin-border">
                  {data.systemStats.last30DaysTransactions.map((t) => (
                    <li
                      key={t.type}
                      className="flex items-center justify-between py-2.5 first:pt-0 last:pb-0 text-sm"
                    >
                      <span className="capitalize text-admin-fg-default">
                        {t.type.replace(/_/g, ' ')}
                      </span>
                      <span className="text-admin-fg-muted tabular-nums">
                        {t.count} txns · {t.totalAmount} credits
                      </span>
                    </li>
                  ))}
                </ul>
              </CardContent>
            </Card>
          )}

          <Card>
            <CardHeader>
              <CardTitle>Policy notes</CardTitle>
            </CardHeader>
            <CardContent>
              <ul className="space-y-1.5">
                {data.notes.map((n, i) => (
                  <li key={i} className="text-sm text-admin-fg-muted">
                    • {n}
                  </li>
                ))}
              </ul>
            </CardContent>
          </Card>
        </>
      ) : (
        <Card>
          <CardContent>
            <EmptyState
              size="md"
              title="No credit policy data"
              description="Unable to load credit lifecycle data."
            />
          </CardContent>
        </Card>
      )}
    </AdminOperationsLayout>
  );
}

function PolicyStat({ label, value }: { label: string; value: string }) {
  return (
    <div className="text-center">
      <p className="text-2xl font-bold tabular-nums tracking-tight text-admin-fg-strong">{value}</p>
      <p className="mt-1 text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
        {label}
      </p>
    </div>
  );
}

function PolicyBadgeStat({ label, yes }: { label: string; yes: boolean }) {
  return (
    <div className="text-center">
      <Badge variant={yes ? 'success' : 'default'} size="md">
        {yes ? 'Yes' : 'No'}
      </Badge>
      <p className="mt-1.5 text-xs font-semibold uppercase tracking-wider text-admin-fg-muted">
        {label}
      </p>
    </div>
  );
}
