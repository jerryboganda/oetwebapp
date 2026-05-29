'use client';

import { Mic, MicOff, Square, Loader2 } from 'lucide-react';
import type { ConversationTurnState } from '@/lib/types/conversation';

interface Props {
  recording: boolean;
  disabled: boolean;
  ending: boolean;
  canEnd: boolean;
  turnState?: ConversationTurnState;
  disabledReason?: string | null;
  micLevel?: number;
  onRecord: () => void;
  onEnd: () => void;
}

function actionLabel(recording: boolean, turnState?: ConversationTurnState) {
  if (recording) return 'Stop turn';
  if (turnState === 'sending') return 'Submitting answer';
  if (turnState === 'fallback') return 'Normal recording ready';
  if (turnState === 'ai-speaking') return 'AI speaking';
  if (turnState === 'ai-thinking') return 'AI thinking';
  if (turnState === 'reconnecting') return 'Reconnecting';
  if (turnState === 'error') return 'Check microphone';
  return 'Speak';
}

export function ConversationMicControl({ recording, disabled, ending, canEnd, turnState, disabledReason, micLevel = 0, onRecord, onEnd }: Props) {
  const levelPct = Math.max(0, Math.min(100, micLevel * 100));
  return (
    <div className="border-t border-border pt-4">
      <div className="flex items-center justify-center gap-4">
        <button onClick={onRecord} disabled={disabled} type="button"
          className={`group relative flex h-16 w-16 items-center justify-center rounded-full transition-[color,background-color,border-color,box-shadow,transform,opacity,filter] duration-200 ${
            recording
              ? 'motion-safe:animate-pulse bg-danger text-white shadow-lg shadow-danger/30 hover:bg-danger/90'
              : 'bg-primary text-white shadow-lg shadow-primary/20 hover:bg-primary/90 active:scale-[0.98] motion-reduce:active:scale-100 dark:bg-violet-700 dark:hover:bg-violet-600'
          } disabled:opacity-50`}
          aria-label={recording ? 'Stop recording' : actionLabel(recording, turnState)}>
          {recording ? <MicOff className="h-7 w-7" /> : <Mic className="h-7 w-7" />}
          {recording && <span className="absolute -inset-1 motion-safe:animate-ping rounded-full border-2 border-danger/50" aria-hidden />}
        </button>
      <button onClick={onEnd} type="button" disabled={ending || !canEnd}
        className="flex items-center gap-2 rounded-xl bg-navy px-5 py-2.5 font-semibold text-white transition-colors hover:bg-navy/90 dark:text-slate-900 disabled:opacity-50">
        {ending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Square className="h-4 w-4" />}
        End conversation
      </button>
      </div>
      <div className="mt-3 text-center text-xs text-muted" role="status" aria-live="polite">
        <span className="font-semibold">{actionLabel(recording, turnState)}</span>
        {disabledReason && <span className="ml-2">{disabledReason}</span>}
      </div>
      {recording && (
        <div className="mx-auto mt-2 h-1.5 w-40 overflow-hidden rounded-full bg-primary/10">
          <div className="h-full rounded-full bg-primary transition-[width] duration-150" style={{ width: `${levelPct}%` }} />
        </div>
      )}
    </div>
  );
}
