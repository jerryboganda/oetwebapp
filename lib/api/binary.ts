/**
 * Authorized binary/media fetch + upload helpers — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { getHeaders, resolveApiUrl, resolveApiUploadUrl, resolveBrowserApiResourceUrl } from './client';
import { fetchWithTimeout } from '../network/fetch-with-timeout';

export async function fetchAuthorizedObjectUrl(pathOrUrl: string): Promise<string> {
  const response = await fetchWithTimeout(resolveBrowserApiResourceUrl(pathOrUrl) ?? resolveApiUrl(pathOrUrl), {
    headers: await getHeaders(pathOrUrl, undefined, { json: false }),
  });

  if (!response.ok) {
    // Never the raw "Request failed: 502": this text can reach a learner (audio playback, proof viewers).
    let message = 'We could not load that file. Please try again.';
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error(`[API] fetchAuthorizedObjectUrl: HTTP ${response.status} with an unreadable error body:`, err);
    }
    throw new Error(message);
  }

  const blob = await response.blob();
  return URL.createObjectURL(blob);
}

/**
 * Authorized blob fetch that returns the raw Blob (caller owns
 * createObjectURL/revoke). Use when the content type matters — e.g. the admin
 * proof viewer branches between an inline image and an embedded PDF.
 */
export async function fetchAuthorizedBlob(pathOrUrl: string): Promise<Blob> {
  const response = await fetchWithTimeout(resolveBrowserApiResourceUrl(pathOrUrl) ?? resolveApiUrl(pathOrUrl), {
    headers: await getHeaders(pathOrUrl, undefined, { json: false }),
  });

  if (!response.ok) {
    let message = 'We could not load that file. Please try again.';
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error(`[API] fetchAuthorizedBlob: HTTP ${response.status} with an unreadable error body:`, err);
    }
    throw new Error(message);
  }

  return response.blob();
}

export async function uploadBinary(pathOrUrl: string, blob: Blob): Promise<void> {
  const response = await fetchWithTimeout(resolveApiUploadUrl(pathOrUrl), {
    method: 'PUT',
    headers: await getHeaders(pathOrUrl, { 'Content-Type': blob.type || 'audio/webm' }, { json: false }),
    body: blob,
  }, 90_000);

  if (!response.ok) {
    let message = `Upload failed: ${response.status}`;
    try {
      const error = await response.json();
      message = error.message ?? error.title ?? message;
    } catch (err) {
      console.error('[API] uploadBinary: failed to parse error response:', err);
    }
    throw new Error(message);
  }
}
