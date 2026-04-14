'use client';

import { useEffect, useState } from 'react';
import { Users, DollarSign, UserCheck, Clock } from 'lucide-react';
import { fetchSponsorDashboard, isApiError, type SponsorDashboardData } from '@/lib/api';

function StatCard({ label, value, icon }: { label: string; value: string | number; icon: React.ReactNode }) {
  return (
    <div className="page-surface rounded-2xl p-6 shadow-sm">
      <div className="flex items-center gap-3 mb-3">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
          {icon}
        </div>
        <span className="text-sm font-medium text-muted">{label}</span>
      </div>
      <p className="text-2xl font-bold text-navy">{value}</p>
    </div>
  );
}

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

      <div className="grid gap-4 sm:grid-cols-2 lg:grid-cols-4">
        <StatCard
          label="Learners Sponsored"
          value={data?.learnersSponsored ?? 0}
          icon={<Users className="h-5 w-5" />}
        />
        <StatCard
          label="Active Sponsorships"
          value={data?.activeSponsorships ?? 0}
          icon={<UserCheck className="h-5 w-5" />}
        />
        <StatCard
          label="Pending Invitations"
          value={data?.pendingSponsorships ?? 0}
          icon={<Clock className="h-5 w-5" />}
        />
        <StatCard
          label="Total Spend"
          value={`£${(data?.totalSpend ?? 0).toFixed(2)}`}
          icon={<DollarSign className="h-5 w-5" />}
        />
      </div>
    </div>
  );
}
