'use client';

import { useState, type FormEvent } from 'react';
import { Lock } from 'lucide-react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Button } from '@/components/ui/button';
import { describeOwnerAgentError } from '@/lib/owner-agent/api';
import type { UnlockClearReason } from '@/lib/owner-agent/unlock-store';

const CLEARED_MESSAGE: Record<UnlockClearReason, string> = {
  locked: 'The console was locked.',
  expired: 'The unlock expired. Unlock again to continue.',
  server_locked: 'The server ended the unlock (revoked or expired).',
  refresh_failed: 'The unlock could not be renewed. Unlock again to continue.',
};

export interface UnlockGateProps {
  onUnlock: (password: string, code: string) => Promise<void>;
  clearedReason?: UnlockClearReason | null;
  /**
   * From GET /me: unlock is refused until this time (e.g. 72 h after an
   * authenticator re-enrolment). The server enforces it; this only explains.
   */
  blockedUntil?: string | null;
}

function formatBlockedUntil(value: string | null | undefined, now: number = Date.now()): string | null {
  if (!value) return null;
  const parsed = Date.parse(value);
  if (!Number.isFinite(parsed) || parsed <= now) return null;
  return new Date(parsed).toLocaleString();
}

/**
 * Password + 6-digit authenticator code → in-memory unlock ticket. Inputs are
 * cleared after every attempt; nothing is persisted. Recovery codes cannot
 * unlock (enforced server-side).
 */
export function UnlockGate({ onUnlock, clearedReason, blockedUntil }: UnlockGateProps) {
  const blockedLabel = formatBlockedUntil(blockedUntil);
  const [password, setPassword] = useState('');
  const [code, setCode] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (submitting) return;
    if (!password) {
      setError('Enter your account password.');
      return;
    }
    if (!/^\d{6}$/.test(code)) {
      setError('Enter the 6-digit code from your authenticator app.');
      return;
    }
    setSubmitting(true);
    setError(null);
    const attemptPassword = password;
    const attemptCode = code;
    // Drop the secrets from component state before the request resolves.
    setPassword('');
    setCode('');
    try {
      await onUnlock(attemptPassword, attemptCode);
    } catch (err) {
      setError(describeOwnerAgentError(err, 'Unlock failed.'));
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="mx-auto flex min-h-[50vh] w-full max-w-md items-center px-4 py-10">
      <Card className="w-full" data-testid="unlock-gate">
        <CardHeader className="flex-col items-start gap-1">
          <CardTitle className="flex items-center gap-2 text-base">
            <Lock className="h-4 w-4" aria-hidden="true" /> Unlock the agent console
          </CardTitle>
          <CardDescription>
            Owner only. Confirm your password and a fresh authenticator code. The unlock lives in this tab only.
          </CardDescription>
        </CardHeader>
        <CardContent>
          <form onSubmit={(event) => void submit(event)} className="space-y-3" autoComplete="off">
            {clearedReason ? (
              <p className="rounded-lg bg-admin-bg-subtle px-3 py-2 text-xs text-admin-fg-muted" role="status">
                {CLEARED_MESSAGE[clearedReason]}
              </p>
            ) : null}
            {blockedLabel ? (
              <p className="rounded-lg bg-red-50 px-3 py-2 text-xs text-red-800 dark:bg-red-950 dark:text-red-200" role="alert" data-testid="unlock-blocked">
                Console unlock is blocked until {blockedLabel} after a recent authenticator change.
              </p>
            ) : null}
            <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-unlock-password">
              Password
            </label>
            <input
              id="owner-agent-unlock-password"
              type="password"
              autoComplete="current-password"
              value={password}
              onChange={(event) => setPassword(event.target.value)}
              className="h-10 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 text-sm"
              disabled={submitting}
            />
            <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-unlock-code">
              Authenticator code
            </label>
            <input
              id="owner-agent-unlock-code"
              inputMode="numeric"
              autoComplete="one-time-code"
              pattern="\d{6}"
              maxLength={6}
              value={code}
              onChange={(event) => setCode(event.target.value.replace(/\D/g, '').slice(0, 6))}
              className="h-10 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 font-mono tracking-[0.3em]"
              disabled={submitting}
            />
            {error ? (
              <p className="text-xs text-red-700 dark:text-red-300" role="alert">
                {error}
              </p>
            ) : null}
            <Button type="submit" variant="primary" fullWidth loading={submitting}>
              Unlock
            </Button>
          </form>
        </CardContent>
      </Card>
    </div>
  );
}
