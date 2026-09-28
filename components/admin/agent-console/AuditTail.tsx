'use client';

import { useCallback, useEffect, useState } from 'react';
import { RefreshCw } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { describeOwnerAgentError, getAudit } from '@/lib/owner-agent/api';
import type { OwnerAgentAuditEvent } from '@/lib/owner-agent/types';
import { VisibleText } from './VisibleText';

const MAX_DETAILS_CHARS = 400;

function formatWhen(value: string): string {
  const parsed = Date.parse(value);
  return Number.isFinite(parsed) ? new Date(parsed).toLocaleString() : value;
}

/** `details` is the sanitized JSON object the API stored; render it as compact text. */
export function formatAuditDetails(details: unknown): string {
  if (details === null || details === undefined) return '';
  if (typeof details === 'string') return details;
  try {
    return JSON.stringify(details) ?? '';
  } catch {
    return '';
  }
}

/** Latest OwnerAgent audit events (hash-chained server-side). */
export function AuditTail({ take = 100 }: { take?: number }) {
  const [rows, setRows] = useState<OwnerAgentAuditEvent[]>([]);
  const [chainIntact, setChainIntact] = useState<boolean | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const page = await getAudit(take);
      setRows(page.items);
      setChainIntact(page.chainIntact);
    } catch (err) {
      setError(describeOwnerAgentError(err, 'Audit log unavailable.'));
    } finally {
      setLoading(false);
    }
  }, [take]);

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <Card data-testid="audit-tail">
      <CardHeader>
        <CardTitle className="flex items-center gap-2 text-base">
          Audit tail
          {chainIntact === null || rows.length === 0 ? null : chainIntact ? (
            <Badge variant="success">Hash chain intact</Badge>
          ) : (
            <Badge variant="danger">Hash chain broken</Badge>
          )}
        </CardTitle>
        <Button variant="outline" size="sm" onClick={() => void load()} loading={loading}>
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" /> Refresh
        </Button>
      </CardHeader>
      <CardContent>
        {error ? (
          <p className="mb-2 text-xs text-red-700 dark:text-red-300" role="alert">
            {error}
          </p>
        ) : null}
        {rows.length === 0 && !loading ? (
          <p className="text-xs text-admin-fg-muted">No audit events yet.</p>
        ) : (
          <div className="max-h-[28rem] overflow-auto rounded-lg border border-admin-border">
            <table className="min-w-full text-left text-xs">
              <thead className="sticky top-0 bg-admin-bg-subtle text-2xs uppercase tracking-wide text-admin-fg-muted">
                <tr>
                  <th scope="col" className="px-3 py-2">When</th>
                  <th scope="col" className="px-3 py-2">Action</th>
                  <th scope="col" className="px-3 py-2">Actor</th>
                  <th scope="col" className="px-3 py-2">Details</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-admin-border">
                {rows.map((row) => {
                  const details = formatAuditDetails(row.details);
                  return (
                    <tr key={row.id}>
                      <td className="whitespace-nowrap px-3 py-1.5 text-admin-fg-muted">{formatWhen(row.occurredAt)}</td>
                      <td className="whitespace-nowrap px-3 py-1.5 font-mono">
                        {row.action}
                        {row.hashValid === false ? (
                          <Badge variant="danger" className="ml-1.5">
                            hash mismatch
                          </Badge>
                        ) : null}
                      </td>
                      <td className="whitespace-nowrap px-3 py-1.5">{row.actorName || row.actorId || '—'}</td>
                      <td className="max-w-[32rem] px-3 py-1.5 font-mono text-admin-fg-muted">
                        {details ? <VisibleText text={details.slice(0, MAX_DETAILS_CHARS)} level="prose" /> : '—'}
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        )}
      </CardContent>
    </Card>
  );
}
