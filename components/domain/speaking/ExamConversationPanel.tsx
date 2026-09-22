'use client';

import { useEffect, useState } from 'react';
import { AlertTriangle, Check, Loader2, Mic, MicOff, Radio, Square } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useSpeakingRealtimeVoice } from '@/hooks/useSpeakingRealtimeVoice';
import type { LiveVoiceProvider } from '@/lib/api/speaking-live-voice';

export interface ExamConversationPanelProps {
  sessionId: string;
  className?: string;
  micAllowed?: boolean;
  requestedProvider?: LiveVoiceProvider;
  onVoiceStopReady?: (stop: (() => Promise<boolean>) | null) => void;
}

const CONNECTION_LABELS = {
  idle: 'Not ready',
  preparing: 'Preparing provider',
  ready: 'Ready for microphone consent',
  connecting: 'Connecting live voice',
  connected: 'Live voice connected',
  ending: 'Saving conversation',
  ended: 'Voice session saved',
  error: 'Voice unavailable',
} as const;

export function ExamConversationPanel({ sessionId, className, micAllowed = true, requestedProvider, onVoiceStopReady }: ExamConversationPanelProps) {
  const voice = useSpeakingRealtimeVoice(sessionId, requestedProvider);
  const [disclosureAccepted, setDisclosureAccepted] = useState(false);
  const connected = voice.connection === 'connected';
  const canStart = voice.connection === 'ready' && disclosureAccepted && micAllowed;

  useEffect(() => {
    onVoiceStopReady?.(voice.stop);
    return () => onVoiceStopReady?.(null);
  }, [onVoiceStopReady, voice.stop]);

  return (
    <div className={cn('flex flex-col gap-4 rounded-xl border border-border bg-surface p-4', className)}>
      <audio ref={voice.audioRef} autoPlay aria-label="Live patient voice" className="hidden" />

      <div className="flex items-start justify-between gap-3">
        <div>
          <p className="flex items-center gap-2 text-sm font-semibold text-foreground">
            <Radio className="h-4 w-4 text-primary" aria-hidden /> Live AI patient
          </p>
          <p className="mt-1 text-xs text-muted">Native realtime voice only. No text or mock fallback.</p>
        </div>
        <span
          className={cn(
            'inline-flex items-center gap-1.5 rounded-full px-2 py-1 text-xs font-medium',
            connected && 'bg-emerald-50 text-emerald-700',
            voice.connection === 'error' && 'bg-rose-50 text-rose-700',
            !connected && voice.connection !== 'error' && 'bg-amber-50 text-amber-700',
          )}
          aria-live="polite"
        >
          <span className={cn('h-2 w-2 rounded-full', connected ? 'bg-emerald-500' : voice.connection === 'error' ? 'bg-rose-500' : 'bg-amber-400')} />
          {CONNECTION_LABELS[voice.connection]}
        </span>
      </div>

      {voice.preflight ? (
        <div className="rounded-lg border border-sky-200 bg-sky-50 p-3 text-sm text-sky-900">
          <p className="font-semibold">Provider disclosure</p>
          <p className="mt-1 leading-relaxed">{voice.preflight.disclosure}</p>
          <p className="mt-2 text-xs font-medium">
            Provider: {voice.preflight.providerDisplayName} · Model: {voice.preflight.model} · Retention: {voice.preflight.retentionDays} days
          </p>
          {!micAllowed && !connected ? (
            <p className="mt-2 text-xs font-medium text-amber-800">Accept the Speaking recording consent above before enabling the microphone.</p>
          ) : null}
          {!connected && !voice.ended ? (
            <label className="mt-3 flex cursor-pointer items-start gap-2 text-xs leading-relaxed">
              <input
                type="checkbox"
                checked={disclosureAccepted}
                onChange={(event) => setDisclosureAccepted(event.target.checked)}
                className="mt-0.5 h-4 w-4 rounded border-border"
              />
              <span>I understand which provider receives my live microphone stream and consent to start this voice session.</span>
            </label>
          ) : null}
        </div>
      ) : (
        <p className="rounded-md bg-muted/10 px-3 py-2 text-sm text-muted" role="status">
          <Loader2 className="mr-2 inline h-4 w-4 animate-spin" aria-hidden /> Loading the provider disclosure before microphone access…
        </p>
      )}

      {voice.error ? (
        <p className="flex items-start gap-2 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
          <AlertTriangle className="mt-0.5 h-4 w-4 flex-shrink-0" aria-hidden />
          <span>{voice.error}</span>
        </p>
      ) : null}

      {voice.micEnabled || connected ? (
        <div className="flex items-center gap-3">
          <span className="min-w-20 text-xs font-medium text-muted" aria-live="polite">
            {voice.phase === 'speaking' ? 'Patient speaking' : voice.micEnabled ? 'Listening' : 'Microphone paused'}
          </span>
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-background/70" aria-label="Microphone level">
            <div className="h-full rounded-full bg-emerald-500 transition-[width] duration-100" style={{ width: `${Math.round(voice.micLevel * 100)}%` }} />
          </div>
        </div>
      ) : null}

      <div className="max-h-64 min-h-[7rem] space-y-2 overflow-y-auto rounded-md bg-background/60 p-3" aria-live="polite">
        {voice.captions.length === 0 ? (
          <p className="text-sm text-muted">
            {connected ? 'Introduce yourself and open the consultation.' : 'Your live captions will appear here after connection.'}
          </p>
        ) : (
          voice.captions.map((caption) => (
            <p key={caption.id} className="text-sm">
              <span className={cn('mr-2 font-semibold', caption.speaker === 'patient' ? 'text-sky-700' : 'text-emerald-700')}>
                {caption.speaker === 'patient' ? 'Patient' : 'You'}:
              </span>
              <span className="text-foreground">{caption.text}</span>
            </p>
          ))
        )}
      </div>

      {!voice.ended ? (
        <div className="flex gap-2">
          {!connected ? (
            voice.connection === 'error' ? (
              <Button type="button" variant="outline" onClick={() => void voice.prepare()} className="w-full">
                Retry provider check
              </Button>
            ) : (
              <Button type="button" onClick={() => void voice.start(disclosureAccepted && micAllowed)} disabled={!canStart || voice.connection === 'connecting'} className="w-full">
                {voice.connection === 'connecting' ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Mic className="mr-2 h-4 w-4" />}
                {voice.connection === 'connecting' ? 'Connecting live voice…' : 'Start live voice'}
              </Button>
            )
          ) : (
            <>
              <Button type="button" onClick={voice.micEnabled ? voice.pauseMicrophone : voice.resumeMicrophone} variant="outline" className="flex-1">
                {voice.micEnabled ? <MicOff className="mr-2 h-4 w-4" /> : <Mic className="mr-2 h-4 w-4" />}
                {voice.micEnabled ? 'Pause microphone' : 'Resume microphone'}
              </Button>
              <Button type="button" onClick={() => void voice.stop()} variant="destructive" disabled={voice.connection === 'ending'}>
                {voice.connection === 'ending' ? <Loader2 className="h-4 w-4 animate-spin" /> : <Square className="h-4 w-4" />}
                <span className="sr-only">Stop and save live voice</span>
              </Button>
            </>
          )}
        </div>
      ) : (
        <p className="flex items-center justify-center gap-2 rounded-md bg-emerald-50 px-3 py-2 text-center text-sm text-emerald-700">
          <Check className="h-4 w-4" aria-hidden /> Live voice transcript saved for assessment.
        </p>
      )}
    </div>
  );
}

export default ExamConversationPanel;
