'use client';

import { useContext } from 'react';
import { CelebrationBurst } from '@/components/ui/celebration-burst';
import { AuthContext } from '@/contexts/auth-context';
import { useUserProfileQuery } from '@/lib/query/hooks';
import { getWritingPassThreshold } from '@/lib/scoring';

/**
 * Bursts once per submission when a Writing practice score meets the learner's
 * own Writing target or, without one, their destination country's pass mark.
 * Writing is country-aware (AGENTS.md), so nothing is assumed: no profile, no
 * resolvable country, no burst.
 */
export function WritingPassCelebration({ score, onceKey }: { score: number; onceKey: string }) {
  const userId = useContext(AuthContext)?.user?.userId ?? '';
  const { data: profile } = useUserProfileQuery(userId, { enabled: Boolean(userId) });
  const target = profile?.targetScores.Writing;
  const passMark = typeof target === 'number' ? target : getWritingPassThreshold(profile?.targetCountry)?.threshold ?? null;

  return <CelebrationBurst active={passMark !== null && score >= passMark} onceKey={onceKey} />;
}
