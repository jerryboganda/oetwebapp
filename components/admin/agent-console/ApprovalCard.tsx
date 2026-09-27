'use client';

import { useEffect, useMemo, useState } from 'react';
import { AlertTriangle, Clock, ShieldAlert } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import {
  countHiddenCharacters,
  countNonAscii,
  findDecodableBase64,
  hasRightToLeftCharacters,
} from '@/lib/owner-agent/text-safety';
import { AGENT_UID, type ApprovalDecision, type ApprovalRequest, type ApprovalResolvedBy } from '@/lib/owner-agent/types';
import { VisibleText } from './VisibleText';

export interface ApprovalCardProps {
  approval: ApprovalRequest;
  /** Omit to render read-only (e.g. already resolved in the transcript). */
  onDecide?: (decision: ApprovalDecision, note?: string) => Promise<void> | void;
  resolution?: { decision: ApprovalDecision; by: ApprovalResolvedBy } | null;
  /** Session title/id shown on the global system queue. */
  context?: string;
  className?: string;
}

function describeUid(uid: number): string {
  if (uid === AGENT_UID) return `${uid} (agent)`;
  if (uid === 0) return '0 (console)';
  if (uid < 0) return 'unknown';
  return String(uid);
}

function useSecondsLeft(expiresAt: string): number | null {
  const target = useMemo(() => {
    const parsed = Date.parse(expiresAt);
    return Number.isFinite(parsed) ? parsed : null;
  }, [expiresAt]);
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (target === null) return;
    const id = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(id);
  }, [target]);
  return target === null ? null : Math.max(0, Math.floor((target - now) / 1000));
}

function formatCountdown(seconds: number): string {
  const m = Math.floor(seconds / 60);
  const s = seconds % 60;
  return `${m}:${String(s).padStart(2, '0')}`;
}

const DECISION_LABEL: Record<ApprovalDecision, string> = {
  approve: 'Approved',
  deny: 'Denied',
  approve_session: 'Approved for session',
};

/**
 * One tool call waiting for the owner. Shows the FULL command with every
 * control / bidi / zero-width character made visible, decoded previews of
 * base64 payloads, and where it runs (cwd, uid, target container/host).
 * "Approve for session" is unavailable on tainted turns (grants are dropped).
 */
