import { apiClient } from '../api';

export async function uploadElevenLabsPronunciationDictionary(
  file: File,
  name?: string,
): Promise<{ dictionaryId: string; versionId: string | null }> {
  const form = new FormData();
  form.append('file', file);
  if (name?.trim()) form.append('name', name.trim());

  return apiClient.postForm<{ dictionaryId: string; versionId: string | null }>(
    '/v1/admin/voice-design/elevenlabs/dictionary',
    form,
  );
}
