'use client';

import { useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { CalendarClock, CheckCircle2, Shield, Timer } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button, Card, CardContent, CardHeader, CardTitle } from '@/components/ui';
import { Input } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { cancelFreeze, confirmFreeze, fetchFreezeStatus, requestFreeze } from '@/lib/api';
import type { LearnerFreezeStatus, FreezePolicy, FreezeEligibility } from '@/lib/types/freeze';

function toLocalInputValue(value: string | null | undefined): string {
  if (!value) return '';
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) return '';
  const offset = date.getTimezoneOffset() * 60_000;
  return new Date(date.getTime() - offset).toISOString().slice(0, 16);
}

function toIsoOrNull(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

export default function FreezePage() {
  const [freezeState, setFreezeState] = useState<LearnerFreezeStatus | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [success, setSuccess] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);
  const [startAt, setStartAt] = useState('');
  const [endAt, setEndAt] = useState('');
  const [reason, setReason] = useState('');

  useEffect(() => {
    analytics.track('content_view', { page: 'freeze' });
    let cancelled = false;

    (async () => {
      try {
        const result = await fetchFreezeStatus();
        if (!cancelled) {
          setFreezeState(result as LearnerFreezeStatus);
        }
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load freeze status.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, []);

  const policy: FreezePolicy = freezeState?.policy ?? { isEnabled: false, selfServiceEnabled: false, approvalMode: '', accessMode: '', minDurationDays: 0, maxDurationDays: 365 };
  const currentFreeze = freezeState?.currentFreeze ?? null;
  const eligibility: FreezeEligibility = freezeState?.eligibility ?? { eligible: false };
  const history = freezeState?.history ?? [];
  const isSelfServiceAvailable = Boolean(policy.selfServiceEnabled && policy.isEnabled);
  const canRequest = Boolean(isSelfServiceAvailable && !currentFreeze && eligibility.eligible !== false);

  const highlights = useMemo(
    () => [
      { icon: Shield, label: 'Mode', value: currentFreeze ? 'Frozen' : 'Available' },
      { icon: Timer, label: 'Max duration', value: `${policy.maxDurationDays ?? 365} days` },
      { icon: CheckCircle2, label: 'Self-service', value: policy.selfServiceEnabled ? 'Enabled' : 'Disabled' },
      { icon: CalendarClock, label: 'History', value: `${history.length} records` },
    ],
    [currentFreeze, history.length, policy.maxDurationDays, policy.selfServiceEnabled],
  );

  const submitRequest = async () => {
    if (!canRequest) {
      setError('A new freeze request is not available right now.');
      return;
    }

    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await requestFreeze({
        startAt: toIsoOrNull(startAt),
        endAt: toIsoOrNull(endAt),
        reason: reason.trim() || null,
        pauseEntitlementClock: String(policy.entitlementPauseMode ?? '').toLowerCase().includes('internal') ? true : null,
      });
      setSuccess('Freeze request submitted.');
      const refreshed = await fetchFreezeStatus();
      setFreezeState(refreshed as LearnerFreezeStatus);
      setReason('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not submit freeze request.');
    } finally {
      setBusy(false);
    }
  };

  const confirmCurrentFreeze = async () => {
    if (!currentFreeze?.id) return;
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await confirmFreeze(currentFreeze.id);
      const refreshed = await fetchFreezeStatus();
      setFreezeState(refreshed as LearnerFreezeStatus);
      setSuccess('Freeze confirmed.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not confirm the freeze.');
    } finally {
      setBusy(false);
    }
  };

  const cancelCurrentFreeze = async () => {
    if (!currentFreeze?.id) return;
    setBusy(true);
    setError(null);
    setSuccess(null);
    try {
      await cancelFreeze(currentFreeze.id);
      const refreshed = await fetchFreezeStatus();
      setFreezeState(refreshed as LearnerFreezeStatus);
      setSuccess('Freeze cancelled.');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not cancel the freeze.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <LearnerDashboardShell pageTitle="Freeze Center" backHref="/">
      <div className="space-y-8">
        <LearnerPageHero
          eyebrow="Account Freeze"
          icon={Shield}
          accent="primary"
          title="Pause learner access without touching billing history"
          description="Use this area to request, confirm, or review freezes while keeping subscription records intact and the learner workspace in a clear read-only state."
          highlights={highlights}
        />

        {loading ? <InlineAlert variant="info">Loading freeze status...</InlineAlert> : null}
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
        {success ? <InlineAlert variant="success">{success}</InlineAlert> : null}
        {currentFreeze ? (
          <InlineAlert variant="warning">
            This learner is currently frozen. Study mutations are paused until the freeze ends.
          </InlineAlert>
        ) : null}

        <section className="grid gap-6 lg:grid-cols-[1.05fr_0.95fr]">
          <Card className="shadow-sm">
            <CardHeader>
              <CardTitle>Freeze request</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <p className="text-sm text-muted">
                {canRequest
                  ? 'Create a one-time self-service freeze request. The backend will validate duration and policy eligibility.'
                  : 'Self-service freeze request is unavailable for the current account state or policy settings.'}
              </p>

              <div className="grid gap-4 sm:grid-cols-2">
                <Input
                  label="Start at"
                  type="datetime-local"
                  value={startAt}
                  onChange={(event) => setStartAt(event.target.value)}
                  hint="Leave blank to start immediately."
                />
                <Input
                  label="End at"
                  type="datetime-local"
                  value={endAt}
                  onChange={(event) => setEndAt(event.target.value)}
                  hint={`Maximum duration: ${policy.maxDurationDays ?? 365} days.`}
                />
              </div>

              <Input
                label="Reason"
                value={reason}
                onChange={(event) => setReason(event.target.value)}
                placeholder="Need a short pause for travel / recovery / exam preparation"
                hint="A clear reason helps admins and your future self understand the freeze history."
              />

              <div className="flex flex-wrap gap-3">
                <Button onClick={submitRequest} loading={busy} disabled={!canRequest}>
                  Submit Freeze Request
                </Button>
                <Link
                  href="/"
                  className="inline-flex items-center justify-center rounded-lg border border-gray-300 px-4 py-3 text-sm font-medium text-navy transition-colors hover:bg-gray-50"
                >
                  Back to dashboard
                </Link>
              </div>
            </CardContent>
          </Card>

          <Card className="shadow-sm">
            <CardHeader>
              <CardTitle>Current status</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              {currentFreeze ? (
                <div className="space-y-3 rounded-2xl border border-gray-200 bg-background-light p-4">
                  <div className="flex items-center justify-between gap-3">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Freeze ID</p>
                      <p className="mt-1 text-sm font-semibold text-navy">{currentFreeze.id}</p>
                    </div>
                    <span className="rounded-full bg-amber-100 px-3 py-1 text-xs font-bold uppercase tracking-[0.16em] text-amber-800">
                      {currentFreeze.status}
                    </span>
                  </div>
                  <div className="grid gap-3 sm:grid-cols-2">
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Started</p>
                      <p className="mt-1 text-sm text-navy">{currentFreeze.startedAt ? new Date(currentFreeze.startedAt).toLocaleString() : 'Pending'}</p>
                    </div>
                    <div>
                      <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Ends</p>
                      <p className="mt-1 text-sm text-navy">{currentFreeze.endedAt ? new Date(currentFreeze.endedAt).toLocaleString() : 'Not set'}</p>
                    </div>
                  </div>
                  <div className="flex flex-wrap gap-3">
                    <Button onClick={confirmCurrentFreeze} loading={busy} disabled={!currentFreeze}>
                      Confirm
                    </Button>
                    <Button variant="outline" onClick={cancelCurrentFreeze} loading={busy} disabled={!currentFreeze}>
                      Cancel
                    </Button>
                  </div>
                </div>
              ) : (
                <div className="rounded-2xl border border-dashed border-gray-200 bg-background-light p-4 text-sm text-muted">
                  No active freeze is currently applied. Requests will appear here after they are submitted or approved.
                </div>
              )}

              <div className="rounded-2xl border border-gray-200 bg-surface p-4">
                <p className="text-xs font-semibold uppercase tracking-[0.16em] text-muted">Eligibility</p>
                <p className="mt-2 text-sm text-navy">
                  {eligibility.eligible === false
                    ? eligibility.reason ?? 'This account is not eligible under the current policy.'
                    : 'This account can request a freeze under the current policy.'}
                </p>
              </div>
            </CardContent>
          </Card>
        </section>

        <section>
          <LearnerSurfaceSectionHeader
            eyebrow="History"
            title="Freeze history remains immutable"
            description="Previous freeze requests, approvals, cancellations, and endings stay visible as a permanent audit trail."
            className="mb-4"
          />
          <div className="grid gap-4">
            {history.length > 0 ? history.map((record) => (
              <Card key={record.id} className="shadow-sm">
                <CardContent className="flex flex-col gap-3 p-5 sm:flex-row sm:items-center sm:justify-between">
                  <div>
                    <p className="text-sm font-semibold text-navy">{record.id}</p>
                    <p className="text-xs text-muted">
                      {record.status} · requested {record.requestedAt ? new Date(record.requestedAt).toLocaleString() : 'unknown time'}
                    </p>
                  </div>
                  <div className="text-sm text-muted">
                    {record.startedAt ? `Started ${new Date(record.startedAt).toLocaleString()}` : 'Pending start'}
                    {record.endedAt ? ` · Ended ${new Date(record.endedAt).toLocaleString()}` : ''}
                  </div>
                </CardContent>
              </Card>
            )) : (
              <Card className="shadow-sm">
                <CardContent className="p-5 text-sm text-muted">
                  No freeze history has been recorded for this learner yet.
                </CardContent>
              </Card>
            )}
          </div>
        </section>
      </div>
    </LearnerDashboardShell>
  );
}
