'use client';

import { useContext } from 'react';
import { CelebrationBurst } from '@/components/ui/celebration-burst';
import { AuthContext } from '@/contexts/auth-context';
import { useUserProfileQuery } from '@/lib/query/hooks';
import { getWritingPassThreshold } from '@/lib/scoring';

/**
 * The learner's Writing pass mark: their own Writing target or, without one,
 * their destination country's pass mark. Writing is country-aware (AGENTS.md),
 * so nothing is assumed: no profile or no resolvable country gives null.
 */
export function useWritingPassMark(): number | null {
  const userId = useContext(AuthContext)?.user?.userId ?? '';
  const { data: profile } = useUserProfileQuery(userId, { enabled: Boolean(userId) });
  const target = profile?.targetScores.Writing;
  return typeof target === 'number' ? target : getWritingPassThreshold(profile?.targetCountry)?.threshold ?? null;
}

/**
 * Green at or above the learner's pass mark, amber within one 50-point band
 * below it, red under that. Neutral while the pass mark is unknown: Writing is
 * country-aware, so a threshold is never assumed (AGENTS.md).
 */
export function writingGaugeColor(score: number, passMark: number | null): string {
  if (passMark === null) return 'var(--color-primary)';
  if (score >= passMark) return 'var(--color-success)';
  return score >= passMark - 50 ? 'var(--color-warning)' : 'var(--color-danger)';
}

/** Bursts once per submission when a Writing practice score meets the learner's pass mark. */
export function WritingPassCelebration({ score, onceKey }: { score: number; onceKey: string }) {
  const passMark = useWritingPassMark();
  return <CelebrationBurst active={passMark !== null && score >= passMark} onceKey={onceKey} />;
}
