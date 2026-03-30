'use client';

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Headphones, Play, Quote, RefreshCw, Volume2 } from 'lucide-react';
import { AppShell } from '@/components/layout/app-shell';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader } from '@/components/domain';
import { analytics } from '@/lib/analytics';
import { fetchAuthorizedObjectUrl, fetchSettingsSection, fetchTranscript } from '@/lib/api';
import type { MarkerType, SpeakingTranscriptReview, TranscriptMarker } from '@/lib/mock-data';

const markerLabel: Record<MarkerType, string> = {
  pronunciation: 'Pronunciation',
  fluency: 'Fluency',
  grammar: 'Grammar',
  vocabulary: 'Vocabulary',
  empathy: 'Empathy',
  structure: 'Structure',
};

export default function SpeakingTranscriptPage() {
  const params = useParams<{ id: string }>();
  const router = useRouter();
  const resultId = params?.id;
  const audioRef = useRef<HTMLAudioElement | null>(null);

  const [review, setReview] = useState<SpeakingTranscriptReview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [audioObjectUrl, setAudioObjectUrl] = useState<string | null>(null);
  const [audioLoading, setAudioLoading] = useState(false);
  const [currentTime, setCurrentTime] = useState(0);
  const [lowBandwidthMode, setLowBandwidthMode] = useState(false);
  const [selectedMarker, setSelectedMarker] = useState<TranscriptMarker | null>(null);

  useEffect(() => {
    if (!resultId) return;
    let cancelled = false;
    analytics.track('content_view', { page: 'speaking-transcript', resultId });

    (async () => {
      try {
        const [transcriptReview, audioSettings] = await Promise.all([
          fetchTranscript(resultId),
          fetchSettingsSection('audio'),
        ]);
        if (cancelled) return;
        setReview(transcriptReview);
        setLowBandwidthMode(Boolean(audioSettings.values.lowBandwidthMode));
      } catch (err) {
        if (!cancelled) {
          setError(err instanceof Error ? err.message : 'Could not load the speaking transcript review.');
        }
      } finally {
        if (!cancelled) {
          setLoading(false);
        }
      }
    })();

    return () => {
      cancelled = true;
    };
  }, [resultId]);

  useEffect(() => {
    return () => {
      if (audioObjectUrl) {
        URL.revokeObjectURL(audioObjectUrl);
      }
    };
  }, [audioObjectUrl]);

  const allMarkers = useMemo(
    () => review?.transcript.flatMap((line) => line.markers ?? []) ?? [],
    [review],
  );

  const waveformBars = useMemo(() => {
    const peaks = review?.waveformPeaks?.length ? review.waveformPeaks : Array.from({ length: 48 }, (_, index) => 18 + ((index * 17) % 52));
    return peaks.slice(0, 60);
  }, [review]);

  const loadAudio = useCallback(async () => {
    if (!review?.audioAvailable || !review.audioUrl || audioObjectUrl) return;
    setAudioLoading(true);
    try {
      const objectUrl = await fetchAuthorizedObjectUrl(review.audioUrl);
      setAudioObjectUrl(objectUrl);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Could not load the speaking audio.');
    } finally {
      setAudioLoading(false);
    }
  }, [audioObjectUrl, review?.audioAvailable, review?.audioUrl]);

  useEffect(() => {
    if (review?.audioAvailable && review.audioUrl && !lowBandwidthMode && !audioObjectUrl) {
      void loadAudio();
    }
  }, [audioObjectUrl, loadAudio, lowBandwidthMode, review?.audioAvailable, review?.audioUrl]);

  if (loading) {
    return (
      <AppShell pageTitle="Transcript Review" distractionFree>
        <div className="grid gap-6 p-6 lg:grid-cols-[1.1fr_0.9fr]">
          <Skeleton className="h-[70vh] rounded-[24px]" />
          <Skeleton className="h-[70vh] rounded-[24px]" />
        </div>
      </AppShell>
    );
  }

  if (!review) {
    return (
      <AppShell pageTitle="Transcript Review" backHref="/speaking">
        <div className="mx-auto max-w-3xl px-4 py-8">
          <InlineAlert variant="error">{error ?? 'Transcript review is unavailable.'}</InlineAlert>
        </div>
      </AppShell>
    );
  }

  return (
    <AppShell pageTitle={review.title} subtitle="Transcript-backed speaking evidence with real audio-derived review data." distractionFree>
      <div className="mx-auto max-w-6xl space-y-8 px-4 py-8 sm:px-6 lg:px-8">
        {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}

        <LearnerPageHero
          eyebrow="Speaking Evidence"
          icon={Quote}
          accent="purple"
          title="Review speaking evidence with the real transcript and waveform"
          description="Use this page to connect transcript markers, waveform evidence, and playback state before you revisit the recording."
          highlights={[
            { icon: Headphones, label: 'Audio', value: review.audioAvailable ? 'Available' : 'Transcript only' },
            { icon: Volume2, label: 'Duration', value: `${Math.round(review.duration)} sec` },
            { icon: Quote, label: 'Markers', value: `${allMarkers.length} flagged` },
          ]}
        />

        <section className="grid gap-6 lg:grid-cols-[1.1fr_0.9fr]">
          <div className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
            <LearnerSurfaceSectionHeader
              eyebrow="Transcript"
              title="Review the real conversation flow"
              description="Each marker stays attached to the line where the issue occurred so the learner can revisit the actual evidence."
              className="mb-4"
            />

            <div className="space-y-4">
              {review.transcript.map((line) => (
                <div key={line.id} className="rounded-2xl border border-gray-100 bg-background-light p-4">
                  <div className="flex items-start justify-between gap-4">
                    <div>
                      <p className="text-xs font-black uppercase tracking-widest text-muted">{line.speaker}</p>
                      <p className="mt-2 text-sm leading-6 text-navy">{line.text}</p>
                    </div>
                    <span className="text-xs font-bold text-muted">{Math.round(line.startTime)}s</span>
                  </div>
                  {line.markers?.length ? (
                    <div className="mt-4 flex flex-wrap gap-2">
                      {line.markers.map((marker) => (
                        <button
                          key={marker.id}
                          onClick={() => setSelectedMarker(marker)}
                          className="rounded-full bg-primary/10 px-3 py-1 text-xs font-black uppercase tracking-widest text-primary"
                        >
                          {markerLabel[marker.type]}
                        </button>
                      ))}
                    </div>
                  ) : null}
                </div>
              ))}
            </div>
          </div>

          <div className="space-y-6">
            <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Audio Review"
                title="Use the real waveform, not a placeholder"
                description="Waveform bars are derived from stored evaluation peaks so the visual review surface matches the learner’s actual recording."
                className="mb-4"
              />

              <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                <div className="mb-4 flex items-center justify-between text-sm text-muted">
                  <span>Current time</span>
                  <span>{Math.round(currentTime)} / {Math.round(review.duration)} sec</span>
                </div>

                <div className="flex h-28 items-end gap-1 rounded-2xl bg-white p-4">
                  {waveformBars.map((peak, index) => (
                    <div
                      key={`${peak}-${index}`}
                      className="flex-1 rounded-full bg-primary/70"
                      style={{ height: `${peak}%` }}
                    />
                  ))}
                </div>

                {review.audioAvailable ? (
                  <div className="mt-4 space-y-3">
                    {!audioObjectUrl ? (
                      <Button onClick={loadAudio} loading={audioLoading} fullWidth>
                        <Headphones className="h-4 w-4" />
                        {lowBandwidthMode ? 'Load speaking audio on demand' : 'Load speaking audio'}
                      </Button>
                    ) : (
                      <>
                        <audio
                          ref={audioRef}
                          controls
                          className="w-full"
                          src={audioObjectUrl}
                          onTimeUpdate={() => setCurrentTime(audioRef.current?.currentTime ?? 0)}
                        />
                        <Button variant="outline" fullWidth onClick={() => audioRef.current?.play()}>
                          <Play className="h-4 w-4" />
                          Play audio
                        </Button>
                      </>
                    )}
                  </div>
                ) : (
                  <InlineAlert variant="info" className="mt-4">
                    Audio is not available for this evaluation, so this page remains transcript-first.
                  </InlineAlert>
                )}
              </div>
            </section>

            <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Marker Detail"
                title={selectedMarker ? markerLabel[selectedMarker.type] : 'Choose a transcript marker'}
                description="Selected markers explain what happened in that moment and why it matters for OET speaking performance."
                className="mb-4"
              />

              {selectedMarker ? (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-muted">Flagged phrase</p>
                    <p className="mt-2 text-sm font-bold text-navy">&quot;{selectedMarker.text}&quot;</p>
                  </div>
                  <div className="rounded-2xl border border-gray-100 bg-background-light p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-muted">Suggestion</p>
                    <p className="mt-2 text-sm leading-6 text-muted">{selectedMarker.suggestion}</p>
                  </div>
                </div>
              ) : (
                <div className="rounded-2xl border border-gray-100 bg-background-light p-4 text-sm text-muted">
                  Choose one of the transcript markers to inspect the feedback attached to that moment.
                </div>
              )}
            </section>

            <section className="rounded-[28px] border border-gray-200 bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Review Summary"
                title="Keep the next speaking actions close"
                description="Once the learner understands the transcript evidence, the next step should still be nearby."
                className="mb-4"
              />
              <div className="space-y-3">
                <div className="rounded-2xl border border-gray-100 bg-background-light p-4 text-sm text-muted">
                  {allMarkers.length} transcript markers surfaced across pronunciation, fluency, grammar, vocabulary, empathy, and structure.
                </div>
                <Button variant="outline" fullWidth onClick={() => router.push(`/speaking/phrasing/${resultId}`)}>
                  <RefreshCw className="h-4 w-4" />
                  Open phrasing review
                </Button>
                <Button variant="ghost" fullWidth onClick={() => router.push('/speaking')}>
                  <Volume2 className="h-4 w-4" />
                  Return to speaking home
                </Button>
              </div>
            </section>
          </div>
        </section>
      </div>
    </AppShell>
  );
}
