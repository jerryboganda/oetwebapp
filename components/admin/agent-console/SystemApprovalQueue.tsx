'use client';

import { useMemo } from 'react';
import { ShieldAlert } from 'lucide-react';
import { useOwnerAgentSession } from '@/hooks/use-owner-agent';
import { SYSTEM_QUEUE_SESSION_ID, type ApprovalRequest } from '@/lib/owner-agent/types';
import { ApprovalCard } from './ApprovalCard';

export interface SystemApprovalQueueProps {
  enabled: boolean;
  /**
   * `ConsoleStatus.systemApprovals` from the last status poll. Shown until the
   * stream has replayed anything, so a waiting request is visible even before
   * (or without) a live hub connection.
   */
  statusApprovals?: readonly ApprovalRequest[] | null;
}

/**
 * Global "system" approval queue: egress/docker proxy requests whose session
 * is unknown or absent (CONTRACT §6). Streamed like a session under the
 * sidecar's system pseudo-session id (an all-zero ULID).
 */
export function SystemApprovalQueue({ enabled, statusApprovals }: SystemApprovalQueueProps) {
  const queue = useOwnerAgentSession(SYSTEM_QUEUE_SESSION_ID, { enabled, loadDetail: false });
  const { lastSeq, resolvedApprovals } = queue.model;
  const streamPending = queue.pendingApprovals;

  const pending = useMemo(() => {
    if (lastSeq > 0 || !statusApprovals) return streamPending;
    return statusApprovals.filter((approval) => approval && !resolvedApprovals[approval.approvalId]);
  }, [lastSeq, statusApprovals, streamPending, resolvedApprovals]);

  return (
    <section aria-labelledby="owner-agent-system-queue" className="space-y-2" data-testid="system-approval-queue">
      <h2 id="owner-agent-system-queue" className="flex items-center gap-2 text-sm font-semibold text-admin-fg-strong">
        <ShieldAlert className="h-4 w-4" aria-hidden="true" /> System approvals
        <span className="text-xs font-normal text-admin-fg-muted">
          {pending.length > 0 ? `${pending.length} waiting` : 'none waiting'}
          {queue.connectionState !== 'connected' && enabled ? ` · ${queue.connectionState}` : ''}
        </span>
      </h2>
      {pending.length > 0 ? (
        <div className="grid gap-3 lg:grid-cols-2">
          {pending.map((approval) => (
            <ApprovalCard
              key={approval.approvalId}
              approval={approval}
              context="system (no session)"
              onDecide={(decision, note) => queue.decide(approval, decision, note)}
            />
          ))}
        </div>
      ) : null}
    </section>
  );
}
