'use client';

import { useCallback, useEffect, useState } from 'react';
import { Bot, RotateCcw, Search, ShieldCheck, UserCheck, UserCog, UserX } from 'lucide-react';
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

/** SAMI §9 — one learner's effective companion state and the override behind it. */
interface UserAccessRow {
  userId: string;
  effective: boolean;
  source: string | null;
  reason: string;
  planCode: string | null;
  planName: string | null;
  overrideEnabled: boolean | null;
  overrideSource: string | null;
  overrideExpiresAt: string | null;
  overrideNote: string | null;
  updatedByAdminId: string | null;
  updatedAt: string | null;
}

type PageStatus = 'loading' | 'success' | 'error';
type UserStatus = 'idle' | 'loading' | 'success' | 'error';

const SOURCE_LABELS: Record<string, string> = {
  package_included: 'Included in package',
  admin_enabled: 'Enabled by admin',
  promotional: 'Promotional grant',
  manually_disabled: 'Disabled by admin',
  expired: 'Grant expired',
  none: 'No access',
};

const REASON_LABELS: Record<string, string> = {
  ok: 'Allowed',
  package_required: 'No package includes it',
  plan_excludes_companion: 'Plan excludes the companion',
  ai_disabled: 'AI disabled for this account',
  kill_switch: 'Emergency kill switch active',
  policy_unavailable: 'Policy could not be read',
  companion_disabled: 'Companion switched off platform-wide',
  manually_disabled: 'Disabled by an admin',
  expired: 'Grant expired',
};

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

  // ── Per-learner lookup (SAMI §9) ──────────────────────────────────────────
  const [userQuery, setUserQuery] = useState('');
  const [userRow, setUserRow] = useState<UserAccessRow | null>(null);
  const [userStatus, setUserStatus] = useState<UserStatus>('idle');
  const [userError, setUserError] = useState<string | null>(null);
  const [grantNote, setGrantNote] = useState('');
  const [grantExpiry, setGrantExpiry] = useState('');
  const [userSaving, setUserSaving] = useState(false);

  const lookupUser = useCallback(async (userId: string) => {
    const id = userId.trim();
    if (!id) return;
    try {
      setUserStatus('loading');
      setUserError(null);
      const data = await apiClient.get<UserAccessRow>(
        `/v1/admin/companion/access/users/${encodeURIComponent(id)}`,
      );
      setUserRow(data);
      setUserStatus('success');
    } catch (err) {
      setUserRow(null);
      setUserStatus('error');
      setUserError(err instanceof Error ? err.message : 'Could not read that learner.');
    }
  }, []);

  /** Apply or clear the per-learner override, then re-read the effective state. */
  const applyUserOverride = useCallback(async (
    userId: string,
    body: { enabled: boolean; source?: string; expiresAt?: string; note?: string } | null,
  ) => {
    try {
      setUserSaving(true);
      setUserError(null);
      const path = `/v1/admin/companion/access/users/${encodeURIComponent(userId)}`;
      if (body === null) {
        await apiClient.delete(path);
      } else {
        await apiClient.post(path, body);
      }
      await lookupUser(userId);
    } catch (err) {
      setUserError(err instanceof Error ? err.message : 'Could not save that change.');
    } finally {
      setUserSaving(false);
    }
  }, [lookupUser]);

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

      {/* ── Per-learner access (SAMI §9) ────────────────────────────────────
          Eligibility auto-enables from the package; this is the administrative
          override for the cases the package rule cannot cover — a learner who
          should have Sami without the package, or one who must be stopped
          despite having it. */}
      <div className="mt-6 rounded-admin border border-admin-border bg-admin-bg-surface p-4">
        <h2 className="text-sm font-bold text-admin-fg-strong">
          <UserCog className="mr-2 inline-block h-4 w-4 text-admin-fg-muted" />
          Per-learner access
        </h2>
        <p className="mt-1 text-xs text-admin-fg-muted">
          An eligible package enables Sami automatically. Use this to override a single
          learner either way; the override beats the package rule in both directions.
        </p>

        <div className="mt-3 flex flex-col gap-2 sm:flex-row sm:items-end">
          <label className="min-w-0 flex-1">
            <span className="block text-xs font-medium text-admin-fg-muted">Learner user ID</span>
            <input
              type="text"
              value={userQuery}
              onChange={(e) => setUserQuery(e.target.value)}
              onKeyDown={(e) => { if (e.key === 'Enter') void lookupUser(userQuery); }}
              placeholder="learner_…"
              className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong"
            />
          </label>
          <Button
            variant="outline"
            size="sm"
            disabled={userStatus === 'loading' || !userQuery.trim()}
            onClick={() => void lookupUser(userQuery)}
          >
            <Search className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
            {userStatus === 'loading' ? 'Reading…' : 'Check access'}
          </Button>
        </div>

        {userError && (
          <p className="mt-3 text-xs text-red-700 dark:text-red-300" role="alert">{userError}</p>
        )}

        {userRow && (
          <div className="mt-4 border-t border-admin-border pt-4">
            <div className="flex flex-wrap items-center gap-2">
              <Badge variant={userRow.effective ? 'success' : 'default'} intensity="tinted" size="sm">
                {userRow.effective ? 'Sami available' : 'Sami blocked'}
              </Badge>
              <Badge variant="default" intensity="tinted" size="sm">
                {SOURCE_LABELS[userRow.source ?? 'none'] ?? userRow.source ?? 'No access'}
              </Badge>
              <span className="text-xs text-admin-fg-muted">
                {REASON_LABELS[userRow.reason] ?? userRow.reason}
                {userRow.planName ? ` · ${userRow.planName}` : ''}
              </span>
            </div>

            <dl className="mt-3 grid grid-cols-1 gap-x-6 gap-y-1 text-xs sm:grid-cols-2">
              <div className="flex justify-between gap-2">
                <dt className="text-admin-fg-muted">User</dt>
                <dd className="truncate text-admin-fg-strong">{userRow.userId}</dd>
              </div>
              <div className="flex justify-between gap-2">
                <dt className="text-admin-fg-muted">Manual override</dt>
                <dd className="text-admin-fg-strong">
                  {userRow.overrideEnabled === null
                    ? 'None'
                    : userRow.overrideEnabled ? 'Enabled' : 'Disabled'}
                </dd>
              </div>
              {userRow.overrideSource && (
                <div className="flex justify-between gap-2">
                  <dt className="text-admin-fg-muted">Override source</dt>
                  <dd className="text-admin-fg-strong">
                    {SOURCE_LABELS[userRow.overrideSource] ?? userRow.overrideSource}
                  </dd>
                </div>
              )}
              {userRow.overrideExpiresAt && (
                <div className="flex justify-between gap-2">
                  <dt className="text-admin-fg-muted">Expires</dt>
                  <dd className="text-admin-fg-strong">
                    {new Date(userRow.overrideExpiresAt).toLocaleString()}
                  </dd>
                </div>
              )}
              {userRow.updatedByAdminId && (
                <div className="flex justify-between gap-2">
                  <dt className="text-admin-fg-muted">Last changed by</dt>
                  <dd className="truncate text-admin-fg-strong">{userRow.updatedByAdminId}</dd>
                </div>
              )}
              {userRow.overrideNote && (
                <div className="flex justify-between gap-2 sm:col-span-2">
                  <dt className="text-admin-fg-muted">Note</dt>
                  <dd className="truncate text-admin-fg-strong">{userRow.overrideNote}</dd>
                </div>
              )}
            </dl>

            {userRow.overrideEnabled === null && (
              <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-2">
                <label>
                  <span className="block text-xs font-medium text-admin-fg-muted">
                    Grant note (optional)
                  </span>
                  <input
                    type="text"
                    value={grantNote}
                    onChange={(e) => setGrantNote(e.target.value)}
                    placeholder="Why this learner is being granted access"
                    className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong"
                  />
                </label>
                <label>
                  <span className="block text-xs font-medium text-admin-fg-muted">
                    Expires (optional)
                  </span>
                  <input
                    type="date"
                    value={grantExpiry}
                    onChange={(e) => setGrantExpiry(e.target.value)}
                    className="mt-1 w-full rounded-admin border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm text-admin-fg-strong"
                  />
                </label>
              </div>
            )}

            <div className="mt-3 flex flex-wrap items-center gap-2">
              {userRow.overrideEnabled === null ? (
                <>
                  <Button
                    size="sm"
                    disabled={userSaving || userRow.effective}
                    onClick={() => void applyUserOverride(userRow.userId, {
                      enabled: true,
                      source: 'admin_enabled',
                      ...(grantExpiry ? { expiresAt: new Date(`${grantExpiry}T23:59:59Z`).toISOString() } : {}),
                      ...(grantNote.trim() ? { note: grantNote.trim() } : {}),
                    })}
                  >
                    <UserCheck className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                    Enable for this learner
                  </Button>
                  <Button
                    variant="destructive"
                    size="sm"
                    disabled={userSaving || !userRow.effective}
                    onClick={() => void applyUserOverride(userRow.userId, { enabled: false })}
                  >
                    <UserX className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                    Disable for this learner
                  </Button>
                </>
              ) : (
                <Button
                  variant="outline"
                  size="sm"
                  disabled={userSaving}
                  onClick={() => void applyUserOverride(userRow.userId, null)}
                >
                  <RotateCcw className="mr-1.5 h-3.5 w-3.5" aria-hidden="true" />
                  Clear override (back to package rule)
                </Button>
              )}
              <p className="text-xs text-admin-fg-muted">
                A learner whose package already includes Sami needs no override.
              </p>
            </div>
          </div>
        )}
      </div>

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
