// ── Gamification Types ─────────────────────────────────────────────────
// Derived from GamificationEntities.cs field shapes


export interface Achievement {
  id: string;
  code: string;
  label: string;
  description: string;
  category: 'practice' | 'streak' | 'milestone' | 'mastery' | 'social';
  iconUrl: string | null;
  xpReward: number;
  criteriaJson: string;
  sortOrder: number;
  status: 'active' | 'inactive';
}


export interface LeaderboardEntry {
  id: string;
  userId: string;
  displayName: string;
  examTypeCode: string;
  period: 'weekly' | 'monthly' | 'alltime';
  periodStart: string;    // ISO date string (DateOnly)
  xp: number;
  rank: number;
  optedIn: boolean;
}

