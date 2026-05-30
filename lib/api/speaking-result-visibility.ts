/**
 * WS6 (§10) — typed API client for the Speaking result-visibility config.
 *
 * Backed by `SpeakingResultVisibilityEndpoints.cs`:
 *
 *   GET  /v1/speaking/result-visibility?rolePlayCardId=...   (learner-effective)
 *   GET  /v1/admin/speaking/result-visibility?rolePlayCardId=...   (admin)
 *   PUT  /v1/admin/speaking/result-visibility                 (admin upsert)
 *
 * The effective config gates what a learner sees on the Speaking result
 * surface. AI estimate / readiness band are always advisory ("estimate, not
 * official") regardless of these flags.
 */
import { apiClient } from '@/lib/api';

export interface SpeakingResultVisibilityDto {
  /** Null for the global default row; otherwise the role-play card id. */
  rolePlayCardId: string | null;
  showSubmissionReceived: boolean;
  showAiEstimate: boolean;
  showReadinessBand: boolean;
  showTutorScore: boolean;
  showFullCriteria: boolean;
  showTranscript: boolean;
  showTutorComments: boolean;
  showRecommendedDrills: boolean;
  allowReattempt: boolean;
  updatedAt: string;
}

/** Admin PUT body: the flags plus the optional target card id. */
export type SpeakingResultVisibilityUpsert = Omit<SpeakingResultVisibilityDto, 'updatedAt'>;

function withCardQuery(path: string, rolePlayCardId?: string | null): string {
  if (!rolePlayCardId) return path;
  return `${path}?rolePlayCardId=${encodeURIComponent(rolePlayCardId)}`;
}

/** Learner-effective visibility for a card (or the global default). */
export async function getSpeakingResultVisibility(
  rolePlayCardId?: string | null,
): Promise<SpeakingResultVisibilityDto> {
  return apiClient.get<SpeakingResultVisibilityDto>(
    withCardQuery('/v1/speaking/result-visibility', rolePlayCardId),
  );
}

/** Admin: read the global or per-card config. */
export async function adminGetSpeakingResultVisibility(
  rolePlayCardId?: string | null,
): Promise<SpeakingResultVisibilityDto> {
  return apiClient.get<SpeakingResultVisibilityDto>(
    withCardQuery('/v1/admin/speaking/result-visibility', rolePlayCardId),
  );
}

/** Admin: upsert the global (rolePlayCardId null) or per-card config. */
export async function adminUpsertSpeakingResultVisibility(
  body: SpeakingResultVisibilityUpsert,
): Promise<SpeakingResultVisibilityDto> {
  return apiClient.put<SpeakingResultVisibilityDto>(
    '/v1/admin/speaking/result-visibility',
    body,
  );
}
