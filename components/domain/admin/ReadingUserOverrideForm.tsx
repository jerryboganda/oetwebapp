'use client';

import { useCallback, useState } from 'react';
import { AlertTriangle, CheckCircle2, Search } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import {
  getReadingUserOverride,
  upsertReadingUserOverride,
  type ReadingUserPolicyOverrideDto,
} from '@/lib/reading-authoring-api';

/**
 * Phase 3 closure — admin form to grant or update a per-user Reading
 * policy override. Two operations live here:
 *
 *   1. Lookup — paste a userId, fetch the existing override (if any) so
 *      the form pre-populates with current values.
 *   2. Upsert — write a new override or update fields on the existing
 *      row. Server-side this calls `IReadingPolicyService.UpsertUserOverrideAsync`
 *      and records an AuditEvent under
 *      `Action="ReadingUserOverrideUpsert"`.
 *
 * The form is intentionally minimal — no userId-by-email lookup yet
 * (that needs a separate user-search endpoint). Operations are expected
 * to copy the userId from the learner support page.
 */
export function ReadingUserOverrideForm() {
  const [userId, setUserId] = useState('');
  const [extraTimePct, setExtraTimePct] = useState<number>(0);
  const [blockAttempts, setBlockAttempts] = useState(false);
  const [reason, setReason] = useState('');
  const [expiresAt, setExpiresAt] = useState<string>('');

  const [loading, setLoading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [savedSnapshot, setSavedSnapshot] = useState<ReadingUserPolicyOverrideDto | null>(null);
  const [found, setFound] = useState<boolean | null>(null);

  const reset = useCallback(() => {
    setExtraTimePct(0);
    setBlockAttempts(false);
    setReason('');
    setExpiresAt('');
    setSavedSnapshot(null);
    setFound(null);
  }, []);

  const handleLookup = useCallback(async () => {
    if (!userId.trim()) {
      setError('Enter a userId first.');
      return;
    }
    setLoading(true);
    setError(null);
    setSavedSnapshot(null);
    try {
      const existing = await getReadingUserOverride(userId.trim());
      if (existing) {
        setExtraTimePct(existing.extraTimeEntitlementPct);
        setBlockAttempts(existing.blockAttempts);
        setReason(existing.reason ?? '');
        setExpiresAt(existing.expiresAt ?? '');
        setFound(true);
      } else {
        reset();
        setFound(false);
      }
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Lookup failed.');
    } finally {
      setLoading(false);
    }
  }, [userId, reset]);

  const handleSave = useCallback(async () => {
    if (!userId.trim()) {
      setError('Enter a userId first.');
      return;
    }
    if (extraTimePct < 0 || extraTimePct > 200) {
      setError('Extra-time percentage must be between 0 and 200.');
      return;
    }
    setSaving(true);
    setError(null);
    try {
      const saved = await upsertReadingUserOverride(userId.trim(), {
        userId: userId.trim(),
        extraTimeEntitlementPct: Math.round(extraTimePct),
        blockAttempts,
        reason: reason.trim() || null,
        expiresAt: expiresAt || null,
      });
      setSavedSnapshot(saved);
      setFound(true);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed.');
    } finally {
      setSaving(false);
    }
  }, [userId, extraTimePct, blockAttempts, reason, expiresAt]);

  return (
    <div className="space-y-5">
      <div className="rounded-2xl border border-border bg-admin-bg-subtle p-4">
        <p className="mb-2 text-xs font-bold uppercase tracking-[0.14em] text-muted">
          Lookup learner
        </p>
        <div className="flex flex-col gap-2 sm:flex-row sm:items-end">
          <div className="flex-1">
            <Input
              label="Learner userId"
              value={userId}
              onChange={(e) => setUserId(e.target.value)}
              placeholder="e.g. usr_2NQ8…"
              hint="Copy from the learner support page. Email-based lookup is not yet wired."
            />
          </div>
          <Button
            type="button"
            variant="outline"
            size="md"
            onClick={() => void handleLookup()}
            disabled={loading || !userId.trim()}
          >
            <Search className="mr-1.5 h-4 w-4" aria-hidden />
            {loading ? 'Loading…' : 'Look up'}
          </Button>
        </div>

        {found === true ? (
          <InlineAlert variant="info" className="mt-3">
            <CheckCircle2 className="mr-1.5 inline h-4 w-4" aria-hidden />
            Existing override loaded. Edit and save to update.
          </InlineAlert>
        ) : null}
        {found === false ? (
          <InlineAlert variant="info" className="mt-3">
            No override exists for this learner. Fill the form below to grant one.
          </InlineAlert>
        ) : null}
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

      <div className="grid gap-4 md:grid-cols-2">
        <Input
          type="number"
          min={0}
          max={200}
          label="Extra-time entitlement (%)"
          value={String(extraTimePct)}
          onChange={(e) => setExtraTimePct(Number(e.target.value) || 0)}
          hint="0–200%. Applied to both Part A and Part B/C timers. Capped server-side."
        />
        <Input
          type="datetime-local"
          label="Expires at (optional)"
          value={expiresAt ? expiresAt.slice(0, 16) : ''}
          onChange={(e) => setExpiresAt(e.target.value ? new Date(e.target.value).toISOString() : '')}
          hint="Override is ignored once expired. Leave blank for no expiry."
        />
      </div>

      <label className="flex items-center gap-2 rounded-2xl border border-border bg-admin-bg-subtle p-3">
        <input
          type="checkbox"
          className="h-4 w-4 rounded border-border text-primary focus:ring-primary/20"
          checked={blockAttempts}
          onChange={(e) => setBlockAttempts(e.target.checked)}
        />
        <span className="text-sm font-semibold text-admin-fg-strong">
          Block this learner from starting new Reading attempts
        </span>
      </label>
      {blockAttempts ? (
        <InlineAlert variant="warning">
          <AlertTriangle className="mr-1.5 inline h-4 w-4" aria-hidden />
          Block stays active until cleared. Existing in-progress attempts are
          unaffected.
        </InlineAlert>
      ) : null}

      <Textarea
        label="Reason (audit trail)"
        rows={3}
        value={reason}
        onChange={(e) => setReason(e.target.value)}
        hint="Captured in the AuditEvent row. Make it specific (e.g. an accessibility entitlement ticket id)."
      />

      <div className="flex items-center justify-between gap-3">
        <p className="text-xs text-muted">
          {savedSnapshot
            ? `Saved at ${new Date(savedSnapshot.updatedAt).toLocaleString()}.`
            : 'Changes are written under the AdminContentWrite policy.'}
        </p>
        <Button
          type="button"
          variant="primary"
          size="md"
          onClick={() => void handleSave()}
          disabled={saving || !userId.trim()}
        >
          {saving ? 'Saving…' : found === true ? 'Update override' : 'Grant override'}
        </Button>
      </div>
    </div>
  );
}
