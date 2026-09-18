'use client';

import { useCallback, useEffect, useState } from 'react';
import { Activity, RefreshCw } from 'lucide-react';
import { Card, CardContent, CardHeader, CardTitle } from '@/components/admin/ui/card';
import { EmptyState } from '@/components/admin/ui/empty-state';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Input, Textarea } from '@/components/ui/form-controls';
import { InlineAlert } from '@/components/ui/alert';
import { useAdminAuth } from '@/lib/hooks/use-admin-auth';
import { readErrorMessage } from '@/lib/read-error-message';
import {
  fetchPlacementEngineHealth,
  fetchPlacementReviewQueue,
  fetchPlacementReviewSession,
  humanScorePlacementSession,
  resolvePlacementReviewAudioUrl,
  rescorePlacementSession,
  type PlacementEngineHealth,
  type PlacementReviewEntry,
  type PlacementReviewSession,
} from '@/lib/api/admin-placement';

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

  const refresh = useCallback(async () => {
    setError(null);
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
      const traits = Object.keys(selected.atLower.length ? selected.atLower : { overall: 0 });
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

      {selected ? (
        <Card>
          <CardHeader>
            <CardTitle>Session {selected.sessionId} — task {selected.taskId}</CardTitle>
          </CardHeader>
          <CardContent className="space-y-4">
            {selected.taskPrompt ? <p className="text-sm text-navy">{selected.taskPrompt}</p> : null}
            {selected.candidateAudioUrl ? (
              // eslint-disable-next-line jsx-a11y/media-has-caption -- candidate recording playback for review
              <audio controls preload="none" src={resolvePlacementReviewAudioUrl(selected.sessionId, selected.taskId)} className="w-full" />
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
    </div>
  );
}
