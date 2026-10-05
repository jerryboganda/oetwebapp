/**
 * Gamification + learner feature flags — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { apiRequest, isApiError } from './client';

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

/** Set once the API answers the batch route with "no such route" (an API without it): this page load then asks per key. */
let featureFlagBatchRouteUnavailable = false;

function isMissingRouteError(error: unknown): boolean {
  // An unmatched route has no JSON body, so the client labels it 'unknown_error'. The single-flag
  // route's own "not exposed to learners" 404 carries a code of its own and is a real answer.
  return isApiError(error)
    && ((error.status === 404 && error.code === 'unknown_error') || error.status === 405 || error.status === 501);
}

/** The pre-batch way: one request per key. A key the single route does not expose (404) reads disabled. */
async function fetchLearnerFeatureFlagsOneByOne(keys: readonly string[]): Promise<Record<string, boolean>> {
  const entries = await Promise.all(keys.map(async (key): Promise<[string, boolean]> => {
    try {
      const flag = await fetchLearnerFeatureFlag(key);
      return [key, flag?.enabled === true];
    } catch (error) {
      if (isApiError(error) && error.status === 404) return [key, false];
      throw error;
    }
  }));
  return Object.fromEntries(entries);
}

/**
 * Several learner release gates in one round trip (`GET /v1/features?keys=a,b`).
 * Resolves to a map keyed by the keys that were asked for. A key the server does
 * not expose to learners is simply absent from the answer and reads `false`, the
 * same fail-closed value the single-flag route's 404 produced. A failed request
 * rejects, like any other API call, so callers can tell "disabled" from "unknown".
 *
 * An API that does not have the batch route yet (an API-only rollback, or web and API
 * promoted a moment apart) answers it with "no such route": the helper then asks per key
 * on the single-flag route, and keeps doing so for the rest of the page load.
 */
export async function fetchLearnerFeatureFlags(featureKeys: readonly string[]): Promise<Record<string, boolean>> {
  const keys = Array.from(new Set(featureKeys.filter((key) => key.length > 0)));
  const result: Record<string, boolean> = Object.fromEntries(keys.map((key) => [key, false]));

  for (let start = 0; start < keys.length; start += LEARNER_FEATURE_FLAG_BATCH_MAX) {
    if (featureFlagBatchRouteUnavailable) {
      Object.assign(result, await fetchLearnerFeatureFlagsOneByOne(keys.slice(start)));
      return result;
    }

    const chunk = keys.slice(start, start + LEARNER_FEATURE_FLAG_BATCH_MAX);
    const params = new URLSearchParams({ keys: chunk.join(',') });
    let response: LearnerFeatureFlagBatchResponse | undefined;
    try {
      response = await apiRequest<LearnerFeatureFlagBatchResponse>(`/v1/features?${params}`);
    } catch (error) {
      if (!isMissingRouteError(error)) throw error;
      featureFlagBatchRouteUnavailable = true;
      Object.assign(result, await fetchLearnerFeatureFlagsOneByOne(keys.slice(start)));
      return result;
    }
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
