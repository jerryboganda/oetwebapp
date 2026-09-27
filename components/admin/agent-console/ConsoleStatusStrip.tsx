'use client';

import { useState } from 'react';
import { Activity, AlertOctagon, Download, Play, Timer } from 'lucide-react';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Modal } from '@/components/ui/modal';
import { describeOwnerAgentError } from '@/lib/owner-agent/api';
import {
  OWNER_AGENT_ENGINES,
  type ApplyUpdateResult,
  type ConsoleStatus,
  type EngineAuth,
  type KillSwitchResult,
} from '@/lib/owner-agent/types';
import { ENGINE_LABEL } from './EngineModelEffortPicker';
import { RateLimitBadge } from './RateLimitBadge';

const AUTH_BADGE: Record<EngineAuth['state'], { label: string; variant: NonNullable<BadgeProps['variant']> }> = {
  signed_in: { label: 'Signed in', variant: 'success' },
  signing_in: { label: 'Signing in', variant: 'info' },
  signed_out: { label: 'Signed out', variant: 'muted' },
  error: { label: 'Auth error', variant: 'danger' },
};

function formatTime(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toLocaleTimeString() : '—';
}

export interface ConsoleStatusStripProps {
  status: ConsoleStatus | null;
  statusError?: string | null;
  leaseExpiresAt?: string | null;
  leaseError?: string | null;
  onKillSwitch: () => Promise<KillSwitchResult>;
  onApplyUpdate: () => Promise<ApplyUpdateResult>;
  /** Accept new turns again after the kill switch or a drain (API POST /resume). */
  onResume?: () => Promise<unknown>;
}

type Confirm = 'kill' | 'update' | null;

