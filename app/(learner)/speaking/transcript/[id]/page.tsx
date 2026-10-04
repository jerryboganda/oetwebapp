'use client';

import { useCallback, useEffect, useMemo, useState } from 'react';
import Link from 'next/link';
import { useParams } from 'next/navigation';
import { Headphones, Quote, RefreshCw, Volume2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import { ErrorState } from '@/components/ui/empty-error';
import { MotionSection } from '@/components/ui/motion-primitives';
import { Skeleton } from '@/components/ui/skeleton';
import { LearnerPageHero, LearnerSurfaceSectionHeader, RulebookFindingsPanel } from '@/components/domain';
import { SelectionToVocab } from '@/components/domain/vocabulary';
import { AudioPlayerWaveform } from '@/components/domain/audio-player-waveform';
import { analytics } from '@/lib/analytics';
import { fetchSettingsSection, fetchTranscript } from '@/lib/api';
import type { MarkerType, SpeakingTranscriptReview, TranscriptMarker } from '@/lib/mock-data';
import { auditSpeakingTranscript, inferSpeakingCardType } from '@/lib/rulebook';
import { cn } from '@/lib/utils';

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

  return (
    <>
      {/* The page heading renders in every state, so loading and failure keep their h1. */}
      <LearnerPageHero
        eyebrow="Speaking Evidence"
        icon={Quote}
        accent="speaking"
        title="Review speaking evidence with the real transcript and waveform"
        description="Review your transcript, waveform, and playback together to see exactly where to focus."
        highlights={review ? [
          { icon: Headphones, label: 'Audio', value: review.audioAvailable ? 'Available' : 'Transcript only' },
          { icon: Volume2, label: 'Duration', value: `${Math.round(review.duration)} sec` },
          { icon: Quote, label: 'Markers', value: `${allMarkers.length} flagged` },
        ] : undefined}
      />

      {loading ? (
        <div className="grid grid-cols-1 gap-6 lg:grid-cols-[1.1fr_0.9fr]">
          <Skeleton className="h-96 rounded-2xl" />
          <Skeleton className="h-96 rounded-2xl" />
        </div>
      ) : !review ? (
        <ErrorState message={error ?? 'Transcript review is unavailable.'} />
      ) : (
        <>
          {error ? <InlineAlert variant="error">{error}</InlineAlert> : null}
          {review.disclaimer ? <InlineAlert variant="info">{review.disclaimer}</InlineAlert> : null}

          <section className="grid grid-cols-1 gap-6 lg:grid-cols-[1.1fr_0.9fr]">
            <MotionSection className="min-w-0">
              <Card padding="lg">
                <LearnerSurfaceSectionHeader
                  eyebrow="Transcript"
                  title="Review the real conversation flow"
                  description="Each marker stays on the line where the issue happened, so you can hear it for yourself."
                  className="mb-4"
                />

                <SelectionToVocab source="speaking" sourceRefPrefix={`speaking:${resultId}`} className="space-y-3">
                  {review.transcript.map((line) => (
                    <div key={line.id} className="rounded-xl bg-background-light p-4">
                      <div className="flex items-start justify-between gap-4">
                        <div className="min-w-0">
                          <p className="eyebrow text-muted">{line.speaker}</p>
                          <p className="mt-2 text-sm leading-6 text-navy">{line.text}</p>
                        </div>
                        <span className="shrink-0 text-xs font-bold tabular-nums text-muted">{Math.round(line.startTime)}s</span>
                      </div>
                      {line.markers?.length ? (
                        <div className="mt-4 flex flex-wrap gap-2">
                          {line.markers.map((marker) => {
                            const isSelected = selectedMarker?.id === marker.id;
                            return (
                              <button
                                key={marker.id}
                                type="button"
                                aria-pressed={isSelected}
                                onClick={() => setSelectedMarker(marker)}
                                className={cn(
                                  'inline-flex min-h-11 items-center rounded-control border px-3 text-xs font-semibold text-primary transition-colors focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary focus-visible:ring-offset-2 sm:min-h-8',
                                  isSelected ? 'border-primary bg-primary/15' : 'border-primary/20 bg-primary/10 hover:bg-primary/15',
                                )}
                              >
                                {markerLabel[marker.type]}
                              </button>
                            );
                          })}
                        </div>
                      ) : null}
                    </div>
                  ))}
                </SelectionToVocab>
              </Card>
            </MotionSection>

            <div className="min-w-0 space-y-6">
              {review.roleCard ? (
                <MotionSection delayIndex={1}>
                  <Card padding="lg">
                    <LearnerSurfaceSectionHeader
                      eyebrow="Role card context"
                      title={review.roleCard.title}
                      className="mb-4"
                    />
                    <div className="space-y-3 text-sm text-muted">
                      <p><span className="font-bold text-navy">Setting:</span> {review.roleCard.setting}</p>
                      <p><span className="font-bold text-navy">Patient:</span> {review.roleCard.patient}</p>
                      <p><span className="font-bold text-navy">Task:</span> {review.roleCard.brief}</p>
                    </div>
                  </Card>
                </MotionSection>
              ) : null}

              <MotionSection delayIndex={2}>
                <Card padding="lg">
                  <LearnerSurfaceSectionHeader
                    eyebrow="Audio Review"
                    title="Real waveform from the learner’s recording"
                    className="mb-4"
                  />

                  {review.audioAvailable && review.audioUrl && !lowBandwidthMode ? (
                    <div className="rounded-xl bg-background-light p-4">
                      <AudioPlayerWaveform
                        audioUrl={review.audioUrl}
                        onTimeUpdate={handleWaveformTimeUpdate}
                        seekToTime={seekToTime}
                      />
                    </div>
                  ) : review.audioAvailable && lowBandwidthMode ? (
                    <div className="space-y-3">
                      <div className="flex items-center justify-between text-sm text-muted">
                        <span>Low-bandwidth mode active</span>
                        <span className="tabular-nums">{Math.round(currentTime)} / {Math.round(review.duration)} sec</span>
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
                </Card>
              </MotionSection>

              <MotionSection delayIndex={3}>
                <Card padding="lg">
                  <LearnerSurfaceSectionHeader
                    eyebrow="Marker Detail"
                    title={selectedMarker ? markerLabel[selectedMarker.type] : 'Choose a transcript marker'}
                    description="Selected markers explain what happened and why it matters for your OET Speaking score."
                    className="mb-4"
                  />

                  {selectedMarker ? (
                    <div className="space-y-3">
                      <div className="rounded-xl bg-background-light p-4">
                        <p className="eyebrow text-muted">Flagged phrase</p>
                        <p className="mt-2 text-sm font-bold text-navy">&quot;{selectedMarker.text}&quot;</p>
                      </div>
                      <div className="rounded-xl bg-background-light p-4">
                        <p className="eyebrow text-muted">Suggestion</p>
                        <p className="mt-2 text-sm leading-6 text-muted">{selectedMarker.suggestion}</p>
                      </div>
                    </div>
                  ) : (
                    <p className="rounded-xl bg-background-light p-4 text-sm text-muted">
                      Choose one of the transcript markers to inspect the feedback attached to that moment.
                    </p>
                  )}
                </Card>
              </MotionSection>

              <MotionSection delayIndex={4}>
                <RulebookFindingsPanel
                  title="Rulebook Audit"
                  subtitle={`Transcript-level checks grounded in Dr. Hesham's Speaking rulebook. Inferred card type: ${inferredCardType.replace(/_/g, ' ')}.`}
                  findings={auditFindings}
                  hideRuleIds
                  className="rounded-2xl"
                />
              </MotionSection>

              <MotionSection delayIndex={5}>
                <Card padding="lg">
                  <LearnerSurfaceSectionHeader
                    eyebrow="Review Summary"
                    title="Keep the next speaking actions close"
                    className="mb-4"
                  />
                  <div className="space-y-3">
                    <p className="rounded-xl bg-background-light p-4 text-sm text-muted">
                      <span className="tabular-nums">{allMarkers.length}</span> transcript markers surfaced across pronunciation, fluency, grammar, vocabulary, empathy, and structure.
                    </p>
                    <Button variant="outline" fullWidth asChild>
                      <Link href={`/speaking/phrasing/${resultId}`}>
                        <RefreshCw className="h-4 w-4" aria-hidden="true" />
                        Open phrasing review
                      </Link>
                    </Button>
                    <Button variant="ghost" fullWidth asChild>
                      <Link href="/speaking">
                        <Volume2 className="h-4 w-4" aria-hidden="true" />
                        Return to speaking home
                      </Link>
                    </Button>
                  </div>
                </Card>
              </MotionSection>
            </div>
          </section>
        </>
      )}
    </>
  );
}
