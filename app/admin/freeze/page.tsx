'use client';

import { useEffect, useMemo, useState } from 'react';
import { Shield, BadgeCheck, PlayCircle, SquarePen, Clock3, Users } from 'lucide-react';
import { useSearchParams } from 'next/navigation';
import {
  AdminRouteHero,
  AdminRoutePanel,
  AdminRouteSummaryCard,
  AdminRouteWorkspace,
} from '@/components/domain/admin-route-surface';
import { AdminQuickAction } from '@/components/domain/admin-quick-action';
import { AsyncStateWrapper } from '@/components/state/async-state-wrapper';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Input } from '@/components/ui/form-controls';
import { fetchAdminFreezeOverview, updateAdminFreezePolicy, createAdminManualFreeze, approveAdminFreeze, rejectAdminFreeze, endAdminFreeze, forceEndAdminFreeze } from '@/lib/api';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { analytics } from '@/lib/analytics';
import type { AdminFreezeOverview, FreezePolicy, FreezeCounts } from '@/lib/types/freeze';

function boolToString(value: boolean): 'true' | 'false' {
  return value ? 'true' : 'false';
}

export default function AdminFreezePage() {
  const { isAuthenticated, role } = useAdminAuth();
  const searchParams = useSearchParams();
  const initialUserId = searchParams.get('userId') ?? '';
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [overview, setOverview] = useState<AdminFreezeOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [policyDraft, setPolicyDraft] = useState<FreezePolicy | null>(null);
  const [manualUserId, setManualUserId] = useState(initialUserId);
  const [manualStartAt, setManualStartAt] = useState('');
  const [manualEndAt, setManualEndAt] = useState('');
  const [manualReason, setManualReason] = useState('');
  const [manualNotes, setManualNotes] = useState('');
  const [manualOverrideEligibility, setManualOverrideEligibility] = useState(false);
  const [manualPauseClock, setManualPauseClock] = useState(true);
  const [actionNotes, setActionNotes] = useState('');

  const load = async () => {
    setPageStatus('loading');
    setError(null);
    try {
      const result = await fetchAdminFreezeOverview() as AdminFreezeOverview;
      setOverview(result);
      setPolicyDraft(result.policy);
      setPageStatus('success');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to load freeze overview.');
      setPageStatus('error');
    }
  };

  useEffect(() => {
    analytics.track('content_view', { page: 'admin_freeze' });
    void load();
  }, []);

  const records = overview?.records ?? [];
  const counts: FreezeCounts = overview?.counts ?? { active: 0, pending: 0, scheduled: 0, ended: 0 };
  const policy: FreezePolicy = policyDraft ?? overview?.policy ?? { isEnabled: false, selfServiceEnabled: false, approvalMode: 'AutoApprove', accessMode: 'ReadOnly', minDurationDays: 1, maxDurationDays: 365 };

  // Safe merger for policy form fields. Uses the current draft, then the
  // loaded policy, then sensible defaults — avoiding non-null assertions
  // and preventing a crash if a user interacts with an input before load.
  const updatePolicy = (patch: Partial<FreezePolicy>) => {
    setPolicyDraft((prev) => ({
      ...(prev ?? overview?.policy ?? {
        isEnabled: false,
        selfServiceEnabled: false,
        approvalMode: 'AutoApprove',
        accessMode: 'ReadOnly',
        minDurationDays: 1,
        maxDurationDays: 365,
      }),
      ...patch,
    }));
  };

  const policyHighlights = useMemo(
    () => [
      { icon: Users, label: 'Self-service', value: policy.selfServiceEnabled ? 'Enabled' : 'Disabled' },
      { icon: Clock3, label: 'Max duration', value: `${policy.maxDurationDays ?? 365} days` },
    ],
    [policy.maxDurationDays, policy.selfServiceEnabled],
  );

  const savePolicy = async () => {
    if (!policyDraft) return;
    setBusy(true);
    setError(null);
    try {
      const result = await updateAdminFreezePolicy(policyDraft) as AdminFreezeOverview;
      setOverview(result);
      setPolicyDraft(result.policy);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to update freeze policy.');
    } finally {
      setBusy(false);
    }
  };

  const createManual = async () => {
    if (!manualUserId) {
      setError('A user id is required for a manual freeze.');
      return;
    }

    setBusy(true);
    setError(null);
    try {
      const result = await createAdminManualFreeze({
        userId: manualUserId,
        startAt: manualStartAt ? new Date(manualStartAt).toISOString() : null,
        endAt: manualEndAt ? new Date(manualEndAt).toISOString() : null,
        reason: manualReason || null,
        internalNotes: manualNotes || null,
        pauseEntitlementClock: manualPauseClock,
        overrideEligibility: manualOverrideEligibility,
      }) as AdminFreezeOverview;
      setOverview(result);
      setManualReason('');
      setManualNotes('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to create manual freeze.');
    } finally {
      setBusy(false);
    }
  };

  const act = async (action: 'approve' | 'reject' | 'end' | 'forceEnd', freezeId: string) => {
    setBusy(true);
    setError(null);
    try {
      const payload = { reason: actionNotes || null, internalNotes: actionNotes || null };
      const result =
        action === 'approve'
          ? await approveAdminFreeze(freezeId, payload)
          : action === 'reject'
            ? await rejectAdminFreeze(freezeId, payload)
            : action === 'end'
              ? await endAdminFreeze(freezeId, payload)
              : await forceEndAdminFreeze(freezeId, payload);
      setOverview(result as AdminFreezeOverview);
      setActionNotes('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to process the freeze action.');
    } finally {
      setBusy(false);
    }
  };

  if (!isAuthenticated || role !== 'admin') return null;

  return (
    <AdminRouteWorkspace role="main" aria-label="Freeze operations">
      <AsyncStateWrapper status={pageStatus} onRetry={() => window.location.reload()} errorMessage={error ?? undefined}>
        {overview ? (
          <div className="space-y-6">
            <AdminRouteHero
              eyebrow="Freeze Center"
              icon={Shield}
              accent="navy"
              title="Control learner freezes without touching billing records"
              description="Use this workspace to update policy, create manual freezes, and approve or end existing requests with an immutable audit trail."
              highlights={policyHighlights}
              aside={(
                <div className="space-y-3 rounded-2xl border border-border bg-background-light p-4 shadow-sm">
                  <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Quick Links</p>
                  <div className="space-y-2">
                    <AdminQuickAction href="/admin/users" label="User directory" icon={SquarePen} />
                    <AdminQuickAction href="/freeze" label="Learner freeze page" icon={PlayCircle} />
                  </div>
                </div>
              )}
            />

            {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard label="Active" value={counts.active ?? 0} icon={<Shield className="h-5 w-5" />} hint="Currently frozen accounts" />
              <AdminRouteSummaryCard label="Pending" value={counts.pending ?? 0} icon={<Clock3 className="h-5 w-5" />} hint="Awaiting approval" tone={(counts.pending ?? 0) > 0 ? 'warning' : 'default'} />
              <AdminRouteSummaryCard label="Scheduled" value={counts.scheduled ?? 0} icon={<PlayCircle className="h-5 w-5" />} hint="Future-dated freezes" />
              <AdminRouteSummaryCard label="Ended" value={counts.ended ?? 0} icon={<BadgeCheck className="h-5 w-5" />} hint="Completed in window" />
            </div>

            <div className="grid gap-6 lg:grid-cols-2">
              <AdminRoutePanel title="Freeze policy" description="Adjust policy for future eligibility only. Existing records preserve their snapshot.">
                <div className="grid gap-4 sm:grid-cols-2">
                  <Input label="Enabled" value={boolToString(Boolean(policyDraft?.isEnabled))} onChange={(event) => updatePolicy({ isEnabled: event.target.value === 'true' })} />
                  <Input label="Self-service" value={boolToString(Boolean(policyDraft?.selfServiceEnabled))} onChange={(event) => updatePolicy({ selfServiceEnabled: event.target.value === 'true' })} />
                  <Input label="Approval mode" value={policyDraft?.approvalMode ?? 'AutoApprove'} onChange={(event) => updatePolicy({ approvalMode: event.target.value })} />
                  <Input label="Access mode" value={policyDraft?.accessMode ?? 'ReadOnly'} onChange={(event) => updatePolicy({ accessMode: event.target.value })} />
                  <Input label="Min days" type="number" value={String(policyDraft?.minDurationDays ?? 1)} onChange={(event) => updatePolicy({ minDurationDays: Number(event.target.value) })} />
                  <Input label="Max days" type="number" value={String(policyDraft?.maxDurationDays ?? 365)} onChange={(event) => updatePolicy({ maxDurationDays: Number(event.target.value) })} />
                </div>
                <div className="mt-4 flex justify-end">
                  <Button onClick={savePolicy} loading={busy}>Save policy</Button>
                </div>
              </AdminRoutePanel>

              <AdminRoutePanel title="Manual freeze" description="Create a freeze directly for a learner with optional overrides.">
                <div className="grid gap-4 sm:grid-cols-2">
                  <Input label="User ID" value={manualUserId} onChange={(event) => setManualUserId(event.target.value)} />
                  <Input label="Start at" type="datetime-local" value={manualStartAt} onChange={(event) => setManualStartAt(event.target.value)} />
                  <Input label="End at" type="datetime-local" value={manualEndAt} onChange={(event) => setManualEndAt(event.target.value)} />
                  <Input label="Reason" value={manualReason} onChange={(event) => setManualReason(event.target.value)} />
                  <Input label="Internal notes" value={manualNotes} onChange={(event) => setManualNotes(event.target.value)} />
                  <Input label="Pause entitlement clock" value={boolToString(manualPauseClock)} onChange={(event) => setManualPauseClock(event.target.value === 'true')} />
                  <Input label="Override eligibility" value={boolToString(manualOverrideEligibility)} onChange={(event) => setManualOverrideEligibility(event.target.value === 'true')} />
                </div>
                <div className="mt-4 flex justify-end">
                  <Button onClick={createManual} loading={busy}>Create freeze</Button>
                </div>
              </AdminRoutePanel>
            </div>

            <AdminRoutePanel title="Lifecycle actions" description="Approve, reject, end, or force-end existing freeze records.">
              <div className="mb-4">
                <Input label="Action notes" value={actionNotes} onChange={(event) => setActionNotes(event.target.value)} hint="Optional reason or notes to carry into the audit trail." />
              </div>
              <div className="space-y-3">
                {records.length > 0 ? records.map((record) => {
                  const status = String(record.status ?? '').toLowerCase();
                  return (
                  <div key={record.id} className="flex flex-col gap-3 rounded-2xl border border-border bg-surface p-4 md:flex-row md:items-center md:justify-between">
                    <div>
                      <p className="text-sm font-semibold text-navy">{record.id}</p>
                      <p className="text-xs text-muted">
                        {record.userId} · {record.status} · requested {record.requestedAt ? new Date(record.requestedAt).toLocaleString() : 'unknown time'}
                      </p>
                    </div>
                    <div className="flex flex-wrap gap-2">
                      {status === 'pendingapproval' ? (
                        <>
                          <Button size="sm" onClick={() => act('approve', record.id)} loading={busy}>Approve</Button>
                          <Button size="sm" variant="outline" onClick={() => act('reject', record.id)} loading={busy}>Reject</Button>
                        </>
                      ) : null}
                      {status === 'scheduled' || status === 'active' ? (
                        <>
                          <Button size="sm" variant="outline" onClick={() => act('end', record.id)} loading={busy}>End</Button>
                          <Button size="sm" variant="destructive" onClick={() => act('forceEnd', record.id)} loading={busy}>Force end</Button>
                        </>
                      ) : null}
                    </div>
                  </div>
                );
                }) : (
                  <div className="rounded-2xl border border-dashed border-border bg-background-light p-4 text-sm text-muted">
                    No freeze records found.
                  </div>
                )}
              </div>
            </AdminRoutePanel>
          </div>
        ) : null}
      </AsyncStateWrapper>
    </AdminRouteWorkspace>
  );
}
