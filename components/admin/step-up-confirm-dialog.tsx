'use client';

import { useEffect, useId, useState, type FormEvent } from 'react';

import { OtpCodeInput } from '@/components/auth/otp-code-input';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { toAsciiDigits } from '@/lib/normalize-digits';

export interface StepUpConfirmDialogProps {
  open: boolean;
  /** Scope the proof authorises, e.g. `billing.refund` or `billing.mark_paid`. */
  scope: string;
  title?: string;
  description?: string;
  loading?: boolean;
  error?: string | null;
  onSubmit: (code: string) => void | Promise<void>;
  onCancel: () => void;
}

const DEFAULT_DESCRIPTION =
  'This confirms a money-moving action. Enter the current 6-digit code from your authenticator app to continue.';

/**
 * Step-up confirmation modal for money-moving admin actions.
 *
 * Presentational only: the caller owns the async `requestStepUp` call, the
 * loading flag, and the error message. Escape and focus trapping come from the
 * shared `Modal`.
 */
export function StepUpConfirmDialog({
  open,
  scope,
  title = 'Confirm with your authenticator',
  description = DEFAULT_DESCRIPTION,
  loading = false,
  error = null,
  onSubmit,
  onCancel,
}: StepUpConfirmDialogProps) {
  const [code, setCode] = useState('');
  const codeLabelId = useId();
  const codeHintId = useId();
  const codeErrorId = useId();

  useEffect(() => {
    if (open) setCode('');
  }, [open]);

  const codeComplete = code.length === 6;
  const submitDisabled = loading || !codeComplete;

  const handleSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault();
    if (submitDisabled) return;
    void onSubmit(code);
  };

  return (
    <Modal open={open} onClose={onCancel} title={title} size="sm">
      <form className="space-y-4 py-2" onSubmit={handleSubmit} data-testid="step-up-confirm-dialog">
        <InlineAlert variant="warning" title="Money-moving action">
          {description}
        </InlineAlert>

        <p className="text-sm text-muted">
          Recovery codes are not accepted here — use the live 6-digit code from your authenticator app.
          Confirming authorises{' '}
          <code className="rounded bg-admin-bg-subtle px-1 py-0.5 font-mono text-xs">{scope}</code>.
        </p>

        <div
          role="group"
          aria-labelledby={codeLabelId}
          aria-describedby={error ? codeErrorId : codeHintId}
          className="flex flex-col gap-1.5"
        >
          <span id={codeLabelId} className="text-sm font-semibold tracking-tight text-navy">
            Authenticator code
          </span>
          <div data-testid="step-up-code-input">
            <OtpCodeInput
              id="step-up-code"
              autoFocus
              value={code}
              disabled={loading}
              onChange={(next) => setCode(toAsciiDigits(next).slice(0, 6))}
            />
          </div>
          <p id={codeHintId} className="text-xs leading-5 text-muted">
            Open your authenticator app and enter the current 6-digit code.
          </p>
        </div>

        {error ? (
          <p
            id={codeErrorId}
            role="alert"
            aria-live="assertive"
            className="text-xs text-red-600"
            data-testid="step-up-confirm-error"
          >
            {error}
          </p>
        ) : null}

        <div className="flex justify-end gap-2 border-t border-border pt-3">
          <Button
            variant="outline"
            type="button"
            onClick={onCancel}
            disabled={loading}
            data-testid="step-up-confirm-cancel"
          >
            Cancel
          </Button>
          <Button
            variant="primary"
            type="submit"
            disabled={submitDisabled}
            loading={loading}
            data-testid="step-up-confirm-action"
          >
            Confirm
          </Button>
        </div>
      </form>
    </Modal>
  );
}
