/**
 * Typed API client for the Phase 3 live-tutor room surface.
 *
 * Backed by `SpeakingLiveRoomEndpoints.cs` (created in parallel by the
 * P3 agent — see plan section C.3). The endpoints surfaced here are:
 *
 *   POST   /v1/speaking/live-rooms                          { speakingSessionId }
 *   POST   /v1/speaking/live-rooms/{id}/tokens              { role }
 *   POST   /v1/speaking/live-rooms/{id}/start-recording
 *   POST   /v1/speaking/live-rooms/{id}/stop-recording
 *   POST   /v1/speaking/live-rooms/{id}/end
 *   GET    /v1/speaking/live-rooms/{id}
 *
 * LiveKit credentials are minted server-side; the frontend never sees
 * the LiveKit API key. The returned `token` is a short-lived JWT
 * scoped to a single room + identity.
 */
import { apiClient } from '@/lib/api';

export type LiveRoomRole = 'learner' | 'tutor' | 'observer';

export type LiveRoomState =
  | 'Scheduled'
  | 'Provisioning'
  | 'Active'
  | 'Ended'
  | 'Failed';

export interface LiveRoomDetail {
  liveRoomId: string;
  speakingSessionId: string;
  roomName: string;
  /**
   * Browser-side WebSocket URL for the LiveKit room. Already includes
   * the protocol (wss://) and host. Pass to `LiveKitRoom` as `serverUrl`.
   */
  livekitWssUrl: string;
  state: LiveRoomState | string;
  recordingEgressId: string | null;
  createdAt: string;
  endedAt: string | null;
  card: {
    cardId: string;
    scenarioTitle?: string | null;
    setting?: string | null;
    candidateRole?: string | null;
    rolePlayTimeSeconds: number;
  };
}

export interface LiveRoomTokenCapabilities {
  /** Whether this participant can publish audio. */
  canPublishAudio: boolean;
  /** Whether this participant can publish video. */
  canPublishVideo: boolean;
  /** Whether this participant can subscribe to other participants. */
  canSubscribe: boolean;
}

export interface LiveRoomTokenResponse {
  /** Short-lived LiveKit JWT (typically 1h). */
  token: string;
  /** ISO 8601 expiry. */
  expiresAt: string;
  capabilities: LiveRoomTokenCapabilities;
}

export interface LiveRoomRecordingResponse {
  egressId: string;
  outputUrl: string;
}

export interface LiveRoomStopRecordingResponse {
  stopped: boolean;
}

export interface CreateLiveRoomInput {
  speakingSessionId: string;
}

export interface CreateLiveRoomResponse {
  liveRoomId: string;
  livekitWssUrl: string;
  roomName: string;
}

export interface IssueLiveRoomTokenInput {
  role: LiveRoomRole;
}

// ─────────────────────────────────────────────────────────────────────────────
// API functions
// ─────────────────────────────────────────────────────────────────────────────

export async function createLiveRoom(input: CreateLiveRoomInput): Promise<CreateLiveRoomResponse> {
  return apiClient.post<CreateLiveRoomResponse>('/v1/speaking/live-rooms', input);
}

export async function getLiveRoom(liveRoomId: string): Promise<LiveRoomDetail> {
  return apiClient.get<LiveRoomDetail>(
    `/v1/speaking/live-rooms/${encodeURIComponent(liveRoomId)}`,
  );
}

export async function issueLiveRoomToken(
  liveRoomId: string,
  role: LiveRoomRole,
): Promise<LiveRoomTokenResponse> {
  return apiClient.post<LiveRoomTokenResponse>(
    `/v1/speaking/live-rooms/${encodeURIComponent(liveRoomId)}/tokens`,
    { role } satisfies IssueLiveRoomTokenInput,
  );
}

export async function startRecording(liveRoomId: string): Promise<LiveRoomRecordingResponse> {
  return apiClient.post<LiveRoomRecordingResponse>(
    `/v1/speaking/live-rooms/${encodeURIComponent(liveRoomId)}/start-recording`,
    {},
  );
}

export async function stopRecording(liveRoomId: string): Promise<LiveRoomStopRecordingResponse> {
  return apiClient.post<LiveRoomStopRecordingResponse>(
    `/v1/speaking/live-rooms/${encodeURIComponent(liveRoomId)}/stop-recording`,
    {},
  );
}

export async function endLiveRoom(liveRoomId: string): Promise<void> {
  return apiClient.post<void>(
    `/v1/speaking/live-rooms/${encodeURIComponent(liveRoomId)}/end`,
    {},
  );
}
