/**
 * Gamification + learner feature flags — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest } from './client';

export interface LearnerFeatureFlag {
  key: string;
  enabled: boolean;
}

export async function fetchXP() {
  return apiRequest('/v1/gamification/xp');
}

export async function fetchStreak() {
  return apiRequest('/v1/gamification/streak');
}

export async function fetchLearnerFeatureFlag(featureKey: string) {
  return apiRequest<LearnerFeatureFlag>(`/v1/features/${encodeURIComponent(featureKey)}`);
}

export async function recordActivity() {
  return apiRequest('/v1/gamification/streak/activity', { method: 'POST' });
}

export async function fetchAchievements() {
  return apiRequest('/v1/gamification/achievements');
}

export async function fetchLeaderboard(examTypeCode?: string, period = 'weekly') {
  const params = new URLSearchParams({ period });
  if (examTypeCode) params.set('examTypeCode', examTypeCode);
  return apiRequest(`/v1/gamification/leaderboard?${params}`);
}

export async function fetchMyLeaderboardPosition(examTypeCode?: string, period = 'weekly') {
  const params = new URLSearchParams({ period });
  if (examTypeCode) params.set('examTypeCode', examTypeCode);
  return apiRequest(`/v1/gamification/leaderboard/my-position?${params}`);
}

export async function setLeaderboardOptIn(optedIn: boolean) {
  return apiRequest('/v1/gamification/leaderboard/opt-in', {
    method: 'POST',
    body: JSON.stringify({ optedIn }),
  });
}
