/**
 * Pure state machine for `BunnyVideoUploadCard`.
 *
 * idle → requesting-session → uploading{progress,paused} → uploaded
 *      → polling-encode{status,progress} → ready | encode-failed
 * with `session-error` / `upload-error` side states whose Retry keeps the
 * selected File so the operator never has to re-pick it.
 *
 * Kept free of React/DOM so it is unit-testable without jsdom rendering.
 */

import type { AdminVideoDetail, VideoEncodeStatus } from '@/lib/api/video-library';

export type UploadCardState =
  | { kind: 'idle' }
  | { kind: 'requesting-session'; file: File }
  | { kind: 'uploading'; file: File; progress: number; paused: boolean }
  | { kind: 'uploaded'; file: File | null }
  | { kind: 'polling-encode'; status: VideoEncodeStatus; progress: number | null }
  | { kind: 'ready' }
  | { kind: 'encode-failed'; message: string | null }
  | { kind: 'session-error'; file: File; message: string }
  | { kind: 'upload-error'; file: File; message: string };

export type UploadCardAction =
  | { type: 'select-file'; file: File }
  | { type: 'session-granted' }
  | { type: 'session-failed'; message: string }
  | { type: 'progress'; progress: number }
  | { type: 'pause' }
  | { type: 'resume' }
  | { type: 'upload-succeeded' }
  | { type: 'upload-failed'; message: string }
  | { type: 'retry' }
  | { type: 'encode-status'; status: VideoEncodeStatus; progress: number | null; error?: string | null }
  | { type: 'reset' };

/** Derive the initial card state from the server snapshot of the video. */
export function initialUploadCardState(
  video: Pick<AdminVideoDetail, 'bunnyVideoId' | 'encodeStatus' | 'encodeProgress' | 'encodeError'>,
): UploadCardState {
  if (!video.bunnyVideoId || video.encodeStatus === 'not_uploaded') return { kind: 'idle' };
  if (video.encodeStatus === 'ready') return { kind: 'ready' };
  if (video.encodeStatus === 'failed') return { kind: 'encode-failed', message: video.encodeError ?? null };
  if (video.encodeStatus === 'uploading') {
    // A previous browser session started (but never finished) the TUS upload.
    // We cannot resume without the File, so ask the operator to re-select it —
    // tus's URL storage then resumes from where the previous session stopped.
    return { kind: 'idle' };
  }
  return { kind: 'polling-encode', status: video.encodeStatus, progress: video.encodeProgress };
}

export function uploadCardReducer(state: UploadCardState, action: UploadCardAction): UploadCardState {
  switch (action.type) {
    case 'select-file':
      return { kind: 'requesting-session', file: action.file };

    case 'session-granted':
      if (state.kind !== 'requesting-session') return state;
      return { kind: 'uploading', file: state.file, progress: 0, paused: false };

    case 'session-failed':
      if (state.kind !== 'requesting-session') return state;
      return { kind: 'session-error', file: state.file, message: action.message };

    case 'progress':
      if (state.kind !== 'uploading') return state;
      return { ...state, progress: Math.min(1, Math.max(0, action.progress)) };

    case 'pause':
      if (state.kind !== 'uploading' || state.paused) return state;
      return { ...state, paused: true };

    case 'resume':
      if (state.kind !== 'uploading' || !state.paused) return state;
      return { ...state, paused: false };

    case 'upload-succeeded':
      if (state.kind !== 'uploading') return state;
      return { kind: 'uploaded', file: state.file };

    case 'upload-failed':
      if (state.kind !== 'uploading' && state.kind !== 'requesting-session') return state;
      return { kind: 'upload-error', file: state.file, message: action.message };

    case 'retry':
      // Retry keeps the File: re-request a session and resume/restart.
      if (state.kind === 'session-error' || state.kind === 'upload-error') {
        return { kind: 'requesting-session', file: state.file };
      }
      return state;

    case 'encode-status':
      // Valid once the bytes are up (uploaded/polling), and also as a refresh
      // while already terminal (a re-poll can flip failed → ready after a
      // Bunny-side retry).
      if (
        state.kind !== 'uploaded' &&
        state.kind !== 'polling-encode' &&
        state.kind !== 'ready' &&
        state.kind !== 'encode-failed'
      ) {
        return state;
      }
      if (action.status === 'ready') return { kind: 'ready' };
      if (action.status === 'failed') return { kind: 'encode-failed', message: action.error ?? null };
      return { kind: 'polling-encode', status: action.status, progress: action.progress };

    case 'reset':
      return { kind: 'idle' };

    default:
      return state;
  }
}
