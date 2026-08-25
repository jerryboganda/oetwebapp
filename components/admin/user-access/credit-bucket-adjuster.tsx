'use client';

import { useState } from 'react';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { toast } from '@/components/admin/ui/toaster';
import {
  adjustAdminUserAiCredits,
  type AiPackageCreditSnapshot,
} from '@/lib/api';

type AdjustMode = 'add' | 'set';

const BUCKETS: { key: string; label: string }[] = [
  { key: 'sharedCredits', label: 'Shared Credits' },
  { key: 'flexibleCredits', label: 'Flexible W/S' },
  { key: 'writingOnlyCredits', label: 'Writing-only' },
  { key: 'speakingOnlyCredits', label: 'Speaking-only' },
  { key: 'listeningTests', label: 'Listening tests' },
  { key: 'readingTests', label: 'Reading tests' },
  { key: 'mockExams', label: 'Mock attempts' },
];

/**
 * Master Catalogue §7: admin must be able to add/remove/set each credit
 * bucket separately, and every change is written to the credit ledger via
 * POST /v1/admin/ai-package-credits/{userId}/adjust (reason AdminAdjustment).
 * "Add" sends *Delta fields; "Set" sends absolute *Set fields.
 */
export function CreditBucketAdjuster({
  userId,
  onAdjusted,
}: {
  userId: string;
  onAdjusted?: (snapshot: AiPackageCreditSnapshot) => void;
}) {
  const [mode, setMode] = useState<AdjustMode>('add');
  const [values, setValues] = useState<Record<string, string>>({});
  const [expiresAt, setExpiresAt] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  const anyValue = Object.values(values).some((value) => value.trim() !== '' && Number(value) !== 0);

  const submit = async () => {
    if (!anyValue && !expiresAt) return;
    setSaving(true);
    try {
      const payload: Record<string, unknown> = { reason: reason || undefined };
      for (const bucket of BUCKETS) {
        const raw = values[bucket.key]?.trim() ?? '';
        if (raw === '') continue;
        const value = Number(raw);
        if (!Number.isFinite(value)) continue;
        if (mode === 'add') {
          if (value !== 0) payload[`${bucket.key}Delta`] = value;
        } else {
          // "Set to 0" is a legitimate exact target, so 0 is sent in set mode.
          payload[`${bucket.key}Set`] = value;
        }
      }
      if (expiresAt) payload.expiresAt = new Date(expiresAt).toISOString();
      const snapshot = await adjustAdminUserAiCredits(userId, payload as Parameters<typeof adjustAdminUserAiCredits>[1]);
      toast.success(mode === 'add' ? 'Credit adjustment saved to the ledger.' : 'Credit buckets set and saved to the ledger.');
      setValues({});
      setExpiresAt('');
      setReason('');
      onAdjusted?.(snapshot);
    } catch (error) {
      toast.error(error instanceof Error ? error.message : 'Adjustment failed.');
    } finally {
      setSaving(false);
    }
  };

  return (
    <div className="rounded-lg border border-gray-200 bg-white p-3">
      <div className="mb-2 flex items-center justify-between gap-2">
        <p className="text-xs font-semibold uppercase tracking-wide text-gray-500">
          Adjust credit buckets (ledgered)
        </p>
        <div className="flex overflow-hidden rounded border border-gray-300 text-[11px] font-medium" role="group" aria-label="Adjustment mode">
          {(['add', 'set'] as const).map((option) => (
            <button
              key={option}
              type="button"
              onClick={() => setMode(option)}
              aria-pressed={mode === option}
              className={
                mode === option
                  ? 'bg-gray-900 px-2 py-1 text-white'
                  : 'bg-white px-2 py-1 text-gray-600 hover:bg-gray-50'
              }
            >
              {option === 'add' ? 'Add / remove (±)' : 'Set exact'}
            </button>
          ))}
        </div>
      </div>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {BUCKETS.map((bucket) => (
          <label key={bucket.key} className="text-xs text-gray-600">
            {bucket.label}
            <Input
              type="number"
              className="mt-1"
              value={values[bucket.key] ?? ''}
              onChange={(event) =>
                setValues((prev) => ({ ...prev, [bucket.key]: event.target.value }))
              }
              placeholder={mode === 'add' ? '±n' : 'exact n'}
            />
          </label>
        ))}
      </div>
      <div className="mt-3 grid grid-cols-1 gap-2 sm:grid-cols-3">
        <label className="text-xs text-gray-600 sm:col-span-1">
          New expiry
          <Input type="datetime-local" className="mt-1" value={expiresAt} onChange={(e) => setExpiresAt(e.target.value)} />
        </label>
        <label className="text-xs text-gray-600 sm:col-span-2">
          Reason (audit)
          <Input className="mt-1" value={reason} onChange={(e) => setReason(e.target.value)} placeholder="Why this adjustment?" />
        </label>
      </div>
      <div className="mt-3 flex justify-end">
        <Button size="sm" disabled={!anyValue && !expiresAt} onClick={() => void submit()}>
          {saving ? 'Saving…' : mode === 'add' ? 'Apply adjustment' : 'Set exact balances'}
        </Button>
      </div>
    </div>
  );
}
