'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Paperclip, Sparkles, Video } from 'lucide-react';

import { LearnerDashboardShell } from '@/components/layout/learner-dashboard-shell';
import { InlineAlert } from '@/components/ui/alert';
import { Button } from '@/components/ui/button';
import { Skeleton } from '@/components/ui/skeleton';
import { RecordingPlayer } from '@/components/class/RecordingPlayer';
import { AskAiPanel } from '@/components/class/AskAiPanel';
import { ClassMaterialList, type ClassMaterial } from '@/components/class/ClassMaterialList';
import { fetchLiveClassRecording, type LiveClassRecording } from '@/lib/api';

type RecordingTab = 'player' | 'materials';

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
  const [activeTab, setActiveTab] = useState<RecordingTab>('player');

  // Materials are fetched server-side in a future wave; for now we surface an
  // empty list so the UI shape stays stable. Tutors that uploaded materials
  // pre-class will still be able to see this tab.
  const materials: ClassMaterial[] = [];

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
            <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div className="flex items-center gap-2">
                <Video className="h-5 w-5 text-primary" />
                <h1 className="text-xl font-semibold text-navy">Class Recording</h1>
              </div>
              {sessionId ? (
                <Link
                  href={`/me/classes/${sessionId}/transcript`}
                  className="inline-flex items-center gap-1.5 text-sm text-primary hover:underline"
                >
                  <FileText className="h-4 w-4" /> View transcript
                </Link>
              ) : null}
            </div>

            <div className="flex gap-3 border-b border-border pb-1 text-sm" role="tablist">
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'player'}
                onClick={() => setActiveTab('player')}
                className={
                  activeTab === 'player'
                    ? 'border-b-2 border-primary pb-2 font-semibold text-primary'
                    : 'pb-2 text-muted hover:text-navy'
                }
              >
                <span className="inline-flex items-center gap-1.5">
                  <Video className="h-3.5 w-3.5" /> Player
                </span>
              </button>
              <button
                type="button"
                role="tab"
                aria-selected={activeTab === 'materials'}
                onClick={() => setActiveTab('materials')}
                className={
                  activeTab === 'materials'
                    ? 'border-b-2 border-primary pb-2 font-semibold text-primary'
                    : 'pb-2 text-muted hover:text-navy'
                }
              >
                <span className="inline-flex items-center gap-1.5">
                  <Paperclip className="h-3.5 w-3.5" /> Materials
                </span>
              </button>
            </div>

            <div className="grid gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
              <div className="min-w-0 space-y-4">
                {activeTab === 'player' ? (
                  <RecordingPlayer
                    videoUrl={recording.videoUrl}
                    chapters={recording.chapters}
                    aiSummary={recording.aiSummary}
                    aiSummaryAr={recording.aiSummaryAr}
                    actionItems={recording.actionItems}
                  />
                ) : (
                  <div className="space-y-3">
                    <h2 className="flex items-center gap-2 text-sm font-semibold text-navy">
                      <Paperclip className="h-4 w-4 text-primary" /> Class materials
                    </h2>
                    <ClassMaterialList materials={materials} />
                  </div>
                )}
              </div>

              <div className="space-y-4">
                {sessionId ? (
                  <AskAiPanel sessionId={sessionId} />
                ) : (
                  <div className="rounded-2xl border border-dashed border-border bg-surface p-4 text-center text-xs text-muted">
                    <Sparkles className="mx-auto mb-2 h-5 w-5 text-muted/50" /> AI assistant unavailable.
                  </div>
                )}
              </div>
            </div>
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
