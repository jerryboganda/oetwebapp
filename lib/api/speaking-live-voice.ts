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
}

export interface LiveVoiceOpenAiOfferResponse {
  provider: 'openai';
  model: string;
  providerSessionId: string;
  answerSdp: string;
}

export interface LiveVoiceGeminiTokenResponse {
  provider: 'gemini';
  model: string;
  providerSessionId: string;
  webSocketUrl: string;
  expiresAt: string;
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

export function createOpenAiLiveOffer(
  sessionId: string,
  sdp: string,
): Promise<LiveVoiceOpenAiOfferResponse> {
  return apiClient.post<LiveVoiceOpenAiOfferResponse>(`${sessionPath(sessionId)}/openai/offer`, { sdp });
}

export function createGeminiLiveToken(
  sessionId: string,
): Promise<LiveVoiceGeminiTokenResponse> {
  return apiClient.post<LiveVoiceGeminiTokenResponse>(`${sessionPath(sessionId)}/gemini/token`, {});
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
