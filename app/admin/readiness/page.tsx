'use client';

import { useCallback, useEffect, useState } from 'react';
import Link from 'next/link';
import { TrendingUp, RefreshCcw, AlertTriangle, ChevronRight } from 'lucide-react';
import type { ColumnDef } from '@tanstack/react-table';

import {
  fetchAdminReadinessLearners,
  recomputeAdminReadiness,
  type AdminReadinessLearnerRow,
  type AdminReadinessLearnerList,
} from '@/lib/api';
import { AdminOperationsLayout } from '@/components/admin/layout/admin-operations-layout';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/admin/ui/badge';
import { DataTable } from '@/components/admin/ui/data-table';
import { TableSkeleton } from '@/components/admin/ui/skeleton';
import { EmptyState } from '@/components/admin/ui/empty-state';

const RISK_OPTIONS: { value: string; label: string }[] = [
  { value: '', label: 'All' },
  { value: 'High', label: 'High risk' },
  { value: 'Moderate', label: 'Moderate' },
  { value: 'Low', label: 'Low' },
  { value: 'Unknown', label: 'Unknown' },
];

function riskToVariant(risk: string): 'danger' | 'warning' | 'success' | 'default' {
  if (risk === 'High') return 'danger';
  if (risk === 'Moderate') return 'warning';
  if (risk === 'Low') return 'success';
  return 'default';
}

