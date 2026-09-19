'use client';

import { useCallback, useEffect, useState } from 'react';
import { Activity, Download, RefreshCw } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { PlacementAccommodationsCard } from '@/components/admin/placement-accommodations-card';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { readErrorMessage } from '@/lib/read-error-message';
import { fetchAuthorizedObjectUrl } from '@/lib/api/binary';
import {
  fetchPlacementEngineHealth,
  fetchPlacementInventory,
  fetchPlacementReviewQueue,
  fetchPlacementReviewSession,
  humanScorePlacementSession,
  resolvePlacementReviewAudioUrl,
  rescorePlacementSession,
  type PlacementEngineHealth,
  type PlacementInventory,
  type PlacementReviewEntry,
  type PlacementReviewSession,
} from '@/lib/api/admin-placement';

const BANDS = ['Pre-A1', 'A1', 'A2', 'B1', 'B2', 'C1', 'C2'] as const;
const OBJECTIVE_MODULES = [
  { key: 'LS', label: 'Language Systems' },
  { key: 'RD', label: 'Reading' },
  { key: 'LSN', label: 'Listening' },
] as const;
/** Below this, a band cannot supply one 5-item confirmation block. */
const CONFIRMATION_BLOCK = 5;
/**
 * The named traits the engine scores (evidence_rules.rs). A human score must
 * fill every one: any other key is ignored and the trait evaluates as 0.
 */
const SPEAKING_TRAITS = ['intelligibility', 'fluency', 'grammar', 'vocabulary', 'communication'] as const;
const WRITING_TRAITS = ['task_fulfilment', 'organisation', 'grammar', 'vocabulary', 'mechanics_register'] as const;

