'use client';

import { useEffect, useState, type MouseEvent } from 'react';
import Link from 'next/link';
import { useRouter } from 'next/navigation';
import { History as HistoryIcon, Search } from 'lucide-react';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Input } from '@/components/admin/ui/input';
import { NativeSelect } from '@/components/admin/ui/native-select';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { useOwnerAgentHistory } from '@/hooks/use-owner-agent';
import { listOwners } from '@/lib/owner-agent/api';
import {
  OWNER_AGENT_ENGINES,
  OWNER_AGENT_SESSIONS_QUERY_MAX,
  OWNER_AGENT_SESSION_STATUSES,
  isEngine,
  isSessionStatus,
  type Engine,
  type SessionStatus,
  type SessionSummary,
} from '@/lib/owner-agent/types';
import { ENGINE_LABEL } from './EngineModelEffortPicker';
import { MODE_LABEL } from './ModeToggle';
import { SESSION_STATUS_BADGE } from './SessionsList';

export const HISTORY_SEARCH_DEBOUNCE_MS = 300;

function formatWhen(value: string | null | undefined): string {
  if (!value) return '—';
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toLocaleString() : '—';
}

/** Session cost in USD (subscription engines may not report one). */
export function formatSessionCost(costUsd: number | null | undefined): string {
  if (typeof costUsd !== 'number' || !Number.isFinite(costUsd)) return '—';
  if (costUsd > 0 && costUsd < 0.01) return '<$0.01';
  return `$${costUsd.toFixed(2)}`;
}

/**
 * "Started by" label. The sidecar records the admin's auth account id; no
 * existing admin API resolves an auth account id to an email in one call
 * (the admin users list has no account id, the detail is keyed by user id),
 * so the id is shown shortened with the full value in the tooltip.
 */
export function formatStartedBy(
  createdBy: string | null | undefined,
  emails?: ReadonlyMap<string, string>,
): { label: string; title: string | undefined } {
  if (typeof createdBy !== 'string' || !createdBy.trim()) return { label: '—', title: 'Not recorded (older session)' };
  const id = createdBy.trim();
  const email = emails?.get(id.toLowerCase());
  if (email) return { label: email, title: `Admin account id ${id}` };
  return { label: id.length > 10 ? `Account ${id.slice(0, 8)}…` : `Account ${id}`, title: `Admin account id ${id}` };
}

function sessionHref(id: string): string {
  return `/admin/agent-console/${encodeURIComponent(id)}`;
}

export interface HistoryTableProps {
  rows: readonly SessionSummary[];
  loading?: boolean;
  error?: string | null;
  hasMore?: boolean;
  loadingMore?: boolean;
  onLoadMore?: () => void;
  onRetry?: () => void;
  /** Filters are active, so an empty result means "no match" rather than "no sessions". */
  filtered?: boolean;
  /** Owner account id (lower-cased) → email, from GET /owners. */
  ownerEmails?: ReadonlyMap<string, string>;
}

/** Dense, scannable session history. A row opens the session's full transcript replay. */
export function HistoryTable({
  rows,
  loading = false,
  error,
  hasMore = false,
  loadingMore = false,
  onLoadMore,
  onRetry,
  filtered = false,
  ownerEmails,
}: HistoryTableProps) {
  const router = useRouter();

  if (error && rows.length === 0 && !loading) {
    return (
      <EmptyState
        variant="error"
        size="sm"
        title="History unavailable"
        description={error}
        primaryAction={onRetry ? { label: 'Retry', onClick: onRetry } : undefined}
      />
    );
  }

  if (!loading && rows.length === 0) {
    return (
      <EmptyState
        size="sm"
        illustration={<HistoryIcon />}
        title={filtered ? 'No sessions match' : 'No sessions yet'}
        description={filtered ? 'Try a different search or clear the filters.' : 'Sessions you start appear here with their full transcript.'}
      />
    );
  }

  const openRow = (event: MouseEvent<HTMLTableRowElement>, id: string) => {
    // Links and buttons inside the row handle their own clicks.
    if ((event.target as HTMLElement | null)?.closest('a,button')) return;
    router.push(sessionHref(id));
  };

  return (
    <div className="space-y-3">
      <div className="overflow-x-auto rounded-lg border border-admin-border" data-testid="history-table">
        {error ? (
          <p className="px-3 py-2 text-xs text-red-700 dark:text-red-300" role="alert">
            {error}
          </p>
        ) : null}
        <table className="min-w-full text-left text-xs">
          <thead className="bg-admin-bg-subtle text-2xs uppercase tracking-wide text-admin-fg-muted">
            <tr>
              <th scope="col" className="px-3 py-2 font-semibold">Session</th>
              <th scope="col" className="px-3 py-2 font-semibold">Started by</th>
              <th scope="col" className="px-3 py-2 font-semibold">Engine / model</th>
              <th scope="col" className="px-3 py-2 font-semibold">Mode</th>
              <th scope="col" className="px-3 py-2 font-semibold">Status</th>
              <th scope="col" className="px-3 py-2 font-semibold">Created</th>
              <th scope="col" className="px-3 py-2 font-semibold">Last activity</th>
              <th scope="col" className="px-3 py-2 text-right font-semibold">Cost</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-admin-border">
            {rows.map((session) => {
              const status = SESSION_STATUS_BADGE[session.status] ?? SESSION_STATUS_BADGE.idle;
              const startedBy = formatStartedBy(session.createdBy, ownerEmails);
              return (
                <tr
                  key={session.id}
                  className="cursor-pointer align-top hover:bg-admin-bg-subtle"
                  onClick={(event) => openRow(event, session.id)}
                  data-testid="history-row"
                >
                  <td className="max-w-[24rem] px-3 py-2">
                    <Link
                      href={sessionHref(session.id)}
                      className="block truncate font-medium text-admin-fg-strong underline-offset-2 hover:underline"
                    >
                      {session.title || session.id}
                    </Link>
                    {session.firstMessage ? (
                      <p className="mt-0.5 line-clamp-2 break-words text-admin-fg-muted" title={session.firstMessage}>
                        {session.firstMessage}
                      </p>
                    ) : null}
                  </td>
                  <td className="whitespace-nowrap px-3 py-2 font-mono text-admin-fg-muted" title={startedBy.title}>
                    {startedBy.label}
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
                  <td className="whitespace-nowrap px-3 py-2 text-admin-fg-muted">{formatWhen(session.createdAt)}</td>
                  <td className="whitespace-nowrap px-3 py-2 text-admin-fg-muted">{formatWhen(session.updatedAt)}</td>
                  <td className="whitespace-nowrap px-3 py-2 text-right font-mono">{formatSessionCost(session.usage?.costUsd)}</td>
                </tr>
              );
            })}
          </tbody>
        </table>
        {loading ? (
          <p className="px-3 py-2 text-xs text-admin-fg-muted" role="status">
            Loading sessions…
          </p>
        ) : null}
      </div>
      {hasMore && onLoadMore ? (
        <div className="flex justify-center">
          <Button variant="outline" size="sm" loading={loadingMore} disabled={loading} onClick={onLoadMore}>
            Load more
          </Button>
        </div>
      ) : null}
    </div>
  );
}

