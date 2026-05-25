'use client';

/**
 * OET Speaking — Phase 10 P10.2 — admin recording-access audit viewer.
 *
 * Surfaces every read/access of a learner speaking recording via
 * `/v1/admin/speaking/recordings/audit` for compliance review.
 */
import { useCallback, useEffect, useState } from 'react';
import { Shield } from 'lucide-react';
import { AdminTableLayout } from '@/components/admin/layout/admin-table-layout';
import { Card, CardContent } from '@/components/admin/ui/card';
import { Button } from '@/components/admin/ui/button';
import { Input } from '@/components/ui/form-controls';
import { Badge } from '@/components/admin/ui/badge';
import { Skeleton } from '@/components/admin/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';

const BREADCRUMBS = [
  { label: 'Admin', href: '/admin' },
  { label: 'Speaking', href: '/admin/speaking' },
  { label: 'Recordings', href: '/admin/speaking/recordings' },
  { label: 'Access audit' },
];
import {
  fetchSpeakingAccessAudit,
  type SpeakingAccessAuditFilter,
  type SpeakingAccessAuditRow,
} from '@/lib/api/speaking-compliance';

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

export default function AdminSpeakingRecordingsAuditPage() {
  const [filter, setFilter] = useState<SpeakingAccessAuditFilter>({ limit: 100 });
  const [rows, setRows] = useState<SpeakingAccessAuditRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const reload = useCallback(async () => {
    setLoading(true);
    setError(null);
    try {
      const res = await fetchSpeakingAccessAudit(filter);
      setRows(res.rows);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load audit log.');
    } finally {
      setLoading(false);
    }
  }, [filter]);

  useEffect(() => {
    reload();
  }, [reload]);

  function update<K extends keyof SpeakingAccessAuditFilter>(
    key: K,
    value: SpeakingAccessAuditFilter[K],
  ) {
    setFilter((f) => ({ ...f, [key]: value || undefined }));
  }

  return (
    <AdminTableLayout
      title="Speaking recordings · access audit"
      description="Every read of a learner's speaking recording is logged here. Each row maps to one AuditEvent."
      breadcrumbs={BREADCRUMBS}
      eyebrow="Compliance"
      icon={<Shield className="h-5 w-5" />}
      banner={(
        <Card>
          <CardContent className="space-y-4 p-4">
            <div className="grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
              <Input
                placeholder="Recording ID"
                value={filter.recordingId ?? ''}
                onChange={(e) => update('recordingId', e.target.value)}
              />
              <Input
                placeholder="Learner email or ID"
                value={filter.learnerEmailOrId ?? ''}
                onChange={(e) => update('learnerEmailOrId', e.target.value)}
              />
              <Input
                placeholder="Tutor / actor email or ID"
                value={filter.tutorEmailOrId ?? ''}
                onChange={(e) => update('tutorEmailOrId', e.target.value)}
              />
              <Input
                type="number"
                min={1}
                max={500}
                placeholder="Limit (max 500)"
                value={filter.limit ?? 100}
                onChange={(e) => update('limit', Number(e.target.value) || undefined)}
              />
              <Input
                type="datetime-local"
                value={filter.from ?? ''}
                onChange={(e) => update('from', e.target.value)}
              />
              <Input
                type="datetime-local"
                value={filter.to ?? ''}
                onChange={(e) => update('to', e.target.value)}
              />
            </div>
            <div className="flex justify-end gap-2">
              <Button variant="outline" onClick={() => setFilter({ limit: 100 })} disabled={loading}>
                Reset
              </Button>
              <Button onClick={reload} disabled={loading}>
                {loading ? 'Loading…' : 'Apply filters'}
              </Button>
            </div>
            {error && <InlineAlert variant="error">{error}</InlineAlert>}
          </CardContent>
        </Card>
      )}
    >
      {!rows ? (
        <div className="p-6">
          <Skeleton className="h-64 w-full rounded-admin-lg" />
        </div>
      ) : rows.length === 0 ? (
        <div className="p-8 text-center text-admin-fg-muted">No audit entries match the filter.</div>
      ) : (
        <div className="overflow-x-auto">
          <table className="w-full text-sm">
            <thead className="bg-admin-bg-subtle text-left text-xs uppercase tracking-wide text-admin-fg-muted">
              <tr>
                <th className="p-3">When</th>
                <th className="p-3">Action</th>
                <th className="p-3">Recording</th>
                <th className="p-3">Learner</th>
                <th className="p-3">Actor</th>
                <th className="p-3">Purpose / reason</th>
              </tr>
            </thead>
            <tbody>
              {rows.map((row) => (
                <tr key={row.auditEventId} className="border-t border-admin-border align-top">
                  <td className="p-3 text-xs font-mono text-admin-fg-muted">
                    {formatDate(row.occurredAt)}
                  </td>
                  <td className="p-3">
                    <Badge variant={row.action.endsWith('Deleted') ? 'danger' : 'info'}>
                      {row.action}
                    </Badge>
                  </td>
                  <td className="p-3 text-xs font-mono">
                    {row.recordingId ?? '—'}
                    {row.sessionId && (
                      <div className="text-[10px] text-admin-fg-muted">session {row.sessionId}</div>
                    )}
                  </td>
                  <td className="p-3 text-xs font-mono">{row.learnerUserId ?? '—'}</td>
                  <td className="p-3">
                    <div className="text-sm text-admin-fg-strong">{row.actorName}</div>
                    <div className="text-[10px] text-admin-fg-muted">
                      {row.actorRole ?? 'unknown role'}
                    </div>
                  </td>
                  <td className="p-3 text-xs text-admin-fg-muted">
                    {row.purpose || row.reason || '—'}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </AdminTableLayout>
  );
}
