'use client';

/**
 * Phase 5 (G) — Speaking Drill Player.
 *
 * A self-contained 30-60 second mini-recorder for a single
 * `SpeakingDrillItem`. Layout:
 *
 *   ┌──────────────────────────────────────────────┐
 *   │ [Kind pill]  Drill title                     │
 *   │ Instruction text (prompt)                    │
 *   ├──────────────────────────────────────────────┤
 *   │ [Record / Stop]   mm:ss   [audio waveform]   │
 *   │   • after stop: <audio> playback + Submit    │
 *   ├──────────────────────────────────────────────┤
 *   │ Feedback panel (score + comments + retry)    │
 *   └──────────────────────────────────────────────┘
 *
 * Recording uses the native `MediaRecorder` API directly (no native
 * dependency). The resulting blob is uploaded via FormData against
 * `/v1/speaking/drills/attempts/{aid}/recordings`. Scoring then
 * triggers `/score` and renders the AI feedback inline so the learner
 * never leaves the page.
 */
import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { Badge } from '@/components/ui/badge';
import { Button } from '@/components/ui/button';
import { Card } from '@/components/ui/card';
import { InlineAlert } from '@/components/ui/alert';
import {
  scoreDrillAttempt,
  uploadDrillRecording,
  type DrillScoringResponse,
  type DrillSummary,
} from '@/lib/api/speaking-drills';

export interface DrillPlayerProps {
  drill: DrillSummary;
  attemptId: string;
  /** Maximum recording length in seconds. Defaults to 60. */
  maxSeconds?: number;
  /** Fired when feedback is available so the parent can refresh state. */
  onComplete?: (feedback: DrillScoringResponse) => void;
}

type PlayerState =
  | 'idle' // freshly loaded, ready to record
  | 'recording' // MediaRecorder is active
  | 'recorded' // we have a blob; learner can preview / submit
  | 'submitting' // upload + score in flight
  | 'scored' // feedback available
  | 'error';

const DEFAULT_MAX_SECONDS = 60;

function formatTime(seconds: number): string {
  const safe = Math.max(0, Math.floor(seconds));
  const m = Math.floor(safe / 60);
  const s = safe % 60;
  return `${m.toString().padStart(2, '0')}:${s.toString().padStart(2, '0')}`;
}

