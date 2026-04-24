'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import { useParams, useRouter } from 'next/navigation';
import { Headphones, Quote, RefreshCw, Volume2 } from 'lucide-react';
import { LearnerDashboardShell } from '@/components/layout';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader, RulebookFindingsPanel } from '@/components/domain';
import { SelectionToVocab } from '@/components/domain/vocabulary';
import { AudioPlayerWaveform } from '@/components/domain/audio-player-waveform';
import { analytics } from '@/lib/analytics';
import { fetchSettingsSection, fetchTranscript } from '@/lib/api';
import type { MarkerType, SpeakingTranscriptReview, TranscriptMarker } from '@/lib/mock-data';
import { auditSpeakingTranscript, inferSpeakingCardType } from '@/lib/rulebook';

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

  const [review, setReview] = useState<SpeakingTranscriptReview | null>(null);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);
  const [currentTime, setCurrentTime] = useState(0);
  const [lowBandwidthMode, setLowBandwidthMode] = useState(false);
  const [selectedMarker, setSelectedMarker] = useState<TranscriptMarker | null>(null);
  const [seekToTime] = useState<number | null>(null);

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

  const allMarkers = useMemo(
    () => review?.transcript.flatMap((line) => line.markers ?? []) ?? [],
    [review],
  );

  const inferredCardType = useMemo(
    () => inferSpeakingCardType(review?.title),
    [review?.title],
  );

  const auditFindings = useMemo(() => {
    if (!review) return [];
    return auditSpeakingTranscript({
      cardType: inferredCardType,
      transcript: review.transcript.map((line) => ({
        speaker: /patient/i.test(line.speaker)
          ? 'patient'
          : /interlocutor/i.test(line.speaker)
            ? 'interlocutor'
            : 'candidate',
        text: line.text,
        startMs: Math.round(line.startTime * 1000),
        endMs: Math.round(line.endTime * 1000),
      })),
      silenceAfterDiagnosisMs: undefined,
      profession: 'medicine',
    });
  }, [review, inferredCardType]);

  const handleWaveformTimeUpdate = useCallback((time: number) => {
    setCurrentTime(time);
  }, []);

  if (loading) {
    return (
      <LearnerDashboardShell pageTitle="Transcript Review">
        <div className="grid gap-6 p-6 lg:grid-cols-[1.1fr_0.9fr]">
          <Skeleton className="h-[70vh] rounded-2xl" />
          <Skeleton className="h-[70vh] rounded-2xl" />
        </div>
      </LearnerDashboardShell>
    );
  }

  if (!review) {
    return (
      <LearnerDashboardShell pageTitle="Transcript Review" backHref="/speaking">
        <div className="mx-auto max-w-3xl px-4 py-8">
          <InlineAlert variant="error">{error ?? 'Transcript review is unavailable.'}</InlineAlert>
        </div>
      </LearnerDashboardShell>
    );
  }

  return (
    <LearnerDashboardShell pageTitle={review.title} subtitle="Transcript-backed speaking evidence with real audio-derived review data.">
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
          <div className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
            <LearnerSurfaceSectionHeader
              eyebrow="Transcript"
              title="Review the real conversation flow"
              description="Each marker stays attached to the line where the issue occurred so the learner can revisit the actual evidence."
              className="mb-4"
            />

            <SelectionToVocab source="speaking" sourceRefPrefix={`speaking:${resultId}`} className="space-y-4">
              {review.transcript.map((line) => (
                <div key={line.id} className="rounded-2xl border border-border bg-background-light p-4">
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
            </SelectionToVocab>
          </div>

          <div className="space-y-6">
                        <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Audio Review"
                title="Real waveform from the learner’s recording"
                description="The waveform is rendered from the actual audio file using wavesurfer.js so the visual review surface matches the learner’s real recording."
                className="mb-4"
              />
            
              <div className="rounded-2xl border border-border bg-background-light p-4">
                {review.audioAvailable && review.audioUrl && !lowBandwidthMode ? (
                  <AudioPlayerWaveform
                    audioUrl={review.audioUrl}
                    onTimeUpdate={handleWaveformTimeUpdate}
                    seekToTime={seekToTime}
                  />
                ) : review.audioAvailable && lowBandwidthMode ? (
                  <div className="space-y-3">
                    <div className="mb-4 flex items-center justify-between text-sm text-muted">
                      <span>Low-bandwidth mode active</span>
                      <span>{Math.round(currentTime)} / {Math.round(review.duration)} sec</span>
                    </div>
                    <InlineAlert variant="info">
                      Audio waveform is hidden in low-bandwidth mode. Change this in audio settings.
                    </InlineAlert>
                  </div>
                ) : (
                  <InlineAlert variant="info">
                    Audio is not available for this evaluation, so this page remains transcript-first.
                  </InlineAlert>
                )}
              </div>
            </section>

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Marker Detail"
                title={selectedMarker ? markerLabel[selectedMarker.type] : 'Choose a transcript marker'}
                description="Selected markers explain what happened in that moment and why it matters for OET speaking performance."
                className="mb-4"
              />

              {selectedMarker ? (
                <div className="space-y-4">
                  <div className="rounded-2xl border border-border bg-background-light p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-muted">Flagged phrase</p>
                    <p className="mt-2 text-sm font-bold text-navy">&quot;{selectedMarker.text}&quot;</p>
                  </div>
                  <div className="rounded-2xl border border-border bg-background-light p-4">
                    <p className="text-xs font-black uppercase tracking-widest text-muted">Suggestion</p>
                    <p className="mt-2 text-sm leading-6 text-muted">{selectedMarker.suggestion}</p>
                  </div>
                </div>
              ) : (
                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
                  Choose one of the transcript markers to inspect the feedback attached to that moment.
                </div>
              )}
            </section>

            <RulebookFindingsPanel
              title="Rulebook Audit"
              subtitle={`Transcript-level checks grounded in Dr. Hesham's Speaking rulebook. Inferred card type: ${inferredCardType.replace(/_/g, ' ')}.`}
              findings={auditFindings}
              className="rounded-2xl"
              ruleHref={(ruleId) => `/speaking/rulebook/${ruleId}`}
            />

            <section className="rounded-2xl border border-border bg-surface p-6 shadow-sm">
              <LearnerSurfaceSectionHeader
                eyebrow="Review Summary"
                title="Keep the next speaking actions close"
                description="Once the learner understands the transcript evidence, the next step should still be nearby."
                className="mb-4"
              />
              <div className="space-y-3">
                <div className="rounded-2xl border border-border bg-background-light p-4 text-sm text-muted">
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
    </LearnerDashboardShell>
  );
}
