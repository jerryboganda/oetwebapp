'use client';

import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { AlertTriangle } from 'lucide-react';

export interface BillingConflictBannerProps {
  /** Optional human label for the entity that conflicted (e.g. "plan", "coupon"). */
  entityLabel?: string;
  /** Optional server message extracted from the 409 response. */
  detail?: string;
  /** Reload the latest server state, then close the editor. */
  onReload: () => void;
  onDismiss?: () => void;
}

/**
 * Shown when a billing edit returns HTTP 409 (optimistic concurrency conflict).
 * Never silently overwrites — forces the admin to reload the latest record.
 */
export function BillingConflictBanner({
  entityLabel = 'record',
  detail,
  onReload,
  onDismiss,
}: BillingConflictBannerProps) {
  return (
    <div data-testid="billing-conflict-banner">
      <InlineAlert
        variant="warning"
        title="Conflicting changes detected"
      >
      <div className="space-y-3">
        <p>
          <AlertTriangle className="mr-1 inline h-4 w-4 align-text-bottom" aria-hidden="true" />
          Another admin updated this {entityLabel} while you were editing. To avoid silently
          overwriting their change, reload the latest version and re-apply your edits.
        </p>
        {detail ? <p className="text-xs text-muted">Server detail: {detail}</p> : null}
        <div className="flex gap-2">
          <Button
            variant="primary"
            size="sm"
            onClick={onReload}
            data-testid="billing-conflict-reload"
          >
            Reload latest
          </Button>
          {onDismiss ? (
            <Button variant="outline" size="sm" onClick={onDismiss}>
              Dismiss
            </Button>
          ) : null}
        </div>
      </div>
    </InlineAlert>
    </div>
  );
}

/**
 * Recognises a fetch / api error as a 409 conflict regardless of phrasing.
 * The admin api throws Errors with `status` properties or messages containing "409"/"conflict".
 */
export function isConflictError(error: unknown): boolean {
  if (!error) return false;
  // Errors surfaced by lib/api.ts may be plain Error or carry { status }.
  if (typeof error === 'object' && error !== null && 'status' in error) {
    const status = (error as { status?: unknown }).status;
    if (status === 409) return true;
  }
  if (error instanceof Error) {
    const message = error.message.toLowerCase();
    if (message.includes('409')) return true;
    if (message.includes('conflict')) return true;
    if (message.includes('etag')) return true;
    if (message.includes('version mismatch')) return true;
  }
  return false;
}
