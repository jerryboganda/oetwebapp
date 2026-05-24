'use client';

import { useEffect, useMemo, useState } from 'react';
import type { ColumnDef } from '@tanstack/react-table';
import { Shield, CheckCircle2, XCircle } from 'lucide-react';

import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge, type BadgeProps } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { DataTable } from '@/components/admin/ui/data-table';
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/admin/ui/dialog';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Input } from '@/components/admin/ui/input';
import { KpiTile } from '@/components/admin/ui/kpi-tile';
import { Label } from '@/components/admin/ui/label';
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/admin/ui/select';
import { toast } from '@/components/admin/ui/toaster';

import { reviewScoreGuaranteeClaim } from '@/lib/api';
import { getAdminScoreGuaranteeClaimsData } from '@/lib/admin';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import type { AdminScoreGuaranteeClaim } from '@/lib/types/admin';

type PageStatus = 'loading' | 'success' | 'empty' | 'error';

type StatusKey =
  | 'active'
  | 'claim_submitted'
  | 'claim_approved'
  | 'claim_rejected'
  | 'expired';

const STATUS_BADGE: Record<
  StatusKey,
  { label: string; variant: NonNullable<BadgeProps['variant']> }
> = {
  active: { label: 'Active', variant: 'primary' },
  claim_submitted: { label: 'Claim Submitted', variant: 'info' },
  claim_approved: { label: 'Approved', variant: 'success' },
  claim_rejected: { label: 'Rejected', variant: 'danger' },
  expired: { label: 'Expired', variant: 'secondary' },
};

const STATUS_OPTIONS: { value: StatusKey | 'all'; label: string }[] = [
  { value: 'all', label: 'All statuses' },
  { value: 'active', label: 'Active' },
  { value: 'claim_submitted', label: 'Claim Submitted' },
  { value: 'claim_approved', label: 'Approved' },
  { value: 'claim_rejected', label: 'Rejected' },
  { value: 'expired', label: 'Expired' },
];

