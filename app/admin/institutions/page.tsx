'use client';

import { useEffect, useState } from 'react';
import { motion, useReducedMotion } from 'motion/react';
import {
  Building2,
  Mail,
  Plus,
  Search,
} from 'lucide-react';
import { Button } from '@/components/ui/button';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Input } from '@/components/ui/form-controls';
import { Skeleton } from '@/components/ui/skeleton';
import { AdminRoutePanel, AdminRouteSectionHeader, AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { fetchAdminSponsors, type AdminSponsorDto } from '@/lib/api';

type Sponsor = AdminSponsorDto;

export default function AdminInstitutionsPage() {
  const reducedMotion = useReducedMotion();
  const [sponsors, setSponsors] = useState<Sponsor[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('');

  useEffect(() => {
    fetchAdminSponsors()
      .then((data) => setSponsors(data.items ?? []))
      .catch(() => setError('Failed to load institutions'))
      .finally(() => setLoading(false));
  }, []);

  const filtered = sponsors.filter(
    (s) =>
      s.name.toLowerCase().includes(filter.toLowerCase()) ||
      s.organizationName?.toLowerCase().includes(filter.toLowerCase()) ||
      s.contactEmail.toLowerCase().includes(filter.toLowerCase())
  );

  const columns: Column<Sponsor>[] = [
    {
      key: 'institution',
      header: 'Institution',
      render: (sponsor) => (
        <div className="flex min-w-0 items-center gap-3">
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-lg bg-violet-500/15 text-violet-500">
            <Building2 className="h-4 w-4" />
          </div>
          <div className="min-w-0">
            <p className="truncate font-semibold text-zinc-900 dark:text-zinc-100">{sponsor.name}</p>
            <p className="mt-1 flex items-center gap-1 truncate text-xs text-zinc-500 dark:text-zinc-400">
              <Mail className="h-3 w-3 shrink-0" />
              {sponsor.contactEmail}
            </p>
          </div>
        </div>
      ),
    },
    {
      key: 'organization',
      header: 'Organization',
      render: (sponsor) => sponsor.organizationName ?? 'Unassigned',
      className: 'text-zinc-600 dark:text-zinc-300',
    },
    {
      key: 'type',
      header: 'Type',
      render: (sponsor) => <span className="capitalize">{sponsor.type}</span>,
      className: 'text-zinc-600 dark:text-zinc-300',
      hideOnMobile: true,
    },
    {
      key: 'learners',
      header: 'Learners',
      render: (sponsor) => sponsor.learnerCount ?? 0,
      className: 'text-right font-semibold tabular-nums text-zinc-900 dark:text-zinc-100',
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (sponsor) => (
        <span
          className={`inline-flex rounded px-2 py-1 text-[10px] font-black uppercase tracking-[0.16em] ${
            sponsor.status === 'active'
              ? 'bg-emerald-500/10 text-emerald-700 dark:text-emerald-400'
              : 'bg-amber-500/10 text-amber-700 dark:text-amber-400'
          }`}
        >
          {sponsor.status}
        </span>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace className="p-6">
      <motion.div
        initial={reducedMotion ? false : { opacity: 0, y: 8 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.3 }}
        className="space-y-4"
      >
        <AdminRouteSectionHeader
          title="Institutions"
          description="Manage sponsor accounts, employers, and institutional partners."
          icon={Building2}
          meta={`${filtered.length} visible`}
          actions={(
          <Button variant="primary">
            <Plus className="mr-1.5 h-4 w-4" />
            Add institution
          </Button>
          )}
        />

        <AdminRoutePanel
          title="Institution Registry"
          description="Search sponsor accounts by legal name, organization, or contact email."
          actions={(
            <div className="relative w-full min-w-[240px] sm:w-80">
            <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-muted" />
            <Input
              placeholder="Search by name, email, or organization..."
              value={filter}
              onChange={(e) => setFilter(e.target.value)}
              className="pl-9"
            />
          </div>
          )}
          contentClassName="p-0"
        >
          {loading ? (
            <div className="divide-y divide-zinc-100 dark:divide-zinc-800">
              {[0, 1, 2].map((i) => (
                <div key={i} className="flex items-center gap-4 p-4">
                  <Skeleton className="h-10 w-10 rounded-lg" />
                  <div className="flex-1 space-y-2">
                    <Skeleton className="h-4 w-1/3" />
                    <Skeleton className="h-3 w-1/4" />
                  </div>
                  <Skeleton className="h-8 w-20" />
                </div>
              ))}
            </div>
          ) : error ? (
            <div className="p-8 text-center text-muted">{error}</div>
          ) : (
            <DataTable
              columns={columns}
              data={filtered}
              keyExtractor={(sponsor) => sponsor.id}
              emptyMessage="No institutions found."
              aria-label="Institutions"
            />
          )}
        </AdminRoutePanel>
      </motion.div>
    </AdminRouteWorkspace>
  );
}
