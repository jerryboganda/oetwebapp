'use client';

import { useEffect, useMemo, useRef, useState } from 'react';
import { Clock, Lock } from 'lucide-react';
import { InlineAlert } from '@/components/ui/alert';

interface PartAStrictTimerProps {
  durationSeconds?: number;
  startedAt?: string | null;
  locked?: boolean;
  onExpire?: () => void;
}

function formatRemaining(totalSeconds: number) {
  const minutes = Math.floor(totalSeconds / 60);
  const seconds = totalSeconds % 60;
  return `${minutes}:${seconds.toString().padStart(2, '0')}`;
}

export function PartAStrictTimer({
  durationSeconds = 15 * 60,
  startedAt,
  locked = false,
  onExpire,
}: PartAStrictTimerProps) {
  const started = useMemo(() => {
    const parsed = startedAt ? Date.parse(startedAt) : Date.now();
    return Number.isFinite(parsed) ? parsed : Date.now();
  }, [startedAt]);
  const [now, setNow] = useState(() => Date.now());
  const expiredReportedRef = useRef(false);
  const remaining = Math.max(0, Math.ceil((started + durationSeconds * 1000 - now) / 1000));
  const isExpired = remaining <= 0 || locked;

  useEffect(() => {
    if (isExpired) return;
    const id = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(id);
  }, [isExpired]);

  useEffect(() => {
    if (!isExpired || expiredReportedRef.current) return;
    expiredReportedRef.current = true;
    onExpire?.();
  }, [isExpired, onExpire]);

  if (isExpired) {
    return (
      <InlineAlert variant="warning">
        <span className="inline-flex items-center gap-2">
          <Lock className="h-4 w-4" aria-hidden />
          Reading Part A is locked. In exam mode, answers auto-submit when the 15-minute window expires.
        </span>
      </InlineAlert>
    );
  }

  return (
    <div className="rounded-2xl border border-warning/30 bg-warning/5 p-4" role="timer" aria-live="polite">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <div className="flex items-center gap-2">
          <Clock className="h-5 w-5 text-warning" aria-hidden />
          <div>
            <p className="text-xs font-black uppercase tracking-widest text-warning">Reading Part A strict timer</p>
            <p className="text-sm text-muted">This section locks automatically when the window ends.</p>
          </div>
        </div>
        <span className="rounded-full bg-surface px-4 py-2 font-mono text-lg font-black text-navy">
          {formatRemaining(remaining)}
        </span>
      </div>
      {remaining <= 120 ? (
        <p className="mt-3 text-sm font-bold text-warning">Two-minute warning: finish your Part A answers now.</p>
      ) : null}
    </div>
  );
}
