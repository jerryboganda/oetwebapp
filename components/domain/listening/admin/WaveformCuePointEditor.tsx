'use client';

// WORK-STREAM 7b — Admin audio waveform + cue-point editor.
//
// Lets an admin preview an extract's section audio as a waveform and set the
// extract's cue window (`AudioStartMs` / `AudioEndMs`) visually. All OET
// Listening extracts on a paper share one section MP3; the cue points window
// *into* that single file, so this editor draws the whole waveform and overlays
// a draggable [start, end] selection.
//
// Audio is fetched through the existing authorized-object-URL helper
// (`fetchAuthorizedObjectUrl`, lib/api.ts) — the same path the Listening player
// uses — decoded with WebAudio (`AudioContext.decodeAudioData`), downsampled to
// per-pixel peaks, and painted to a <canvas>. On save it calls the existing
// `patchListeningExtract` helper (lib/listening-authoring-api.ts); this file
// never mutates that module.
//
// SSR-safe: every WebAudio / canvas / DOM access is guarded behind `useEffect`
// and an `isBrowser()` check. Reduced-motion safe and keyboard-accessible
// (handles are ARIA sliders driven by arrow keys).

import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { useReducedMotion } from 'motion/react';
import { Loader2, Play, Square } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { Input } from '@/components/ui/form-controls';
import { cn } from '@/lib/utils';
import { prefersReducedMotion } from '@/lib/motion';
import { fetchAuthorizedObjectUrl } from '@/lib/api';
// Reuse the EXISTING per-extract patch helper — do NOT modify the module.
import {
  patchListeningExtract,
  type ListeningPartCode,
} from '@/lib/listening-authoring-api';

declare global {
  interface Window {
    webkitAudioContext?: typeof AudioContext;
  }
}

const WAVE_COLOR = '#d8e0e8';
const SELECTED_COLOR = '#7c3aed'; // brand violet — stays per spec
const HANDLE_COLOR = '#6d28d9';
const CANVAS_HEIGHT = 96;
// Coarse keyboard nudge; Shift = fine. Mirrors common DAW cue-point editors.
const KEY_STEP_MS = 1000;
const KEY_STEP_FINE_MS = 100;

type DecodeStatus = 'idle' | 'loading' | 'ready' | 'error';
type ActiveHandle = 'start' | 'end' | null;

export interface WaveformCuePointEditorProps {
  paperId: string;
  /** Canonical part code identifying the extract row (A1 | A2 | B | C1 | C2). */
  extractCode: ListeningPartCode;
  /**
   * Authorized media path for the paper's section audio (e.g.
   * `/v1/media/{id}/content`). `null` when no audio asset is attached yet.
   */
  audioUrl: string | null;
  audioStartMs: number | null;
  audioEndMs: number | null;
  /**
   * Bubble cue-point edits up so the host form's numeric inputs and dirty
   * state stay in sync. Called on every handle drag / numeric edit / clear.
   */
  onChange: (next: { audioStartMs: number | null; audioEndMs: number | null }) => void;
  /** Notify the host after a successful PATCH so it can refresh its rows. */
  onSaved?: () => void;
  className?: string;
}

function isBrowser(): boolean {
  return typeof window !== 'undefined';
}

function clampMs(value: number, max: number): number {
  if (!Number.isFinite(value)) return 0;
  return Math.min(Math.max(0, Math.round(value)), max);
}

function formatClock(ms: number): string {
  if (!Number.isFinite(ms) || ms < 0) return '00:00.0';
  const totalSeconds = ms / 1000;
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = Math.floor(totalSeconds % 60);
  const tenths = Math.floor((ms % 1000) / 100);
  return `${minutes.toString().padStart(2, '0')}:${seconds.toString().padStart(2, '0')}.${tenths}`;
}

/**
 * Reduce a decoded mono/stereo buffer to one absolute peak per target bucket.
 * Averages both channels so the drawn shape matches what the learner hears.
 */
function computePeaks(buffer: AudioBuffer, buckets: number): number[] {
  const channelCount = buffer.numberOfChannels;
  const length = buffer.length;
  if (buckets <= 0 || length === 0) return [];
  const samplesPerBucket = Math.max(1, Math.floor(length / buckets));
  const peaks: number[] = new Array(buckets).fill(0);

  for (let bucket = 0; bucket < buckets; bucket++) {
    const start = bucket * samplesPerBucket;
    const end = Math.min(length, start + samplesPerBucket);
    let peak = 0;
    for (let channel = 0; channel < channelCount; channel++) {
      const data = buffer.getChannelData(channel);
      for (let i = start; i < end; i++) {
        const amplitude = Math.abs(data[i]);
        if (amplitude > peak) peak = amplitude;
      }
    }
    peaks[bucket] = peak;
  }
  return peaks;
}

