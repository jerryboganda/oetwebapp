'use client';

import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';

export interface BillingConfirmDialogProps {
  open: boolean;
  title: string;
  description: string;
  /** What you must type to confirm. If omitted no typed phrase is required. */
  confirmPhrase?: string;
  /** Current value of the confirmation input. */
  confirmInput?: string;
  onConfirmInputChange?: (value: string) => void;
  confirmLabel?: string;
  cancelLabel?: string;
  /** Visual emphasis for destructive vs benign actions. */
  variant?: 'danger' | 'warning';
  loading?: boolean;
  onConfirm: () => void;
  onCancel: () => void;
}

/**
 * Reusable destructive-action confirmation modal for admin billing flows
 * (archive plan, archive add-on, expire coupon, remove wallet tier).
 *
 * Wraps the existing `Modal` so we keep a single modal stack and a11y.
 */
export function BillingConfirmDialog({
  open,
  title,
  description,
  confirmPhrase,
  confirmInput = '',
  onConfirmInputChange,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  variant = 'danger',
  loading = false,
  onConfirm,
  onCancel,
}: BillingConfirmDialogProps) {
  const phraseRequired = Boolean(confirmPhrase);
  const phraseMatches = !phraseRequired || confirmInput.trim() === confirmPhrase;
  const confirmDisabled = loading || !phraseMatches;

  return (
    <Modal open={open} onClose={onCancel} title={title} size="sm">
      <div className="space-y-4 py-2" data-testid="billing-confirm-dialog">
        <InlineAlert
          variant={variant === 'danger' ? 'error' : 'warning'}
          title={variant === 'danger' ? 'Destructive action' : 'Heads up'}
        >
          {description}
        </InlineAlert>

        {phraseRequired ? (
          <label className="block text-sm">
            <span className="mb-1 block text-foreground">
              Type <code className="rounded bg-background-light px-1 py-0.5 font-mono text-xs">{confirmPhrase}</code> to confirm
            </span>
            <input
              type="text"
              value={confirmInput}
              onChange={(event) => onConfirmInputChange?.(event.target.value)}
              className="block w-full rounded border border-border px-2 py-1 text-sm focus:border-primary focus:outline-none focus:ring-1 focus:ring-primary"
              autoComplete="off"
              autoFocus
              data-testid="billing-confirm-input"
            />
          </label>
        ) : null}

        <div className="flex justify-end gap-2 border-t border-border pt-3">
          <Button variant="outline" onClick={onCancel} type="button" disabled={loading}>
            {cancelLabel}
          </Button>
          <Button
            variant={variant === 'danger' ? 'destructive' : 'primary'}
            onClick={onConfirm}
            type="button"
            disabled={confirmDisabled}
            loading={loading}
            data-testid="billing-confirm-action"
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
