'use client';

import { useState } from 'react';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/admin/ui/input';
import { toast } from '@/components/admin/ui/toaster';
import {
  adjustAdminUserAiCredits,
  type AiPackageCreditSnapshot,
} from '@/lib/api';

const BUCKETS: { key: string; label: string }[] = [
  { key: 'sharedCreditsDelta', label: 'Shared Credits' },
  { key: 'flexibleCreditsDelta', label: 'Flexible W/S' },
  { key: 'writingOnlyCreditsDelta', label: 'Writing-only' },
  { key: 'speakingOnlyCreditsDelta', label: 'Speaking-only' },
  { key: 'listeningTestsDelta', label: 'Listening tests' },
  { key: 'readingTestsDelta', label: 'Reading tests' },
  { key: 'mockExamsDelta', label: 'Mock attempts' },
];

/**
 * Master Catalogue §7: admin must be able to grant/remove/adjust each credit
 * bucket separately, and every change is written to the credit ledger via
 * POST /v1/admin/ai-package-credits/{userId}/adjust (reason AdminAdjustment).
 */
export function CreditBucketAdjuster({
  userId,
  onAdjusted,
}: {
  userId: string;
  onAdjusted?: (snapshot: AiPackageCreditSnapshot) => void;
}) {
  const [deltas, setDeltas] = useState<Record<string, number>>({});
  const [expiresAt, setExpiresAt] = useState('');
  const [reason, setReason] = useState('');
  const [saving, setSaving] = useState(false);

  const anyDelta = Object.values(deltas).some((value) => Number(value) !== 0 && !Number.isNaN(Number(value)));

  const submit = async () => {
    if (!anyDelta && !expiresAt) return;
    setSaving(true);
    try {
      const payload: Record<string, unknown> = { reason: reason || undefined };
      for (const bucket of BUCKETS) {
        const value = Number(deltas[bucket.key] ?? 0);
        if (Number.isFinite(value) && value !== 0) payload[bucket.key] = value;
      }
      if (expiresAt) payload.expiresAt = new Date(expiresAt).toISOString();
      const snapshot = await adjustAdminUserAiCredits(userId, payload as Parameters<typeof adjustAdminUserAiCredits>[1]);
      toast.success('Credit adjustment saved to the ledger.');
      setDeltas({});
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
      <p className="mb-2 text-xs font-semibold uppercase tracking-wide text-gray-500">
        Adjust credit buckets (ledgered)
      </p>
      <div className="grid grid-cols-2 gap-2 sm:grid-cols-4">
        {BUCKETS.map((bucket) => (
          <label key={bucket.key} className="text-xs text-gray-600">
            {bucket.label}
            <Input
              type="number"
              className="mt-1"
              value={deltas[bucket.key] ?? ''}
              onChange={(event) =>
                setDeltas((prev) => ({ ...prev, [bucket.key]: Number(event.target.value) }))
              }
              placeholder="±n"
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
        <Button size="sm" disabled={!anyDelta && !expiresAt} onClick={() => void submit()}>
          {saving ? 'Saving…' : 'Apply adjustment'}
        </Button>
      </div>
    </div>
  );
}
