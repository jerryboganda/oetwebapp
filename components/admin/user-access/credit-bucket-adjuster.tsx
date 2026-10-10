'use client';

import { useMemo, useState } from 'react';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { toast } from '@/components/ui/toaster';
import { adjustAdminUserAiCredits } from '@/lib/api';
import type { AiPackageCreditBucket, AiPackageCreditSnapshot } from '@/lib/billing-types';
import {
  CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY,
  describeCreditsAsActivities,
} from '@/lib/format-allowance';

type AdjustMode = 'add' | 'set';

// `key` is the API field prefix (<key>Delta / <key>Set); `bucketKey` is the same bucket's key on the
// credit summary card, and `label` matches that card so the two never name a bucket differently.
const BUCKETS: { key: string; label: string; bucketKey: AiPackageCreditBucket['key'] }[] = [
  { key: 'sharedCredits', label: 'Shared Credits', bucketKey: 'shared' },
  { key: 'flexibleCredits', label: 'Flexible W/S Credits', bucketKey: 'flexible_ws' },
  { key: 'writingOnlyCredits', label: 'Writing Credits', bucketKey: 'writing' },
  { key: 'speakingOnlyCredits', label: 'Speaking Credits', bucketKey: 'speaking' },
  { key: 'listeningTests', label: 'Listening Credits', bucketKey: 'listening' },
  { key: 'readingTests', label: 'Reading Credits', bucketKey: 'reading' },
  { key: 'mockExams', label: 'Full Mock Attempts', bucketKey: 'mock' },
];

const WHOLE_NUMBER = /^-?\d+$/;

function fieldError(raw: string, mode: AdjustMode): string | undefined {
  const text = raw.trim();
  if (text === '') return undefined;
  if (!WHOLE_NUMBER.test(text)) return 'Enter a whole number.';
  if (mode === 'set' && Number(text) < 0) return 'Enter 0 or more.';
  return undefined;
}

/** What a typed amount buys, for the pools counted in credits (Writing, Speaking, Flexible W/S). */
function activityPreview(bucketKey: string, raw: string, mode: AdjustMode): string | undefined {
  const text = raw.trim();
  if (!WHOLE_NUMBER.test(text)) return undefined;
  const value = Number(text);
  const activities = describeCreditsAsActivities(bucketKey, Math.abs(value));
  if (!activities) return undefined;
  return mode === 'add' ? `${value < 0 ? '−' : '+'}${activities}` : `= ${activities}`;
}

function nowLine(bucket: AiPackageCreditBucket): string {
  if (bucket.unlimited) return 'Now: Unlimited';
  const expired = bucket.expired ?? 0;
  const lapsed = expired > 0 ? ` · ${expired} expired (not in Total)` : '';
  return `Now: Total ${bucket.totalGranted} · Used ${bucket.used} · Remaining ${bucket.remaining}${lapsed}`;
}

/**
 * Master Catalogue §7: admin must be able to add/remove/set each credit
 * bucket separately, and every change is written to the credit ledger via
 * POST /v1/admin/ai-package-credits/{userId}/adjust (reason AdminAdjustment).
 * "Add" sends *Delta fields; "Set" sends absolute *Set fields, which target the
 * bucket TOTAL (Remaining = Total - Used).
 */
