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

const DEFAULT_POLICY: FreezePolicy = {
  isEnabled: false,
  selfServiceEnabled: false,
  approvalMode: 'AutoApprove',
  accessMode: 'ReadOnly',
  minDurationDays: 1,
  maxDurationDays: 365,
  allowScheduling: false,
  entitlementPauseMode: 'InternalClock',
  requireReason: true,
  requireInternalNotes: true,
  allowActivePaid: true,
  allowGracePeriod: true,
  allowTrial: false,
  allowComplimentary: true,
  allowCancelled: false,
  allowExpired: false,
  allowReviewOnly: false,
  allowPastDue: false,
  allowSuspended: false,
  policyNotes: '',
  eligibilityReasonCodesJson: '[]',
};

const approvalModes = ['AutoApprove', 'AdminApprovalRequired'] as const;
const accessModes = ['ReadOnly'] as const;
const entitlementPauseModes = ['InternalClock', 'None'] as const;

function toIsoOrNull(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

function requirePolicyArrayJson(value?: string | null): string | null {
  if (!value?.trim()) return null;
  try {
    const parsed = JSON.parse(value);
    return Array.isArray(parsed) && parsed.every((item) => typeof item === 'string') ? null : 'Eligibility reason codes must be a JSON array of strings.';
  } catch {
    return 'Eligibility reason codes must be valid JSON.';
  }
}

function SelectField({ label, value, options, onChange }: { label: string; value: string; options: readonly string[]; onChange: (value: string) => void }) {
  return (
    <label className="space-y-1 text-sm font-medium text-navy">
      <span>{label}</span>
      <select
        className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/20"
        value={value}
        onChange={(event) => onChange(event.target.value)}
      >
        {options.map((option) => <option key={option} value={option}>{option}</option>)}
      </select>
    </label>
  );
}

function ToggleField({ label, checked, onChange, hint }: { label: string; checked: boolean; onChange: (checked: boolean) => void; hint?: string }) {
  return (
    <label className="flex items-start gap-3 rounded-lg border border-border bg-surface p-3 text-sm text-navy">
      <input
        type="checkbox"
        className="mt-1 h-4 w-4 rounded border-border text-primary focus:ring-primary/20"
        checked={checked}
        onChange={(event) => onChange(event.target.checked)}
      />
      <span>
        <span className="block font-medium">{label}</span>
        {hint ? <span className="mt-1 block text-xs text-muted">{hint}</span> : null}
      </span>
    </label>
  );
}

function TextAreaField({ label, value, onChange, rows = 3, hint }: { label: string; value: string; onChange: (value: string) => void; rows?: number; hint?: string }) {
  return (
    <label className="space-y-1 text-sm font-medium text-navy">
      <span>{label}</span>
      <textarea
        className="w-full rounded-lg border border-border bg-surface px-3 py-2 text-sm text-navy outline-none transition focus:border-primary focus:ring-2 focus:ring-primary/20"
        rows={rows}
        value={value}
        onChange={(event) => onChange(event.target.value)}
      />
      {hint ? <span className="block text-xs font-normal text-muted">{hint}</span> : null}
    </label>
  );
}

export default function AdminFreezePage() {
  const { isAuthenticated, role } = useAdminAuth();
  const searchParams = useSearchParams();
  const initialUserId = searchParams?.get('userId') ?? '';
  const [pageStatus, setPageStatus] = useState<'loading' | 'success' | 'error'>('loading');
  const [overview, setOverview] = useState<AdminFreezeOverview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [actionBusyId, setActionBusyId] = useState<string | null>(null);
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
    setSuccess(null);
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
  const policy: FreezePolicy = policyDraft ?? overview?.policy ?? DEFAULT_POLICY;

  // Safe merger for policy form fields. Uses the current draft, then the
  // loaded policy, then sensible defaults — avoiding non-null assertions
  // and preventing a crash if a user interacts with an input before load.
  const updatePolicy = (patch: Partial<FreezePolicy>) => {
    setPolicyDraft((prev) => ({
      ...(prev ?? overview?.policy ?? DEFAULT_POLICY),
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
    const policyJsonError = requirePolicyArrayJson(policyDraft.eligibilityReasonCodesJson);
    if (policyJsonError) {
      setError(policyJsonError);
      return;
    }

    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      const result = await updateAdminFreezePolicy(policyDraft) as AdminFreezeOverview;
      setOverview(result);
      setPolicyDraft(result.policy);
      setSuccess('Freeze policy updated.');
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

    if (policy.requireReason && !manualReason.trim()) {
      setError('A reason is required for manual freezes under the current policy.');
      return;
    }

    if (policy.requireInternalNotes && !manualNotes.trim()) {
      setError('Internal notes are required for manual freezes under the current policy.');
      return;
    }

    const manualStartDate = manualStartAt ? new Date(manualStartAt) : null;
    const manualEndDate = manualEndAt ? new Date(manualEndAt) : null;
    if ((manualStartDate && Number.isNaN(manualStartDate.getTime())) || (manualEndDate && Number.isNaN(manualEndDate.getTime()))) {
      setError('Choose valid start and end dates for the manual freeze.');
      return;
    }

    if (manualStartDate && manualEndDate && manualEndDate <= manualStartDate) {
      setError('The manual freeze end time must be after the start time.');
      return;
    }

    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      const result = await createAdminManualFreeze({
        userId: manualUserId,
        startAt: toIsoOrNull(manualStartAt),
        endAt: toIsoOrNull(manualEndAt),
        reason: manualReason.trim() || null,
        internalNotes: manualNotes.trim() || null,
        pauseEntitlementClock: manualPauseClock,
        overrideEligibility: manualOverrideEligibility,
      }) as AdminFreezeOverview;
      setOverview(result);
      setManualReason('');
      setManualNotes('');
      setManualStartAt('');
      setManualEndAt('');
      setSuccess('Manual freeze created.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to create manual freeze.');
    } finally {
      setBusy(false);
    }
  };

  const act = async (action: 'approve' | 'reject' | 'end' | 'forceEnd', freezeId: string) => {
    if (policy.requireInternalNotes && !actionNotes.trim()) {
      setError('Internal notes are required before processing freeze lifecycle actions.');
      return;
    }

    setActionBusyId(freezeId);
    setError(null);
    setSuccess(null);
    try {
      const payload = { reason: actionNotes.trim() || null, internalNotes: actionNotes.trim() || null };
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
      setSuccess('Freeze action completed.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Unable to process the freeze action.');
    } finally {
      setActionBusyId(null);
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
            {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}

            <div className="grid gap-4 md:grid-cols-2 xl:grid-cols-4">
              <AdminRouteSummaryCard label="Active" value={counts.active ?? 0} icon={<Shield className="h-5 w-5" />} hint="Currently frozen accounts" />
              <AdminRouteSummaryCard label="Pending" value={counts.pending ?? 0} icon={<Clock3 className="h-5 w-5" />} hint="Awaiting approval" tone={(counts.pending ?? 0) > 0 ? 'warning' : 'default'} />
              <AdminRouteSummaryCard label="Scheduled" value={counts.scheduled ?? 0} icon={<PlayCircle className="h-5 w-5" />} hint="Future-dated freezes" />
              <AdminRouteSummaryCard label="Closed" value={(counts.ended ?? 0) + (counts.cancelled ?? 0) + (counts.rejected ?? 0)} icon={<BadgeCheck className="h-5 w-5" />} hint="Ended, rejected, or cancelled" />
            </div>

            <div className="grid gap-6 lg:grid-cols-2">
              <AdminRoutePanel title="Freeze policy" description="Adjust policy for future eligibility only. Existing records preserve their snapshot.">
                <div className="grid gap-3 sm:grid-cols-2">
                  <ToggleField label="Policy enabled" checked={Boolean(policy.isEnabled)} onChange={(checked) => updatePolicy({ isEnabled: checked })} />
                  <ToggleField label="Self-service enabled" checked={Boolean(policy.selfServiceEnabled)} onChange={(checked) => updatePolicy({ selfServiceEnabled: checked })} />
                  <ToggleField label="Allow scheduling" checked={Boolean(policy.allowScheduling)} onChange={(checked) => updatePolicy({ allowScheduling: checked })} />
                  <ToggleField label="Require learner reason" checked={Boolean(policy.requireReason)} onChange={(checked) => updatePolicy({ requireReason: checked })} />
                  <ToggleField label="Require admin notes" checked={Boolean(policy.requireInternalNotes)} onChange={(checked) => updatePolicy({ requireInternalNotes: checked })} />
                  <ToggleField label="Allow active paid" checked={Boolean(policy.allowActivePaid)} onChange={(checked) => updatePolicy({ allowActivePaid: checked })} />
                  <ToggleField label="Allow grace period" checked={Boolean(policy.allowGracePeriod)} onChange={(checked) => updatePolicy({ allowGracePeriod: checked })} />
                  <ToggleField label="Allow trial" checked={Boolean(policy.allowTrial)} onChange={(checked) => updatePolicy({ allowTrial: checked })} />
                  <ToggleField label="Allow complimentary" checked={Boolean(policy.allowComplimentary)} onChange={(checked) => updatePolicy({ allowComplimentary: checked })} />
                  <ToggleField label="Allow cancelled" checked={Boolean(policy.allowCancelled)} onChange={(checked) => updatePolicy({ allowCancelled: checked })} />
                  <ToggleField label="Allow expired" checked={Boolean(policy.allowExpired)} onChange={(checked) => updatePolicy({ allowExpired: checked })} />
                  <ToggleField label="Allow review-only" checked={Boolean(policy.allowReviewOnly)} onChange={(checked) => updatePolicy({ allowReviewOnly: checked })} />
                  <ToggleField label="Allow past due" checked={Boolean(policy.allowPastDue)} onChange={(checked) => updatePolicy({ allowPastDue: checked })} />
                  <ToggleField label="Allow suspended" checked={Boolean(policy.allowSuspended)} onChange={(checked) => updatePolicy({ allowSuspended: checked })} />
                </div>
                <div className="mt-4 grid gap-4 sm:grid-cols-2">
                  <SelectField label="Approval mode" value={policy.approvalMode ?? 'AutoApprove'} options={approvalModes} onChange={(value) => updatePolicy({ approvalMode: value })} />
                  <SelectField label="Access mode" value={policy.accessMode ?? 'ReadOnly'} options={accessModes} onChange={(value) => updatePolicy({ accessMode: value })} />
                  <SelectField label="Entitlement pause mode" value={policy.entitlementPauseMode ?? 'InternalClock'} options={entitlementPauseModes} onChange={(value) => updatePolicy({ entitlementPauseMode: value })} />
                  <Input label="Min days" type="number" value={String(policyDraft?.minDurationDays ?? 1)} onChange={(event) => updatePolicy({ minDurationDays: Number(event.target.value) })} />
                  <Input label="Max days" type="number" value={String(policyDraft?.maxDurationDays ?? 365)} onChange={(event) => updatePolicy({ maxDurationDays: Number(event.target.value) })} />
                </div>
                <div className="mt-4 space-y-4">
                  <TextAreaField label="Policy notes" value={policy.policyNotes ?? ''} onChange={(value) => updatePolicy({ policyNotes: value })} />
                  <TextAreaField label="Eligibility reason codes JSON" value={policy.eligibilityReasonCodesJson ?? '[]'} onChange={(value) => updatePolicy({ eligibilityReasonCodesJson: value })} hint="Must be a JSON array of strings." />
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
                </div>
                <div className="mt-4 grid gap-3 sm:grid-cols-2">
                  <ToggleField label="Pause entitlement clock" checked={manualPauseClock} onChange={setManualPauseClock} />
                  <ToggleField label="Override eligibility" checked={manualOverrideEligibility} onChange={setManualOverrideEligibility} hint="Use only for staff-authorized exceptions." />
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
                          <Button size="sm" onClick={() => act('approve', record.id)} loading={actionBusyId === record.id}>Approve</Button>
                          <Button size="sm" variant="outline" onClick={() => act('reject', record.id)} loading={actionBusyId === record.id}>Reject</Button>
                        </>
                      ) : null}
                      {status === 'scheduled' || status === 'active' ? (
                        <>
                          <Button size="sm" variant="outline" onClick={() => act('end', record.id)} loading={actionBusyId === record.id}>End</Button>
                          <Button size="sm" variant="destructive" onClick={() => act('forceEnd', record.id)} loading={actionBusyId === record.id}>Force end</Button>
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
