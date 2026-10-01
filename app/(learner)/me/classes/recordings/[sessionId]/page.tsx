'use client';

import Link from 'next/link';
import { useEffect, useState } from 'react';
import { useParams } from 'next/navigation';
import { ArrowLeft, FileText, Paperclip, Sparkles, Video } from 'lucide-react';

import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { EmptyState, ErrorState } from '@/components/ui/empty-error';
import { Skeleton } from '@/components/ui/skeleton';
import { Tabs, TabPanel, type Tab } from '@/components/ui/tabs';
import { RecordingPlayer } from '@/components/class/RecordingPlayer';
import { AiHelpTooltip } from '@/components/ui/ai-help-tooltip';
import { ClassMaterialList, type ClassMaterial } from '@/components/class/ClassMaterialList';
import { fetchLiveClassRecording, type LiveClassRecording } from '@/lib/api';

type RecordingTab = 'player' | 'materials';

const RECORDING_TABS: Tab[] = [
  { id: 'player', label: 'Player', icon: <Video className="h-3.5 w-3.5" aria-hidden="true" /> },
  { id: 'materials', label: 'Materials', icon: <Paperclip className="h-3.5 w-3.5" aria-hidden="true" /> },
];

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

  const isReady = Boolean(recording && recording.status === 'Ready' && recording.videoUrl);

  return (
    <>
      <div className="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
        <div className="flex items-center gap-3">
          <span className="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-primary/10 text-primary">
            <Video className="h-5 w-5" aria-hidden="true" />
          </span>
          <h1 className="text-xl font-bold tracking-tight text-navy sm:text-2xl">Class Recording</h1>
        </div>
        <div className="flex flex-wrap items-center gap-2">
          {isReady && sessionId ? (
            <Button asChild variant="ghost" size="sm" className="text-primary">
              <Link href={`/me/classes/${sessionId}/transcript`}>
                <FileText className="h-4 w-4" aria-hidden="true" /> View transcript
              </Link>
            </Button>
          ) : null}
          <Button asChild variant="outline" size="sm">
            <Link href="/me/classes/past">
              <ArrowLeft className="h-4 w-4 rtl:rotate-180" aria-hidden="true" />
              Back to past classes
            </Link>
          </Button>
        </div>
      </div>

      {loading ? (
        <div className="space-y-4">
          <Skeleton className="aspect-video w-full rounded-2xl" />
          <Skeleton className="h-32 rounded-2xl" />
        </div>
      ) : error ? (
        <ErrorState message={error} />
      ) : !recording ? (
        <EmptyState
          icon={<Video className="h-8 w-8" />}
          title="Recording not found."
          action={{ label: 'Return to past classes', href: '/me/classes/past' }}
        />
      ) : isReady && recording.videoUrl ? (
        <>
          <Tabs
            tabs={RECORDING_TABS}
            activeTab={activeTab}
            onChange={(id) => setActiveTab(id as RecordingTab)}
          />

          <div className="grid grid-cols-1 gap-4 lg:grid-cols-[minmax(0,2fr)_minmax(0,1fr)]">
            <div className="min-w-0">
              <TabPanel id="player" activeTab={activeTab}>
                <RecordingPlayer
                  videoUrl={recording.videoUrl}
                  chapters={recording.chapters}
                  aiSummary={recording.aiSummary}
                  aiSummaryAr={recording.aiSummaryAr}
                  actionItems={recording.actionItems}
                />
              </TabPanel>
              <TabPanel id="materials" activeTab={activeTab} className="space-y-3">
                <h2 className="flex items-center gap-2 text-sm font-semibold text-navy">
                  <Paperclip className="h-4 w-4 text-primary" aria-hidden="true" /> Class materials
                </h2>
                <ClassMaterialList materials={materials} />
              </TabPanel>
            </div>

            <div>
              <Card className="flex items-center justify-between gap-3">
                <div className="flex items-center gap-2">
                  <Sparkles className="h-4 w-4 shrink-0 text-primary" aria-hidden="true" />
                  <div>
                    <h3 className="text-sm font-semibold text-navy">AI study assistant</h3>
                    <p className="text-xs text-muted">Summarise the class, explain tricky parts, or build recall questions.</p>
                  </div>
                </div>
                <AiHelpTooltip variant="class" />
              </Card>
            </div>
          </div>
        </>
      ) : (
        <EmptyState
          icon={<Video className="h-8 w-8" />}
          title={statusMessage(recording.status)}
          action={{ label: 'Return to past classes', href: '/me/classes/past' }}
        />
      )}
    </>
  );
}
