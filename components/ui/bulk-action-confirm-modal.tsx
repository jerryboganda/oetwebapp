'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle } from 'lucide-react';

import { Button } from './button';
import { Textarea } from './form-controls';
import { Modal } from './modal';

interface BulkActionConfirmModalProps {
  open: boolean;
  title: string;
  description: string;
  confirmLabel?: string;
  cancelLabel?: string;
  destructive?: boolean;
  loading?: boolean;
  /**
   * When true, renders a reason textarea. The entered reason is passed to
   * `onConfirm`. Backward-compatible: existing callers that ignore the
   * argument keep working unchanged.
   */
  requireReason?: boolean;
  /** Label for the reason textarea. Defaults to "Reason". */
  reasonLabel?: string;
  /** Placeholder for the reason textarea. */
  reasonPlaceholder?: string;
  onConfirm: (reason?: string) => void;
  onClose: () => void;
}

export function BulkActionConfirmModal({
  open,
  title,
  description,
  confirmLabel = 'Confirm',
  cancelLabel = 'Cancel',
  destructive,
  loading,
  requireReason = false,
  reasonLabel = 'Reason',
  reasonPlaceholder,
  onConfirm,
  onClose,
}: BulkActionConfirmModalProps) {
  const [reason, setReason] = useState('');

  // Reset the reason whenever the modal is (re)opened so a stale value from a
  // previous action never leaks into the next confirmation.
  useEffect(() => {
    if (open) setReason('');
  }, [open]);

  const reasonMissing = requireReason && reason.trim().length === 0;

  return (
    <Modal open={open} onClose={loading ? () => {} : onClose} title={title} size="sm">
      <div className="space-y-5">
        <div className="flex gap-3 rounded-xl border border-border bg-background-light p-4">
          <span className="mt-0.5 inline-flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-danger/10 text-danger">
            <AlertTriangle className="h-4 w-4" aria-hidden="true" />
          </span>
          <p className="text-sm leading-6 text-muted">{description}</p>
        </div>
        {requireReason ? (
          <Textarea
            label={reasonLabel}
            placeholder={reasonPlaceholder}
            value={reason}
            onChange={(event) => setReason(event.target.value)}
            disabled={loading}
          />
        ) : null}
        <div className="flex flex-col-reverse gap-2 sm:flex-row sm:justify-end">
          <Button type="button" variant="outline" onClick={onClose} disabled={loading}>
            {cancelLabel}
          </Button>
          <Button
            type="button"
            variant={destructive ? 'destructive' : 'primary'}
            loading={loading}
            disabled={reasonMissing}
            onClick={() => onConfirm(requireReason ? reason : undefined)}
          >
            {confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
