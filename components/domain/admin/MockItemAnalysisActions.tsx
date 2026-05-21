'use client';

/**
 * Mocks Module Phase 6 — admin item-retire action surface for the
 * `/admin/content/mocks/item-analysis` dashboard.
 *
 * Renders a per-row "Retire" trigger that opens a confirmation Modal asking
 * the admin to supply a rationale. Already-retired rows surface a tombstone
 * pill instead. The PATCH endpoint is idempotent — repeated calls return the
 * cached envelope without re-emitting audit, so accidental double-submission
 * is safe by construction.
 */

import { useCallback, useId, useState } from 'react';
import { AlertTriangle, ShieldOff } from 'lucide-react';

import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Modal } from '@/components/ui/modal';
import { Textarea } from '@/components/ui/form-controls';
import { retireMockItem, type MockItemRetireResponse } from '@/lib/api';

export interface MockItemAnalysisActionsProps {
  itemId: string;
  itemLabel?: string | null;
  bundleId?: string | null;
  retiredAt?: string | null;
  retiredReason?: string | null;
  retiredByAdminId?: string | null;
  /**
   * Notify the parent that a retire transition succeeded so the list can
   * refresh (or optimistically merge the response into the row).
   */
  onRetired?: (response: MockItemRetireResponse) => void;
}

export function MockItemAnalysisActions({
  itemId,
  itemLabel,
  bundleId,
  retiredAt,
  retiredReason,
  retiredByAdminId,
  onRetired,
}: MockItemAnalysisActionsProps) {
  const formId = useId();
  const [open, setOpen] = useState(false);
  const [reason, setReason] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const close = useCallback(() => {
    if (submitting) return;
    setOpen(false);
    setReason('');
    setError(null);
  }, [submitting]);

  const submit = useCallback(async () => {
    setSubmitting(true);
    setError(null);
    try {
      const trimmed = reason.trim();
      const response = await retireMockItem(itemId, {
        reason: trimmed || undefined,
        bundleId: bundleId ?? undefined,
      });
      onRetired?.(response);
      setOpen(false);
      setReason('');
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to retire item.');
    } finally {
      setSubmitting(false);
    }
  }, [bundleId, itemId, onRetired, reason]);

  if (retiredAt) {
    return (
      <div
        className="flex flex-col items-start gap-1"
        title={retiredReason ?? undefined}
      >
        <Badge variant="muted" className="inline-flex items-center gap-1">
          <ShieldOff className="h-3 w-3" aria-hidden /> Retired
        </Badge>
        <span className="text-[10px] text-muted">
          {new Date(retiredAt).toLocaleDateString()}
          {retiredByAdminId ? ` · ${retiredByAdminId}` : null}
        </span>
      </div>
    );
  }

  return (
    <>
      <Button
        type="button"
        size="sm"
        variant="outline"
        onClick={() => setOpen(true)}
        aria-label={`Retire item ${itemLabel ?? itemId}`}
      >
        <ShieldOff className="mr-1 h-3.5 w-3.5" aria-hidden /> Retire
      </Button>

      <Modal open={open} onClose={close} title="Retire mock item" size="md">
        <form
          id={formId}
          className="space-y-4"
          onSubmit={(event) => {
            event.preventDefault();
            void submit();
          }}
        >
          <div className="flex gap-3 rounded-2xl border border-amber-200/60 bg-amber-50/50 p-3 text-sm text-amber-900 dark:border-amber-800/60 dark:bg-amber-950/30 dark:text-amber-200">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
            <div>
              <p className="font-bold">This is a soft-retire.</p>
              <p className="mt-0.5 text-xs">
                Historical analytics keep the snapshot row, but the item is
                excluded from new attempts and a Quality Control review row is
                opened. Audit fires once per logical retire.
              </p>
            </div>
          </div>

          <div className="rounded-2xl border border-border bg-background-light/60 p-3 text-sm">
            <p className="font-semibold text-navy">{itemLabel ?? itemId}</p>
            <p className="font-mono text-xs text-muted">{itemId}</p>
            {bundleId ? (
              <p className="mt-1 text-xs text-muted">
                Bundle: <span className="font-mono">{bundleId}</span>
              </p>
            ) : null}
          </div>

          <Textarea
            label="Reason"
            placeholder="Why is this item being retired? e.g. ambiguous distractor, image rights expired…"
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            rows={4}
            required
            hint="Captured in the audit trail and the new Quality Control review row."
          />

          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

          <div className="flex flex-wrap items-center justify-end gap-2 pt-1">
            <Button type="button" variant="ghost" onClick={close} disabled={submitting}>
              Cancel
            </Button>
            <Button
              type="submit"
              variant="destructive"
              disabled={submitting || reason.trim().length === 0}
              loading={submitting}
            >
              {submitting ? 'Retiring…' : 'Retire item'}
            </Button>
          </div>
        </form>
      </Modal>
    </>
  );
}
