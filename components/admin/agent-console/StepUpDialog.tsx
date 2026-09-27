'use client';

import { createContext, useCallback, useContext, useMemo, useRef, useState, type FormEvent, type ReactNode } from 'react';
import { KeyRound } from 'lucide-react';
import { Modal } from '@/components/ui/modal';
import { Button } from '@/components/ui/button';
import { describeOwnerAgentError, stepUp } from '@/lib/owner-agent/api';

export class StepUpCancelledError extends Error {
  constructor() {
    super('Step-up cancelled.');
    this.name = 'StepUpCancelledError';
  }
}

export function isStepUpCancelled(error: unknown): boolean {
  return error instanceof StepUpCancelledError;
}

/** Resolves with a single-use step-up token, or rejects with StepUpCancelledError. */
export type RequestStepUp = (actionLabel: string) => Promise<string>;

const StepUpContext = createContext<RequestStepUp | null>(null);

export function useStepUp(): RequestStepUp {
  const context = useContext(StepUpContext);
  if (!context) throw new Error('useStepUp must be used inside <StepUpProvider>.');
  return context;
}

interface PendingRequest {
  action: string;
  resolve: (token: string) => void;
  reject: (error: Error) => void;
}

const CODE_PATTERN = /^\d{6}$/;

/**
 * Hosts the step-up modal for protected actions (Autopilot, Ship, GitHub
 * tokens, engine connect/logout). A fresh 6-digit authenticator code is
 * exchanged for a 5-minute single-use token that the caller attaches as
 * `X-Owner-Agent-StepUp`. Neither the code nor the token is stored.
 */
export function StepUpProvider({ children }: { children: ReactNode }) {
  const [pending, setPending] = useState<PendingRequest | null>(null);
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const pendingRef = useRef<PendingRequest | null>(null);

  const requestStepUp = useCallback<RequestStepUp>((actionLabel) => {
    // Only one prompt at a time; a second request cancels the first.
    pendingRef.current?.reject(new StepUpCancelledError());
    return new Promise<string>((resolve, reject) => {
      const request: PendingRequest = { action: actionLabel, resolve, reject };
      pendingRef.current = request;
      setPending(request);
      setCode('');
      setError(null);
    });
  }, []);

  const close = useCallback(() => {
    const current = pendingRef.current;
    pendingRef.current = null;
    setPending(null);
    setCode('');
    setError(null);
    setSubmitting(false);
    current?.reject(new StepUpCancelledError());
  }, []);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    const current = pendingRef.current;
    if (!current || submitting) return;
    const trimmed = code.trim();
    if (!CODE_PATTERN.test(trimmed)) {
      setError('Enter the 6-digit code from your authenticator app.');
      return;
    }
    setSubmitting(true);
    setError(null);
    try {
      const response = await stepUp(trimmed);
      if (pendingRef.current !== current) return;
      pendingRef.current = null;
      setPending(null);
      setCode('');
      setSubmitting(false);
      current.resolve(response.stepUpToken);
    } catch (err) {
      if (pendingRef.current !== current) return;
      setCode('');
      setSubmitting(false);
      setError(describeOwnerAgentError(err, 'That code was not accepted.'));
    }
  };

  const value = useMemo(() => requestStepUp, [requestStepUp]);

  return (
    <StepUpContext.Provider value={value}>
      {children}
      <Modal open={pending !== null} onClose={close} title="Confirm with your authenticator" size="sm">
        <form onSubmit={(event) => void submit(event)} className="space-y-4" data-testid="step-up-form">
          <p className="flex items-start gap-2 text-sm text-admin-fg-default">
            <KeyRound className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <span>
              <strong>{pending?.action ?? 'This action'}</strong> needs a fresh 6-digit code.
            </span>
          </p>
          <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-step-up-code">
            Authenticator code
          </label>
          <input
            id="owner-agent-step-up-code"
            name="one-time-code"
            inputMode="numeric"
            autoComplete="one-time-code"
            pattern="\d{6}"
            maxLength={6}
            value={code}
            onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
            className="h-11 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 text-center font-mono text-lg tracking-[0.4em]"
            aria-invalid={Boolean(error)}
            aria-describedby={error ? 'owner-agent-step-up-error' : undefined}
          />
          {error ? (
            <p id="owner-agent-step-up-error" className="text-xs text-red-700 dark:text-red-300" role="alert">
              {error}
            </p>
          ) : null}
          <div className="flex justify-end gap-2">
            <Button type="button" variant="ghost" size="sm" onClick={close} disabled={submitting}>
              Cancel
            </Button>
            <Button type="submit" variant="primary" size="sm" loading={submitting} disabled={code.length !== 6}>
              Confirm
            </Button>
          </div>
        </form>
      </Modal>
    </StepUpContext.Provider>
  );
}
