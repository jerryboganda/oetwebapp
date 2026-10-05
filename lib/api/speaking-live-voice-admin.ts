/**
 * Live AI-patient voices — typed admin client for the owner's listening check (owner decision 2026-10-05).
 *
 * Backed by `AiOperationsAdminEndpoints.cs` under `/v1/admin/ai/live-voice/voices`. OpenAI is the primary provider with the
 * quartz / willow / ripple / vesper pool; Gemini voices stay configured as the fallback and are only listed here.
 */
import { apiClient } from '@/lib/api';

export interface LiveVoicePreviewCell {
  /** female-younger | female-older | male-younger | male-older */
  key: string;
  gender: string;
  ageBand: string;
  openAiVoice: string;
  geminiVoice: string;
  /** The accent GPT-Live documents for the default voice; empty for an overridden voice. Not verified by ear. */
  accentNote: string;
}

export interface LiveVoicePreviewList {
  primaryProvider: string;
  /** Providers in the order a new attempt tries them (primary first, only those configured and healthy). */
  candidateOrder: string[];
  cells: LiveVoicePreviewCell[];
  sampleText: string;
  maxSeconds: number;
}

export interface LiveVoicePreviewOfferResponse {
  cell: string;
  voice: string;
  model: string;
  providerSessionId: string;
  answerSdp: string;
  sampleText: string;
  maxSeconds: number;
}

const BASE = '/v1/admin/ai/live-voice/voices';

export function adminGetLiveVoicePreviewList(): Promise<LiveVoicePreviewList> {
  return apiClient.get<LiveVoicePreviewList>(BASE);
}

export function adminCreateLiveVoicePreviewOffer(cell: string, sdp: string): Promise<LiveVoicePreviewOfferResponse> {
  return apiClient.post<LiveVoicePreviewOfferResponse>(`${BASE}/preview-offer`, { cell, sdp });
}
