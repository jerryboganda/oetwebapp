'use client';

import { useCallback, useEffect, useState } from 'react';
import { Archive, RefreshCcw } from 'lucide-react';
import { Badge } from '@/components/admin/ui/badge';
import { Button } from '@/components/admin/ui/button';
import { Card, CardContent } from '@/components/admin/ui/card';
import { apiClient } from '@/lib/api';
import type { WritingContentAuditEntryDto, WritingContentAuditListDto } from '@/lib/writing/types';

function toneForAction(action: string) {
  if (action.includes('deleted')) return 'danger';
  if (action.includes('published') || action.includes('approved')) return 'success';
  if (action.includes('viewed')) return 'info';
  if (action.includes('created')) return 'primary';
  return 'warning';
}

export default function AdminWritingAuditPage() {
  const [items, setItems] = useState<WritingContentAuditEntryDto[]>([]);
  const [entityType, setEntityType] = useState('');
  const [action, setAction] = useState('');
  const [appliedEntityType, setAppliedEntityType] = useState('');
  const [appliedAction, setAppliedAction] = useState('');
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const load = useCallback(async () => {
    setBusy(true);
    try {
      const params = new URLSearchParams();
      if (appliedEntityType.trim()) params.set('entityType', appliedEntityType.trim());
      if (appliedAction.trim()) params.set('action', appliedAction.trim());
      params.set('pageSize', '100');
      const response = await apiClient.get<WritingContentAuditListDto>(`/v1/admin/writing/audit?${params.toString()}`);
      setItems(response.items);
      setError(null);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Failed to load audit entries.');
    } finally {
      setBusy(false);
    }
  }, [appliedAction, appliedEntityType]);

  const applyFilters = () => {
    setAppliedEntityType(entityType);
    setAppliedAction(action);
  };

  useEffect(() => {
    void load();
  }, [load]);

  return (
    <div className="space-y-6">
      <header className="flex flex-wrap items-center justify-between gap-3">
        <div>
          <h1 className="text-xl font-bold text-navy"><Archive className="mr-2 inline h-5 w-5 text-amber-600" aria-hidden="true" /> Writing Audit</h1>
          <p className="mt-1 text-sm text-muted">Review Writing content mutations, publish activity, and audit-log access events.</p>
        </div>
        <Button onClick={() => void load()} variant="outline" loading={busy}><RefreshCcw className="h-4 w-4" aria-hidden="true" /> Refresh</Button>
      </header>

      {error ? <p role="alert" className="rounded border border-red-300 bg-red-50 p-3 text-sm text-red-800">{error}</p> : null}

      <Card>
        <CardContent>
          <div className="grid gap-2 md:grid-cols-[1fr_1fr_auto]">
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Entity type<input value={entityType} onChange={(event) => setEntityType(event.target.value)} placeholder="WritingScenario" className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            <label className="flex flex-col gap-1 text-xs font-bold uppercase tracking-wider text-muted">Action<input value={action} onChange={(event) => setAction(event.target.value)} placeholder="writing.scenario.updated" className="min-h-9 rounded border border-border bg-background px-2 text-sm" /></label>
            <div className="flex items-end"><Button onClick={applyFilters} loading={busy}>Apply</Button></div>
          </div>
        </CardContent>
      </Card>

      <Card>
        <CardContent>
          <table className="w-full text-sm" aria-label="Writing audit entries">
            <thead>
              <tr className="border-b border-border text-xs uppercase tracking-wider text-muted">
                <th className="py-2 text-left">Time</th>
                <th className="text-left">Action</th>
                <th className="text-left">Entity</th>
                <th className="text-left">Actor</th>
                <th className="text-left">Note</th>
              </tr>
            </thead>
            <tbody>
              {items.length === 0 ? <tr><td colSpan={5} className="py-4 text-center text-xs text-muted">No audit entries match these filters.</td></tr> : null}
              {items.map((entry) => (
                <tr key={entry.id} className="border-b border-border/60 align-top">
                  <td className="py-2 whitespace-nowrap text-xs text-muted">{new Date(entry.occurredAt).toLocaleString()}</td>
                  <td><Badge variant={toneForAction(entry.action)} size="sm">{entry.action}</Badge></td>
                  <td><span className="font-bold text-navy">{entry.entityType}</span><span className="block max-w-64 truncate text-xs text-muted">{entry.entityId}</span></td>
                  <td className="max-w-44 truncate text-xs">{entry.actorUserId}</td>
                  <td className="max-w-xl text-xs">{entry.note ?? '-'}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </CardContent>
      </Card>
    </div>
  );
}