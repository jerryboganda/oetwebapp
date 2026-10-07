'use client';

import Link from 'next/link';
import { Badge, type BadgeProps } from '@/components/ui/badge';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Bot } from 'lucide-react';
import type { SessionStatus, SessionSummary } from '@/lib/owner-agent/types';
import { ENGINE_LABEL } from './EngineModelEffortPicker';
import { MODE_LABEL } from './ModeToggle';

export const SESSION_STATUS_BADGE: Record<SessionStatus, { label: string; variant: NonNullable<BadgeProps['variant']> }> = {
  idle: { label: 'Idle', variant: 'muted' },
  running: { label: 'Running', variant: 'info' },
  awaiting_approval: { label: 'Awaiting approval', variant: 'warning' },
  interrupted: { label: 'Interrupted', variant: 'muted' },
  error: { label: 'Error', variant: 'danger' },
  archived: { label: 'Archived', variant: 'outline' },
};

function formatWhen(value: string): string {
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toLocaleString() : '—';
}

export interface SessionsListProps {
  sessions: readonly SessionSummary[];
  loading?: boolean;
  error?: string | null;
  onCreate?: () => void;
}

export function SessionsList({ sessions, loading = false, error, onCreate }: SessionsListProps) {
  if (!loading && sessions.length === 0 && !error) {
    return (
      <EmptyState
        size="sm"
        illustration={<Bot />}
        title="No sessions yet"
        description="Start a session with Claude Code, Codex or Direct OpenCode gateway."
        primaryAction={onCreate ? { label: 'New session', onClick: onCreate } : undefined}
      />
    );
  }
  return (
    <div className="overflow-x-auto rounded-lg border border-admin-border" data-testid="sessions-list">
      {error ? (
        <p className="px-3 py-2 text-xs text-red-700 dark:text-red-300" role="alert">
          {error}
        </p>
      ) : null}
      <table className="min-w-full text-left text-xs">
        <thead className="bg-admin-bg-subtle text-2xs uppercase tracking-wide text-admin-fg-muted">
          <tr>
            <th scope="col" className="px-3 py-2 font-semibold">Session</th>
            <th scope="col" className="px-3 py-2 font-semibold">Engine / model</th>
            <th scope="col" className="px-3 py-2 font-semibold">Mode</th>
            <th scope="col" className="px-3 py-2 font-semibold">Status</th>
            <th scope="col" className="px-3 py-2 font-semibold">Branch</th>
            <th scope="col" className="px-3 py-2 font-semibold">Updated</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-admin-border">
          {sessions.map((session) => {
            const status = SESSION_STATUS_BADGE[session.status] ?? SESSION_STATUS_BADGE.idle;
            return (
              <tr key={session.id} className="hover:bg-admin-bg-subtle">
                <td className="max-w-[18rem] px-3 py-2">
                  <Link
                    href={`/admin/agent-console/${encodeURIComponent(session.id)}`}
                    className="block truncate font-medium text-admin-fg-strong underline-offset-2 hover:underline"
                  >
                    {session.title || session.id}
                  </Link>
                </td>
                <td className="px-3 py-2">
                  <span className="font-medium">{ENGINE_LABEL[session.engine] ?? session.engine}</span>
                  <span className="ml-1 font-mono text-admin-fg-muted">
                    {session.model}
                    {session.effort ? ` · ${session.effort}` : ''}
                  </span>
                </td>
                <td className="px-3 py-2">
                  <span className="inline-flex items-center gap-1">
                    <Badge variant={session.mode === 'autopilot' ? 'warning' : 'outline'}>{MODE_LABEL[session.mode] ?? session.mode}</Badge>
                    {session.tainted ? <Badge variant="danger">Tainted</Badge> : null}
                  </span>
                </td>
                <td className="px-3 py-2">
                  <Badge variant={status.variant}>{status.label}</Badge>
                </td>
                <td className="max-w-[14rem] truncate px-3 py-2 font-mono text-admin-fg-muted">{session.branch}</td>
                <td className="whitespace-nowrap px-3 py-2 text-admin-fg-muted">{formatWhen(session.updatedAt)}</td>
              </tr>
            );
          })}
        </tbody>
      </table>
    </div>
  );
}
