import { describe, expect, it } from 'vitest';
import {
  initialUploadCardState,
  uploadCardReducer,
  type UploadCardState,
} from '../upload-card-reducer';

const file = new File(['bytes'], 'lesson.mp4', { type: 'video/mp4' });

function run(state: UploadCardState, ...actions: Parameters<typeof uploadCardReducer>[1][]): UploadCardState {
  return actions.reduce((s, a) => uploadCardReducer(s, a), state);
}

describe('initialUploadCardState', () => {
  it('starts idle with no bunny video', () => {
    expect(
      initialUploadCardState({ bunnyVideoId: null, encodeStatus: 'not_uploaded', encodeProgress: null, encodeError: null }),
    ).toEqual({ kind: 'idle' });
  });

  it('hydrates terminal encode states', () => {
    expect(
      initialUploadCardState({ bunnyVideoId: 'b1', encodeStatus: 'ready', encodeProgress: 100, encodeError: null }),
    ).toEqual({ kind: 'ready' });
    expect(
      initialUploadCardState({ bunnyVideoId: 'b1', encodeStatus: 'failed', encodeProgress: null, encodeError: 'boom' }),
    ).toEqual({ kind: 'encode-failed', message: 'boom' });
  });

  it('hydrates mid-encode into polling', () => {
    expect(
      initialUploadCardState({ bunnyVideoId: 'b1', encodeStatus: 'encoding', encodeProgress: 42, encodeError: null }),
    ).toEqual({ kind: 'polling-encode', status: 'encoding', progress: 42 });
  });

  it('drops an interrupted server-side upload back to idle (file must be re-picked)', () => {
    expect(
      initialUploadCardState({ bunnyVideoId: 'b1', encodeStatus: 'uploading', encodeProgress: null, encodeError: null }),
    ).toEqual({ kind: 'idle' });
  });
});

describe('uploadCardReducer', () => {
  it('walks the happy path idle → ready', () => {
    let state = run({ kind: 'idle' }, { type: 'select-file', file });
    expect(state).toEqual({ kind: 'requesting-session', file });

    state = run(state, { type: 'session-granted' });
    expect(state).toEqual({ kind: 'uploading', file, progress: 0, paused: false });

    state = run(state, { type: 'progress', progress: 0.5 });
    expect(state).toMatchObject({ kind: 'uploading', progress: 0.5 });

    state = run(state, { type: 'upload-succeeded' });
    expect(state).toEqual({ kind: 'uploaded', file });

    state = run(state, { type: 'encode-status', status: 'processing', progress: 10 });
    expect(state).toEqual({ kind: 'polling-encode', status: 'processing', progress: 10 });

    state = run(state, { type: 'encode-status', status: 'ready', progress: 100 });
    expect(state).toEqual({ kind: 'ready' });
  });

  it('clamps progress into [0, 1]', () => {
    const uploading: UploadCardState = { kind: 'uploading', file, progress: 0, paused: false };
    expect(run(uploading, { type: 'progress', progress: 1.5 })).toMatchObject({ progress: 1 });
    expect(run(uploading, { type: 'progress', progress: -0.2 })).toMatchObject({ progress: 0 });
  });

  it('pauses and resumes only while uploading', () => {
    const uploading: UploadCardState = { kind: 'uploading', file, progress: 0.3, paused: false };
    const paused = run(uploading, { type: 'pause' });
    expect(paused).toMatchObject({ kind: 'uploading', paused: true, progress: 0.3 });
    expect(run(paused, { type: 'resume' })).toMatchObject({ paused: false });
    // Pause is a no-op outside uploading.
    expect(run({ kind: 'ready' }, { type: 'pause' })).toEqual({ kind: 'ready' });
  });

  it('keeps the file through session failure and retry', () => {
    let state = run({ kind: 'idle' }, { type: 'select-file', file }, { type: 'session-failed', message: 'nope' });
    expect(state).toEqual({ kind: 'session-error', file, message: 'nope' });

    state = run(state, { type: 'retry' });
    expect(state).toEqual({ kind: 'requesting-session', file });
  });

  it('keeps the file through upload failure and retry', () => {
    let state = run(
      { kind: 'idle' },
      { type: 'select-file', file },
      { type: 'session-granted' },
      { type: 'progress', progress: 0.7 },
      { type: 'upload-failed', message: 'network died' },
    );
    expect(state).toEqual({ kind: 'upload-error', file, message: 'network died' });

    state = run(state, { type: 'retry' });
    expect(state).toEqual({ kind: 'requesting-session', file });
  });

  it('maps encode failure and supports the replace flow via reset', () => {
    let state: UploadCardState = { kind: 'uploaded', file };
    state = run(state, { type: 'encode-status', status: 'failed', progress: null, error: 'codec unsupported' });
    expect(state).toEqual({ kind: 'encode-failed', message: 'codec unsupported' });

    // Replace-video: reset-upload succeeded → back to idle for a fresh pick.
    state = run(state, { type: 'reset' });
    expect(state).toEqual({ kind: 'idle' });
  });

  it('allows a re-poll to flip a terminal state (failed → ready)', () => {
    const state = run({ kind: 'encode-failed', message: 'x' }, { type: 'encode-status', status: 'ready', progress: 100 });
    expect(state).toEqual({ kind: 'ready' });
  });

  it('ignores out-of-order actions', () => {
    expect(run({ kind: 'idle' }, { type: 'session-granted' })).toEqual({ kind: 'idle' });
    expect(run({ kind: 'idle' }, { type: 'progress', progress: 0.4 })).toEqual({ kind: 'idle' });
    expect(run({ kind: 'idle' }, { type: 'encode-status', status: 'ready', progress: null })).toEqual({ kind: 'idle' });
    expect(run({ kind: 'ready' }, { type: 'retry' })).toEqual({ kind: 'ready' });
  });
});