function useDebouncedValue<T>(value: T, delayMs: number): T {
  const [debounced, setDebounced] = useState(value);
  useEffect(() => {
    const id = setTimeout(() => setDebounced(value), delayMs);
    return () => clearTimeout(id);
  }, [value, delayMs]);
  return debounced;
}

const ENGINE_OPTIONS = [
  { value: '', label: 'All engines' },
  ...OWNER_AGENT_ENGINES.map((engine) => ({ value: engine, label: ENGINE_LABEL[engine] ?? engine })),
];

const STATUS_OPTIONS = [
  { value: '', label: 'All statuses' },
  ...OWNER_AGENT_SESSION_STATUSES.map((status) => ({ value: status, label: SESSION_STATUS_BADGE[status]?.label ?? status })),
];

export interface SessionHistoryProps {
  /** Load only while the console is unlocked. */
  enabled: boolean;
  debounceMs?: number;
  pageSize?: number;
}

/**
 * Search (title / first message, debounced), engine and status filters,
 * "Show archived" (on by default here) and the paged history table.
 */
export function SessionHistory({ enabled, debounceMs = HISTORY_SEARCH_DEBOUNCE_MS, pageSize }: SessionHistoryProps) {
  const [search, setSearch] = useState('');
  const [engine, setEngine] = useState<Engine | ''>('');
  const [status, setStatus] = useState<SessionStatus | ''>('');
  const [includeArchived, setIncludeArchived] = useState(true);
  const q = useDebouncedValue(search.trim(), debounceMs);

  const history = useOwnerAgentHistory({ q, engine, status, includeArchived }, { enabled, pageSize });
  const filtered = Boolean(q || engine || status);
  const [ownerEmails, setOwnerEmails] = useState<ReadonlyMap<string, string>>(new Map());
  useEffect(() => {
    if (!enabled) return;
    let alive = true;
    listOwners()
      .then((owners) => {
        if (alive) setOwnerEmails(new Map(owners.map((o) => [o.accountId.toLowerCase(), o.email])));
      })
      .catch(() => {
        // Non-critical: the column falls back to the account id.
      });
    return () => {
      alive = false;
    };
  }, [enabled]);

  return (
    <section aria-labelledby="owner-agent-history" className="space-y-3">
      <h2 id="owner-agent-history" className="sr-only">
        Session history
      </h2>
      <div className="grid gap-3 md:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)_auto] md:items-end">
        <Input
          type="search"
          label="Search"
          placeholder="Title or first message"
          value={search}
          maxLength={OWNER_AGENT_SESSIONS_QUERY_MAX}
          onChange={(event) => setSearch(event.target.value)}
          startIcon={<Search className="h-4 w-4" aria-hidden="true" />}
        />
        <NativeSelect
          label="Engine"
          value={engine}
          options={ENGINE_OPTIONS}
          onChange={(event) => setEngine(isEngine(event.target.value) ? event.target.value : '')}
        />
        <NativeSelect
          label="Status"
          value={status}
          options={STATUS_OPTIONS}
          onChange={(event) => setStatus(isSessionStatus(event.target.value) ? event.target.value : '')}
        />
        <label className="inline-flex h-10 items-center gap-2 whitespace-nowrap text-xs text-admin-fg-muted">
          <input
            type="checkbox"
            checked={includeArchived}
            onChange={(event) => setIncludeArchived(event.target.checked)}
          />
          Show archived
        </label>
      </div>
      <HistoryTable
        rows={history.rows}
        loading={history.state === 'loading' || history.state === 'idle'}
        error={history.error}
        hasMore={history.hasMore}
        loadingMore={history.loadingMore}
        onLoadMore={() => void history.loadMore()}
        onRetry={() => void history.refresh()}
        filtered={filtered}
        ownerEmails={ownerEmails}
      />
    </section>
  );
}
