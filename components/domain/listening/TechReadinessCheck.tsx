'use client';

import { AlertTriangle, CheckCircle2, Loader2, Mic, Volume2 } from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Button } from '@/components/ui/button';
import { fetchAuthorizedObjectUrl } from '@/lib/api';

/**
 * Listening V2 R10 — pre-attempt audio sound check. It verifies the
 * candidate's audio output before the strict FSM can advance out of `intro`.
 * Separate device, resolution, scale, and network requirements are
 * candidate-facing real-exam guidance only; they are recorded as telemetry
 * and never used to block an AI practice attempt.
 */

type ProbeStatus = 'idle' | 'running' | 'ok' | 'failed';

export interface TechReadinessCheckProps {
  audioProbeUrl?: string;
  /** Every scored audio asset in the session, not only the sound-check clip. */
  audioUrls?: string[];
  onReady: (result: { audioOk: boolean; durationMs: number }) => void;
  onSkip?: () => void;
}

export function TechReadinessCheck({ audioProbeUrl, audioUrls = [], onReady, onSkip }: TechReadinessCheckProps) {
  const [status, setStatus] = useState<ProbeStatus>('idle');
  const [error, setError] = useState<string | null>(null);
  const cleanupRef = useRef<(() => void) | null>(null);
  const mountedRef = useRef(true);

  useEffect(() => {
    return () => {
      mountedRef.current = false;
      cleanupRef.current?.();
    };
  }, []);

  const runProbe = async () => {
    setStatus('running');
    setError(null);
    cleanupRef.current?.();
    const startedAt = Date.now();
    try {
      await playProbe(audioProbeUrl, (cleanup) => {
        cleanupRef.current = cleanup;
      });
      await verifyScoredAudioAssets(audioUrls);
      if (!mountedRef.current) return;
      cleanupRef.current?.();
      cleanupRef.current = null;
      setStatus('ok');
      onReady({
        audioOk: true,
        durationMs: Date.now() - startedAt,
      });
    } catch (err) {
      if (!mountedRef.current) return;
      cleanupRef.current?.();
      cleanupRef.current = null;
      setStatus('failed');
      setError(err instanceof Error ? err.message : 'Audio probe failed.');
    }
  };

  return (
    <div className="rounded-lg border border-border bg-surface p-6 shadow-sm">
      <h2 className="mb-1 text-lg font-semibold text-navy">Audio readiness check</h2>
      <p className="mb-4 text-sm text-muted">
        Before the test begins, we&apos;ll play a short audio clip to confirm your speakers or
        headphones are working. You will not be able to replay the test audio later.
      </p>
      <div className="flex items-center gap-3">
        {status === 'idle' && (
          <Button variant="primary" onClick={runProbe}>
            <Volume2 className="mr-2 h-4 w-4" /> Play audio probe
          </Button>
        )}
        {status === 'running' && (
          <span role="status" className="inline-flex items-center gap-2 text-sm text-muted">
            <Loader2 aria-hidden="true" className="h-4 w-4 animate-spin" /> Playing probe…
          </span>
        )}
        {status === 'ok' && (
          <span role="status" className="inline-flex items-center gap-2 text-sm text-success">
            <CheckCircle2 aria-hidden="true" className="h-4 w-4" /> Audio confirmed.
          </span>
        )}
        {status === 'failed' && (
          <div className="space-y-2">
            <span role="alert" className="inline-flex items-center gap-2 text-sm text-danger">
              <AlertTriangle aria-hidden="true" className="h-4 w-4" /> {error ?? 'Probe failed.'}
            </span>
            <div className="flex gap-2">
              <Button variant="ghost" onClick={runProbe}>Retry</Button>
              {onSkip && (
                <Button variant="ghost" onClick={onSkip}>Continue anyway</Button>
              )}
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

async function verifyScoredAudioAssets(audioUrls: string[]) {
  const uniqueUrls = [...new Set(audioUrls.map((url) => url.trim()).filter(Boolean))];
  await Promise.all(uniqueUrls.map((url) => verifyAudioAsset(url)));
}

async function verifyAudioAsset(url: string) {
  let sourceUrl = url;
  let objectUrl: string | null = null;
  try {
    // Authenticated media cannot be loaded directly by a native audio element.
    // Fetching it first also proves the complete response is available before
    // the server-authoritative attempt timer is allowed to start.
    if (!/^https?:\/\//i.test(url) || /\/v1\//i.test(url)) {
      objectUrl = await fetchAuthorizedObjectUrl(url);
      sourceUrl = objectUrl;
    }
    const audio = new Audio();
    audio.preload = 'auto';
    await new Promise<void>((resolve, reject) => {
      let timeoutId: number | null = null;
      let cleanup = () => {};
      const onReady = () => {
        cleanup();
        resolve();
      };
      const onError = () => {
        cleanup();
        reject(new Error('A scored audio asset failed its readiness check.'));
      };
      cleanup = () => {
        if (timeoutId !== null) window.clearTimeout(timeoutId);
        audio.removeEventListener('canplaythrough', onReady);
        audio.removeEventListener('error', onError);
      };
      timeoutId = window.setTimeout(() => {
        cleanup();
        reject(new Error('A scored audio asset timed out during its readiness check.'));
      }, 15_000);
      audio.addEventListener('canplaythrough', onReady, { once: true });
      audio.addEventListener('error', onError, { once: true });
      audio.src = sourceUrl;
      audio.load();
    });
  } finally {
    if (objectUrl) URL.revokeObjectURL(objectUrl);
  }
}

async function playProbe(audioProbeUrl: string | undefined, setCleanup: (cleanup: () => void) => void) {
  if (audioProbeUrl) {
    const audio = new Audio(audioProbeUrl);
    audio.preload = 'auto';
    let timeoutId: number | null = null;
    setCleanup(() => {
      if (timeoutId !== null) window.clearTimeout(timeoutId);
      audio.pause();
      audio.removeAttribute('src');
      audio.load();
    });
    await new Promise<void>((resolve, reject) => {
      const onCanPlay = () => {
        audio.play().then(resolve).catch(reject);
      };
      audio.addEventListener('canplaythrough', onCanPlay, { once: true });
      audio.addEventListener('error', () => reject(new Error('Audio load failed')), { once: true });
      timeoutId = window.setTimeout(() => reject(new Error('Audio probe timed out')), 8000);
    });
    await delay(1500);
    return;
  }

  const AudioContextCtor = window.AudioContext ?? window.webkitAudioContext;
  if (!AudioContextCtor) throw new Error('Audio probe is not supported in this browser.');
  const context = new AudioContextCtor();
  const oscillator = context.createOscillator();
  const gain = context.createGain();
  oscillator.frequency.value = 660;
  gain.gain.value = 0.04;
  oscillator.connect(gain);
  gain.connect(context.destination);
  setCleanup(() => {
    try {
      oscillator.stop();
    } catch {
      // Already stopped.
    }
    void context.close().catch(() => undefined);
  });
  await context.resume();
  oscillator.start();
  await delay(1500);
}

function delay(ms: number) {
  return new Promise((resolve) => window.setTimeout(resolve, ms));
}

declare global {
  interface Window {
    webkitAudioContext?: typeof AudioContext;
  }
}
