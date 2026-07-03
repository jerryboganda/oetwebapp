/**
 * Bunny Stream TUS upload wrapper (tus-js-client).
 *
 * Video files upload DIRECTLY from the admin browser to Bunny via TUS using a
 * presigned authorization minted by our backend
 * (`POST /v1/admin/video-library/videos/{id}/upload-authorization`). This
 * module owns the tus mechanics only — it never talks to our API itself; the
 * caller supplies the session and a `refreshSession` callback that re-fetches
 * an authorization for the SAME `bunnyVideoId` when the signature expires
 * mid-upload (Bunny answers 401/403).
 *
 * Reload-resume: `start()` first consults tus's URL storage
 * (`findPreviousUploads` → `resumeFromPreviousUpload`) so re-selecting the
 * same file after a page reload continues where the upload left off instead
 * of starting over.
 */

import * as tus from 'tus-js-client';
import type { VideoUploadAuthorization } from '@/lib/api/video-library';

/** Bunny's documented retry cadence for TUS uploads. */
export const BUNNY_TUS_RETRY_DELAYS = [0, 3000, 5000, 10000, 20000];

/** Max automatic re-authorization attempts on 401/403 before failing loudly. */
const MAX_REAUTH_ATTEMPTS = 3;

export interface BunnyUploadEvents {
  onProgress?: (bytesUploaded: number, bytesTotal: number) => void;
  onSuccess?: () => void;
  onError?: (error: Error) => void;
  /**
   * Re-fetch a fresh presigned authorization for the SAME bunnyVideoId.
   * Called when Bunny rejects the signature mid-upload (401/403); the upload
   * resumes with the new headers.
   */
  refreshSession?: () => Promise<VideoUploadAuthorization>;
}

export interface BunnyUploadHandle {
  /** Begin (or reload-resume) the upload. */
  start: () => Promise<void>;
  /** Pause — keeps the upload URL so `resume()` continues in place. */
  pause: () => Promise<void>;
  resume: () => void;
  /** Abort and terminate the remote upload. */
  abort: () => Promise<void>;
}

/** Headers Bunny's TUS endpoint requires on every request. */
export function buildBunnyTusHeaders(session: VideoUploadAuthorization): Record<string, string> {
  return {
    AuthorizationSignature: session.authorizationSignature,
    AuthorizationExpire: String(session.authorizationExpire),
    VideoId: session.bunnyVideoId,
    LibraryId: String(session.libraryId),
  };
}

function statusOf(error: unknown): number | null {
  const response = (error as { originalResponse?: { getStatus?: () => number } | null } | null)
    ?.originalResponse;
  if (response && typeof response.getStatus === 'function') return response.getStatus();
  return null;
}

export function createBunnyUpload(
  file: File,
  session: VideoUploadAuthorization,
  events: BunnyUploadEvents = {},
): BunnyUploadHandle {
  let reauthAttempts = 0;

  const upload = new tus.Upload(file, {
    endpoint: session.tusEndpoint,
    headers: buildBunnyTusHeaders(session),
    metadata: {
      filetype: file.type,
      title: file.name,
    },
    retryDelays: BUNNY_TUS_RETRY_DELAYS,
    // Chunk large files (50 MB = exact 256 KiB multiple, required by TUS). Each
    // acknowledged chunk advances the server offset, so an interrupted upload
    // resumes at the last completed chunk instead of restarting a giant PATCH.
    // Files ≤ 50 MB upload as a single chunk — identical to the prior behaviour.
    chunkSize: 50 * 1024 * 1024,
    // Clear the resume fingerprint once done so a finished video isn't mistaken
    // for a resumable partial upload if the same file is picked again.
    removeFingerprintOnSuccess: true,
    onProgress: (bytesUploaded, bytesTotal) => {
      events.onProgress?.(bytesUploaded, bytesTotal);
    },
    onSuccess: () => {
      events.onSuccess?.();
    },
    // Auth failures are not retryable with the same signature — hand them to
    // onError, which re-fetches a fresh authorization and resumes.
    onShouldRetry: (error) => {
      const status = statusOf(error);
      return status !== 401 && status !== 403;
    },
    onError: (error) => {
      const status = statusOf(error);
      const canReauth =
        (status === 401 || status === 403) &&
        typeof events.refreshSession === 'function' &&
        reauthAttempts < MAX_REAUTH_ATTEMPTS;

      if (!canReauth) {
        events.onError?.(error);
        return;
      }

      reauthAttempts += 1;
      events
        .refreshSession!()
        .then((fresh) => {
          // Same bunnyVideoId, fresh signature — swap headers and resume.
          upload.options.headers = buildBunnyTusHeaders(fresh);
          upload.start();
        })
        .catch((refreshError: unknown) => {
          events.onError?.(
            refreshError instanceof Error
              ? refreshError
              : new Error('Could not refresh the upload authorization.'),
          );
        });
    },
  });

  return {
    async start() {
      // Reload-resume: continue a previous upload of the same file if tus's
      // URL storage has one for this fingerprint (same file + endpoint).
      try {
        const previous = await upload.findPreviousUploads();
        if (previous.length > 0) {
          upload.resumeFromPreviousUpload(previous[0]);
        }
      } catch {
        // URL storage unavailable (e.g. blocked localStorage) — start fresh.
      }
      upload.start();
    },
    pause() {
      return upload.abort();
    },
    resume() {
      upload.start();
    },
    abort() {
      return upload.abort(true);
    },
  };
}
