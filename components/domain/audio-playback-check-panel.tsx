'use client';

import { useEffect, useRef, useState } from 'react';
import { cn } from '@/lib/utils';
import { Volume2, CheckCircle2, AlertCircle, PlayCircle, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { InlineAlert } from '@/components/ui/alert';

declare global {
  interface Window {
    webkitAudioContext?: typeof AudioContext;
  }
}

interface AudioPlaybackCheckPanelProps {
  /** Called once when the learner confirms the test tone played clearly. */
  onComplete?: () => void;
  /** Called whenever the learner explicitly reports a playback failure. */
  onFail?: (reason: 'inaudible' | 'browser_unsupported' | 'autoplay_blocked') => void;
  className?: string;
}

type CheckStatus = 'idle' | 'playing' | 'awaiting_confirmation' | 'passed' | 'failed';

const TEST_TONE_DURATION_MS = 1500;
const TEST_TONE_FREQUENCY_HZ = 660;

/**
 * Pre-Listening hard-block audio playback check.
 *
 * Plays a brief deterministic test tone synthesised in the browser via the
 * Web Audio API (no remote asset, fully offline-capable) and asks the
 * learner to confirm they heard it. Used to gate Listening section launches
 * — listed alongside MicCheckPanel as a parallel pre-flight control.
 *
 * The component is intentionally minimal:
 *   • Press "Play test sound" → ramps a sine wave for 1.5s.
 *   • Two buttons: "I heard it clearly" (pass) vs "I didn't hear anything" (fail).
 *   • Emits onComplete() once on success; onFail(reason) on failures so the
 *     parent can record proctoring events and keep the launch gate locked.
 */
export function AudioPlaybackCheckPanel({ onComplete, onFail, className }: AudioPlaybackCheckPanelProps) {
  const [status, setStatus] = useState<CheckStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const audioContextRef = useRef<AudioContext | null>(null);
  const completedRef = useRef(false);

  useEffect(() => () => {
    if (audioContextRef.current && audioContextRef.current.state !== 'closed') {
      void audioContextRef.current.close().catch(() => undefined);
    }
  }, []);

  const playTone = async () => {
    setError(null);
    const Ctor = window.AudioContext ?? window.webkitAudioContext;
    if (!Ctor) {
      setStatus('failed');
      setError('Your browser does not support audio playback. Please use a recent Chrome, Edge, or Safari.');
      onFail?.('browser_unsupported');
      return;
    }

    let ctx = audioContextRef.current;
    if (!ctx || ctx.state === 'closed') {
      ctx = new Ctor();
      audioContextRef.current = ctx;
    }
    if (ctx.state === 'suspended') {
      try {
        await ctx.resume();
      } catch {
        setStatus('failed');
        setError('We could not start audio playback. Please tap "Play test sound" again.');
        onFail?.('autoplay_blocked');
        return;
      }
    }

    setStatus('playing');
    try {
      const osc = ctx.createOscillator();
      const gain = ctx.createGain();
      osc.type = 'sine';
      osc.frequency.value = TEST_TONE_FREQUENCY_HZ;
      // Soft envelope to avoid clicks.
      const now = ctx.currentTime;
      gain.gain.setValueAtTime(0, now);
      gain.gain.linearRampToValueAtTime(0.18, now + 0.05);
      gain.gain.linearRampToValueAtTime(0.18, now + (TEST_TONE_DURATION_MS / 1000) - 0.1);
      gain.gain.linearRampToValueAtTime(0, now + (TEST_TONE_DURATION_MS / 1000));
      osc.connect(gain).connect(ctx.destination);
      osc.start(now);
      osc.stop(now + (TEST_TONE_DURATION_MS / 1000));

      await new Promise<void>((resolve) => {
        osc.onended = () => resolve();
      });
      setStatus('awaiting_confirmation');
    } catch {
      setStatus('failed');
      setError('Audio playback failed. Check your speakers or headphones and try again.');
      onFail?.('inaudible');
    }
  };

  const handleHeard = () => {
    if (completedRef.current) return;
    completedRef.current = true;
    setStatus('passed');
    onComplete?.();
  };

  const handleNotHeard = () => {
    setStatus('failed');
    setError('We could not detect playback. Increase volume, check your headphones/speakers, then try again.');
    onFail?.('inaudible');
  };

  const handleRetry = () => {
    completedRef.current = false;
    setStatus('idle');
    setError(null);
  };

  return (
    <section
      className={cn(
        'rounded-2xl border border-border bg-surface p-5 shadow-sm',
        status === 'passed' && 'border-success/40 bg-success/5',
        status === 'failed' && 'border-danger/40 bg-danger/5',
        className,
      )}
      data-testid="audio-playback-check-panel"
      aria-live="polite"
    >
      <header className="flex items-center gap-3">
        <div className="flex h-10 w-10 items-center justify-center rounded-xl bg-primary/10 text-primary">
          {status === 'passed' ? (
            <CheckCircle2 className="h-5 w-5 text-success" />
          ) : status === 'failed' ? (
            <AlertCircle className="h-5 w-5 text-danger" />
          ) : status === 'playing' ? (
            <Loader2 className="h-5 w-5 animate-spin" />
          ) : (
            <Volume2 className="h-5 w-5" />
          )}
        </div>
        <div>
          <h3 className="text-sm font-bold text-navy">Audio playback check</h3>
          <p className="text-xs text-muted">
            Listening sections require working audio. Confirm you can hear a short test tone before you start.
          </p>
        </div>
      </header>

      {status === 'idle' || status === 'playing' ? (
        <div className="mt-4">
          <Button
            variant="primary"
            size="sm"
            onClick={playTone}
            disabled={status === 'playing'}
            loading={status === 'playing'}
            data-testid="audio-playback-play"
          >
            <PlayCircle className="mr-2 h-4 w-4" />
            Play test sound
          </Button>
        </div>
      ) : null}

      {status === 'awaiting_confirmation' ? (
        <div className="mt-4 flex flex-wrap gap-2">
          <Button variant="primary" size="sm" onClick={handleHeard} data-testid="audio-playback-heard">
            <CheckCircle2 className="mr-2 h-4 w-4" /> I heard it clearly
          </Button>
          <Button variant="secondary" size="sm" onClick={handleNotHeard} data-testid="audio-playback-not-heard">
            I didn&apos;t hear anything
          </Button>
        </div>
      ) : null}

      {status === 'passed' ? (
        <p className="mt-3 text-sm text-success" role="status">
          Audio confirmed. You can launch the Listening section.
        </p>
      ) : null}

      {status === 'failed' ? (
        <div className="mt-3 space-y-2">
          <InlineAlert variant="error">{error ?? 'Audio playback could not be confirmed.'}</InlineAlert>
          <ul className="text-xs leading-5 text-muted list-disc pl-5">
            <li>Make sure your device volume is up and not muted.</li>
            <li>Disconnect and reconnect headphones or speakers.</li>
            <li>Close other apps that may be using audio output.</li>
          </ul>
          <Button variant="secondary" size="sm" onClick={handleRetry} data-testid="audio-playback-retry">
            Try again
          </Button>
        </div>
      ) : null}
    </section>
  );
}

export default AudioPlaybackCheckPanel;
