'use client';

import { useMemo, useState } from 'react';
import { Snowflake } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Input, Select, Textarea } from '@/components/ui/form-controls';
import { requestFreeze } from '@/lib/api';
import { describeFreezeReasonCodes } from '@/lib/freeze-copy';
import type { LearnerFreezeStatus } from '@/lib/types/freeze';

interface FreezeRequestModalProps {
  open: boolean;
  onClose: () => void;
  freezeState: LearnerFreezeStatus | null;
  /** Called after a freeze request succeeds so the parent can refresh status. */
  onCompleted: () => void | Promise<void>;
}

const MONTH_DAYS = 30;

/** Duration presets, in days. Clamped to the policy max at render time. */
const DURATION_PRESETS: Array<{ value: string; label: string; days: number }> = [
  { value: '1m', label: '1 month', days: MONTH_DAYS },
  { value: '3m', label: '3 months', days: 3 * MONTH_DAYS },
  { value: '6m', label: '6 months', days: 6 * MONTH_DAYS },
  { value: '12m', label: '12 months (max)', days: 365 },
  { value: 'custom', label: 'Custom…', days: 0 },
];

function toIsoOrNull(value: string): string | null {
  if (!value) return null;
  const date = new Date(value);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

export function FreezeRequestModal({ open, onClose, freezeState, onCompleted }: FreezeRequestModalProps) {
  const policy = freezeState?.policy ?? null;
  const eligibility = freezeState?.eligibility ?? null;
  const currentFreeze = freezeState?.currentFreeze ?? null;
  const entitlementUsed = freezeState?.entitlement?.used === true;

  const maxDurationDays = Math.min(policy?.maxDurationDays ?? 365, 365);
  const minDurationDays = Math.max(1, policy?.minDurationDays ?? 1);
  const selfServiceAvailable = Boolean(policy?.selfServiceEnabled && policy?.isEnabled);
  const canSchedule = Boolean(eligibility?.canSchedule ?? policy?.allowScheduling);
  const reasonRequired = Boolean(policy?.requireReason);
  const canRequest = Boolean(
    selfServiceAvailable &&
      !currentFreeze &&
      !entitlementUsed &&
      eligibility?.eligible !== false &&
      eligibility?.canRequest !== false,
  );

  const [preset, setPreset] = useState('3m');
  const [customDays, setCustomDays] = useState(String(Math.min(90, maxDurationDays)));
  const [startAt, setStartAt] = useState('');
  const [reason, setReason] = useState('');
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const eligibilityMessages = useMemo(
    () => describeFreezeReasonCodes(eligibility?.reasonCodes),
    [eligibility?.reasonCodes],
  );

  const presetOptions = useMemo(
    () =>
      DURATION_PRESETS.filter((p) => p.value === 'custom' || p.days <= maxDurationDays).map((p) => ({
        value: p.value,
        label: p.label,
      })),
    [maxDurationDays],
  );

  const resolvedDays = useMemo(() => {
    if (preset === 'custom') {
      const parsed = Number(customDays);
      return Number.isFinite(parsed) ? Math.round(parsed) : NaN;
    }
    return DURATION_PRESETS.find((p) => p.value === preset)?.days ?? NaN;
  }, [preset, customDays]);

  const submit = async () => {
    setError(null);

    if (!canRequest) {
      setError('A new freeze is not available for your account right now.');
      return;
    }
    if (!Number.isFinite(resolvedDays) || resolvedDays < minDurationDays || resolvedDays > maxDurationDays) {
      setError(`Choose a duration between ${minDurationDays} and ${maxDurationDays} days.`);
      return;
    }
    if (startAt && !canSchedule) {
      setError('Future-dated freezes are not enabled under the current policy.');
      return;
    }
    if (reasonRequired && !reason.trim()) {
      setError('Please add a reason for this freeze.');
      return;
    }

    const start = startAt ? new Date(startAt) : new Date();
    if (Number.isNaN(start.getTime())) {
      setError('Choose a valid start date.');
      return;
    }
    const end = new Date(start.getTime() + resolvedDays * 24 * 60 * 60 * 1000);

    setBusy(true);
    try {
      await requestFreeze({
        startAt: toIsoOrNull(startAt),
        endAt: end.toISOString(),
        reason: reason.trim() || null,
        pauseEntitlementClock: String(policy?.entitlementPauseMode ?? '').toLowerCase().includes('internal')
          ? true
          : null,
      });
      await onCompleted();
      onClose();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not submit your freeze request.');
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal open={open} onClose={onClose} title="Freeze your subscription" size="md">
      <div className="space-y-5">
        <div className="flex items-start gap-3 rounded-2xl border border-sky-200 bg-sky-50 p-4">
          <div className="rounded-xl bg-sky-500/10 p-2 text-sky-600">
            <Snowflake className="h-5 w-5" aria-hidden="true" />
          </div>
          <div className="text-sm leading-6 text-navy">
            <p className="font-semibold">You can freeze once per subscription, for up to 12 months.</p>
            <p className="text-muted">
              While frozen, your access is paused and your remaining time is preserved. Buying a new course later
              renews your freeze.
            </p>
          </div>
        </div>

        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        {!canRequest ? (
          <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-navy">
            <p className="font-semibold">A freeze isn’t available right now.</p>
            <p className="mt-1 text-muted">
              {entitlementUsed
                ? 'You’ve already used your one freeze for this subscription. Purchasing a new course renews it.'
                : currentFreeze
                  ? 'There is already an active or pending freeze on your subscription.'
                  : eligibility?.reason ??
                    eligibilityMessages[0] ??
                    'Your account is not eligible for a self-service freeze under the current policy.'}
            </p>
            {eligibilityMessages.length > 1 ? (
              <ul className="mt-3 list-disc space-y-1 pl-5 text-muted">
                {eligibilityMessages.slice(1).map((message) => (
                  <li key={message}>{message}</li>
                ))}
              </ul>
            ) : null}
          </div>
        ) : (
          <div className="space-y-4">
            <Select
              label="Freeze duration"
              options={presetOptions}
              value={preset}
              onChange={(event) => setPreset(event.target.value)}
              hint={`Maximum ${maxDurationDays} days. Your one freeze can be used all at once or for a shorter period.`}
            />

            {preset === 'custom' ? (
              <Input
                label="Custom duration (days)"
                type="number"
                min={minDurationDays}
                max={maxDurationDays}
                value={customDays}
                onChange={(event) => setCustomDays(event.target.value)}
                hint={`Between ${minDurationDays} and ${maxDurationDays} days.`}
              />
            ) : null}

            {canSchedule ? (
              <Input
                label="Start date (optional)"
                type="datetime-local"
                value={startAt}
                onChange={(event) => setStartAt(event.target.value)}
                hint="Leave blank to start the freeze immediately."
              />
            ) : null}

            <Textarea
              label={reasonRequired ? 'Reason' : 'Reason (optional)'}
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="e.g. travel, recovery, or a break before your exam"
              rows={3}
              hint="A short note helps you and our team understand your freeze history."
            />
          </div>
        )}

        <div className="flex flex-wrap justify-end gap-3 border-t border-border pt-4">
          <Button variant="ghost" onClick={onClose} disabled={busy}>
            Cancel
          </Button>
          <Button onClick={submit} loading={busy} disabled={!canRequest || busy}>
            <Snowflake className="h-4 w-4" /> Freeze my subscription
          </Button>
        </div>
      </div>
    </Modal>
  );
}
