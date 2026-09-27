'use client';

import { useState, type FormEvent } from 'react';
import { CheckCircle2, Circle, ExternalLink, Loader2, Rocket, XCircle } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Badge } from '@/components/ui/badge';
import { cn } from '@/lib/utils';
import type { ShipLogEntry } from '@/lib/owner-agent/event-reducer';
import { GITHUB_HOSTS, safeExternalUrl } from '@/lib/owner-agent/text-safety';
import { SHIP_PHASES, type ShipPhase, type ShipState } from '@/lib/owner-agent/types';
import { VisibleText } from './VisibleText';

const PHASE_LABEL: Record<ShipPhase, string> = {
  queued: 'Queued',
  scanning: 'Secret scan',
  pushing: 'Push agent branch',
  pr_open: 'Pull request',
  visibility: 'Repo visibility lease',
  merging: 'Merge',
  deploying: 'Build & Deploy',
  health: 'Live health',
  restoring_visibility: 'Restore visibility',
  done: 'Done',
  failed: 'Failed',
};

export function isShipActive(state: ShipState | null | undefined): boolean {
  return Boolean(state && state.phase !== 'done' && state.phase !== 'failed');
}

export interface ShipPanelProps {
  ship: ShipState | null;
  log: readonly ShipLogEntry[];
  branch?: string;
  /** The caller performs POST /ship. */
  onShip: (body: { prTitle?: string; prBody?: string }) => Promise<void>;
  onRefresh?: () => void;
  disabled?: boolean;
  disabledReason?: string;
}

/**
 * Ship = push `agent/*` → PR → merge → existing Build & Deploy → live health →
 * restore repo visibility. Shows phase progress, PR / Actions links (GitHub
 * only) and the live ship log streamed on the session.
 */
export function ShipPanel({ ship, log, branch, onShip, onRefresh, disabled = false, disabledReason }: ShipPanelProps) {
  const [prTitle, setPrTitle] = useState('');
  const [prBody, setPrBody] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const active = isShipActive(ship);
  const currentIndex = ship ? SHIP_PHASES.indexOf(ship.phase) : -1;
  const failed = ship?.phase === 'failed';
  const prUrl = safeExternalUrl(ship?.prUrl, GITHUB_HOSTS);
  const runUrl = safeExternalUrl(ship?.runUrl, GITHUB_HOSTS);

  const submit = async (event: FormEvent) => {
    event.preventDefault();
    if (submitting || active || disabled) return;
    setSubmitting(true);
    setError(null);
    try {
      await onShip({ prTitle: prTitle.trim() || undefined, prBody: prBody.trim() || undefined });
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Ship failed to start.');
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-4" data-testid="ship-panel">
      {!active ? (
        <form onSubmit={(event) => void submit(event)} className="space-y-3 rounded-lg border border-admin-border p-3">
          <p className="text-xs text-admin-fg-muted">
            Ships <span className="font-mono">{branch ?? 'the session branch'}</span> via a pull request, merges it and watches Build &amp; Deploy
            until live health is green.
          </p>
          <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-pr-title">
            PR title (optional)
          </label>
          <input
            id="owner-agent-pr-title"
            value={prTitle}
            maxLength={200}
            onChange={(event) => setPrTitle(event.target.value)}
            className="h-9 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 text-sm"
          />
          <label className="block text-xs font-medium text-admin-fg-muted" htmlFor="owner-agent-pr-body">
            PR description (optional)
          </label>
          <textarea
            id="owner-agent-pr-body"
            value={prBody}
            rows={4}
            maxLength={20_000}
            onChange={(event) => setPrBody(event.target.value)}
            className="w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 py-2 text-sm"
          />
          {disabled && disabledReason ? <p className="text-xs text-admin-fg-muted">{disabledReason}</p> : null}
          {error ? (
            <p className="text-xs text-red-700 dark:text-red-300" role="alert">
              {error}
            </p>
          ) : null}
          <Button type="submit" variant="primary" loading={submitting} disabled={disabled}>
            <Rocket className="h-4 w-4" aria-hidden="true" /> Ship
          </Button>
        </form>
      ) : null}

      {ship ? (
        <div className="space-y-3 rounded-lg border border-admin-border p-3">
          <div className="flex flex-wrap items-center gap-2 text-xs">
            <Badge variant={failed ? 'danger' : ship.phase === 'done' ? 'success' : 'info'}>{PHASE_LABEL[ship.phase] ?? ship.phase}</Badge>
            {ship.prNumber ? <span className="text-admin-fg-muted">PR #{ship.prNumber}</span> : null}
            {prUrl ? (
              <a href={prUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-[var(--admin-primary)] underline">
                Pull request <ExternalLink className="h-3 w-3" aria-hidden="true" />
              </a>
            ) : null}
            {runUrl ? (
              <a href={runUrl} target="_blank" rel="noopener noreferrer" className="inline-flex items-center gap-1 text-[var(--admin-primary)] underline">
                Actions run <ExternalLink className="h-3 w-3" aria-hidden="true" />
              </a>
            ) : null}
            {ship.mergeSha ? <span className="font-mono text-admin-fg-muted">merge {ship.mergeSha.slice(0, 12)}</span> : null}
            {onRefresh ? (
              <Button variant="ghost" size="sm" onClick={onRefresh} className="ml-auto">
                Refresh
              </Button>
            ) : null}
          </div>
          <ol className="grid gap-1 text-xs sm:grid-cols-2" aria-label="Ship progress">
            {SHIP_PHASES.map((phase, index) => {
              const done = !failed && (ship.phase === 'done' || index < currentIndex);
              const current = index === currentIndex && !failed && ship.phase !== 'done';
              return (
                <li key={phase} className={cn('flex items-center gap-1.5', current ? 'font-semibold text-admin-fg-strong' : 'text-admin-fg-muted')}>
                  {done ? (
                    <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600" aria-hidden="true" />
                  ) : current ? (
                    <Loader2 className="h-3.5 w-3.5 animate-spin motion-reduce:animate-none" aria-hidden="true" />
                  ) : (
                    <Circle className="h-3.5 w-3.5" aria-hidden="true" />
                  )}
                  {PHASE_LABEL[phase]}
                </li>
              );
            })}
          </ol>
          {failed ? (
            <p className="flex items-start gap-1.5 text-xs text-red-700 dark:text-red-300" role="alert">
              <XCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              <VisibleText text={ship.error ?? 'Ship failed.'} level="prose" />
            </p>
          ) : null}
        </div>
      ) : null}

      <div>
        <p className="mb-1 text-[10px] font-semibold uppercase tracking-wide text-admin-fg-muted">Ship log</p>
        {log.length === 0 ? (
          <p className="text-xs text-admin-fg-muted">No ship activity in this session yet.</p>
        ) : (
          <pre className="max-h-72 overflow-auto rounded-lg bg-admin-bg-subtle p-2 font-mono text-[11px] leading-5" data-testid="ship-log">
            {log.map((entry) => (
              <span
                key={entry.seq}
                className={cn(
                  'block',
                  entry.level === 'error' && 'text-red-700 dark:text-red-300',
                  entry.level === 'warn' && 'text-amber-700 dark:text-amber-300',
                )}
              >
                {entry.ts ? `${new Date(entry.ts).toLocaleTimeString()} ` : ''}[{entry.phase}] <VisibleText text={entry.message} level="prose" />
              </span>
            ))}
          </pre>
        )}
      </div>
    </div>
  );
}
