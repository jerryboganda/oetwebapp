'use client';

/**
 * Wait state of the live AI Speaking admission queue (owner decision 5 Oct 2026).
 *
 * Shown instead of the "Begin" button while the server's cap of live AI patient sessions is full. Nothing is timed
 * and no credit is held while it is on screen. It owns the retry loop: it repeats `onAttempt` (the same
 * finish-intro / finish-warmup call that put the learner in the line, which doubles as the heartbeat that keeps the
 * place) and the parent swaps this component out as soon as an attempt returns the normal next state.
 *
 * Polling is visibility-aware: every `pollAfterSeconds` (server-chosen) while the tab is visible, every 20 s while
 * hidden (slow, but inside the server's heartbeat window so a learner who switches tabs keeps the place), and an
 * immediate attempt the moment the tab becomes visible again. Attempts never overlap.
 */
import { useEffect, useRef } from 'react';
import { Clock, Loader2 } from 'lucide-react';
import { Button } from '@/components/ui/button';
import { cn } from '@/lib/utils';
import { formatAdmissionWait, type SpeakingLiveAdmission } from '@/lib/api/speaking-admission';

/** Retry interval while the tab is hidden. Browsers throttle hidden timers anyway; this stays inside the 90 s heartbeat. */
export const HIDDEN_RETRY_MS = 20_000;

export interface SpeakingAdmissionWaitProps {
  admission: SpeakingLiveAdmission;
  /** Which activity is waiting; only changes the wording. */
  subject: 'exam' | 'practice';
  /**
   * Repeats the start call. The parent applies the result (it unmounts this component once admitted) and handles
   * its own errors; a rejection here is swallowed and the next attempt is scheduled as usual.
   */
  onAttempt: () => Promise<unknown>;
  /** Optional "leave the queue" action. The place is given up once the page stops retrying. */
  onLeave?: () => void;
  className?: string;
}

export function SpeakingAdmissionWait({ admission, subject, onAttempt, onLeave, className }: SpeakingAdmissionWaitProps) {
  // The loop below lives for the whole wait but must always call the parent's latest handler / interval.
  const attemptRef = useRef(onAttempt);
  attemptRef.current = onAttempt;
  const pollSecondsRef = useRef(admission.pollAfterSeconds);
  pollSecondsRef.current = admission.pollAfterSeconds;

  useEffect(() => {
    let cancelled = false;
    let inFlight = false;
    let timer: number | undefined;

    // The server lengthens the interval for a very long line; a hidden tab never polls faster than that either.
    const delayMs = () => {
      const visibleMs = Math.max(2, pollSecondsRef.current) * 1_000;
      return document.visibilityState === 'hidden' ? Math.max(HIDDEN_RETRY_MS, visibleMs) : visibleMs;
    };

    const schedule = () => {
      if (cancelled) return;
      window.clearTimeout(timer);
      timer = window.setTimeout(() => void run(), delayMs());
    };

    const run = async () => {
      if (cancelled || inFlight) return;
      inFlight = true;
      try {
        await attemptRef.current();
      } catch {
        // The parent reports a refusal; a blip is retried on the next tick.
      } finally {
        inFlight = false;
        schedule();
      }
    };

    const onVisibilityChange = () => {
      if (document.visibilityState !== 'visible') return;
      window.clearTimeout(timer);
      void run();
    };

    document.addEventListener('visibilitychange', onVisibilityChange);
    schedule();
    return () => {
      cancelled = true;
      window.clearTimeout(timer);
      document.removeEventListener('visibilitychange', onVisibilityChange);
    };
  }, []);

  const activity = subject === 'exam' ? 'exam' : 'role-play';

  return (
    <section
      aria-labelledby="speaking-admission-wait-title"
      className={cn('space-y-4 rounded-2xl border border-border bg-surface p-4 sm:p-6', className)}
      data-testid="speaking-admission-wait"
    >
      <div className="flex items-start gap-3">
        <Clock className="mt-0.5 h-5 w-5 shrink-0 text-primary" aria-hidden />
        <div>
          <h2 id="speaking-admission-wait-title" className="text-lg font-bold text-navy">
            You&apos;re in the queue
          </h2>
          <p className="text-sm text-muted">
            Live AI patients are very busy right now. Your {activity} starts as soon as a place is free.
          </p>
        </div>
      </div>

      <div
        role="status"
        aria-live="polite"
        aria-atomic="true"
        className="rounded-lg border border-border bg-background-light p-3 text-sm text-navy"
        data-testid="speaking-admission-position"
      >
        <p>
          <strong>Position {admission.position}</strong> of {admission.queueLength}
        </p>
        <p className="mt-0.5 text-muted">Estimated wait: {formatAdmissionWait(admission.estimatedWaitSeconds)}</p>
      </div>

      <p className="text-xs leading-relaxed text-muted">
        No timer is running and no credit is used while you wait. Keep this page open: if you leave it for more than a
        minute or two, you give up your place.
      </p>

      <div className="flex items-center justify-between gap-3">
        <span className="inline-flex items-center gap-2 text-xs text-muted">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden /> Checking for a free place…
        </span>
        {onLeave ? (
          <Button type="button" variant="outline" size="sm" onClick={onLeave}>
            Leave the queue
          </Button>
        ) : null}
      </div>
    </section>
  );
}

export default SpeakingAdmissionWait;