export function CreditBucketAdjuster({
  userId,
  onAdjusted,
  snapshot,
  disabled = false,
}: {
  userId: string;
  onAdjusted?: (snapshot: AiPackageCreditSnapshot) => void;
  /** Current balances; when given, each bucket shows its Total / Used / Remaining. */
  snapshot?: AiPackageCreditSnapshot | null;
  /** Disables every control, e.g. until the balances have loaded. */
  disabled?: boolean;
}) {
  const [mode, setMode] = useState<AdjustMode>('add');
  const [values, setValues] = useState<Record<string, string>>({});
  const [expiresAt, setExpiresAt] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  const nowByKey = useMemo(() => {
    const map = new Map<string, AiPackageCreditBucket>();
    for (const bucket of snapshot?.buckets ?? []) map.set(bucket.key, bucket);
    return map;
  }, [snapshot]);

  const hasAnyInput = Object.values(values).some((value) => value.trim() !== '');
  const hasNonZeroInput = Object.values(values).some((value) => {
    const trimmed = value.trim();
    return trimmed !== '' && Number(trimmed) !== 0 && Number.isFinite(Number(trimmed));
  });
  const anyValue = mode === 'set' ? hasAnyInput : hasNonZeroInput;
  const hasErrors = BUCKETS.some((bucket) => fieldError(values[bucket.key] ?? '', mode) !== undefined);
  // The expiry only stamps credits added in the same submit, so it can never enable a save by itself.
  const canSubmit = anyValue && !hasErrors && !saving && !disabled;

  const submit = async () => {
    if (!canSubmit) return;
    setSaving(true);
    try {
      const payload: Record<string, unknown> = { reason: reason || undefined };
      for (const bucket of BUCKETS) {
        const raw = values[bucket.key]?.trim() ?? '';
        if (raw === '') continue;
        const value = Number(raw);
        if (!Number.isInteger(value)) continue;
        if (mode === 'add') {
          if (value !== 0) payload[`${bucket.key}Delta`] = value;
        } else {
          // "Set to 0" is a legitimate target, so 0 is sent in set mode.
          payload[`${bucket.key}Set`] = value;
        }
      }
      if (expiresAt) payload.expiresAt = new Date(expiresAt).toISOString();
      const next = await adjustAdminUserAiCredits(userId, payload as Parameters<typeof adjustAdminUserAiCredits>[1]);
      toast.success(mode === 'add' ? 'Credit adjustment saved to the ledger.' : 'Credit totals set and saved to the ledger.');
      setValues({});
      setExpiresAt('');
      setReason('');
      onAdjusted?.(next);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Adjustment failed.');
    } finally {
      setSaving(false);
    }
  };

  return (
    // A native disabled fieldset disables every input and button inside it.
    <fieldset
      disabled={disabled}
      aria-label="Adjust credit buckets"
      className="min-w-0 rounded-lg border border-admin-border bg-admin-bg-surface p-3"
    >
      <div className="mb-2 flex items-center justify-between gap-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-admin-fg-muted">
          Adjust credit buckets (ledgered)
        </p>
        <div className="flex overflow-hidden rounded border border-admin-border-strong text-2xs font-medium" role="group" aria-label="Adjustment mode">
          {(['add', 'set'] as const).map((option) => (
            <button
              key={option}
              type="button"
              onClick={() => setMode(option)}
              aria-pressed={mode === option}
              className={
                mode === option
                  ? 'bg-admin-fg-strong px-2 py-1 text-admin-fg-inverse'
                  : 'bg-admin-bg-surface px-2 py-1 text-admin-fg-muted hover:bg-admin-bg-subtle'
              }
            >
              {option === 'add' ? 'Add / remove (±)' : 'Set total'}
            </button>
          ))}
        </div>
      </div>
      <p className="mb-3 text-xs leading-5 text-admin-fg-muted" data-testid="credit-adjuster-help">
        {mode === 'add'
          ? 'Adds (+) or removes (−) credits from the balance. Used is never changed.'
          : 'Sets the bucket TOTAL: Remaining becomes Total − Used. Credits that already expired are not counted, and a Total below Used is refused.'}{' '}
        Writing, Speaking and Flexible W/S are counted in credits: 1 letter or card = {CREDITS_PER_WRITING_OR_SPEAKING_ACTIVITY} credits
        (a full two-card Speaking exam = 4). Listening, Reading and mock buckets count 1 per paper or attempt.
      </p>
      <div className="grid grid-cols-2 items-start gap-3 sm:grid-cols-4">
        {BUCKETS.map((bucket) => {
          const raw = values[bucket.key] ?? '';
          const error = fieldError(raw, mode);
          const now = nowByKey.get(bucket.bucketKey);
          return (
            <div key={bucket.key} className="space-y-1">
              <Input
                label={bucket.label}
                type="number"
                inputMode="numeric"
                step={1}
                min={mode === 'set' ? 0 : undefined}
                value={raw}
                onChange={(event) =>
                  setValues((prev) => ({ ...prev, [bucket.key]: event.target.value }))
                }
                placeholder={mode === 'add' ? '±n' : 'total'}
                error={error}
                hint={error ? undefined : activityPreview(bucket.bucketKey, raw, mode)}
              />
              {now ? (
                <p className="text-2xs leading-4 text-admin-fg-muted" data-testid={`credit-adjuster-now-${bucket.bucketKey}`}>
                  {nowLine(now)}
                </p>
              ) : null}
            </div>
          );
        })}
      </div>
      <div className="mt-3 grid grid-cols-1 gap-3 sm:grid-cols-3">
        <div className="sm:col-span-1">
          <Input
            label="Expiry for credits added now"
            type="datetime-local"
            value={expiresAt}
            onChange={(e) => setExpiresAt(e.target.value)}
            hint="Applies only to credits added in this adjustment. It does not change the expiry of credits the learner already holds, and cannot be saved on its own."
          />
        </div>
        <div className="sm:col-span-2">
          <Input
            label="Reason (audit)"
            value={reason}
            onChange={(e) => setReason(e.target.value)}
            placeholder="Why this adjustment?"
          />
        </div>
      </div>
      <div className="mt-3 flex justify-end">
        <Button size="sm" disabled={!canSubmit} loading={saving} onClick={() => void submit()}>
          {saving ? 'Saving…' : mode === 'add' ? 'Apply adjustment' : 'Set totals'}
        </Button>
      </div>
    </fieldset>
  );
}