export default function ScoreGuaranteeClaimsPage() {
  useAdminAuth();
  const [pageStatus, setPageStatus] = useState<PageStatus>('loading');
  const [statusFilter, setStatusFilter] = useState<StatusKey | 'all'>('all');
  const [claims, setClaims] = useState<AdminScoreGuaranteeClaim[]>([]);
  const [total, setTotal] = useState(0);
  const [page] = useState(1);
  const [reviewTarget, setReviewTarget] =
    useState<AdminScoreGuaranteeClaim | null>(null);
  const [reviewNote, setReviewNote] = useState('');
  const [isMutating, setIsMutating] = useState(false);

  const selectedStatus = statusFilter === 'all' ? undefined : statusFilter;

  useEffect(() => {
    let cancelled = false;
    async function load() {
      setPageStatus('loading');
      try {
        const data = await getAdminScoreGuaranteeClaimsData({
          status: selectedStatus,
          page,
          pageSize: 20,
        });
        if (cancelled) return;
        setClaims(data.items);
        setTotal(data.total);
        setPageStatus(data.items.length > 0 ? 'success' : 'empty');
      } catch {
        if (!cancelled) setPageStatus('error');
      }
    }
    load();
    return () => {
      cancelled = true;
    };
  }, [selectedStatus, page]);

  async function handleReview(decision: 'approve' | 'reject') {
    if (!reviewTarget) return;
    setIsMutating(true);
    try {
      await reviewScoreGuaranteeClaim(
        reviewTarget.id,
        decision,
        reviewNote || undefined,
      );
      setClaims((prev) =>
        prev.map((c) =>
          c.id === reviewTarget.id
            ? {
                ...c,
                status:
                  decision === 'approve'
                    ? 'claim_approved'
                    : 'claim_rejected',
              }
            : c,
        ),
      );
      toast.success(`Claim ${decision === 'approve' ? 'approved' : 'rejected'}.`);
      setReviewTarget(null);
      setReviewNote('');
    } catch {
      toast.error('Failed to review claim.');
    } finally {
      setIsMutating(false);
    }
  }

  const pendingReviewCount = useMemo(
    () => claims.filter((c) => c.status === 'claim_submitted').length,
    [claims],
  );

  const columns = useMemo<ColumnDef<AdminScoreGuaranteeClaim>[]>(
    () => [
      {
        id: 'userId',
        accessorKey: 'userId',
        header: 'User',
        cell: ({ row }) => (
          <span className="font-mono text-xs text-admin-fg-default">
            {row.original.userId.slice(0, 12)}…
          </span>
        ),
      },
      {
        id: 'baselineScore',
        accessorKey: 'baselineScore',
        header: 'Baseline',
        cell: ({ row }) => (
          <span className="tabular-nums text-admin-fg-default">
            {row.original.baselineScore}
          </span>
        ),
      },
      {
        id: 'guaranteedImprovement',
        accessorKey: 'guaranteedImprovement',
        header: 'Target +',
        cell: ({ row }) => (
          <span className="tabular-nums text-admin-fg-default">
            +{row.original.guaranteedImprovement}
          </span>
        ),
      },
      {
        id: 'actualScore',
        accessorKey: 'actualScore',
        header: 'Actual',
        cell: ({ row }) => (
          <span className="tabular-nums text-admin-fg-default">
            {row.original.actualScore ?? '—'}
          </span>
        ),
      },
      {
        id: 'status',
        accessorKey: 'status',
        header: 'Status',
        cell: ({ row }) => {
          const meta =
            STATUS_BADGE[row.original.status as StatusKey] ?? {
              label: row.original.status,
              variant: 'default' as const,
            };
          return <Badge variant={meta.variant}>{meta.label}</Badge>;
        },
      },
      {
        id: 'activatedAt',
        accessorKey: 'activatedAt',
        header: 'Activated',
        cell: ({ row }) => (
          <span className="text-xs text-admin-fg-muted">
            {new Date(row.original.activatedAt).toLocaleDateString()}
          </span>
        ),
      },
      {
        id: 'actions',
        header: '',
        enableSorting: false,
        enableHiding: false,
        cell: ({ row }) =>
          row.original.status === 'claim_submitted' ? (
            <Button
              size="sm"
              variant="outline"
              onClick={() => {
                setReviewTarget(row.original);
                setReviewNote('');
              }}
            >
              Review
            </Button>
          ) : null,
      },
    ],
    [],
  );

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Score Guarantee Claims' },
  ];

  const statusFilterBanner = (
    <div className="flex flex-col gap-4">
      <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-3">
        <KpiTile
          label="Total Claims"
          value={total}
          tone="primary"
          icon={<Shield className="h-4 w-4" />}
          size="sm"
        />
        <KpiTile
          label="Pending Review"
          value={pendingReviewCount}
          tone={pendingReviewCount > 0 ? 'warning' : 'default'}
          size="sm"
        />
      </div>

      <div className="flex items-center gap-2">
        <Label htmlFor="status-filter" className="text-xs text-admin-fg-muted">
          Status
        </Label>
        <Select
          value={statusFilter}
          onValueChange={(v) => setStatusFilter(v as StatusKey | 'all')}
        >
          <SelectTrigger
            id="status-filter"
            className="w-[200px]"
            aria-label="Filter by status"
          >
            <SelectValue placeholder="All statuses" />
          </SelectTrigger>
          <SelectContent>
            {STATUS_OPTIONS.map((opt) => (
              <SelectItem key={opt.value} value={opt.value}>
                {opt.label}
              </SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );

  // Empty / error UI rendered inside the table card.
  const tableBody = (() => {
    if (pageStatus === 'error') {
      return (
        <div className="px-6 py-16">
          <EmptyState
            variant="error"
            illustration={<Shield />}
            title="Unable to load score guarantee claims."
            description="Refresh the page or check back shortly. If the issue persists, contact support."
            primaryAction={{
              label: 'Retry',
              onClick: () => setStatusFilter((prev) => prev),
            }}
          />
        </div>
      );
    }

    if (pageStatus === 'empty') {
      return (
        <div className="px-6 py-16">
          <EmptyState
            illustration={<Shield />}
            title="No claims found"
            description="No score guarantee claims match the current filters."
          />
        </div>
      );
    }

    return (
      <DataTable
        columns={columns}
        data={claims}
        loading={pageStatus === 'loading'}
        enableSelection
        searchPlaceholder="Search claims…"
        searchableColumns={['userId', 'status']}
        emptyMessage="No claims match your search."
      />
    );
  })();

  return (
    <>
      <AdminTableLayout
        title="Score Guarantee Claims"
        description="Review and process score guarantee refund claims."
        breadcrumbs={breadcrumbs}
        banner={statusFilterBanner}
      >
        {tableBody}
      </AdminTableLayout>

      {/* Review Dialog */}
      <Dialog
        open={!!reviewTarget}
        onOpenChange={(open) => {
          if (!open) {
            setReviewTarget(null);
            setReviewNote('');
          }
        }}
      >
        <DialogContent size="md">
          <DialogHeader>
            <DialogTitle>Review Score Guarantee Claim</DialogTitle>
            <DialogDescription>
              Approve or reject this learner&rsquo;s refund claim.
            </DialogDescription>
          </DialogHeader>

          {reviewTarget ? (
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-3 text-sm">
                <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                  <div className="text-xs uppercase tracking-wide text-admin-fg-muted">
                    Baseline
                  </div>
                  <div className="mt-1 text-base font-semibold tabular-nums text-admin-fg-strong">
                    {reviewTarget.baselineScore}
                  </div>
                </div>
                <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                  <div className="text-xs uppercase tracking-wide text-admin-fg-muted">
                    Actual
                  </div>
                  <div className="mt-1 text-base font-semibold tabular-nums text-admin-fg-strong">
                    {reviewTarget.actualScore ?? '—'}
                  </div>
                </div>
                <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                  <div className="text-xs uppercase tracking-wide text-admin-fg-muted">
                    Target
                  </div>
                  <div className="mt-1 text-base font-semibold tabular-nums text-admin-fg-strong">
                    {reviewTarget.baselineScore +
                      reviewTarget.guaranteedImprovement}
                  </div>
                </div>
                <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3">
                  <div className="text-xs uppercase tracking-wide text-admin-fg-muted">
                    Improvement needed
                  </div>
                  <div className="mt-1 text-base font-semibold tabular-nums text-admin-fg-strong">
                    +{reviewTarget.guaranteedImprovement}
                  </div>
                </div>
              </div>

              {reviewTarget.claimNote ? (
                <div className="rounded-admin border border-admin-border bg-admin-bg-subtle p-3 text-sm text-admin-fg-default">
                  <div className="text-xs uppercase tracking-wide text-admin-fg-muted">
                    Learner note
                  </div>
                  <p className="mt-1">{reviewTarget.claimNote}</p>
                </div>
              ) : null}

              <Input
                label="Review note (optional)"
                value={reviewNote}
                onChange={(e) => setReviewNote(e.target.value)}
                placeholder="Add a note…"
              />
            </div>
          ) : null}

          <DialogFooter>
            <Button
              variant="outline"
              onClick={() => handleReview('reject')}
              disabled={isMutating}
              startIcon={<XCircle className="h-4 w-4" />}
            >
              Reject
            </Button>
            <Button
              onClick={() => handleReview('approve')}
              loading={isMutating}
              startIcon={<CheckCircle2 className="h-4 w-4" />}
            >
              Approve
            </Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
