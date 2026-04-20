'use client';

import { useCallback, useEffect, useState } from 'react';
import { Mic, Square, RotateCcw, Volume2, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';
import { usePronunciationRecorder } from '@/hooks/usePronunciationRecorder';
import { WaveformMeter } from './WaveformMeter';

export type PronunciationRecorderPanelProps = {
  onUpload: (blob: Blob, durationMs: number, mimeType: string) => Promise<void>;
  maxDurationMs?: number;
  disabled?: boolean;
  modelAudioUrl?: string | null;
};

/**
 * Full recording panel used on the drill detail page. Wraps
 * <usePronunciationRecorder /> with:
 *   - Permission primer
 *   - Live waveform meter
 *   - "Play yours" playback
 *   - "A/B compare" toggle (if model audio is available)
 *   - Submit + retry affordances
 *
 * Accessibility:
 *   - All interactive controls are real <button> elements with aria-labels
 *   - Live elapsed time announced via aria-live=polite in WaveformMeter
 *   - Reduced-motion aware via CSS (meter animation only scales by level, which
 *     is user-generated, not decorative)
 */
export function PronunciationRecorderPanel({
  onUpload,
  maxDurationMs = 60_000,
  disabled,
  modelAudioUrl,
}: PronunciationRecorderPanelProps) {
  const {
    status, permission, errorMessage, level, elapsedMs, result,
    requestPermission, start, stop, reset,
  } = usePronunciationRecorder({ maxDurationMs });

  const [isUploading, setIsUploading] = useState(false);
  const [uploadError, setUploadError] = useState<string | null>(null);
  const [playingOwn, setPlayingOwn] = useState(false);

  useEffect(() => () => {
    if (result?.url) URL.revokeObjectURL(result.url);
  }, [result]);

  const handleSubmit = useCallback(async () => {
    if (!result) return;
    setIsUploading(true);
    setUploadError(null);
    try {
      await onUpload(result.blob, result.durationMs, result.mimeType);
    } catch (err) {
      setUploadError(err instanceof Error ? err.message : 'Could not upload the recording.');
    } finally {
      setIsUploading(false);
    }
  }, [onUpload, result]);

  const isRecording = status === 'recording';

  const recorderStatusMessage =
    status === 'requesting-permission' ? 'Waiting for microphone permission.'
    : status === 'recording' ? 'Recording in progress.'
    : status === 'stopping' ? 'Stopping recording.'
    : status === 'stopped' ? 'Recording complete. Review it and submit for scoring.'
    : status === 'error' ? (errorMessage ?? 'Microphone unavailable.')
    : permission === 'granted' ? 'Microphone ready. Press start to record.'
    : 'Microphone access has not been enabled yet.';

  return (
    <section
      aria-labelledby="pronunciation-recorder-heading"
      className="space-y-4 rounded-3xl border border-border bg-surface p-5 shadow-sm"
    >
      <div className="flex items-center justify-between">
        <div>
          <div className="text-xs uppercase tracking-[0.18em] text-muted">Practice</div>
          <h2 id="pronunciation-recorder-heading" className="text-lg font-semibold text-navy dark:text-white">
            Record your attempt
          </h2>
          <p className="text-sm text-muted">
            {modelAudioUrl
              ? 'Listen to the model, then record yourself. We\'ll score each phoneme and give targeted feedback.'
              : 'Record yourself saying the example words and sentences above. We\'ll score each phoneme and give targeted feedback.'}
          </p>
        </div>
      </div>

      {/* Single ARIA live region consolidates status announcements so assistive
          tech hears one coherent message per state transition rather than a
          scatter of visual changes. */}
      <p
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className="sr-only"
      >
        {recorderStatusMessage}
      </p>

      {errorMessage && (
        <InlineAlert variant="warning">{errorMessage}</InlineAlert>
      )}
      {uploadError && (
        <InlineAlert variant="warning">{uploadError}</InlineAlert>
      )}

      {permission === 'unknown' && status !== 'requesting-permission' && status !== 'recording' && !result && (
        <div className="rounded-2xl border border-dashed border-border p-4 text-sm text-muted">
          <p>Allow microphone access to record and get AI-scored feedback.</p>
          <Button
            variant="primary"
            onClick={requestPermission}
            className="mt-3"
            aria-label="Enable microphone for pronunciation recording"
          >
            Enable microphone
          </Button>
        </div>
      )}

      {status === 'requesting-permission' && (
        <div className="flex items-center gap-2 text-sm text-muted">
          <Loader2 className="h-4 w-4 animate-spin" /> Waiting for microphone permission…
        </div>
      )}

      {(permission === 'granted' || isRecording || result) && (
        <>
          <WaveformMeter
            level={level}
            isRecording={isRecording}
            elapsedMs={elapsedMs}
            maxDurationMs={maxDurationMs}
          />

          <div className="flex flex-wrap items-center gap-3">
            {!isRecording && !result && (
              <Button
                variant="primary"
                onClick={start}
                disabled={disabled}
                aria-label="Start recording"
                className="gap-2"
              >
                <Mic className="h-4 w-4" /> Start recording
              </Button>
            )}
            {isRecording && (
              <Button
                variant="primary"
                onClick={stop}
                aria-label="Stop recording"
                className="gap-2"
              >
                <Square className="h-4 w-4" /> Stop
              </Button>
            )}
            {result && !isUploading && (
              <>
                <Button
                  variant="primary"
                  onClick={handleSubmit}
                  disabled={disabled}
                  aria-label="Submit recording for scoring"
                  className="gap-2"
                >
                  <Volume2 className="h-4 w-4" /> Score my attempt
                </Button>
                <Button
                  variant="ghost"
                  onClick={reset}
                  aria-label="Discard and retry"
                  className="gap-2"
                >
                  <RotateCcw className="h-4 w-4" /> Retry
                </Button>
                <PlayOwnRecording
                  url={result.url}
                  mimeType={result.mimeType}
                  isPlaying={playingOwn}
                  onPlayingChange={setPlayingOwn}
                />
                {modelAudioUrl && (
                  <a
                    href={modelAudioUrl}
                    target="_blank"
                    rel="noreferrer"
                    className="text-sm text-primary underline underline-offset-2"
                  >
                    Open model audio
                  </a>
                )}
              </>
            )}
            {isUploading && (
              <div className="flex items-center gap-2 text-sm text-muted">
                <Loader2 className="h-4 w-4 animate-spin" /> Scoring your recording…
              </div>
            )}
          </div>
        </>
      )}
    </section>
  );
}

function PlayOwnRecording({
  url,
  mimeType,
  isPlaying,
  onPlayingChange,
}: {
  url: string;
  mimeType: string;
  isPlaying: boolean;
  onPlayingChange: (v: boolean) => void;
}) {
  return (
    <div className="flex items-center gap-2">
      <audio
        src={url}
        controls
        preload="metadata"
        className="h-8 max-w-[220px]"
        onPlay={() => onPlayingChange(true)}
        onPause={() => onPlayingChange(false)}
        onEnded={() => onPlayingChange(false)}
        aria-label={`Play your ${isPlaying ? 'current' : ''} recording (${mimeType})`}
      />
    </div>
  );
}
