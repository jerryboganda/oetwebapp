'use client';

import { Mic, MicOff, Square, Loader2 } from 'lucide-react';

interface Props {
  recording: boolean;
  disabled: boolean;
  ending: boolean;
  canEnd: boolean;
  onRecord: () => void;
  onEnd: () => void;
}

export function ConversationMicControl({ recording, disabled, ending, canEnd, onRecord, onEnd }: Props) {
  return (
    <div className="flex items-center justify-center gap-4 border-t border-gray-200 pt-4 dark:border-gray-700">
      <button onClick={onRecord} disabled={disabled} type="button"
        className={`group relative flex h-16 w-16 items-center justify-center rounded-full transition-all ${
          recording
            ? 'animate-pulse bg-red-500 text-white shadow-lg shadow-red-500/30 hover:bg-red-600'
            : 'bg-purple-600 text-white shadow-lg shadow-purple-500/20 hover:bg-purple-700'
        } disabled:opacity-50`}
        aria-label={recording ? 'Stop recording' : 'Start recording'}>
        {recording ? <MicOff className="h-7 w-7" /> : <Mic className="h-7 w-7" />}
        {recording && <span className="absolute -inset-1 animate-ping rounded-full border-2 border-red-500/50" aria-hidden />}
      </button>
      <button onClick={onEnd} type="button" disabled={ending || !canEnd}
        className="flex items-center gap-2 rounded-xl bg-gray-800 px-5 py-2.5 font-semibold text-white transition-colors hover:bg-gray-900 disabled:opacity-50 dark:bg-gray-700 dark:hover:bg-gray-600">
        {ending ? <Loader2 className="h-4 w-4 animate-spin" /> : <Square className="h-4 w-4" />}
        End conversation
      </button>
    </div>
  );
}