export function DrillPlayer({ drill, attemptId, maxSeconds = DEFAULT_MAX_SECONDS, onComplete }: DrillPlayerProps) {
  const [state, setState] = useState<PlayerState>('idle');
  const [elapsed, setElapsed] = useState(0);
  const [blob, setBlob] = useState<Blob | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [feedback, setFeedback] = useState<DrillScoringResponse | null>(null);

  const recorderRef = useRef<MediaRecorder | null>(null);
  const streamRef = useRef<MediaStream | null>(null);
  const chunksRef = useRef<Blob[]>([]);
  const intervalRef = useRef<ReturnType<typeof setInterval> | null>(null);
  const startTsRef = useRef<number>(0);

  const previewUrl = useMemo(() => (blob ? URL.createObjectURL(blob) : null), [blob]);

  const cleanupRecorder = useCallback(() => {
    if (intervalRef.current) {
      clearInterval(intervalRef.current);
      intervalRef.current = null;
    }
    if (recorderRef.current && recorderRef.current.state !== 'inactive') {
      try {
        recorderRef.current.stop();
      } catch {
        // safe — recorder already stopped
      }
    }
    if (streamRef.current) {
      for (const track of streamRef.current.getTracks()) track.stop();
      streamRef.current = null;
    }
  }, []);

  useEffect(() => {
    return () => {
      cleanupRecorder();
      if (previewUrl) URL.revokeObjectURL(previewUrl);
    };
  }, [cleanupRecorder, previewUrl]);

  const handleRecordToggle = useCallback(async () => {
    setError(null);
    if (state === 'recording') {
      cleanupRecorder();
      return;
    }
    if (state === 'scored' || state === 'recorded' || state === 'error') {
      // reset to allow re-record
      setBlob(null);
      setFeedback(null);
      setElapsed(0);
    }
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      streamRef.current = stream;
      const recorder = new MediaRecorder(stream);
      recorderRef.current = recorder;
      chunksRef.current = [];

      recorder.ondataavailable = (ev) => {
        if (ev.data && ev.data.size > 0) chunksRef.current.push(ev.data);
      };
      recorder.onstop = () => {
        const merged = new Blob(chunksRef.current, { type: recorder.mimeType || 'audio/webm' });
        setBlob(merged);
        setState('recorded');
        if (intervalRef.current) {
          clearInterval(intervalRef.current);
          intervalRef.current = null;
        }
        if (streamRef.current) {
          for (const track of streamRef.current.getTracks()) track.stop();
          streamRef.current = null;
        }
      };

      startTsRef.current = Date.now();
      recorder.start();
      setState('recording');
      setElapsed(0);
      intervalRef.current = setInterval(() => {
        const next = Math.floor((Date.now() - startTsRef.current) / 1000);
        setElapsed(next);
        if (next >= maxSeconds) {
          cleanupRecorder();
        }
      }, 250);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not access microphone.';
      setError(`Microphone error: ${message}`);
      setState('error');
    }
  }, [cleanupRecorder, maxSeconds, state]);

  const handleSubmit = useCallback(async () => {
    if (!blob) return;
    setState('submitting');
    setError(null);
    try {
      await uploadDrillRecording(attemptId, blob, blob.type);
      const scored = await scoreDrillAttempt(attemptId);
      setFeedback(scored);
      setState('scored');
      onComplete?.(scored);
    } catch (err) {
      const message = err instanceof Error ? err.message : 'Could not submit drill recording.';
      setError(message);
      setState('error');
    }
  }, [attemptId, blob, onComplete]);

  const handleTryAgain = useCallback(() => {
    setBlob(null);
    setFeedback(null);
    setElapsed(0);
    setError(null);
    setState('idle');
  }, []);

  return (
    <Card className="space-y-6 p-6" data-testid="drill-player">
      {/* ── Prompt header ─────────────────────────────────────────────── */}
      <header className="space-y-3">
        <div className="flex items-center gap-2">
          <Badge variant="muted" data-testid="drill-player-kind">
            {drill.drillKind}
          </Badge>
          {drill.targetCriteria.slice(0, 3).map((c) => (
            <Badge key={c} variant="info">
              {c}
            </Badge>
          ))}
        </div>
        <h2 className="text-xl font-bold text-navy" data-testid="drill-player-title">
          {drill.title}
        </h2>
        <p className="text-sm text-muted" data-testid="drill-player-prompt">
          {drill.instructionText}
        </p>
      </header>

      {/* ── Recorder ──────────────────────────────────────────────────── */}
      <section className="space-y-4">
        <div className="flex items-center gap-4">
          <Button
            type="button"
            onClick={handleRecordToggle}
            disabled={state === 'submitting'}
            variant={state === 'recording' ? 'destructive' : 'primary'}
            data-testid="drill-player-record"
            aria-pressed={state === 'recording'}
          >
            {state === 'recording' ? 'Stop recording' : state === 'recorded' || state === 'scored' ? 'Re-record' : 'Start recording'}
          </Button>
          <span className="font-mono text-lg" data-testid="drill-player-timer">
            {formatTime(elapsed)} / {formatTime(maxSeconds)}
          </span>
          {state === 'recording' && (
            <DrillWaveform stream={null} />
          )}
        </div>
        {previewUrl && state !== 'recording' && (
          <audio
            controls
            src={previewUrl}
            data-testid="drill-player-playback"
            className="w-full"
          />
        )}
        {state === 'recorded' && (
          <Button
            type="button"
            onClick={handleSubmit}
            data-testid="drill-player-submit"
            variant="primary"
          >
            Submit for feedback
          </Button>
        )}
        {state === 'submitting' && (
          <InlineAlert variant="info">
            Scoring your drill. This only takes a few seconds.
          </InlineAlert>
        )}
      </section>

      {/* ── Feedback ──────────────────────────────────────────────────── */}
      {feedback && (
        <section className="space-y-3 border-t pt-4" data-testid="drill-player-feedback">
          <div className="flex items-baseline gap-3">
            <span className="text-3xl font-black text-navy" data-testid="drill-player-score">
              {feedback.score}
            </span>
            <span className="text-sm text-muted">/ 100</span>
          </div>
          <p className="text-sm">{feedback.summary}</p>
          {feedback.specificComments.length > 0 && (
            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">
                What the AI noticed
              </h3>
              <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">
                {feedback.specificComments.map((c, i) => (
                  <li key={`comment-${i}`}>{c}</li>
                ))}
              </ul>
            </div>
          )}
          {feedback.nextRecommendations.length > 0 && (
            <div>
              <h3 className="text-xs font-semibold uppercase tracking-wide text-muted">
                Try next
              </h3>
              <ul className="mt-1 list-disc space-y-1 pl-5 text-sm">
                {feedback.nextRecommendations.map((r, i) => (
                  <li key={`reco-${i}`}>{r}</li>
                ))}
              </ul>
            </div>
          )}
          <Button type="button" onClick={handleTryAgain} variant="secondary" data-testid="drill-player-retry">
            Try this drill again
          </Button>
        </section>
      )}

      {error && (
        <InlineAlert variant="error">
          <span data-testid="drill-player-error">{error}</span>
        </InlineAlert>
      )}
    </Card>
  );
}

/**
 * Tiny live waveform built on top of `AnalyserNode`. Kept inline so the
 * DrillPlayer ships without depending on the heavier
 * `audio-player-waveform.tsx` component — which is geared at playback,
 * not live capture.
 */
function DrillWaveform({ stream }: { stream: MediaStream | null }) {
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const audioCtxRef = useRef<AudioContext | null>(null);
  const rafRef = useRef<number | null>(null);

  useEffect(() => {
    if (!stream || !canvasRef.current) return;
    const canvas = canvasRef.current;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const AudioCtor =
      window.AudioContext ||
      (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
    const audioCtx = new AudioCtor();
    const source = audioCtx.createMediaStreamSource(stream);
    const analyser = audioCtx.createAnalyser();
    analyser.fftSize = 256;
    source.connect(analyser);
    audioCtxRef.current = audioCtx;

    const data = new Uint8Array(analyser.frequencyBinCount);
    const draw = () => {
      analyser.getByteFrequencyData(data);
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      const barWidth = canvas.width / data.length;
      for (let i = 0; i < data.length; i += 1) {
        const v = data[i] / 255;
        const h = v * canvas.height;
        ctx.fillStyle = `rgba(15, 35, 95, ${0.4 + v * 0.6})`;
        ctx.fillRect(i * barWidth, canvas.height - h, barWidth - 1, h);
      }
      rafRef.current = requestAnimationFrame(draw);
    };
    draw();

    return () => {
      if (rafRef.current) cancelAnimationFrame(rafRef.current);
      audioCtx.close().catch(() => undefined);
    };
  }, [stream]);

  return (
    <canvas
      ref={canvasRef}
      width={120}
      height={32}
      className="rounded bg-muted"
      data-testid="drill-player-waveform"
    />
  );
}
