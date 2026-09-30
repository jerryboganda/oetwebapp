import { apiClient } from '@/lib/api';

export type LiveVoiceProvider = 'openai' | 'gemini';

export interface LiveVoicePreflight {
  provider: LiveVoiceProvider;
  providerDisplayName: string;
  model: string;
  disclosure: string;
  retentionDays: number;
  sessionId: string;
  rolePlayCardId: string;
  /** Providers to try in order (server-decided: configured primary first, unhealthy ones filtered out). Absent on an older server: one attempt with `provider`. */
  candidates?: LiveVoiceProvider[];
  /** True when the caller forced a provider (`?provider=`): that run must not fail over. */
  pinned?: boolean;
}

export interface LiveVoiceOpenAiOfferResponse {
  provider: 'openai';
  model: string;
  providerSessionId: string;
  answerSdp: string;
  /** ISO time after which the server closes this role-play (deadline plus grace). Absent on an older server. */
  hardStopAt?: string;
}

export interface LiveVoiceGeminiTokenResponse {
  provider: 'gemini';
  model: string;
  providerSessionId: string;
  webSocketUrl: string;
  expiresAt: string;
  /** ISO time after which the server closes this role-play (deadline plus grace). Absent on an older server. */
  hardStopAt?: string;
}

export interface LiveVoiceTurnResponse {
  sessionId: string;
  sequenceNumber: number;
  duplicate: boolean;
  advisoryStatus: string | null;
}

export interface LiveVoiceTranscriptSegmentInput {
  speaker: 'candidate' | 'patient';
  startMs: number;
  endMs: number;
  text: string;
  confidence?: number | null;
}

export interface LiveVoiceTranscriptResponse {
  transcriptId: string;
  provider: string;
  wordCount: number;
  meanConfidence: number;
  generatedAt: string;
}

function sessionPath(sessionId: string): string {
  return `/v1/speaking/realtime/sessions/${encodeURIComponent(sessionId)}`;
}

export function getLiveVoicePreflight(
  sessionId: string,
  provider?: LiveVoiceProvider,
): Promise<LiveVoicePreflight> {
  const query = provider ? `?provider=${encodeURIComponent(provider)}` : '';
  return apiClient.get<LiveVoicePreflight>(`${sessionPath(sessionId)}/preflight${query}`);
}

// Creating a provider session is never retried by the API client: a repeat POST after a
// post-creation failure could open a second billed session, and the hook's failover to the
// other provider is the retry. The timeout is the server's provider timeout plus transport.
const CREATE_SESSION_OPTIONS = { maxRetries: 0, timeoutMs: 22_000 };

export function createOpenAiLiveOffer(
  sessionId: string,
  sdp: string,
): Promise<LiveVoiceOpenAiOfferResponse> {
  return apiClient.request<LiveVoiceOpenAiOfferResponse>(
    `${sessionPath(sessionId)}/openai/offer`,
    { method: 'POST', body: JSON.stringify({ sdp }) },
    CREATE_SESSION_OPTIONS,
  );
}

export function createGeminiLiveToken(
  sessionId: string,
): Promise<LiveVoiceGeminiTokenResponse> {
  return apiClient.request<LiveVoiceGeminiTokenResponse>(
    `${sessionPath(sessionId)}/gemini/token`,
    { method: 'POST', body: JSON.stringify({}) },
    CREATE_SESSION_OPTIONS,
  );
}

export function persistLiveVoiceTurn(
  sessionId: string,
  input: {
    provider: LiveVoiceProvider;
    providerSessionId: string;
    candidateText: string | null;
    patientText: string | null;
    clientTurnId: string;
    turnIndex: number;
    startedAt?: string;
    endedAt?: string;
  },
): Promise<LiveVoiceTurnResponse> {
  return apiClient.post<LiveVoiceTurnResponse>(`${sessionPath(sessionId)}/turns`, input);
}

export function persistLiveVoiceTranscript(
  sessionId: string,
  input: {
    provider: LiveVoiceProvider;
    providerSessionId: string;
    segments: LiveVoiceTranscriptSegmentInput[];
  },
): Promise<LiveVoiceTranscriptResponse> {
  return apiClient.post<LiveVoiceTranscriptResponse>(`${sessionPath(sessionId)}/transcript`, input);
}
