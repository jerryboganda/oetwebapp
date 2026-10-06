'use client';

import { useMemo, useRef } from 'react';
import { useTranslations } from 'next-intl';
import { releaseDeadlineMs } from '@/lib/writing/release';
import type { WritingReleaseState } from '@/lib/writing/types';
import { useDeadlineCountdown } from '@/lib/writing/useCountdown';
import { cn } from '@/lib/utils';

export interface WritingReleaseCountdownProps {
  /** Server release instant; null/absent for accounts without a hold and for failed rows (renders nothing). */
  releaseAt?: string | null;
  /** Server clock when `releaseAt` was sent: the deadline is anchored to it, never to the device clock. */
  serverNow?: string;
  releaseState?: WritingReleaseState | null;
  /** Fires once at zero, and only for a finished-but-held result, so the host can refetch it. */
  onHeldElapsed?: () => void;
  /** Just the mm:ss timer (list rows); renders nothing once the window has elapsed. */
  compact?: boolean;
  /** False when the host already shows the release notice (e.g. in its hero). */
  showNotice?: boolean;
  className?: string;
}

/**
 * Release-aware poll delay for pages that watch a submission. Inside the window a
 * finished (held) result cannot change, so poll sparsely and land just after the
 * release time; once the window has passed the server releases the moment
 * grading is done, so poll at the plain pace (at most 4 s). `baseMs` is the pace
 * with no hold in play.
 */
export function releasePollDelayMs(
  sub: { releaseAt?: string | null; serverNow?: string; releaseState?: string | null } | null | undefined,
  baseMs: number,
): number {
  const deadline = releaseDeadlineMs(sub?.releaseAt, sub?.serverNow);
  if (deadline == null) return baseMs;
  const untilRelease = deadline - Date.now();
  if (untilRelease <= 0) return Math.min(baseMs, 4000);
  const cap = sub?.releaseState === 'held' ? 15_000 : baseMs;
  return Math.max(1000, Math.min(cap, untilRelease + 500));
}

const HELD_REFETCH_MIN_GAP_MS = 4000;

function formatMmSs(totalSeconds: number): string {
  const safe = Math.max(0, Math.floor(totalSeconds));
  const m = Math.floor(safe / 60).toString().padStart(2, '0');
  const s = (safe % 60).toString().padStart(2, '0');
  return `${m}:${s}`;
}

/**
 * The visible release countdown (15:00 at submission). Every fetch brings a
 * fresh releaseAt/serverNow pair, which re-anchors the deadline, so a refresh,
 * another device or a skewed clock can never reset or duplicate it. At zero it
 * never claims a result: a held result is refetched via `onHeldElapsed`, and a
 * letter still being processed shows a finalising line until the server
 * releases it.
 */
export function WritingReleaseCountdown({
  releaseAt,
  serverNow,
  releaseState,
  onHeldElapsed,
  compact = false,
  showNotice = true,
  className,
}: WritingReleaseCountdownProps) {
  const t = useTranslations();
  const deadline = useMemo(() => releaseDeadlineMs(releaseAt, serverNow), [releaseAt, serverNow]);
  const lastRefetchAtRef = useRef(0);
  const seconds = useDeadlineCountdown(deadline, {
    // Held only: for 'processing' the host's normal poll delivers the release,
    // and firing here would refetch in a loop (releaseAt <= serverNow stays true).
    // Every refetch re-anchors the deadline and re-arms this callback, so a server
    // that keeps answering 'held' is retried at most once per HELD_REFETCH_MIN_GAP_MS.
    onZero: () => {
      if (releaseState !== 'held') return;
      const now = Date.now();
      if (now - lastRefetchAtRef.current < HELD_REFETCH_MIN_GAP_MS) return;
      lastRefetchAtRef.current = now;
      onHeldElapsed?.();
    },
  });

  if (deadline == null || releaseState === 'released') return null;

  const elapsed = seconds === 0;
  const label = t('writing.release.countdownLabel');

  if (compact) {
    if (elapsed) return null;
    return (
      <p role="timer" aria-label={label} data-testid="writing-release-timer" className={cn('text-xs font-bold tabular-nums text-warning-strong', className)}>
        {formatMmSs(seconds)}
      </p>
    );
  }

  return (
    <div data-testid="writing-release-countdown" data-release-state={releaseState ?? undefined} className={cn('space-y-2', className)}>
      {elapsed ? (
        <p role="status" className="text-sm font-semibold text-navy">{t('writing.release.finalising')}</p>
      ) : (
        <div>
          <p className="tile-label text-muted">{label}</p>
          <p role="timer" aria-label={label} data-testid="writing-release-timer" className="text-3xl font-black tabular-nums text-navy">
            {formatMmSs(seconds)}
          </p>
        </div>
      )}
      {showNotice && !elapsed ? <p className="text-sm text-muted">{t('writing.release.notice')}</p> : null}
      <p className="text-xs text-muted">{t('writing.release.savedNote')}</p>
    </div>
  );
}
