/**
 * Voice design studio + audio regeneration batches — extracted from
 * `lib/api.ts`. Re-exported there, so `@/lib/api` imports keep working.
 *
 * NOTE: `uploadElevenLabsPronunciationDictionary` stays in `lib/api.ts` —
 * it uses `apiClient.postForm`, and importing the client here would cycle.
 */
import { apiBlobRequest, apiRequest } from './client';

/** Preview an ElevenLabs voice (returns an MP3 Blob). */
export async function previewAdminVoiceDesign(body: {
  voiceId?: string;
  text: string;
  locale?: string;
}): Promise<Blob> {
  return apiBlobRequest('/v1/admin/voice-design/preview', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Bulk regenerate audio across the platform with specified voice config */
export interface AdminAudioRegenerateRequest {
  audioType: 'all' | 'listening' | 'vocabulary' | 'conversation' | 'recalls';
  scope: 'all' | 'missing' | 'different-voice';
  modelVariant?: 'flash' | 'voicedesign' | string;
  voiceId?: string;
  instructions?: string;
  speed?: number;
  pitch?: number;
  emotion?: string;
  providerName?: string;
  forceRegenerate?: boolean;
  dryRun?: boolean;
}

export interface AdminAudioRegenerateBatchResult {
  batchId: string;
  audioType: string;
  scope: string;
  totalItems: number;
  dryRun: boolean;
  modelVariant: string;
  voiceId?: string;
  providerName?: string | null;
}

export async function regenerateAllAudio(
  body: AdminAudioRegenerateRequest
): Promise<AdminAudioRegenerateBatchResult> {
  return apiRequest<AdminAudioRegenerateBatchResult>('/v1/admin/voice-design/regenerate', {
    method: 'POST',
    body: JSON.stringify(body),
  });
}

/** Get active/completed audio regeneration batches */
export interface AdminAudioBatch {
  batchId: string;
  audioType: 'all' | 'listening' | 'vocabulary' | 'conversation' | 'recalls';
  scope: string;
  status: 'running' | 'completed' | 'failed' | 'cancelled';
  totalItems: number;
  completedItems: number;
  failedItems: number;
  voiceId: string;
  modelVariant: string;
  providerName: string;
  speed: number;
  pitch: number;
  emotion: string;
  startedAt: string;
  completedAt: string | null;
  requestedBy: string;
}

export async function getAudioRegenerationBatches(): Promise<{ batches: AdminAudioBatch[] }> {
  return apiRequest<{ batches: AdminAudioBatch[] }>('/v1/admin/voice-design/batches');
}

/** Get progress details for a specific batch */
export async function getAudioRegenerationBatchProgress(batchId: string): Promise<AdminAudioBatch> {
  return apiRequest<AdminAudioBatch>(`/v1/admin/voice-design/batches/${encodeURIComponent(batchId)}`);
}

/** Cancel an in-progress batch */
export async function cancelAudioRegenerationBatch(batchId: string): Promise<{ cancelled: boolean }> {
  return apiRequest<{ cancelled: boolean }>(`/v1/admin/voice-design/batches/${encodeURIComponent(batchId)}/cancel`, {
    method: 'POST',
  });
}

/** Retry failed or incomplete recall audio jobs in a batch */
export async function retryAudioRegenerationBatch(batchId: string): Promise<AdminAudioBatch> {
  return apiRequest<AdminAudioBatch>(`/v1/admin/voice-design/batches/${encodeURIComponent(batchId)}/retry`, {
    method: 'POST',
  });
}

/** Get the current globally configured voice settings */
export interface AdminVoiceDesignConfig {
  elevenLabsTtsBaseUrl: string;
  elevenLabsDefaultVoiceId: string;
  elevenLabsModel: string;
  elevenLabsOutputFormat: string;
  elevenLabsPronunciationDictionaryId: string | null;
  elevenLabsPronunciationDictionaryVersionId: string | null;
  elevenLabsStability: number;
  elevenLabsSimilarityBoost: number;
  elevenLabsStyle: number;
  elevenLabsUseSpeakerBoost: boolean;
  elevenLabsApiKeyPresent: boolean;
  lastUpdatedAt: string | null;
  lastUpdatedBy: string | null;
}

export async function getAdminVoiceDesignConfig(): Promise<AdminVoiceDesignConfig> {
  return apiRequest<AdminVoiceDesignConfig>('/v1/admin/voice-design/config');
}

/** Save voice design configuration globally */
export async function saveAdminVoiceDesignConfig(body: {
  elevenLabsApiKey?: string;
  elevenLabsTtsBaseUrl?: string;
  elevenLabsDefaultVoiceId?: string;
  elevenLabsModel?: string;
  elevenLabsOutputFormat?: string;
  elevenLabsPronunciationDictionaryId?: string;
  elevenLabsPronunciationDictionaryVersionId?: string;
  elevenLabsStability?: number;
  elevenLabsSimilarityBoost?: number;
  elevenLabsStyle?: number;
  elevenLabsUseSpeakerBoost?: boolean;
}): Promise<{ saved: boolean }> {
  return apiRequest<{ saved: boolean }>('/v1/admin/voice-design/config', {
    method: 'PUT',
    body: JSON.stringify(body),
  });
}

export async function startAdminRecallsAudioBackfill(
  body: Omit<AdminAudioRegenerateRequest, 'audioType'>,
): Promise<AdminAudioRegenerateBatchResult> {
  return apiRequest<AdminAudioRegenerateBatchResult>('/v1/admin/recalls/audio/backfill', {
    method: 'POST',
    body: JSON.stringify({ ...body, audioType: 'recalls', providerName: body.providerName ?? 'elevenlabs' }),
  });
}

export async function getAdminRecallsAudioBatchProgress(batchId: string): Promise<AdminAudioBatch> {
  return apiRequest<AdminAudioBatch>(`/v1/admin/recalls/audio/batches/${encodeURIComponent(batchId)}`);
}
