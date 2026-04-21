'use client';

import { Clock } from 'lucide-react';

interface Props {
  elapsed: number;
  timeLimit: number;
  turns: number;
  scenarioTitle?: string;
}

function formatTime(s: number) {
  return `${Math.floor(s / 60).toString().padStart(2, '0')}:${(s % 60).toString().padStart(2, '0')}`;
}

export function ConversationTimerBar({ elapsed, timeLimit, turns, scenarioTitle }: Props) {
  const pct = timeLimit > 0 ? Math.min(100, (elapsed / timeLimit) * 100) : 0;
  return (
    <>
      <div className="mb-3 flex items-center justify-between px-1">
        <div className="flex items-center gap-2 text-sm text-gray-500">
          <Clock className="h-4 w-4" />
          <span className="tabular-nums font-semibold">{formatTime(elapsed)} / {formatTime(timeLimit)}</span>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-xs text-gray-400">{turns} turns</span>
          {scenarioTitle && <span className="text-xs font-semibold text-purple-500">{scenarioTitle}</span>}
        </div>
      </div>
      <div className="mb-4 h-1.5 w-full rounded-full bg-gray-200 dark:bg-gray-700">
        <div className={`h-1.5 rounded-full transition-all duration-1000 ${
          pct > 80 ? 'bg-red-500' : pct > 60 ? 'bg-yellow-500' : 'bg-purple-500'}`}
          style={{ width: `${pct}%` }} />
      </div>
    </>
  );
}
