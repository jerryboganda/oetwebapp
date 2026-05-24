'use client';

import { useEffect, useState } from 'react';
import {
  Building2,
  Mail,
  Plus,
  Search,
} from 'lucide-react';
import { AdminRouteWorkspace } from '@/components/domain/admin-route-surface';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Button } from '@/components/admin/ui/button';
import { Badge } from '@/components/admin/ui/badge';
import { Input } from '@/components/admin/ui/input';
import { TableSkeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { DataTable, type Column } from '@/components/ui/data-table';
import { fetchAdminSponsors, type AdminSponsorDto } from '@/lib/api';
import { BulkActionBar } from '@/components/ui/bulk-action-bar';

type Sponsor = AdminSponsorDto;

export default function AdminInstitutionsPage() {
  const [sponsors, setSponsors] = useState<Sponsor[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [filter, setFilter] = useState('');
  const [selectedKeys, setSelectedKeys] = useState<Set<string>>(new Set());

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
          <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-admin bg-[var(--admin-primary-tint)] text-[var(--admin-primary)]">
            <Building2 className="h-4 w-4" />
          </div>
          <div className="min-w-0">
            <p className="truncate font-semibold text-admin-fg-strong">{sponsor.name}</p>
            <p className="mt-1 flex items-center gap-1 truncate text-xs text-admin-fg-muted">
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
      className: 'text-admin-fg-default',
    },
    {
      key: 'type',
      header: 'Type',
      render: (sponsor) => <span className="capitalize">{sponsor.type}</span>,
      className: 'text-admin-fg-default',
      hideOnMobile: true,
    },
    {
      key: 'learners',
      header: 'Learners',
      render: (sponsor) => sponsor.learnerCount ?? 0,
      className: 'text-right font-semibold tabular-nums text-admin-fg-strong',
      hideOnMobile: true,
    },
    {
      key: 'status',
      header: 'Status',
      render: (sponsor) => (
        <Badge variant={(sponsor.status === 'active' ? 'success' : 'warning') as any}>
          {sponsor.status}
        </Badge>
      ),
    },
  ];

  return (
    <AdminRouteWorkspace>
      <AdminTableLayout
        title="Institutions"
        description="Manage sponsor accounts, employers, and institutional partners."
        eyebrow="Directory"
        breadcrumbs={[
          { label: 'Admin', href: '/admin' },
          { label: 'Institutions' },
        ]}
        actions={
          <Button variant="primary">
            <Plus className="h-4 w-4" />
            Add institution
          </Button>
        }
        banner={
          <div className="flex flex-col gap-3 rounded-admin-lg border border-admin-border bg-admin-bg-surface p-3 shadow-admin-sm sm:flex-row sm:items-center sm:justify-between">
            <div className="text-sm text-admin-fg-muted">
              {filtered.length} visible {filtered.length === 1 ? 'institution' : 'institutions'}
            </div>
            <div className="relative w-full min-w-[240px] sm:w-80">
              <Search className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-admin-fg-muted" />
              <Input
                placeholder="Search by name, email, or organization…"
                value={filter}
                onChange={(e) => setFilter(e.target.value)}
                className="pl-9"
              />
            </div>
          </div>
        }
      >
        {loading ? (
          <TableSkeleton rows={6} columns={5} />
        ) : error ? (
          <EmptyState
            variant="error"
            illustration={<Building2 className="h-10 w-10" />}
            title="Could not load institutions"
            description={error}
          />
        ) : (
          <>
            <DataTable
              columns={columns}
              data={filtered}
              keyExtractor={(sponsor) => sponsor.id}
              emptyMessage="No institutions found."
              aria-label="Institutions"
              selectable
              selectedKeys={selectedKeys}
              onSelectionChange={setSelectedKeys}
            />
            <BulkActionBar
              selectedCount={selectedKeys.size}
              onClearSelection={() => setSelectedKeys(new Set())}
              actions={[
                { key: 'archive', label: 'Archive selected', variant: 'danger', onClick: () => {} },
              ]}
            />
          </>
        )}
      </AdminTableLayout>
    </AdminRouteWorkspace>
  );
}
