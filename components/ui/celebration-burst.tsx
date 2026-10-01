'use client';

import { useReducedMotionConfig } from 'motion/react';
import { useEffect, useState, type CSSProperties } from 'react';
import { triggerNotificationHaptic } from '@/lib/mobile/haptics';
import { prefersReducedMotion } from '@/lib/motion';
import { cn } from '@/lib/utils';
import { useHasHydrated } from './motion-primitives';

const COLORS = ['var(--color-primary)', 'var(--color-success)', 'var(--color-gold)', 'var(--color-info)', 'var(--color-warning)'];

// Deterministic spread (no Math.random): 20 particles around the full circle.
const PARTICLES = Array.from({ length: 20 }, (_, i) => ({
  angle: i * 18 + (i % 3) * 7,
  distance: 64 + (i % 4) * 18,
  size: 6 + (i % 3) * 1.5,
  delay: (i % 5) * 24,
  spin: (i % 2 ? 1 : -1) * (120 + (i % 4) * 60),
  round: i % 4 === 0,
  color: COLORS[i % COLORS.length],
}));

function celebratedKey(onceKey: string) {
  return `oet_celebrated:${onceKey}`;
}

function alreadyCelebrated(onceKey?: string) {
  if (!onceKey) return false;
  try {
    return window.sessionStorage.getItem(celebratedKey(onceKey)) === '1';
  } catch {
    return false;
  }
}

export interface CelebrationBurstProps {
  /** Fires when this becomes true; pass a real outcome (target grade met, streak up). */
  active: boolean;
  /** Celebrate this event once per session, e.g. `writing-result:${submissionId}`. */
  onceKey?: string;
}

/**
 * A short confetti burst for real wins, drawn with CSS keyframes (no library).
 * Place it inside a `relative` box; it bursts from that box's centre without
 * affecting layout. Not rendered under reduced motion or on exam/live routes
 * (their MotionConfig reports reduced motion), and a native success haptic
 * accompanies it in the mobile app.
 */
export function CelebrationBurst({ active, onceKey }: CelebrationBurstProps) {
  const hasHydrated = useHasHydrated();
  const reducedMotion = prefersReducedMotion(useReducedMotionConfig());
  // Read once per mount: a burst seen this session never repeats on revisit.
  const [seen] = useState(() => typeof window !== 'undefined' && alreadyCelebrated(onceKey));
  const fire = hasHydrated && active && !reducedMotion && !seen;

  useEffect(() => {
    if (!fire) return;
    if (onceKey) {
      try {
        window.sessionStorage.setItem(celebratedKey(onceKey), '1');
      } catch {
        // Storage disabled: the burst may repeat on a revisit, which is harmless.
      }
    }
    void triggerNotificationHaptic('SUCCESS');
  }, [fire, onceKey]);

  if (!fire) return null;

  return (
    <span aria-hidden="true" className="pointer-events-none absolute inset-0 z-10">
      {PARTICLES.map((p, i) => (
        <span
          key={i}
          className={cn('celebration-particle', p.round ? 'rounded-full' : 'rounded-[2px]')}
          style={{
            '--angle': `${p.angle}deg`,
            '--distance': `${p.distance}px`,
            '--size': `${p.size}px`,
            '--delay': `${p.delay}ms`,
            '--spin': `${p.spin}deg`,
            '--color': p.color,
          } as CSSProperties}
        />
      ))}
    </span>
  );
}
