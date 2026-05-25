'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, Video } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { RecordingPlayer } from '@/components/class/RecordingPlayer';
import { fetchLiveClassRecording, type LiveClassRecording } from '@/lib/api';

function statusMessage(status: string) {
  switch (status) {
    case 'Processing':
      return 'Recording is being processed. Check back in a few minutes.';
    case 'Failed':
      return 'Recording processing failed. Please contact support if this persists.';
    case 'Expired':
      return 'This recording has expired and is no longer available.';
    default:
      return `Recording status: ${status}`;
  }
}

export default function RecordingPage() {
  const params = useParams();
  const sessionId = typeof params?.sessionId === 'string' ? params.sessionId : null;

  const [recording, setRecording] = useState<LiveClassRecording | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (!sessionId) return;
    let cancelled = false;
    fetchLiveClassRecording(sessionId)
      .then((data) => { if (!cancelled) setRecording(data); })
      .catch((err: unknown) => { if (!cancelled) setError(err instanceof Error ? err.message : 'Could not load this recording.'); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, [sessionId]);

  return (
    <LearnerDashboardShell>
      <div className="space-y-6">
        <div className="flex items-center gap-3">
          <Link
            href="/me/classes/past"
            className="flex items-center gap-1.5 text-sm text-muted hover:text-navy"
          >
            <ArrowLeft className="h-4 w-4" />
            Back to past classes
          </Link>
        </div>

        {loading ? (
          <div className="space-y-4">
            <Skeleton className="aspect-video w-full rounded-xl" />
            <Skeleton className="h-32 rounded-xl" />
          </div>
        ) : error ? (
          <InlineAlert variant="warning" className="flex items-center justify-between gap-3">
            <span>{error}</span>
            <Button type="button" variant="ghost" size="sm" onClick={() => setError(null)}>
              Dismiss
            </Button>
          </InlineAlert>
        ) : !recording ? (
          <InlineAlert variant="warning">Recording not found.</InlineAlert>
        ) : recording.status === 'Ready' && recording.videoUrl ? (
          <>
            <div className="flex items-center gap-2">
              <Video className="h-5 w-5 text-primary" />
              <h1 className="text-xl font-semibold text-navy">Class Recording</h1>
            </div>
            <RecordingPlayer
              videoUrl={recording.videoUrl}
              chapters={recording.chapters}
              aiSummary={recording.aiSummary}
              aiSummaryAr={recording.aiSummaryAr}
              actionItems={recording.actionItems}
            />
          </>
        ) : (
          <div className="rounded-2xl border border-border bg-surface p-8 text-center shadow-sm">
            <Video className="mx-auto mb-3 h-8 w-8 text-muted/50" />
            <p className="text-sm font-medium text-navy">{statusMessage(recording.status)}</p>
            <Link
              href="/me/classes/past"
              className="mt-4 inline-flex items-center gap-1.5 text-sm text-primary hover:underline"
            >
              <ArrowLeft className="h-3.5 w-3.5" />
              Return to past classes
            </Link>
          </div>
        )}
      </div>
    </LearnerDashboardShell>
  );
}