function csvCell(value: string | number): string {
  const text = String(value);
  return /[",\n]/.test(text) ? `"${text.replace(/"/g, '""')}"` : text;
}

function downloadInventoryCsv(inventory: PlacementInventory) {
  const rows: Array<Array<string | number>> = [['section', 'module_or_route', 'band_or_task_type', 'total', 'active', 'inactive']];
  for (const cell of inventory.objective) rows.push(['objective', cell.module, cell.band, cell.total, cell.active, cell.inactive]);
  for (const task of inventory.speaking) rows.push(['speaking', task.route, task.taskType, task.total, task.active, task.total - task.active]);
  for (const task of inventory.writing) rows.push(['writing', task.route, task.taskType, task.total, task.active, task.total - task.active]);
  const blob = new Blob([rows.map((row) => row.map(csvCell).join(',')).join('\n')], { type: 'text/csv;charset=utf-8' });
  const url = URL.createObjectURL(blob);
  const link = document.createElement('a');
  link.href = url;
  link.download = `placement-inventory-${new Date().toISOString().slice(0, 10)}.csv`;
  link.click();
  URL.revokeObjectURL(url);
}

/** Candidate recording, fetched with the admin's bearer token. */
function ReviewAudio({ sessionId, taskId }: { sessionId: string; taskId: string }) {
  const [url, setUrl] = useState<string | null>(null);
  const [failed, setFailed] = useState(false);
  useEffect(() => {
    let cancelled = false;
    let created: string | null = null;
    fetchAuthorizedObjectUrl(resolvePlacementReviewAudioUrl(sessionId, taskId))
      .then((objectUrl) => {
        if (cancelled) {
          URL.revokeObjectURL(objectUrl);
          return;
        }
        created = objectUrl;
        setUrl(objectUrl);
      })
      .catch(() => {
        if (!cancelled) setFailed(true);
      });
    return () => {
      cancelled = true;
      if (created) URL.revokeObjectURL(created);
    };
  }, [sessionId, taskId]);

  if (failed) return <p className="text-sm text-danger">The recording could not be loaded.</p>;
  if (!url) return <p className="text-sm text-muted">Loading recording…</p>;
  // eslint-disable-next-line jsx-a11y/media-has-caption -- candidate recording playback for review
  return <audio controls preload="metadata" src={url} className="w-full" />;
}

function InventoryCard({ inventory, error }: { inventory: PlacementInventory | null; error: string | null }) {
  const cell = (module: string, band: string) =>
    inventory?.objective.find((row) => row.module === module && row.band === band) ?? null;

  return (
    <Card>
      <CardHeader className="flex flex-row flex-wrap items-center justify-between gap-3">
        <CardTitle>Item bank inventory</CardTitle>
        {inventory ? (
          <Button variant="outline" size="sm" onClick={() => downloadInventoryCsv(inventory)}>
            <Download className="mr-2 h-4 w-4" aria-hidden /> Download CSV
          </Button>
        ) : null}
      </CardHeader>
      <CardContent className="space-y-4">
        {error ? (
          <InlineAlert variant="warning">{error}</InlineAlert>
        ) : !inventory ? (
          <p className="text-sm text-muted">Loading…</p>
        ) : (
          <>
            <p className="text-xs text-muted">
              Active / total items per CEFR band. Generated {inventory.generatedAt ? new Date(inventory.generatedAt).toLocaleString() : 'now'} ·
              ruleset {inventory.rulesetVersion}. Cells below {CONFIRMATION_BLOCK} active items cannot supply one confirmation
              block; empty cells cannot be served at all.
            </p>
            <div className="overflow-x-auto">
              <table className="w-full min-w-[640px] border-collapse text-sm">
                <caption className="sr-only">Active and total objective items by module and CEFR band</caption>
                <thead>
                  <tr className="border-b border-border text-left text-xs text-muted">
                    <th scope="col" className="py-2 pr-3 font-medium">Module</th>
                    {BANDS.map((band) => (
                      <th key={band} scope="col" className="px-2 py-2 text-center font-medium">{band}</th>
                    ))}
                    <th scope="col" className="px-2 py-2 text-center font-medium">Total</th>
                  </tr>
                </thead>
                <tbody>
                  {OBJECTIVE_MODULES.map((module) => (
                    <tr key={module.key} className="border-b border-border last:border-0">
                      <th scope="row" className="py-2 pr-3 text-left font-medium text-navy">{module.label}</th>
                      {BANDS.map((band) => {
                        const row = cell(module.key, band);
                        const active = row?.active ?? 0;
                        const variant = active === 0 ? 'danger' : active < CONFIRMATION_BLOCK ? 'warning' : 'slate';
                        return (
                          <td key={band} className="px-2 py-2 text-center">
                            <Badge variant={variant}>
                              {active}/{row?.total ?? 0}
                            </Badge>
                          </td>
                        );
                      })}
                      <td className="px-2 py-2 text-center font-medium text-navy">{inventory.totals[module.key] ?? '—'}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
            <div className="grid gap-4 lg:grid-cols-2">
              {([
                ['Speaking prompts', inventory.speaking, 'SPK'],
                ['Writing tasks', inventory.writing, 'WRT'],
              ] as const).map(([title, rows, totalKey]) => (
                <div key={title}>
                  <p className="mb-1 text-xs font-medium text-muted">
                    {title} · total {inventory.totals[totalKey] ?? rows.reduce((sum, row) => sum + row.total, 0)}
                  </p>
                  <table className="w-full border-collapse text-sm">
                    <thead>
                      <tr className="border-b border-border text-left text-xs text-muted">
                        <th scope="col" className="py-1.5 pr-3 font-medium">Route</th>
                        <th scope="col" className="py-1.5 pr-3 font-medium">Task type</th>
                        <th scope="col" className="py-1.5 text-right font-medium">Active / total</th>
                      </tr>
                    </thead>
                    <tbody>
                      {rows.map((row) => (
                        <tr key={`${row.route}-${row.taskType}`} className="border-b border-border last:border-0">
                          <td className="py-1.5 pr-3 text-navy">{row.route}</td>
                          <td className="py-1.5 pr-3 text-muted">{row.taskType}</td>
                          <td className="py-1.5 text-right">
                            <Badge variant={row.active === 0 ? 'danger' : 'slate'}>
                              {row.active}/{row.total}
                            </Badge>
                          </td>
                        </tr>
                      ))}
                    </tbody>
                  </table>
                </div>
              ))}
            </div>
          </>
        )}
      </CardContent>
    </Card>
  );
}

/**
 * Placement review console: the private GEPA engine's pending-review queue,
 * proxied by the OET API. Reviewers act here — they never log into the
 * engine. Rescore re-runs the automated rating; human score records an
 * expert judgement that supersedes it (append-only).
 */
export default function AdminPlacementReviewPage() {
  const { role, isAuthenticated, isLoading } = useAdminAuth();
  const isReady = isAuthenticated && role === 'admin' && !isLoading;
  const [queue, setQueue] = useState<PlacementReviewEntry[] | null>(null);
  const [health, setHealth] = useState<PlacementEngineHealth | null>(null);
  const [selected, setSelected] = useState<PlacementReviewSession | null>(null);
  const [busy, setBusy] = useState(false);
  const [notice, setNotice] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [rationale, setRationale] = useState('');
  const [score, setScore] = useState('3');
  const [inventory, setInventory] = useState<PlacementInventory | null>(null);
  const [inventoryError, setInventoryError] = useState<string | null>(null);

  const refresh = useCallback(async () => {
    setError(null);
    // Inventory loads independently: an older engine without the report
    // must not take the review queue down with it.
    fetchPlacementInventory()
      .then((result) => {
        setInventory(result);
        setInventoryError(null);
      })
      .catch((err) => setInventoryError(readErrorMessage(err, 'Inventory is not available from the placement engine yet.')));
    try {
      const [entries, engineHealth] = await Promise.all([
        fetchPlacementReviewQueue(),
        fetchPlacementEngineHealth().catch(() => ({ ready: false }) as PlacementEngineHealth),
      ]);
      setQueue(entries);
      setHealth(engineHealth);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not load the review queue.'));
    }
  }, []);

  useEffect(() => {
    if (isReady) void refresh();
  }, [isReady, refresh]);

  const openSession = useCallback(async (sessionId: string) => {
    setNotice(null);
    setError(null);
    try {
      setSelected(await fetchPlacementReviewSession(sessionId));
      setRationale('');
      setScore('3');
    } catch (err) {
      setError(readErrorMessage(err, 'Could not load the review session.'));
    }
  }, []);

  const rescore = useCallback(async () => {
    if (!selected || busy) return;
    setBusy(true);
    setNotice(null);
    setError(null);
    try {
      await rescorePlacementSession(selected.sessionId, {
        taskId: selected.taskId,
        reason: 'Rescored from the placement review console',
      });
      setNotice('Rescore requested — the session re-enters the rating pipeline.');
      await openSession(selected.sessionId);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not rescore the session.'));
    } finally {
      setBusy(false);
    }
  }, [busy, openSession, selected]);

  const humanScore = useCallback(async () => {
    if (!selected || busy) return;
    const value = Number.parseInt(score, 10);
    if (Number.isNaN(value) || value < 0 || value > 5) {
      setError('Human score must be a rubric value from 0 to 5.');
      return;
    }
    if (rationale.trim().length < 10) {
      setError('A rationale of at least 10 characters is required for a human score.');
      return;
    }
    setBusy(true);
    setNotice(null);
    setError(null);
    try {
      // Not derived from selected.atLower: a pending_review rating has empty maps.
      const traits: readonly string[] = selected.taskId.startsWith('WRT-') ? WRITING_TRAITS : SPEAKING_TRAITS;
      const atLower = Object.fromEntries(traits.map((t) => [t, value]));
      const atUpper = Object.fromEntries(traits.map((t) => [t, value]));
      await humanScorePlacementSession(selected.sessionId, selected.taskId, atLower, atUpper, rationale.trim());
      setNotice('Human score recorded (append-only) — the session result recomputes on next view.');
      await openSession(selected.sessionId);
    } catch (err) {
      setError(readErrorMessage(err, 'Could not record the human score.'));
    } finally {
      setBusy(false);
    }
  }, [busy, openSession, rationale, score, selected]);

  if (!isReady) return null;

  return (
    <div className="space-y-6">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2 text-sm text-muted">
          <Activity className="h-4 w-4" aria-hidden />
          Engine: {health ? (health.ready ? 'ready' : 'not ready') : 'unknown'}
        </div>
        <Button variant="outline" size="sm" onClick={() => void refresh()} disabled={busy}>
          <RefreshCw className="mr-2 h-4 w-4" aria-hidden /> Refresh
        </Button>
      </div>

      {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
      {notice ? <InlineAlert variant="success">{notice}</InlineAlert> : null}

      <Card>
        <CardHeader>
          <CardTitle>Pending reviews ({queue?.length ?? 0})</CardTitle>
        </CardHeader>
        <CardContent>
          {!queue ? (
            <p className="text-sm text-muted">Loading…</p>
          ) : queue.length === 0 ? (
            <EmptyState title="Queue empty" description="No placement submissions are waiting for review." />
          ) : (
            <ul className="divide-y divide-border">
              {queue.map((entry, index) => (
                <li key={`${entry.sessionId}-${entry.flagType}-${index}`} className="flex flex-wrap items-center justify-between gap-3 py-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium text-navy">{entry.details}</p>
                    <p className="mt-0.5 text-xs text-muted">
                      {entry.sessionId} · {entry.module} · {new Date(entry.timestamp).toLocaleString()}
                    </p>
                  </div>
                  <div className="flex items-center gap-2">
                    <Badge variant="slate">{entry.flagType}</Badge>
                    <Button size="sm" variant="outline" onClick={() => void openSession(entry.sessionId)}>
                      Review
                    </Button>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </CardContent>
      </Card>

      <InventoryCard inventory={inventory} error={inventoryError} />

      {selected ? (
        <Card>
          <CardHeader>
            <CardTitle>Session {selected.sessionId} — task {selected.taskId}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {selected.taskPrompt ? <p className="text-sm text-navy">{selected.taskPrompt}</p> : null}
            {selected.candidateAudioUrl ? (
              <ReviewAudio key={`${selected.sessionId}:${selected.taskId}`} sessionId={selected.sessionId} taskId={selected.taskId} />
            ) : (
              <p className="text-sm text-muted">No recording stored for this task.</p>
            )}
            {selected.candidateDraft ? (
              <div className="max-h-56 overflow-y-auto rounded-xl border border-border bg-background-light p-3 text-sm text-navy">
                {selected.candidateDraft}
              </div>
            ) : null}
            {selected.aiRationale ? (
              <p className="text-xs text-muted">Rater note: {selected.aiRationale}</p>
            ) : null}
            {selected.flags.length > 0 ? (
              <div className="flex flex-wrap gap-2">
                {selected.flags.map((flag) => (
                  <Badge key={flag} variant="slate">{flag}</Badge>
                ))}
              </div>
            ) : null}

            <div className="flex flex-wrap items-end gap-3 border-t border-border pt-4">
              <div className="w-24">
                <label htmlFor="placement-human-score" className="mb-1 block text-xs font-medium text-muted">
                  Score (0–5)
                </label>
                <Input id="placement-human-score" value={score} onChange={(event) => setScore(event.target.value)} inputMode="numeric" />
              </div>
              <div className="min-w-0 flex-1">
                <label htmlFor="placement-human-rationale" className="mb-1 block text-xs font-medium text-muted">
                  Human score rationale (append-only)
                </label>
                <Textarea
                  id="placement-human-rationale"
                  rows={2}
                  value={rationale}
                  onChange={(event) => setRationale(event.target.value)}
                  placeholder="Why the human judgement differs from (or replaces) the automated rating…"
                />
              </div>
              <div className="flex gap-2">
                <Button variant="outline" onClick={() => void rescore()} disabled={busy}>
                  Re-run rating
                </Button>
                <Button onClick={() => void humanScore()} disabled={busy}>
                  Record human score
                </Button>
              </div>
            </div>
          </CardContent>
        </Card>
      ) : null}

      <PlacementAccommodationsCard />
    </div>
  );
}
