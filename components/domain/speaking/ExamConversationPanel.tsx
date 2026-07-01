'use client';

/**
 * Speaking module — AI patient conversation panel for the Active phase of one
 * exam card.
 *
 * Thin view over `useSpeakingConversation`: it renders the live caption strip,
 * connection/turn status, a mic level meter, and the hands-free controls. The
 * exam page owns the countdown + phase machine; this panel is purely the
 * conversation surface.
 *
 * Voice loop (hands-free, barge-in): tap "Start talking" once (browsers require
 * a user gesture for mic + audio), then just speak — the AI patient listens,
 * replies in a real voice, and the mic re-opens automatically.
 */
import { useState } from 'react';
import { Loader2, Mic, MicOff, Send, Volume2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { useSpeakingConversation, type ConversationPhase } from '@/hooks/useSpeakingConversation';

export interface ExamConversationPanelProps {
  /** The child SpeakingSession id for the current card. */
  sessionId: string;
  className?: string;
}

const PHASE_LABEL: Record<ConversationPhase, string> = {
  idle: 'Paused',
  listening: 'Listening…',
  thinking: 'Thinking…',
  speaking: 'Speaking…',
};

export function ExamConversationPanel({ sessionId, className }: ExamConversationPanelProps) {
  const {
    connection,
    phase,
    captions,
    error,
    micEnabled,
    micLevel,
    voiceUnavailable,
    ended,
    enableMic,
    disableMic,
    sendText,
  } = useSpeakingConversation(sessionId);

  const [text, setText] = useState('');
  const [showText, setShowText] = useState(false);
  const connected = connection === 'connected';

  const submitText = async () => {
    const value = text.trim();
    if (!value) return;
    setText('');
    await sendText(value);
  };

  return (
    <div className={cn('flex flex-col gap-3 rounded-xl border border-border bg-surface p-4', className)}>
      <div className="flex items-center justify-between">
        <span className="text-sm font-medium text-foreground">AI patient</span>
        <span
          className={cn(
            'inline-flex items-center gap-1.5 text-xs',
            connected ? 'text-emerald-600' : connection === 'error' ? 'text-rose-600' : 'text-muted',
          )}
        >
          <span
            className={cn(
              'h-2 w-2 rounded-full',
              connected ? 'bg-emerald-500' : connection === 'error' ? 'bg-rose-500' : 'bg-amber-400',
            )}
          />
          {connected ? 'Connected' : connection === 'error' ? 'Disconnected' : 'Connecting…'}
        </span>
      </div>

      {error ? (
        <p className="rounded-md bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
          {error}
        </p>
      ) : null}

      {voiceUnavailable ? (
        <p className="flex items-center gap-2 rounded-md bg-amber-50 px-3 py-2 text-xs text-amber-800">
          <Volume2 className="h-4 w-4 flex-shrink-0" />
          The patient&apos;s voice is currently unavailable — showing text only. Please tell your administrator.
        </p>
      ) : null}

      {/* Live status + mic meter */}
      {micEnabled ? (
        <div className="flex items-center gap-3">
          <span
            className={cn(
              'inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium',
              phase === 'listening' && 'bg-emerald-50 text-emerald-700',
              phase === 'thinking' && 'bg-sky-50 text-sky-700',
              phase === 'speaking' && 'bg-violet-50 text-violet-700',
              phase === 'idle' && 'bg-muted/10 text-muted',
            )}
            aria-live="polite"
          >
            {phase === 'thinking' ? <Loader2 className="h-3 w-3 animate-spin" /> : null}
            {PHASE_LABEL[phase]}
          </span>
          <div className="h-1.5 flex-1 overflow-hidden rounded-full bg-background/70">
            <div
              className="h-full rounded-full bg-emerald-500 transition-[width] duration-100"
              style={{ width: `${Math.round(micLevel * 100)}%` }}
            />
          </div>
        </div>
      ) : null}

      <div
        className="max-h-56 min-h-[6rem] space-y-2 overflow-y-auto rounded-md bg-background/60 p-3"
        aria-live="polite"
      >
        {captions.length === 0 ? (
          <p className="text-sm text-muted">
            {micEnabled
              ? 'The patient will speak first. Just talk naturally when you are ready.'
              : 'Tap “Start talking” to begin the conversation with the patient.'}
          </p>
        ) : (
          captions.map((c) => (
            <p key={c.id} className="text-sm">
              <span
                className={cn(
                  'mr-2 font-semibold',
                  c.speaker === 'patient' ? 'text-sky-700' : 'text-emerald-700',
                )}
              >
                {c.speaker === 'patient' ? 'Patient' : 'You'}:
              </span>
              <span className="text-foreground">{c.text}</span>
            </p>
          ))
        )}
      </div>

      {ended ? (
        <p className="rounded-md bg-muted/10 px-3 py-2 text-center text-sm text-muted">
          The patient has finished. The card will move on automatically.
        </p>
      ) : !micEnabled ? (
        <Button type="button" onClick={() => void enableMic()} disabled={!connected} className="w-full">
          <Mic className="mr-2 h-4 w-4" /> Start talking
        </Button>
      ) : (
        <Button type="button" onClick={disableMic} variant="outline" className="w-full">
          <MicOff className="mr-2 h-4 w-4" /> Pause microphone
        </Button>
      )}

      {/* Keyboard fallback for accessibility / no-mic environments */}
      {!ended ? (
        <div>
          {showText ? (
            <div className="flex items-center gap-2">
              <input
                type="text"
                value={text}
                onChange={(e) => setText(e.target.value)}
                onKeyDown={(e) => {
                  if (e.key === 'Enter') void submitText();
                }}
                placeholder="Type your response…"
                className="flex-1 rounded-md border border-border bg-background px-3 py-2 text-sm text-foreground outline-none focus:border-primary"
                aria-label="Type your response to the patient"
              />
              <Button type="button" size="sm" onClick={() => void submitText()} disabled={!connected || !text.trim()}>
                <Send className="h-4 w-4" />
              </Button>
            </div>
          ) : (
            <button
              type="button"
              onClick={() => setShowText(true)}
              className="text-xs text-muted underline underline-offset-2 hover:text-foreground"
            >
              Type instead
            </button>
          )}
        </div>
      ) : null}
    </div>
  );
}

export default ExamConversationPanel;