export function ApprovalCard({ approval, onDecide, resolution, context, className }: ApprovalCardProps) {
  const [note, setNote] = useState('');
  const [busy, setBusy] = useState<ApprovalDecision | null>(null);
  const [error, setError] = useState<string | null>(null);
  const secondsLeft = useSecondsLeft(approval.expiresAt);
  const expired = secondsLeft === 0;

  const command = approval.command ?? '';
  const analysis = useMemo(() => {
    const scanned = `${approval.summary}\n${command}\n${approval.target ?? ''}\n${approval.cwd ?? ''}`;
    return {
      hidden: countHiddenCharacters(scanned),
      nonAscii: countNonAscii(command),
      rtl: hasRightToLeftCharacters(scanned),
      base64: findDecodableBase64(command),
    };
  }, [approval.summary, approval.target, approval.cwd, command]);

  const decide = async (decision: ApprovalDecision) => {
    if (!onDecide || busy) return;
    setBusy(decision);
    setError(null);
    try {
      await onDecide(decision, note.trim() || undefined);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Decision failed.');
    } finally {
      setBusy(null);
    }
  };

  const actionable = Boolean(onDecide) && !resolution && !expired;

  return (
    <Card
      surface={resolution ? 'default' : 'tinted-warning'}
      className={cn('overflow-hidden', className)}
      data-testid="approval-card"
      aria-label={`Approval request: ${approval.summary}`}
    >
      <CardHeader className="flex-wrap gap-2 pb-2">
        <CardTitle className="flex min-w-0 items-center gap-2 text-sm">
          <ShieldAlert className="h-4 w-4 shrink-0" aria-hidden="true" />
          <span className="min-w-0 break-words">
            <VisibleText text={approval.summary || 'Tool call needs approval'} level="prose" />
          </span>
        </CardTitle>
        <div className="flex flex-wrap items-center gap-1.5">
          {approval.tainted ? <Badge variant="danger">Tainted turn</Badge> : null}
          {resolution ? (
            <Badge variant={resolution.decision === 'deny' ? 'danger' : 'success'}>
              {DECISION_LABEL[resolution.decision]} · {resolution.by.replace('_', ' ')}
            </Badge>
          ) : secondsLeft !== null ? (
            <Badge variant={expired ? 'muted' : 'warning'}>
              <Clock className="mr-1 inline h-3 w-3" aria-hidden="true" />
              {expired ? 'Expired' : formatCountdown(secondsLeft)}
            </Badge>
          ) : null}
        </div>
      </CardHeader>
      <CardContent className="space-y-3 pt-0 text-xs">
        {context ? <p className="text-admin-fg-muted">Session: {context}</p> : null}

        {command ? (
          <div>
            <p className="mb-1 font-semibold uppercase tracking-wide text-[10px] text-admin-fg-muted">Full command</p>
            <pre
              className="max-h-80 overflow-auto rounded-lg border border-admin-border bg-admin-bg-subtle p-3 font-mono text-xs leading-5 text-admin-fg-default"
              data-testid="approval-command"
            >
              <VisibleText text={command} level="strict" />
            </pre>
          </div>
        ) : null}

        {analysis.hidden > 0 || analysis.rtl || analysis.nonAscii > 0 ? (
          <div className="flex items-start gap-2 rounded-lg border border-red-300 bg-red-50 p-2 text-red-800 dark:border-red-800 dark:bg-red-950 dark:text-red-200" role="alert">
            <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <div className="space-y-0.5">
              {analysis.hidden > 0 ? <p>{analysis.hidden} hidden or control character(s) shown as markers.</p> : null}
              {analysis.rtl ? <p>Contains right-to-left characters; shown in logical order.</p> : null}
              {analysis.nonAscii > 0 ? <p>{analysis.nonAscii} non-ASCII character(s) in the command (check for look-alikes).</p> : null}
            </div>
          </div>
        ) : null}

        {analysis.base64.length > 0 ? (
          <div>
            <p className="mb-1 font-semibold uppercase tracking-wide text-[10px] text-admin-fg-muted">Decoded base64</p>
            <ul className="space-y-1.5">
              {analysis.base64.map((segment) => (
                <li key={segment.encoded} className="rounded-lg border border-admin-border bg-admin-bg-surface p-2">
                  <p className="truncate font-mono text-[10px] text-admin-fg-muted" title={segment.encoded}>
                    {segment.encoded.length > 60 ? `${segment.encoded.slice(0, 60)}…` : segment.encoded}
                  </p>
                  <pre className="mt-1 max-h-40 overflow-auto font-mono text-xs" data-testid="approval-base64-decoded">
                    <VisibleText text={segment.decoded.slice(0, 4096)} level="strict" />
                  </pre>
                </li>
              ))}
            </ul>
          </div>
        ) : null}

        <dl className="grid grid-cols-[auto_1fr] gap-x-3 gap-y-1">
          <dt className="text-admin-fg-muted">cwd</dt>
          <dd className="min-w-0 font-mono">{approval.cwd ? <VisibleText text={approval.cwd} /> : '—'}</dd>
          <dt className="text-admin-fg-muted">uid</dt>
          <dd className="font-mono">{describeUid(approval.uid)}</dd>
          <dt className="text-admin-fg-muted">target</dt>
          <dd className="min-w-0 font-mono">{approval.target ? <VisibleText text={approval.target} /> : '—'}</dd>
        </dl>

        {approval.reasons.length > 0 ? (
          <div>
            <p className="mb-1 font-semibold uppercase tracking-wide text-[10px] text-admin-fg-muted">Why it needs you</p>
            <ul className="list-disc space-y-0.5 pl-5">
              {approval.reasons.map((reason, index) => (
                <li key={`${reason}-${index}`}>
                  <VisibleText text={reason} level="prose" />
                </li>
              ))}
            </ul>
          </div>
        ) : null}

        {actionable ? (
          <div className="space-y-2">
            <label className="block">
              <span className="sr-only">Note for the audit log (optional)</span>
              <input
                type="text"
                value={note}
                maxLength={500}
                onChange={(event) => setNote(event.target.value)}
                placeholder="Note (optional, audited)"
                className="h-9 w-full rounded-lg border border-admin-border bg-admin-bg-surface px-3 text-xs"
                autoComplete="off"
              />
            </label>
            <div className="flex flex-wrap gap-2">
              <Button variant="primary" size="sm" loading={busy === 'approve'} disabled={busy !== null} onClick={() => void decide('approve')}>
                Approve
              </Button>
              <Button variant="destructive" size="sm" loading={busy === 'deny'} disabled={busy !== null} onClick={() => void decide('deny')}>
                Deny
              </Button>
              <Button
                variant="outline"
                size="sm"
                loading={busy === 'approve_session'}
                disabled={busy !== null || approval.tainted}
                title={approval.tainted ? 'Session-wide grants are not allowed on tainted turns.' : 'Allow matching calls for the rest of this session.'}
                onClick={() => void decide('approve_session')}
              >
                Approve for session
              </Button>
            </div>
          </div>
        ) : null}

        {error ? (
          <p className="text-red-700 dark:text-red-300" role="alert">
            {error}
          </p>
        ) : null}
      </CardContent>
    </Card>
  );
}
