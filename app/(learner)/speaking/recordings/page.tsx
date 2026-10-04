'use client';

/**
 * OET Speaking — Phase 10 P10.1 — learner self-management for recordings.
 *
 * Lists every recording owned by the current user, shows retention
 * countdown, and exposes the existing GDPR delete endpoint.
 */
import { useCallback, useEffect, useState } from 'react';
import { Mic } from 'lucide-react';
import { LearnerPageHero } from '@/components/domain';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { Badge } from '@/components/ui/badge';
import { InlineAlert } from '@/components/ui/alert';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { LearnerSkeleton } from '@/components/domain/learner-skeletons';
import { MotionItem } from '@/components/ui/motion-primitives';
import { WRITING_PROFESSION_LABELS } from '@/lib/writing/types';
import {
  deleteSpeakingRecording,
  fetchMySpeakingRecordings,
  type MyRecordingRow,
} from '@/lib/api/speaking-compliance';
import { trackSpeaking } from '@/lib/analytics/speaking-events';

function formatDate(iso: string | null): string {
  if (!iso) return '–';
  try {
    return new Date(iso).toLocaleString();
  } catch {
    return iso;
  }
}

function formatDuration(seconds: number): string {
  if (!Number.isFinite(seconds) || seconds <= 0) return '–';
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
    <>
      <LearnerPageHero
        eyebrow="Speaking"
        icon={Mic}
        accent="speaking"
        title="My speaking recordings"
        description="Manage saved Speaking audio, including consented short microphone clips linked to candidate speech in live AI-patient conversations. The app does not make a full-session recording or directly record provider playback. Browser echo cancellation is enabled, but speaker or background audio may still be picked up by your microphone. You can delete any item at any time; saved audio is also automatically removed after its retention window expires."
      />

      {!rows ? (
        // A failed first load offers a retry instead of a skeleton that never resolves.
        error ? <ErrorState message={error} onRetry={reload} /> : <LearnerSkeleton variant="list" />
      ) : (
        <>
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {rows.length === 0 ? (
            <EmptyState
              icon={<Mic className="h-8 w-8" />}
              title="You don't have any saved Speaking audio."
              description="Recorder submissions, tutor-room recordings and consented live microphone clips appear here when available."
            />
          ) : (
            <ul className="space-y-3">
              {rows.map((r, index) => (
                <li key={r.recordingId}>
                  <MotionItem delayIndex={Math.min(index, 5)}>
                    <Card padding="md">
                      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
                        <div className="min-w-0 space-y-1">
                          <div className="flex flex-wrap items-center gap-2">
                            <span className="font-semibold text-navy">{r.scenarioTitle}</span>
                            <Badge variant="muted">{r.mode}</Badge>
                            <Badge variant="muted">
                              {WRITING_PROFESSION_LABELS[r.professionId as keyof typeof WRITING_PROFESSION_LABELS] ?? r.professionId}
                            </Badge>
                            <Badge variant="muted">
                              {r.source === 'ConversationHub'
                                ? 'Live microphone clip'
                                : r.source === 'LiveKitEgress'
                                  ? 'Live tutor recording'
                                  : 'Role-play recording'}
                            </Badge>
                            {r.isArchived && <Badge variant="warning">archived</Badge>}
                          </div>
                          <p className="text-xs tabular-nums text-muted">
                            Captured {formatDate(r.createdAt)} · Duration {formatDuration(r.durationSeconds)}
                            {r.retentionExpiresAt
                              ? ` · Auto-deletes ${formatDate(r.retentionExpiresAt)}`
                              : ''}
                          </p>
                        </div>
                        <Button
                          variant="outline"
                          className="shrink-0 self-start sm:self-auto"
                          onClick={() => handleDelete(r.recordingId)}
                          disabled={busyId === r.recordingId || r.isArchived}
                        >
                          {busyId === r.recordingId ? 'Deleting…' : 'Delete'}
                        </Button>
                      </div>
                    </Card>
                  </MotionItem>
                </li>
              ))}
            </ul>
          )}
        </>
      )}
    </>
  );
}
