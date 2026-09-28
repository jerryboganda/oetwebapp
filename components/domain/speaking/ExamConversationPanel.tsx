'use client';

/**
 * 23 Sep 2026 owner flow: the ONE microphone / voice-activity indicator for
 * a timed Speaking card. No pause, no stop square, no consent here (consent
 * is taken on the Rules + consent step before prep). The page owns the single
 * "Finish & submit" and calls the `stop` handed out via `onVoiceStopReady`.
 *
 * - liveVoiceAvailable=true  → live AI patient (realtime voice).
 * - liveVoiceAvailable=false → recorder fallback: the role-play is recorded
 *   and uploaded on finish; the server transcribes and grades it.
 *
 * Both start automatically when the browser allows it (the learner already
 * tapped "Start speaking" / "Start preparation"); otherwise one simple
 * "Start speaking" control is shown.
 */
import { useEffect, useRef } from 'react';
import { AlertTriangle, Loader2, Mic } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useSpeakingRealtimeVoice } from '@/hooks/useSpeakingRealtimeVoice';
import { useSpeakingSessionRecorder } from '@/hooks/useSpeakingSessionRecorder';
import { OpenAppSettingsButton } from '@/components/domain/speaking/OpenAppSettingsButton';
import type { LiveVoiceProvider } from '@/lib/api/speaking-live-voice';

export interface ExamConversationPanelProps {
  sessionId: string;
  /** From the session/exam DTO. Missing/false = recorder fallback (production default today). */
  liveVoiceAvailable?: boolean;
  className?: string;
  requestedProvider?: LiveVoiceProvider;
  /** Receives the finalize hook: live = save transcript; fallback = stop + upload recording. */
  onVoiceStopReady?: (stop: (() => Promise<boolean>) | null) => void;
  /** Fired once when speaking actually begins (the 5-minute countdown starts here). */
  onSpeakingStarted?: () => void;
}

interface IndicatorProps {
  active: boolean;
  label: string;
  level: number;
}

/** The single voice-activity indicator. */
function ActivityIndicator({ active, label, level }: IndicatorProps) {
  return (
    <div className="flex items-center gap-3" data-testid="speaking-mic-indicator">
      <span
        className={cn(
          'flex h-10 w-10 shrink-0 items-center justify-center rounded-full',
          active ? 'bg-success/15 text-success' : 'bg-background-light text-muted',
        )}
        aria-hidden
      >
        <Mic className="h-5 w-5" />
      </span>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-semibold text-navy" aria-live="polite">{label}</p>
        <div className="mt-1 h-1.5 overflow-hidden rounded-full bg-background-light" aria-hidden>
          <div
            className="h-full rounded-full bg-success transition-[width] duration-100"
            style={{ width: `${active ? Math.round(level * 100) : 0}%` }}
          />
        </div>
      </div>
    </div>
  );
}

function canAutoStartAudio(): boolean {
  // Sticky activation: the learner tapped a control earlier in this document
  // (e.g. "Start speaking" at the end of prep), so audio playback is allowed.
  const activation = (navigator as Navigator & { userActivation?: { hasBeenActive: boolean } }).userActivation;
  return activation?.hasBeenActive ?? false;
}

function ErrorLine({ message }: { message: string }) {
  return (
    <p className="flex items-start gap-2 rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
      <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
      <span>{message}</span>
    </p>
  );
}

function StartButton({ busy, onClick }: { busy: boolean; onClick: () => void }) {
  return (
    <Button type="button" fullWidth onClick={onClick} disabled={busy}>
      {busy ? <Loader2 className="mr-2 h-4 w-4 animate-spin" aria-hidden /> : <Mic className="mr-2 h-4 w-4" aria-hidden />}
      Start speaking
    </Button>
  );
}

