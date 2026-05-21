'use client';

import { useState } from 'react';
import { AlertTriangle } from 'lucide-react';

import { Modal } from './modal';
import { Button } from './button';

interface BulkActionConfirmModalProps {
  open: boolean;
  onClose: () => void;
  title: string;
  description: string;
  confirmLabel?: string;
  variant?: 'danger' | 'primary';
  count: number;
  onConfirm: () => Promise<void>;
}

export function BulkActionConfirmModal({
  open,
  onClose,
  title,
  description,
  confirmLabel = 'Confirm',
  variant = 'danger',
  count,
  onConfirm,
}: BulkActionConfirmModalProps) {
  const [loading, setLoading] = useState(false);

  async function handleConfirm() {
    setLoading(true);
    try {
      await onConfirm();
    } finally {
      setLoading(false);
      onClose();
    }
  }

  return (
    <Modal open={open} onClose={onClose} title={title} size="sm">
      <div className="space-y-4">
        <div className="flex items-start gap-3">
          <div className="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-red-100">
            <AlertTriangle className="h-5 w-5 text-red-600" />
          </div>
          <div className="text-sm text-muted">
            <p>{description}</p>
            <p className="mt-2 font-semibold text-navy">
              This will affect {count} {count === 1 ? 'item' : 'items'}.
            </p>
          </div>
        </div>

        <div className="flex justify-end gap-2 pt-2">
          <Button variant="secondary" size="sm" onClick={onClose} disabled={loading}>
            Cancel
          </Button>
          <Button
            variant={variant === 'danger' ? 'destructive' : 'primary'}
            size="sm"
            onClick={handleConfirm}
            disabled={loading}
          >
            {loading ? 'Processing…' : confirmLabel}
          </Button>
        </div>
      </div>
    </Modal>
  );
}