/** Engine auth + rate limits, active turns, lease, update pending, kill switch. */
export function ConsoleStatusStrip({
  status,
  statusError,
  leaseExpiresAt,
  leaseError,
  onKillSwitch,
  onApplyUpdate,
  onResume,
}: ConsoleStatusStripProps) {
  const [confirm, setConfirm] = useState<Confirm>(null);
  const [busy, setBusy] = useState(false);
  const [message, setMessage] = useState<{ tone: 'ok' | 'error'; text: string } | null>(null);

  const resume = async () => {
    if (!onResume || busy) return;
    setBusy(true);
    setMessage(null);
    try {
      await onResume();
      setMessage({ tone: 'ok', text: 'The console accepts new turns again.' });
    } catch (error) {
      setMessage({ tone: 'error', text: describeOwnerAgentError(error, 'Request failed.') });
    } finally {
      setBusy(false);
    }
  };

  const run = async () => {
    if (!confirm || busy) return;
    setBusy(true);
    setMessage(null);
    try {
      if (confirm === 'kill') {
        const result = await onKillSwitch();
        setMessage({ tone: 'ok', text: `Stopped ${result?.stoppedTurns ?? 0} turn(s) and ${result?.killedProcesses ?? 0} process(es).` });
      } else {
        const result = await onApplyUpdate();
        setMessage(
          result?.dispatched === false
            ? {
                tone: 'error',
                text: typeof result.instructions === 'string' && result.instructions
                  ? result.instructions
                  : 'The console is draining, but the update workflow was not dispatched. Run agent-console.yml with apply=true.',
              }
            : { tone: 'ok', text: 'Update requested: the console drains, then the deploy workflow recreates it.' },
        );
      }
      setConfirm(null);
    } catch (error) {
      setMessage({ tone: 'error', text: describeOwnerAgentError(error, 'Request failed.') });
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="space-y-3" data-testid="console-status-strip">
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2 xl:grid-cols-4">
        {OWNER_AGENT_ENGINES.map((engine) => {
          const engineStatus = status?.engines?.[engine];
          const auth = engineStatus?.auth;
          const badge = auth ? AUTH_BADGE[auth.state] ?? AUTH_BADGE.error : null;
          return (
            <Card key={engine}>
              <CardContent className="space-y-1.5 p-3 pt-3 text-xs">
                <div className="flex items-center justify-between gap-2">
                  <span className="font-semibold text-admin-fg-strong">{ENGINE_LABEL[engine]}</span>
                  {badge ? <Badge variant={badge.variant}>{badge.label}</Badge> : <Badge variant="muted">unknown</Badge>}
                </div>
                <p className="truncate text-admin-fg-muted">
                  {auth?.account?.plan ?? '—'}
                  {auth?.account?.workspace ? ` · ${auth.account.workspace}` : ''}
                  {engineStatus?.version ? ` · v${engineStatus.version}` : ''}
                </p>
                <RateLimitBadge limits={engineStatus?.rateLimits} />
              </CardContent>
            </Card>
          );
        })}
        <Card>
          <CardContent className="space-y-1.5 p-3 pt-3 text-xs">
            <div className="flex items-center gap-2 font-semibold text-admin-fg-strong">
              <Activity className="h-3.5 w-3.5" aria-hidden="true" /> Turns
            </div>
            <p className="text-lg font-semibold text-admin-fg-strong">
              {status ? `${status.activeTurns} / ${status.maxConcurrentTurns}` : '—'}
            </p>
            <div className="flex flex-wrap gap-1">
              {status?.killed ? <Badge variant="danger">Killed</Badge> : null}
              {status?.draining ? <Badge variant="warning">Draining</Badge> : null}
              {status?.version ? <Badge variant="outline">v{status.version}</Badge> : null}
            </div>
          </CardContent>
        </Card>
        <Card>
          <CardContent className="space-y-1.5 p-3 pt-3 text-xs">
            <div className="flex items-center gap-2 font-semibold text-admin-fg-strong">
              <Timer className="h-3.5 w-3.5" aria-hidden="true" /> Lease
            </div>
            <p className="text-admin-fg-muted">
              Heartbeat until <strong className="text-admin-fg-default">{formatTime(leaseExpiresAt ?? status?.lease?.expiresAt)}</strong>
            </p>
            {leaseError ? <p className="text-red-700 dark:text-red-300">{leaseError}</p> : null}
            <div className="flex flex-wrap gap-2 pt-1">
              <Button variant="destructive" size="sm" onClick={() => setConfirm('kill')}>
                <AlertOctagon className="h-3.5 w-3.5" aria-hidden="true" /> Kill switch
              </Button>
              {onResume && (status?.killed || status?.draining) ? (
                <Button variant="outline" size="sm" loading={busy} onClick={() => void resume()}>
                  <Play className="h-3.5 w-3.5" aria-hidden="true" /> Resume
                </Button>
              ) : null}
              {status?.updatePending ? (
                <Button variant="outline" size="sm" onClick={() => setConfirm('update')}>
                  <Download className="h-3.5 w-3.5" aria-hidden="true" /> Apply update
                </Button>
              ) : null}
            </div>
          </CardContent>
        </Card>
      </div>
      {statusError ? (
        <p className="text-xs text-red-700 dark:text-red-300" role="alert">
          Status: {statusError}
        </p>
      ) : null}
      {message ? (
        <p className={message.tone === 'ok' ? 'text-xs text-emerald-700 dark:text-emerald-300' : 'text-xs text-red-700 dark:text-red-300'} role="status">
          {message.text}
        </p>
      ) : null}

      <Modal
        open={confirm !== null}
        onClose={() => (busy ? undefined : setConfirm(null))}
        title={confirm === 'kill' ? 'Stop all agent activity?' : 'Apply the console update?'}
        size="sm"
      >
        <div className="space-y-4 text-sm">
          <p className="text-admin-fg-default">
            {confirm === 'kill'
              ? 'Aborts every running turn, kills all agent processes and stops containers the agent started. Sessions stay resumable.'
              : 'Drains the console (no new turns), then dispatches the agent-console deploy workflow to recreate it on the new image.'}
          </p>
          <div className="flex justify-end gap-2">
            <Button variant="ghost" size="sm" onClick={() => setConfirm(null)} disabled={busy}>
              Cancel
            </Button>
            <Button variant={confirm === 'kill' ? 'destructive' : 'primary'} size="sm" loading={busy} onClick={() => void run()}>
              {confirm === 'kill' ? 'Stop everything' : 'Apply update'}
            </Button>
          </div>
        </div>
      </Modal>
    </div>
  );
}
