'use client';

import { useEffect, useState } from 'react';
import { Users, DollarSign, UserCheck, Clock } from 'lucide-react';
import { fetchSponsorDashboard, isApiError, type SponsorDashboardData } from '@/lib/api';
import { StatCard } from '@/components/ui/stat-card';

export default function SponsorDashboardPage() {
  const [data, setData] = useState<SponsorDashboardData | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    let cancelled = false;

    async function load() {
      try {
        const result = await fetchSponsorDashboard();
        if (!cancelled) {
          setData(result);
        }
      } catch (err) {
        if (!cancelled) {
          setError(isApiError(err) ? err.userMessage : 'Failed to load dashboard.');
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
        <h1 className="text-2xl font-bold text-navy">Sponsor Dashboard</h1>
        <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
          {[1, 2, 3, 4].map((i) => (
            <div key={i} className="page-surface h-28 animate-pulse rounded-2xl" />
          ))}
        </div>
      </div>
    );
  }

  if (error) {
    return (
      <div className="space-y-6">
        <h1 className="text-2xl font-bold text-navy">Sponsor Dashboard</h1>
        <div className="page-surface rounded-2xl p-6 text-center">
          <p className="text-sm text-red-600">{error}</p>
        </div>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <div>
        <h1 className="text-2xl font-bold text-navy">Sponsor Dashboard</h1>
        {data?.organizationName && (
          <p className="mt-1 text-sm text-muted">{data.organizationName}</p>
        )}
      </div>

      <div className="grid gap-2 grid-cols-2 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="Learners Sponsored"
          value={data?.learnersSponsored ?? 0}
          icon={<Users />}
          tone="info"
        />
        <StatCard
          label="Active Sponsorships"
          value={data?.activeSponsorships ?? 0}
          icon={<UserCheck />}
          tone="success"
        />
        <StatCard
          label="Pending Invites"
          value={data?.pendingSponsorships ?? 0}
          icon={<Clock />}
          tone="warning"
        />
        <StatCard
          label="Total Spend"
          value={`£${(data?.totalSpend ?? 0).toFixed(0)}`}
          icon={<DollarSign />}
        />
      </div>

      {/* Success rate and avg score require backend analytics — not yet available */}
      <div className="rounded-xl border border-dashed border-border p-6 text-center">
        <p className="text-sm font-medium text-muted">Coming soon</p>
        <p className="mt-1 text-xs text-muted/70">Success rate and average score analytics are under development.</p>
      </div>
    </div>
  );
}
