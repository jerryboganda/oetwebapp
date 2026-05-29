'use client';

import { Clock } from 'lucide-react';
import type { RealtimeSttMode, SpeakingSessionConnectionState } from '@/lib/types/conversation';

interface Props {
  elapsed: number;
  timeLimit: number;
  turns: number;
  scenarioTitle?: string;
  connectionState?: SpeakingSessionConnectionState;
  sttMode?: RealtimeSttMode;
  fallbackReason?: string | null;
}

function formatTime(s: number) {
  return `${Math.floor(s / 60).toString().padStart(2, '0')}:${(s % 60).toString().padStart(2, '0')}`;
}

function statusLabel(connectionState: SpeakingSessionConnectionState, sttMode: RealtimeSttMode) {
  if (sttMode === 'batch-fallback') return 'Normal recording';
  return connectionState.replace('-', ' ');
}

function fallbackCopy(reason: string) {
  const normalized = reason.toLowerCase();
  if (normalized.includes('not_configured') || normalized.includes('unavailable')) {
    return 'Live captions are unavailable; your turn will transcribe after recording.';
  }
  if (normalized.includes('disabled') || normalized.includes('gated')) {
    return 'Using normal recording for this practice session.';
  }
  if (normalized.includes('large') || normalized.includes('size')) {
    return 'Live captions paused for this turn; recording is still saved normally.';
  }
  return reason;
}

export function ConversationTimerBar({ elapsed, timeLimit, turns, scenarioTitle, connectionState = 'idle', sttMode = 'batch-fallback', fallbackReason }: Props) {
  const pct = timeLimit > 0 ? Math.min(100, (elapsed / timeLimit) * 100) : 0;
  return (
    <>
      <div className="mb-3 flex items-center justify-between px-1">
        <div className="flex flex-wrap items-center gap-2 text-sm text-muted" role="status" aria-live="polite">
          <span className="inline-flex items-center gap-2">
            <Clock className="h-4 w-4" />
            <span className="tabular-nums font-semibold">{formatTime(elapsed)} / {formatTime(timeLimit)}</span>
          </span>
          <span className="rounded-full border border-border bg-surface px-2 py-0.5 text-xs font-semibold capitalize text-muted">
            {statusLabel(connectionState, sttMode)}
          </span>
          {fallbackReason && (
            <span className="text-xs text-warning">{fallbackCopy(fallbackReason)}</span>
          )}
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-muted/60">{turns} turns</span>
          {scenarioTitle && <span className="text-xs font-semibold text-primary">{scenarioTitle}</span>}
        </div>
      </div>
      <div
        className="mb-4 h-1.5 w-full rounded-full bg-background-light"
        role="progressbar"
        aria-label="Conversation time elapsed"
        aria-valuemin={0}
        aria-valuemax={timeLimit}
        aria-valuenow={Math.min(elapsed, timeLimit)}
      >
        <div className={`h-1.5 rounded-full transition-[width,background-color] duration-1000 ease-linear ${
          pct > 80 ? 'bg-danger' : pct > 60 ? 'bg-warning' : 'bg-primary'}`}
          style={{ width: `${pct}%` }} />
      </div>
    </>
  );
}
