/**
 * Tutor Book API (learner + admin) — extracted from `lib/api.ts`.
 * Re-exported there, so `@/lib/api` imports keep working.
 */
import { API_BASE_URL, apiRequest } from './client';

export interface TutorBookAudioScript {
  chapter: string;
  title: string;
  audioUrl: string;
  transcriptUrl?: string | null;
}

export interface TutorBookUpdate {
  id: string;
  title: string;
  bodyMarkdown: string;
  publishedAt: string;
  audience: string;
}

export interface TutorBookWhatsAppResponse {
  number: string;
  url: string;
}

export async function fetchTutorBookAudioScripts(): Promise<TutorBookAudioScript[]> {
  return apiRequest('/v1/tutor-book/audio-scripts');
}

export async function fetchTutorBookUpdates(): Promise<TutorBookUpdate[]> {
  return apiRequest('/v1/tutor-book/updates');
}

export async function fetchTutorBookWhatsApp(): Promise<TutorBookWhatsAppResponse> {
  return apiRequest('/v1/tutor-book/whatsapp');
}

/** Returns the URL for the watermarked PDF download (same-origin /api/backend proxy). */
export function tutorBookDownloadUrl(): string {
  // FE-003: reuse the module's resolved API base instead of re-reading env with a
  // wrong-port (5199) localhost fallback. In the browser this is the same-origin
  // `/api/backend` proxy path, so the download is cookie-authenticated and works
  // without depending on NEXT_PUBLIC_API_BASE_URL being set.
  return `${API_BASE_URL.replace(/\/$/, '')}/v1/tutor-book/download`;
}

export interface AdminTutorBookUpdate {
  id: string;
  title: string;
  bodyMarkdown: string;
  publishedAt: string;
  audience: string;
  isPublished: boolean;
}

export interface AdminTutorBookAudioScript {
  id: string;
  chapter: string;
  title: string;
  audioUrl: string;
  transcriptUrl?: string | null;
  displayOrder: number;
  isPublished: boolean;
}

export async function adminListTutorBookUpdates(): Promise<AdminTutorBookUpdate[]> {
  return apiRequest('/v1/admin/tutor-book/updates');
}

export async function adminUpsertTutorBookUpdate(payload: {
  id?: string;
  title: string;
  bodyMarkdown: string;
  audience?: string;
  isPublished?: boolean;
  publishedAt?: string | null;
}): Promise<AdminTutorBookUpdate> {
  return apiRequest('/v1/admin/tutor-book/updates', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminDeleteTutorBookUpdate(id: string): Promise<void> {
  await apiRequest(`/v1/admin/tutor-book/updates/${encodeURIComponent(id)}`, { method: 'DELETE' });
}

export async function adminListTutorBookAudioScripts(): Promise<AdminTutorBookAudioScript[]> {
  return apiRequest('/v1/admin/tutor-book/audio-scripts');
}

export async function adminUpsertTutorBookAudioScript(payload: {
  id?: string;
  chapter: string;
  title: string;
  audioUrl: string;
  transcriptUrl?: string | null;
  displayOrder: number;
  isPublished?: boolean;
}): Promise<AdminTutorBookAudioScript> {
  return apiRequest('/v1/admin/tutor-book/audio-scripts', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminDeleteTutorBookAudioScript(id: string): Promise<void> {
  await apiRequest(`/v1/admin/tutor-book/audio-scripts/${encodeURIComponent(id)}`, { method: 'DELETE' });
}