function LiveVoiceIndicator({ sessionId, requestedProvider, onVoiceStopReady, onSpeakingStarted }: ExamConversationPanelProps) {
  const voice = useSpeakingRealtimeVoice(sessionId, requestedProvider);
  const connected = voice.connection === 'connected';
  const autoTriedRef = useRef(false);
  const startedRef = useRef(false);

  useEffect(() => {
    onVoiceStopReady?.(voice.stop);
    return () => onVoiceStopReady?.(null);
  }, [onVoiceStopReady, voice.stop]);

  useEffect(() => {
    if (voice.connection !== 'ready' || autoTriedRef.current || !canAutoStartAudio()) return;
    autoTriedRef.current = true;
    void voice.start();
  }, [voice]);

  useEffect(() => {
    if (!connected || startedRef.current) return;
    startedRef.current = true;
    onSpeakingStarted?.();
  }, [connected, onSpeakingStarted]);

  const label = voice.ended
    ? 'Conversation saved'
    : voice.connection === 'ending'
      ? 'Saving conversation…'
      : connected
        ? voice.phase === 'speaking' ? 'Patient speaking' : 'Live — the patient is listening'
        : voice.connection === 'connecting' ? 'Connecting to the AI patient…' : 'Microphone off';

  return (
    <>
      <audio ref={voice.audioRef} autoPlay aria-label="Live patient voice" className="hidden" />
      <ActivityIndicator active={connected} label={label} level={voice.micLevel} />
      {voice.error ? <ErrorLine message={voice.error} /> : null}
      {!connected && !voice.ended && voice.connection !== 'ending' ? (
        voice.connection === 'error' && !voice.preflight ? (
          <Button type="button" variant="outline" fullWidth onClick={() => void voice.prepare()}>
            Retry connection
          </Button>
        ) : (
          <StartButton
            busy={voice.connection === 'connecting' || voice.connection === 'preparing'}
            onClick={() => void voice.start()}
          />
        )
      ) : null}
    </>
  );
}

function RecorderIndicator({ sessionId, onVoiceStopReady, onSpeakingStarted }: ExamConversationPanelProps) {
  const recorder = useSpeakingSessionRecorder(sessionId);
  const autoTriedRef = useRef(false);
  const startedRef = useRef(false);

  useEffect(() => {
    onVoiceStopReady?.(recorder.stop);
    return () => onVoiceStopReady?.(null);
  }, [onVoiceStopReady, recorder.stop]);

  useEffect(() => {
    if (autoTriedRef.current) return;
    autoTriedRef.current = true;
    void recorder.start();
  }, [recorder]);

  useEffect(() => {
    if (recorder.status !== 'recording' || startedRef.current) return;
    startedRef.current = true;
    onSpeakingStarted?.();
  }, [onSpeakingStarted, recorder.status]);

  const label = {
    idle: 'Microphone off',
    starting: 'Starting microphone…',
    recording: 'Recording — speak to the patient',
    uploading: 'Uploading your recording…',
    uploaded: 'Recording received',
    upload_failed: 'Recording kept on this device',
    error: 'Microphone off',
  }[recorder.status];

  return (
    <>
      <ActivityIndicator active={recorder.status === 'recording'} label={label} level={recorder.level} />
      {/* Upload failures are shown once, next to the page's Retry upload control. */}
      {recorder.error && recorder.status !== 'upload_failed' ? <ErrorLine message={recorder.error} /> : null}
      {recorder.status === 'error' && recorder.micPermissionDenied ? <OpenAppSettingsButton /> : null}
      {recorder.status === 'error' ? (
        <StartButton busy={false} onClick={() => void recorder.start()} />
      ) : null}
    </>
  );
}

export function ExamConversationPanel(props: ExamConversationPanelProps) {
  return (
    <div
      className={cn('flex flex-col gap-3 rounded-xl border border-border bg-surface p-4', props.className)}
      data-testid="speaking-conversation-panel"
    >
      {props.liveVoiceAvailable ? <LiveVoiceIndicator {...props} /> : <RecorderIndicator {...props} />}
    </div>
  );
}

export default ExamConversationPanel;
