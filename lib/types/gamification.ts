// ── Gamification Types ─────────────────────────────────────────────────
// Derived from GamificationEntities.cs field shapes

export interface LearnerXP {
  userId: string;
  totalXP: number;
  weeklyXP: number;
  monthlyXP: number;
  level: number;
  weekStartDate: string;   // ISO date string (DateOnly)
  monthStartDate: string;
}

export interface LearnerStreak {
  userId: string;
  currentStreak: number;
  longestStreak: number;
  lastActiveDate: string;  // ISO date string (DateOnly)
  streakFreezeCount: number;
  streakFreezeUsedCount: number;
  lastFreezeUsedDate: string | null;
}

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

export interface LearnerAchievement {
  id: string;
  userId: string;
  achievementId: string;
  achievement?: Achievement;
  unlockedAt: string;      // ISO datetime
  notified: boolean;
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

export interface GamificationSummary {
  xp: LearnerXP;
  streak: LearnerStreak;
  recentAchievements: LearnerAchievement[];
  unlockedCount: number;
  totalAchievements: number;
  nextMilestone: {
    label: string;
    xpRequired: number;
    xpCurrent: number;
    progressPercent: number;
  } | null;
}

export interface XPEvent {
  amount: number;
  reason: string;
  timestamp: string;
}

export interface AchievementUnlockEvent {
  achievement: Achievement;
  xpAwarded: number;
  timestamp: string;
}
