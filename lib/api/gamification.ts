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

/** The server resolves at most this many keys per request; the helper splits larger sets. */
export const LEARNER_FEATURE_FLAG_BATCH_MAX = 16;

interface LearnerFeatureFlagBatchResponse {
  flags?: LearnerFeatureFlag[] | null;
}

/**
 * Several learner release gates in one round trip (`GET /v1/features?keys=a,b`).
 * Resolves to a map keyed by the keys that were asked for. A key the server does
 * not expose to learners is simply absent from the answer and reads `false`, the
 * same fail-closed value the single-flag route's 404 produced. A failed request
 * rejects, like any other API call, so callers can tell "disabled" from "unknown".
 */
export async function fetchLearnerFeatureFlags(featureKeys: readonly string[]): Promise<Record<string, boolean>> {
  const keys = Array.from(new Set(featureKeys.filter((key) => key.length > 0)));
  const result: Record<string, boolean> = Object.fromEntries(keys.map((key) => [key, false]));

  for (let start = 0; start < keys.length; start += LEARNER_FEATURE_FLAG_BATCH_MAX) {
    const chunk = keys.slice(start, start + LEARNER_FEATURE_FLAG_BATCH_MAX);
    const params = new URLSearchParams({ keys: chunk.join(',') });
    const response = await apiRequest<LearnerFeatureFlagBatchResponse>(`/v1/features?${params}`);
    for (const flag of response?.flags ?? []) {
      if (typeof flag?.key === 'string' && chunk.includes(flag.key)) {
        result[flag.key] = flag.enabled === true;
      }
    }
  }

  return result;
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
