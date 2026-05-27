'use client';

/**
 * OET Speaking — Phase 10 P10.1 — learner self-management for recordings.
 *
 * Lists every recording owned by the current user, shows retention
 * countdown, and exposes the existing GDPR delete endpoint.
 */
import { useCallback, useEffect, useState } from 'react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { Skeleton } from '@/components/ui/skeleton';
import { InlineAlert } from '@/components/ui/alert';
import {
  deleteSpeakingRecording,
  fetchMySpeakingRecordings,
  type MyRecordingRow,
} from '@/lib/api/speaking-compliance';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

function formatDate(iso: string | null): string {
  if (!iso) return '—';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '—';
  const m = Math.floor(seconds / 60);
  const s = Math.floor(seconds % 60);
  return `${m}:${String(s).padStart(2, '0')}`;
}

export default function SpeakingRecordingsPage() {
  const [rows, setRows] = useState<MyRecordingRow[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyId, setBusyId] = useState<string | null>(null);

  const reload = useCallback(async () => {
    setError(null);
    try {
      const res = await fetchMySpeakingRecordings();
      setRows(res.recordings);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load your recordings.');
    }
  }, []);

  useEffect(() => {
    reload();
  }, [reload]);

  async function handleDelete(recordingId: string) {
    if (busyId) return;
    if (!window.confirm('Delete this recording? This cannot be undone.')) return;
    setBusyId(recordingId);
    setError(null);
    try {
      await deleteSpeakingRecording(recordingId);
      trackSpeaking('recording_deleted', { recordingId });
      await reload();
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed.');
    } finally {
      setBusyId(null);
    }
  }

  return (
    <LearnerDashboardShell>
      <div className="mx-auto max-w-5xl space-y-6 py-8">
        <header className="space-y-2">
          <h1 className="text-2xl font-semibold text-foreground">My speaking recordings</h1>
          <p className="text-muted-foreground">
            Manage the audio captured during your role-plays. You can delete a recording at any
            time. Recordings are also automatically removed after the retention window expires.
          </p>
        </header>

        {error && <InlineAlert variant="error">{error}</InlineAlert>}

        {!rows ? (
          <Skeleton className="h-48 w-full rounded-xl" />
        ) : rows.length === 0 ? (
          <Card className="p-8 text-center text-muted-foreground">
            You don&apos;t have any saved recordings yet.
          </Card>
        ) : (
          <div className="space-y-3">
            {rows.map((r) => (
              <Card key={r.recordingId} className="p-4">
                <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                  <div className="space-y-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="font-medium text-foreground">{r.scenarioTitle}</span>
                      <Badge variant="default">{r.mode}</Badge>
                      <Badge variant="default">{r.professionId}</Badge>
                      {r.isArchived && <Badge variant="warning">archived</Badge>}
                    </div>
                    <div className="text-xs text-muted-foreground">
                      Captured {formatDate(r.createdAt)} · Duration {formatDuration(r.durationSeconds)}
                      {r.retentionExpiresAt
                        ? ` · Auto-deletes ${formatDate(r.retentionExpiresAt)}`
                        : ''}
                    </div>
                  </div>
                  <div className="flex gap-2">
                    <Button
                      variant="outline"
                      onClick={() => handleDelete(r.recordingId)}
                      disabled={busyId === r.recordingId || r.isArchived}
                    >
                      {busyId === r.recordingId ? 'Deleting…' : 'Delete'}
                    </Button>
                  </div>
                </div>
              </Card>
            ))}
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
