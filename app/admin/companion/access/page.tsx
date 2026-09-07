'use client';

import { useCallback, useEffect, useState } from 'react';
import { Bot, RotateCcw, ShieldCheck } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { DataTable, type Column } from '@/components/ui/data-table';
import { Modal } from '@/components/ui/modal';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { apiClient } from '@/lib/api';

interface PlanAccessRow {
  planCode: string;
  planName: string;
  effective: boolean;
  source: 'manifest' | 'override' | 'none';
  inManifest: boolean;
  overrideEnabled: boolean | null;
  updatedByAdminId: string | null;
  updatedAt: string | null;
}

interface AccessResponse {
  items: PlanAccessRow[];
}

type PageStatus = 'loading' | 'success' | 'error';

function sourceBadge(source: PlanAccessRow['source']): 'success' | 'warning' | 'default' {
  switch (source) {
    case 'manifest': return 'success';
    case 'override': return 'warning';
    default: return 'default';
  }
}

function sourceLabel(row: PlanAccessRow): string {
  if (row.source === 'manifest') return 'Catalog';
  if (row.source === 'override') return row.overrideEnabled ? 'Granted here' : 'Revoked here';
  return 'Off';
}

export default function CompanionAccessPage() {
  const { isAuthenticated, role } = useAdminAuth();
  const [status, setStatus] = useState<PageStatus>('loading');
  const [rows, setRows] = useState<PlanAccessRow[]>([]);
  const [revokeTarget, setRevokeTarget] = useState<PlanAccessRow | null>(null);
  const [saving, setSaving] = useState(false);

  const loadAccess = useCallback(async () => {
    try {
      setStatus('loading');
      const data = await apiClient.get<AccessResponse>('/v1/admin/companion/access');
      setRows(data.items);
      setStatus('success');
    } catch {
      setStatus('error');
    }
  }, []);

  useEffect(() => {
    if (isAuthenticated && role === 'admin') {
      // eslint-disable-next-line react-hooks/set-state-in-effect
      loadAccess();
    }
  }, [isAuthenticated, role, loadAccess]);

  const applyGrant = useCallback(async (planCode: string, enabled: boolean) => {
    try {
      setSaving(true);
      await apiClient.post('/v1/admin/companion/access', { planCode, enabled });
      setRevokeTarget(null);
      await loadAccess();
    } finally {
      setSaving(false);
    }
  }, [loadAccess]);

  const resetOverride = useCallback(async (planCode: string) => {
    try {
      setSaving(true);
      await apiClient.delete(`/v1/admin/companion/access/${planCode}`);
      await loadAccess();
    } finally {
      setSaving(false);
    }
  }, [loadAccess]);

  const breadcrumbs = [
    { label: 'Admin', href: '/admin' },
    { label: 'Learning Companion', href: '/admin/companion/access' },
    { label: 'Plan Access' },
  ];

  if (!isAuthenticated || role !== 'admin') {
    return (
      <AdminTableLayout title="Companion Plan Access" eyebrow="Learning Companion" breadcrumbs={breadcrumbs}>
        <EmptyState
          title="Admin access required"
          description="Sign in with an admin account to manage companion plan access."
          illustration={<ShieldCheck className="h-8 w-8" />}
        />
      </AdminTableLayout>
    );
  }

  const grantedCount = rows.filter((row) => row.effective).length;

  const columns: Column<PlanAccessRow>[] = [
    {
      key: 'planName',
      header: 'Plan',
      render: (row) => (
        <div className="min-w-0">
          <p className="truncate text-sm font-medium text-admin-fg-strong">{row.planName}</p>
          <p className="truncate text-xs text-admin-fg-muted">{row.planCode}</p>
        </div>
      ),
    },
    {
      key: 'effective',
      header: 'Companion',
      render: (row) => (
        <Badge variant={row.effective ? 'success' : 'default'} intensity="tinted" size="sm">
          {row.effective ? 'On' : 'Off'}
        </Badge>
      ),
    },
    {
      key: 'source',
      header: 'Source',
      render: (row) => (
        <Badge variant={sourceBadge(row.source)} intensity="tinted" size="sm">
          {sourceLabel(row)}
        </Badge>
      ),
    },
    {
      key: 'updatedAt',
      header: 'Last change',
      render: (row) => (
        <span className="text-xs text-admin-fg-muted">
          {row.updatedAt ? new Date(row.updatedAt).toLocaleString() : '—'}
        </span>
      ),
    },
    {
      key: 'actions' as keyof PlanAccessRow,
      header: 'Actions',
      render: (row) => (
        <div className="flex items-center gap-1">
          {row.effective ? (
            <Button
              variant="ghost"
              size="sm"
              className="h-7 px-2 text-xs"
              disabled={saving}
              onClick={() => setRevokeTarget(row)}
            >
              Disable
            </Button>
          ) : (
            <Button
              variant="ghost"
              size="sm"
              className="h-7 px-2 text-xs"
              disabled={saving}
              onClick={() => applyGrant(row.planCode, true)}
            >
              Enable
            </Button>
          )}
          {row.source === 'override' && (
            <Button
              variant="ghost"
              size="sm"
              className="h-7 w-7 p-0"
              disabled={saving}
              onClick={() => resetOverride(row.planCode)}
              aria-label={`Reset ${row.planName} to catalog`}
              title="Reset to catalog"
            >
              <RotateCcw className="h-3.5 w-3.5" aria-hidden="true" />
            </Button>
          )}
        </div>
      ),
    },
  ];

  return (
    <AdminTableLayout
      title="Companion Plan Access"
      description="Choose which plans unlock the AI Learning Companion. Catalog-managed plans survive deploys; changes here apply immediately and beat the catalog."
      eyebrow="Learning Companion"
      breadcrumbs={breadcrumbs}
      banner={
        <div className="rounded-admin border border-admin-border bg-admin-bg-surface p-4">
          <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
            <h2 className="text-sm font-bold text-admin-fg-strong">
              <Bot className="mr-2 inline-block h-4 w-4 text-admin-fg-muted" />
              {grantedCount} of {rows.length} plans grant access
            </h2>
            <p className="text-xs text-admin-fg-muted">
              Disabling removes the companion for every learner on that plan.
            </p>
          </div>
        </div>
      }
    >
      <AsyncStateWrapper status={status} onRetry={loadAccess}>
        <DataTable
          columns={columns}
          data={rows}
          keyExtractor={(row) => row.planCode}
          emptyMessage="No plans found"
        />
      </AsyncStateWrapper>

      {revokeTarget && (
        <Modal open onClose={() => setRevokeTarget(null)} title="Disable companion access">
          <div className="p-4">
            <p className="text-sm text-admin-fg-strong">
              Turn off the AI Learning Companion for <span className="font-semibold">{revokeTarget.planName}</span>?
            </p>
            <p className="mt-2 text-xs text-admin-fg-muted">
              Learners on this plan will see the upgrade card instead of the chat. You can re-enable it here at any time.
            </p>
            <div className="mt-4 flex justify-end gap-2">
              <Button variant="ghost" size="sm" onClick={() => setRevokeTarget(null)}>Cancel</Button>
              <Button
                variant="destructive"
                size="sm"
                onClick={() => applyGrant(revokeTarget.planCode, false)}
                disabled={saving}
              >
                {saving ? 'Disabling…' : 'Disable access'}
              </Button>
            </div>
          </div>
        </Modal>
      )}
    </AdminTableLayout>
  );
}
