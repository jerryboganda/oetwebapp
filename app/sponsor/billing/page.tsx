'use client';

import { useEffect, useState } from 'react';
import { CreditCard, Building2, Users } from 'lucide-react';
import { fetchSponsorBilling, isApiError, type SponsorBillingData } from '@/lib/api';

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
            <div key={i} className="page-surface h-28 animate-pulse rounded-2xl" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Billing</h1>
        <div className="page-surface rounded-2xl p-6 text-center">
          <p className="text-sm text-red-600">{error}</p>
        </div>
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
        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Users className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">Active Sponsorships</span>
          </div>
          <p className="text-2xl font-bold text-navy">{data?.totalSponsorships ?? 0}</p>
        </div>

        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <CreditCard className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">This Month</span>
          </div>
          <p className="text-2xl font-bold text-navy">£{(data?.currentMonthSpend ?? 0).toFixed(2)}</p>
        </div>

        <div className="page-surface rounded-2xl p-6 shadow-sm">
          <div className="flex items-center gap-3 mb-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
              <Building2 className="h-5 w-5" />
            </div>
            <span className="text-sm font-medium text-muted">Total Spend</span>
          </div>
          <p className="text-2xl font-bold text-navy">£{(data?.totalSpend ?? 0).toFixed(2)}</p>
        </div>
      </div>

      {/* Billing cycle */}
      <div className="page-surface rounded-2xl p-6 shadow-sm">
        <h2 className="text-lg font-semibold text-navy mb-4">Billing Details</h2>
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
      </div>

      {/* Invoices placeholder */}
      <div className="page-surface rounded-2xl p-6 shadow-sm">
        <h2 className="text-lg font-semibold text-navy mb-4">Invoices</h2>
        {(data?.invoices?.length ?? 0) === 0 ? (
          <p className="text-sm text-muted">No invoices yet.</p>
        ) : (
          <p className="text-sm text-muted">Invoice list will appear here.</p>
        )}
      </div>
    </div>
  );
}
