'use client';

import { useEffect, useState } from 'react';
import { Clock } from 'lucide-react';

export type WritingPhase = 'reading' | 'editing' | 'submitted';

interface WritingPhaseTimerProps {
  phase: WritingPhase;
  durationSeconds: number;
  startedAt?: number;
  onExpire: () => void;
}

function label(phase: WritingPhase) {
  if (phase === 'reading') return 'Reading window';
  if (phase === 'editing') return 'Writing window';
  return 'Submitted';
}

function format(totalSeconds: number) {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

export function WritingPhaseTimer({ phase, durationSeconds, startedAt, onExpire }: WritingPhaseTimerProps) {
  const [startMs, setStartMs] = useState(startedAt ?? 0);
  const [now, setNow] = useState(Date.now);

  useEffect(() => {
    window.queueMicrotask(() => {
      setStartMs(startedAt ?? Date.now());
      setNow(Date.now());
    });
  }, [startedAt, phase]);

  const remaining = phase === 'submitted' ? 0 : Math.max(0, Math.ceil((startMs + durationSeconds * 1000 - now) / 1000));

  useEffect(() => {
    if (phase === 'submitted' || remaining <= 0) return;
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [phase, remaining]);

  useEffect(() => {
    if (phase !== 'submitted' && remaining <= 0) onExpire();
  }, [phase, remaining, onExpire]);

  return (
    <div className="rounded-2xl border border-primary/20 bg-primary/5 p-4" role="timer" aria-live="polite">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Clock className="h-5 w-5 text-primary" aria-hidden />
          <div>
            <p className="text-xs font-black uppercase tracking-widest text-primary">{label(phase)}</p>
            <p className="text-sm text-muted">
              {phase === 'reading'
                ? 'Read the case notes only. The editor unlocks after this phase.'
                : phase === 'editing'
                  ? 'Write your letter. AI and grammar assistance are disabled in exam mode.'
                  : 'Your response is locked.'}
            </p>
          </div>
        </div>
        <span className="rounded-full bg-surface px-4 py-2 font-mono text-lg font-black text-navy">
          {format(remaining)}
        </span>
      </div>
    </div>
  );
}
