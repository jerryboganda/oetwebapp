'use client';

import { useState, type FormEvent } from 'react';
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import type { GithubStatus } from '@/lib/owner-agent/types';

export interface GithubTokensCardProps {
  github: GithubStatus | null | undefined;
  /** Caller performs the step-up and PUT /github-tokens. */
  onSave: (tokens: { agentToken?: string; shipToken?: string }) => Promise<void>;
}

/**
 * Write-only GitHub token fields. Values are never shown back, never logged,
 * and cleared from component state as soon as the request is sent.
 */
export function GithubTokensCard({ github, onSave }: GithubTokensCardProps) {
  const [agentToken, setAgentToken] = useState('');
  const [shipToken, setShipToken] = useState('');
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (busy || (!agentToken.trim() && !shipToken.trim())) return;
    const tokens = {
      agentToken: agentToken.trim() || undefined,
      shipToken: shipToken.trim() || undefined,
    };
    setAgentToken('');
    setShipToken('');
    setBusy(true);
    setMessage(null);
    try {
      await onSave(tokens);
      setMessage({ tone: 'ok', text: 'Saved. Tokens are write-only and are never shown again.' });
    } catch (error) {
      setMessage({ tone: 'error', text: error instanceof Error ? error.message : 'Tokens were not saved.' });
    } finally {
      setBusy(false);
    }
  };

  return (
    <Card data-testid="github-tokens-card">
      <CardHeader className="flex-col items-start gap-1">
        <CardTitle className="text-base">GitHub tokens</CardTitle>
        <CardDescription>
          Agent token: fine-grained, repo-scoped (contents, pull requests, actions, workflows). Ship token: used only by the Ship
          executor. Both are write-only.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <div className="mb-3 flex flex-wrap gap-2 text-xs">
          <Badge variant={github?.agentTokenSet ? 'success' : 'muted'}>Agent token {github?.agentTokenSet ? 'set' : 'not set'}</Badge>
          <Badge variant={github?.shipTokenSet ? 'success' : 'muted'}>Ship token {github?.shipTokenSet ? 'set' : 'not set'}</Badge>
          {github?.login ? <Badge variant="outline">as {github.login}</Badge> : null}
        </div>
        <form onSubmit={(event) => void submit(event)} className="grid gap-3 sm:grid-cols-2" autoComplete="off">
          <label className="block text-xs font-medium text-admin-fg-muted">
            New agent token
            <input
              type="password"
              autoComplete="new-password"
              spellCheck={false}
              value={agentToken}
              onChange={(event) => setAgentToken(event.target.value)}
              className="mt-1 h-9 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 font-mono text-sm"
              disabled={busy}
            />
          </label>
          <label className="block text-xs font-medium text-admin-fg-muted">
            New ship token
            <input
              type="password"
              autoComplete="new-password"
              spellCheck={false}
              value={shipToken}
              onChange={(event) => setShipToken(event.target.value)}
              className="mt-1 h-9 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 font-mono text-sm"
              disabled={busy}
            />
          </label>
          <div className="sm:col-span-2 flex items-center gap-3">
            <Button type="submit" variant="primary" size="sm" loading={busy} disabled={!agentToken.trim() && !shipToken.trim()}>
              Save tokens
            </Button>
            {message ? (
              <p className={message.tone === 'ok' ? 'text-xs text-emerald-700 dark:text-emerald-300' : 'text-xs text-red-700 dark:text-red-300'} role="status">
                {message.text}
              </p>
            ) : null}
          </div>
        </form>
      </CardContent>
    </Card>
  );
}
