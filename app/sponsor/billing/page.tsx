'use client';

import { useEffect, useState } from 'react';
import { CreditCard, Building2, Users } from 'lucide-react';
import { fetchSponsorBilling, isApiError, type SponsorBillingData } from '@/lib/api';
import { Card } from '@/components/ui/card';
import { StatCard } from '@/components/ui/stat-card';

export default function SponsorBillingPage() {
  const [data, setData] = useState<SponsorBillingData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const result = await fetchSponsorBilling();
        if (!cancelled) {
          setData(result);
        }
      } catch (err) {
        if (!cancelled) {
          setError(isApiError(err) ? err.userMessage : 'Failed to load billing data.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    }

    void load();
    return () => { cancelled = true; };
  }, []);

  if (loading) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
          {[1, 2, 3].map((i) => (
            <div key={i} className="h-28 animate-pulse rounded-2xl border border-border bg-surface shadow-sm" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <Card padding="lg" className="text-center">
          <p className="text-sm text-danger">{error}</p>
        </Card>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        {data?.organizationName && (
          <p className="mt-1 text-sm text-muted">{data.organizationName}</p>
        )}
      </div>

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
        <StatCard
          label="Active Sponsorships"
          value={data?.totalSponsorships ?? 0}
          icon={<Users />}
          tone="info"
        />
        <StatCard
          label="This Month"
          value={`£${(data?.currentMonthSpend ?? 0).toFixed(2)}`}
          icon={<CreditCard />}
          tone="warning"
        />
        <StatCard
          label="Total Spend"
          value={`£${(data?.totalSpend ?? 0).toFixed(2)}`}
          icon={<Building2 />}
        />
      </div>

      {/* Billing cycle */}
      <Card padding="lg">
        <h2 className="text-lg font-bold text-navy mb-4">Billing Details</h2>
        <dl className="grid gap-4 sm:grid-cols-2">
          <div>
            <dt className="text-sm font-medium text-muted">Billing Cycle</dt>
            <dd className="mt-1 text-sm text-navy capitalize">{data?.billingCycle ?? 'monthly'}</dd>
          </div>
          <div>
            <dt className="text-sm font-medium text-muted">Account</dt>
            <dd className="mt-1 text-sm text-navy">{data?.sponsorName ?? '—'}</dd>
          </div>
        </dl>
      </Card>

      {/* Invoices */}
      <Card padding="lg">
        <h2 className="text-lg font-bold text-navy mb-4">Invoices</h2>
        {(data?.invoices?.length ?? 0) === 0 ? (
          <p className="text-sm text-muted">No invoices yet. Invoices will appear here once billing is active.</p>
        ) : (
          <div className="rounded-2xl border border-dashed border-border p-6 text-center">
            <p className="text-sm font-medium text-muted">Coming soon</p>
            <p className="mt-1 text-xs text-muted">Invoice display is under development. {data?.invoices?.length} invoice{(data?.invoices?.length ?? 0) !== 1 ? 's' : ''} on file.</p>
          </div>
        )}
      </Card>
    </div>
  );
}