export function WaveformCuePointEditor({
  paperId,
  extractCode,
  audioUrl,
  audioStartMs,
  audioEndMs,
  onChange,
  onSaved,
  className,
}: WaveformCuePointEditorProps) {
  const reducedMotion = prefersReducedMotion(useReducedMotion());

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const trackRef = useRef<HTMLDivElement>(null);
  const audioElementRef = useRef<HTMLAudioElement>(null);
  const objectUrlRef = useRef<string | null>(null);
  const peaksRef = useRef<number[]>([]);

  const [status, setStatus] = useState<DecodeStatus>('idle');
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [durationMs, setDurationMs] = useState<number>(0);
  const [resolvedAudioUrl, setResolvedAudioUrl] = useState<string | null>(null);
  const [activeHandle, setActiveHandle] = useState<ActiveHandle>(null);
  const [isPreviewing, setIsPreviewing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [saveError, setSaveError] = useState<string | null>(null);
  const [saved, setSaved] = useState(false);

  // The effective window. End falls back to full duration so an unset cue
  // still renders a meaningful selection once we know the audio length.
  const effectiveStartMs = clampMs(audioStartMs ?? 0, durationMs || Number.MAX_SAFE_INTEGER);
  const effectiveEndMs =
    audioEndMs != null
      ? clampMs(audioEndMs, durationMs || Number.MAX_SAFE_INTEGER)
      : durationMs;

  // ── Fetch + decode the section audio ──────────────────────────────────────
  useEffect(() => {
    if (!isBrowser() || !audioUrl) {
      setStatus(audioUrl ? 'loading' : 'idle');
      return;
    }

    let cancelled = false;
    let createdUrl: string | null = null;
    const AudioContextCtor = window.AudioContext ?? window.webkitAudioContext;

    const run = async () => {
      setStatus('loading');
      setErrorMessage(null);
      try {
        const blobUrl = audioUrl.startsWith('blob:')
          ? audioUrl
          : await fetchAuthorizedObjectUrl(audioUrl);
        if (cancelled) {
          if (blobUrl.startsWith('blob:') && blobUrl !== audioUrl) URL.revokeObjectURL(blobUrl);
          return;
        }
        createdUrl = blobUrl;
        objectUrlRef.current = blobUrl;
        setResolvedAudioUrl(blobUrl);

        if (!AudioContextCtor) {
          // No WebAudio (very old browser / SSR fallthrough). The <audio>
          // element below still drives metadata duration + preview playback.
          setStatus('ready');
          return;
        }

        const context = new AudioContextCtor();
        try {
          const response = await fetch(blobUrl);
          const encoded = await response.arrayBuffer();
          // Safari still wants the callback overload; the promise overload
          // covers every evergreen target and our jsdom mock.
          const decoded = await context.decodeAudioData(encoded.slice(0));
          if (cancelled) return;
          peaksRef.current = computePeaks(decoded, 800);
          setDurationMs(Math.round(decoded.duration * 1000));
          setStatus('ready');
        } finally {
          void context.close?.().catch?.(() => undefined);
        }
      } catch {
        if (cancelled) return;
        peaksRef.current = [];
        setErrorMessage('Audio could not be decoded. Confirm an audio asset is attached and try again.');
        setStatus('error');
      }
    };

    void run();

    return () => {
      cancelled = true;
      if (createdUrl && createdUrl.startsWith('blob:') && createdUrl !== audioUrl) {
        URL.revokeObjectURL(createdUrl);
      }
      objectUrlRef.current = null;
    };
  }, [audioUrl]);

  // Revoke any outstanding object URL on unmount.
  useEffect(() => {
    return () => {
      const url = objectUrlRef.current;
      if (url && url.startsWith('blob:')) URL.revokeObjectURL(url);
    };
  }, []);

  // ── Draw the waveform + selection overlay ──────────────────────────────────
  const draw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const dpr = isBrowser() ? window.devicePixelRatio || 1 : 1;
    const cssWidth = canvas.clientWidth || 600;
    const cssHeight = CANVAS_HEIGHT;
    if (canvas.width !== Math.floor(cssWidth * dpr) || canvas.height !== Math.floor(cssHeight * dpr)) {
      canvas.width = Math.floor(cssWidth * dpr);
      canvas.height = Math.floor(cssHeight * dpr);
    }
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, cssWidth, cssHeight);

    const peaks = peaksRef.current;
    const mid = cssHeight / 2;
    const total = durationMs || 1;
    const startX = (effectiveStartMs / total) * cssWidth;
    const endX = (effectiveEndMs / total) * cssWidth;

    if (peaks.length > 0) {
      const barCount = peaks.length;
      const barWidth = cssWidth / barCount;
      for (let i = 0; i < barCount; i++) {
        const x = i * barWidth;
        const amplitude = peaks[i] * (mid - 2);
        const inWindow = x >= startX && x <= endX;
        ctx.fillStyle = inWindow ? SELECTED_COLOR : WAVE_COLOR;
        ctx.fillRect(x, mid - amplitude, Math.max(1, barWidth - 0.5), amplitude * 2);
      }
    } else {
      // Decoding unavailable — render a flat baseline so the track still reads.
      ctx.fillStyle = WAVE_COLOR;
      ctx.fillRect(0, mid - 1, cssWidth, 2);
    }

    // Selection veil outside [start, end].
    ctx.fillStyle = 'rgba(15, 23, 42, 0.06)';
    ctx.fillRect(0, 0, startX, cssHeight);
    ctx.fillRect(endX, 0, cssWidth - endX, cssHeight);

    // Handle stems.
    ctx.fillStyle = HANDLE_COLOR;
    ctx.fillRect(Math.max(0, startX - 1), 0, 2, cssHeight);
    ctx.fillRect(Math.min(cssWidth - 2, endX - 1), 0, 2, cssHeight);
  }, [durationMs, effectiveStartMs, effectiveEndMs]);

  useEffect(() => {
    if (status !== 'ready') return;
    draw();
  }, [status, draw]);

  // Redraw on container resize so the per-pixel mapping stays correct.
  useEffect(() => {
    if (!isBrowser() || status !== 'ready') return;
    const canvas = canvasRef.current;
    if (!canvas || typeof ResizeObserver === 'undefined') return;
    const observer = new ResizeObserver(() => draw());
    observer.observe(canvas);
    return () => observer.disconnect();
  }, [status, draw]);

  // ── Pointer dragging ───────────────────────────────────────────────────────
  const msFromClientX = useCallback(
    (clientX: number): number => {
      const track = trackRef.current;
      if (!track || durationMs <= 0) return 0;
      const rect = track.getBoundingClientRect();
      const ratio = rect.width > 0 ? (clientX - rect.left) / rect.width : 0;
      return clampMs(ratio * durationMs, durationMs);
    },
    [durationMs],
  );

  const commitWindow = useCallback(
    (nextStart: number, nextEnd: number) => {
      const lo = clampMs(Math.min(nextStart, nextEnd), durationMs || Number.MAX_SAFE_INTEGER);
      const hi = clampMs(Math.max(nextStart, nextEnd), durationMs || Number.MAX_SAFE_INTEGER);
      setSaved(false);
      onChange({ audioStartMs: lo, audioEndMs: hi });
    },
    [durationMs, onChange],
  );

  const handlePointerDown = (handle: 'start' | 'end') => (event: React.PointerEvent) => {
    if (status !== 'ready' || durationMs <= 0) return;
    event.preventDefault();
    (event.currentTarget as HTMLElement).setPointerCapture?.(event.pointerId);
    setActiveHandle(handle);
  };

  useEffect(() => {
    if (!activeHandle || !isBrowser()) return;
    const move = (event: PointerEvent) => {
      const ms = msFromClientX(event.clientX);
      if (activeHandle === 'start') commitWindow(ms, effectiveEndMs);
      else commitWindow(effectiveStartMs, ms);
    };
    const up = () => setActiveHandle(null);
    window.addEventListener('pointermove', move);
    window.addEventListener('pointerup', up);
    window.addEventListener('pointercancel', up);
    return () => {
      window.removeEventListener('pointermove', move);
      window.removeEventListener('pointerup', up);
      window.removeEventListener('pointercancel', up);
    };
  }, [activeHandle, msFromClientX, commitWindow, effectiveStartMs, effectiveEndMs]);

  const handleKeyDown = (handle: 'start' | 'end') => (event: React.KeyboardEvent) => {
    if (status !== 'ready' || durationMs <= 0) return;
    const step = event.shiftKey ? KEY_STEP_FINE_MS : KEY_STEP_MS;
    let delta = 0;
    if (event.key === 'ArrowLeft' || event.key === 'ArrowDown') delta = -step;
    else if (event.key === 'ArrowRight' || event.key === 'ArrowUp') delta = step;
    else if (event.key === 'Home') delta = -durationMs;
    else if (event.key === 'End') delta = durationMs;
    else return;
    event.preventDefault();
    if (handle === 'start') commitWindow(effectiveStartMs + delta, effectiveEndMs);
    else commitWindow(effectiveStartMs, effectiveEndMs + delta);
  };

  // ── Numeric inputs ─────────────────────────────────────────────────────────
  const handleNumeric = (field: 'start' | 'end') => (event: React.ChangeEvent<HTMLInputElement>) => {
    const raw = event.target.value.trim();
    const max = durationMs || Number.MAX_SAFE_INTEGER;
    if (raw === '') {
      // Empty end clears the cap; empty start collapses to 0.
      if (field === 'end') {
        setSaved(false);
        onChange({ audioStartMs, audioEndMs: null });
      } else {
        commitWindow(0, effectiveEndMs);
      }
      return;
    }
    const parsed = Number(raw);
    if (!Number.isFinite(parsed)) return;
    const value = clampMs(parsed, max);
    if (field === 'start') commitWindow(value, effectiveEndMs);
    else commitWindow(effectiveStartMs, value);
  };

  // ── Segment preview ────────────────────────────────────────────────────────
  const stopPreview = useCallback(() => {
    const el = audioElementRef.current;
    if (el) {
      el.pause();
    }
    setIsPreviewing(false);
  }, []);

  const previewSegment = useCallback(() => {
    const el = audioElementRef.current;
    if (!el || status !== 'ready') return;
    if (isPreviewing) {
      stopPreview();
      return;
    }
    el.currentTime = effectiveStartMs / 1000;
    setIsPreviewing(true);
    void el.play().catch(() => setIsPreviewing(false));
  }, [status, isPreviewing, effectiveStartMs, stopPreview]);

  // Stop playback exactly at the window end.
  useEffect(() => {
    const el = audioElementRef.current;
    if (!el) return;
    const onTime = () => {
      if (el.currentTime * 1000 >= effectiveEndMs) stopPreview();
    };
    const onEnded = () => setIsPreviewing(false);
    el.addEventListener('timeupdate', onTime);
    el.addEventListener('ended', onEnded);
    return () => {
      el.removeEventListener('timeupdate', onTime);
      el.removeEventListener('ended', onEnded);
    };
  }, [effectiveEndMs, stopPreview, resolvedAudioUrl]);

  // Read duration from the media element when WebAudio is unavailable.
  const handleLoadedMetadata = () => {
    const el = audioElementRef.current;
    if (el && durationMs <= 0 && Number.isFinite(el.duration)) {
      setDurationMs(Math.round(el.duration * 1000));
      if (status === 'loading') setStatus('ready');
    }
  };

  // ── Save ───────────────────────────────────────────────────────────────────
  const handleSave = async () => {
    setSaving(true);
    setSaveError(null);
    try {
      // `effectiveEndMs` already resolves to the decoded duration when the cap
      // is unset, so persist whatever the handles currently show. Stay null
      // only when we genuinely have no end and no known duration to fall back
      // on (audio not yet decoded).
      const endToPersist = audioEndMs != null || durationMs > 0 ? effectiveEndMs : null;
      await patchListeningExtract(paperId, extractCode, {
        audioStartMs: effectiveStartMs,
        audioEndMs: endToPersist,
      });
      setSaved(true);
      onSaved?.();
    } catch (error) {
      setSaveError(error instanceof Error ? error.message : 'Could not save cue points.');
    } finally {
      setSaving(false);
    }
  };

  const handlePositions = useMemo(() => {
    const total = durationMs || 1;
    return {
      startPercent: Math.min(100, Math.max(0, (effectiveStartMs / total) * 100)),
      endPercent: Math.min(100, Math.max(0, (effectiveEndMs / total) * 100)),
    };
  }, [durationMs, effectiveStartMs, effectiveEndMs]);

  return (
    <div
      className={cn('space-y-3 rounded-lg border border-border bg-admin-bg-subtle p-3', className)}
      role="group"
      aria-label={`Audio cue points for extract ${extractCode}`}
    >
      <div className="flex items-center justify-between gap-3">
        <h4 className="text-sm font-semibold text-admin-fg-strong">Audio waveform &amp; cue points</h4>
        {status === 'ready' && durationMs > 0 ? (
          <span className="font-mono text-xs text-muted">Length {formatClock(durationMs)}</span>
        ) : null}
      </div>

      {!audioUrl ? (
        <p className="rounded-lg border border-dashed border-border bg-surface px-4 py-3 text-sm text-muted">
          Attach an audio asset to this paper to preview the waveform and set cue points.
        </p>
      ) : (
        <>
          {status === 'loading' ? (
            <div className="flex items-center gap-2 px-1 py-6 text-sm text-muted">
              <Loader2 className="h-4 w-4 animate-spin" /> Decoding audio waveform…
            </div>
          ) : null}

          {status === 'error' ? (
            <p className="rounded-lg border border-danger/40 bg-danger/5 px-4 py-3 text-sm text-danger" role="alert">
              {errorMessage}
            </p>
          ) : null}

          {/* Waveform track. The canvas is decorative; the two sliders carry
              the accessible semantics. */}
          <div
            ref={trackRef}
            className={cn(
              'relative w-full select-none rounded-md bg-surface',
              status === 'ready' ? 'cursor-pointer' : 'opacity-60',
            )}
            style={{ height: CANVAS_HEIGHT }}
          >
            <canvas ref={canvasRef} className="block h-full w-full" aria-hidden="true" />

            {status === 'ready' && durationMs > 0 ? (
              <>
                <button
                  type="button"
                  role="slider"
                  aria-label={`Segment start for extract ${extractCode}`}
                  aria-valuemin={0}
                  aria-valuemax={durationMs}
                  aria-valuenow={effectiveStartMs}
                  aria-valuetext={formatClock(effectiveStartMs)}
                  tabIndex={0}
                  onPointerDown={handlePointerDown('start')}
                  onKeyDown={handleKeyDown('start')}
                  className={cn(
                    'absolute top-0 h-full w-4 -translate-x-1/2 cursor-ew-resize rounded-sm bg-transparent',
                    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                    !reducedMotion && 'transition-[left] duration-75',
                  )}
                  style={{ left: `${handlePositions.startPercent}%` }}
                  data-testid="cue-handle-start"
                >
                  <span className="pointer-events-none absolute inset-y-0 left-1/2 w-1 -translate-x-1/2 rounded-full bg-[#6d28d9]" />
                </button>

                <button
                  type="button"
                  role="slider"
                  aria-label={`Segment end for extract ${extractCode}`}
                  aria-valuemin={0}
                  aria-valuemax={durationMs}
                  aria-valuenow={effectiveEndMs}
                  aria-valuetext={formatClock(effectiveEndMs)}
                  tabIndex={0}
                  onPointerDown={handlePointerDown('end')}
                  onKeyDown={handleKeyDown('end')}
                  className={cn(
                    'absolute top-0 h-full w-4 -translate-x-1/2 cursor-ew-resize rounded-sm bg-transparent',
                    'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-primary',
                    !reducedMotion && 'transition-[left] duration-75',
                  )}
                  style={{ left: `${handlePositions.endPercent}%` }}
                  data-testid="cue-handle-end"
                >
                  <span className="pointer-events-none absolute inset-y-0 left-1/2 w-1 -translate-x-1/2 rounded-full bg-[#6d28d9]" />
                </button>
              </>
            ) : null}
          </div>

          {/* Hidden media element drives segment preview + metadata-duration
              fallback. Never user-visible. */}
          {resolvedAudioUrl ? (
            <audio
              ref={audioElementRef}
              src={resolvedAudioUrl}
              preload="metadata"
              className="sr-only"
              onLoadedMetadata={handleLoadedMetadata}
            />
          ) : null}

          <div className="flex flex-wrap items-end gap-3">
            <Input
              label="Start (ms)"
              inputMode="numeric"
              value={audioStartMs ?? ''}
              onChange={handleNumeric('start')}
              className="w-32"
              data-testid="cue-input-start"
            />
            <Input
              label="End (ms)"
              inputMode="numeric"
              value={audioEndMs ?? ''}
              onChange={handleNumeric('end')}
              className="w-32"
              data-testid="cue-input-end"
            />
            <Button
              type="button"
              variant="outline"
              size="sm"
              onClick={previewSegment}
              disabled={status !== 'ready'}
              className="gap-2"
            >
              {isPreviewing ? <Square className="h-4 w-4" /> : <Play className="h-4 w-4" />}
              {isPreviewing ? 'Stop' : 'Preview segment'}
            </Button>
            <Button
              type="button"
              size="sm"
              onClick={handleSave}
              loading={saving}
              disabled={saving || status === 'loading'}
              className="gap-2"
            >
              Save cue points
            </Button>
          </div>

          {saveError ? (
            <p className="text-xs text-danger" role="alert">{saveError}</p>
          ) : null}
          {saved && !saveError ? (
            <p className="text-xs text-success" role="status">Cue points saved.</p>
          ) : null}
        </>
      )}
    </div>
  );
}
