/**
 * Typed API client for the learner Video Library.
 *
 * Backed by `VideoLibraryEndpoints.cs` (`/v1/video-library/*`). Wraps the
 * shared `apiClient` from `lib/api.ts` so retry, CSRF, and Authorization
 * headers stay consistent. Kept out of `lib/api.ts` per the feature-scoped
 * client convention (see `lib/api/speaking-sessions.ts`).
 *
 * SECURITY NOTE: `createPlaybackSession` only succeeds for attested native
 * clients (Tauri desktop / Capacitor mobile). Browsers always receive 403 —
 * that server-side rejection, not any client check, is the enforcement
 * boundary for the app-only playback rule.
 */
import { apiClient } from '@/lib/api';
import type {
  PlaybackChallenge,
  PlaybackSession,
  VideoDetail,
  VideoLibraryHome,
  VideoPlaybackEventType,
  VideoProgressResponse,
} from '@/lib/types/videos';

const BASE = '/v1/video-library';

export async function fetchVideoLibraryHome(): Promise<VideoLibraryHome> {
  return apiClient.get<VideoLibraryHome>(BASE);
}

export async function fetchVideo(videoId: string): Promise<VideoDetail> {
  return apiClient.get<VideoDetail>(`${BASE}/videos/${encodeURIComponent(videoId)}`);
}

export async function fetchPlaybackChallenge(): Promise<PlaybackChallenge> {
  return apiClient.post<PlaybackChallenge>(`${BASE}/attestation/challenge`, {});
}

export interface PlaybackAttestationInput {
  nonce: string;
  platform: string;
  keyId: string;
  signature: string;
}

export async function createPlaybackSession(
  videoId: string,
  attestation: PlaybackAttestationInput,
): Promise<PlaybackSession> {
  return apiClient.post<PlaybackSession>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/playback-session`,
    attestation,
  );
}

export async function renewPlaybackSession(sessionId: string): Promise<PlaybackSession> {
  return apiClient.post<PlaybackSession>(
    `${BASE}/playback-sessions/${encodeURIComponent(sessionId)}/renew`,
    {},
  );
}

export async function postVideoProgress(videoId: string, positionSeconds: number): Promise<VideoProgressResponse> {
  return apiClient.post<VideoProgressResponse>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/progress`,
    { positionSeconds: Math.max(0, Math.floor(positionSeconds)) },
  );
}

export async function toggleVideoBookmark(videoId: string): Promise<{ bookmarked: boolean }> {
  return apiClient.post<{ bookmarked: boolean }>(
    `${BASE}/videos/${encodeURIComponent(videoId)}/bookmark`,
    {},
  );
}

/** Fire-and-forget playback analytics event. Never throws. */
export async function postVideoEvent(input: {
  videoId: string;
  sessionId?: string | null;
  eventType: VideoPlaybackEventType;
  positionSeconds: number;
}): Promise<void> {
  try {
    await apiClient.post(`${BASE}/events`, {
      videoId: input.videoId,
      sessionId: input.sessionId ?? null,
      eventType: input.eventType,
      positionSeconds: Math.max(0, Math.floor(input.positionSeconds)),
    });
  } catch {
    // Analytics must never interrupt playback.
  }
}