export default function AdminReadinessLearnersPage() {
  const [data, setData] = useState<AdminReadinessLearnerList | null>(null);
  const [risk, setRisk] = useState('');
  const [page, setPage] = useState(1);
  const [error, setError] = useState('');
  const [recomputing, setRecomputing] = useState<string | null>(null);

  const load = useCallback(async () => {
    setError('');
    try {
      const result = await fetchAdminReadinessLearners({
        risk: risk || undefined,
        page,
        pageSize: 25,
      });
      setData(result);
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Could not load learner readiness list.');
    }
  }, [risk, page]);

  useEffect(() => {
    void load();
  }, [load]);

  async function handleRecompute(userId: string) {
    setRecomputing(userId);
    try {
      await recomputeAdminReadiness(userId);
      await load();
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Recompute failed.');
    } finally {
      setRecomputing(null);
    }
  }

  const columns: ColumnDef<AdminReadinessLearnerRow>[] = [
    {
      accessorKey: 'displayName',
      header: 'Learner',
      cell: ({ row }) => (
        <span className="font-semibold text-admin-fg-strong">{row.original.displayName}</span>
      ),
    },
    {
      accessorKey: 'targetExamDate',
      header: 'Target date',
      cell: ({ row }) => (
        <span className="text-admin-fg-muted">{row.original.targetExamDate ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'overallReadiness',
      header: 'Overall',
      cell: ({ row }) => (
        <span className="font-semibold tabular-nums text-admin-fg-strong">
          {Math.round(row.original.overallReadiness)}
        </span>
      ),
    },
    {
      accessorKey: 'overallRisk',
      header: 'Risk',
      cell: ({ row }) => (
        <Badge variant={riskToVariant(row.original.overallRisk)} size="sm">
          {row.original.overallRisk}
        </Badge>
      ),
    },
    {
      accessorKey: 'weakestSubtest',
      header: 'Weakest',
      cell: ({ row }) => (
        <span className="text-admin-fg-muted">{row.original.weakestSubtest ?? '—'}</span>
      ),
    },
    {
      accessorKey: 'targetDateProbability',
      header: 'Probability',
      cell: ({ row }) => (
        <span className="text-admin-fg-muted tabular-nums">
          {row.original.targetDateProbability != null
            ? `${Math.round(row.original.targetDateProbability)}%`
            : '—'}
        </span>
      ),
    },
    {
      accessorKey: 'computedAt',
      header: 'Updated',
      cell: ({ row }) => (
        <span className="text-xs text-admin-fg-muted">
          {new Date(row.original.computedAt).toLocaleDateString()}
        </span>
      ),
    },
    {
      id: 'actions',
      header: '',
      cell: ({ row }) => {
        const r = row.original;
        const isRecomputing = recomputing === r.userId;
        return (
          <div className="flex items-center justify-end gap-1">
            <Button
              variant="ghost"
              size="sm"
              onClick={() => handleRecompute(r.userId)}
              disabled={isRecomputing}
              startIcon={
                <RefreshCcw className={`h-3.5 w-3.5 ${isRecomputing ? 'animate-spin' : ''}`} />
              }
            >
              {isRecomputing ? 'Recomputing' : 'Recompute'}
            </Button>
            <Button asChild variant="ghost" size="sm" endIcon={<ChevronRight className="h-3.5 w-3.5" />}>
              <Link href={`/admin/readiness/${encodeURIComponent(r.userId)}`}>Open</Link>
            </Button>
          </div>
        );
      },
    },
  ];

  return (
    <AdminOperationsLayout
      title="Learner readiness oversight"
      description="Identify learners at risk of missing target dates and trigger intervention."
      breadcrumbs={[
        { label: 'Admin', href: '/admin' },
        { label: 'Learner readiness' },
      ]}
      actions={
        <Button asChild variant="outline" size="sm" startIcon={<TrendingUp className="h-4 w-4" />}>
          <Link href="/admin/readiness/metrics">View platform metrics</Link>
        </Button>
      }
    >
      {error && (
        <Card surface="tinted-danger">
          <CardContent>
            <p className="text-sm font-medium text-[var(--admin-danger)]">{error}</p>
          </CardContent>
        </Card>
      )}

      <Card>
        <CardHeader>
          <CardTitle>Filter by risk</CardTitle>
        </CardHeader>
        <CardContent>
          <div className="flex flex-wrap items-center gap-2">
            {RISK_OPTIONS.map((opt) => (
              <Button
                key={opt.value}
                variant={risk === opt.value ? 'primary' : 'outline'}
                size="sm"
                onClick={() => {
                  setRisk(opt.value);
                  setPage(1);
                }}
              >
                {opt.label}
              </Button>
            ))}
          </div>
        </CardContent>
      </Card>

      {!data ? (
        <TableSkeleton rows={6} columns={8} />
      ) : data.items.length === 0 ? (
        <Card>
          <CardContent>
            <EmptyState
              size="md"
              title="No learners matched"
              description="No learners matched the current risk filter. Try a different filter."
            />
          </CardContent>
        </Card>
      ) : (
        <Card>
          <CardContent className="p-0 pt-0">
            <DataTable
              columns={columns as ColumnDef<AdminReadinessLearnerRow, unknown>[]}
              data={data.items}
              enableSorting={false}
              enableFiltering={false}
              enableColumnVisibility={false}
              enablePagination={false}
            />
            {data.total > data.pageSize && (
              <div className="flex items-center justify-between border-t border-admin-border-default px-4 py-3 text-xs text-admin-fg-muted">
                <span>
                  Page {data.page} · {data.total} learners
                </span>
                <div className="flex gap-2">
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setPage(Math.max(1, page - 1))}
                    disabled={page <= 1}
                  >
                    Prev
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    onClick={() => setPage(page + 1)}
                    disabled={data.items.length < data.pageSize}
                  >
                    Next
                  </Button>
                </div>
              </div>
            )}
          </CardContent>
        </Card>
      )}

      {data && data.items.some((r) => r.overallRisk === 'High') && (
        <Card surface="tinted-danger">
          <CardContent>
            <div className="flex items-start gap-3">
              <AlertTriangle className="mt-0.5 h-5 w-5 shrink-0 text-[var(--admin-danger)]" />
              <div>
                <p className="text-sm font-semibold text-admin-fg-strong">
                  Intervention candidates detected
                </p>
                <p className="text-xs text-admin-fg-muted">
                  High-risk learners benefit from outreach — consider a tutor check-in or revised study plan.
                </p>
              </div>
            </div>
          </CardContent>
        </Card>
      )}
    </AdminOperationsLayout>
  );
}
