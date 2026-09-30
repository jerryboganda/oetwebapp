import { act, renderHook } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { useSpeakingSessionRecorder } from '../useSpeakingSessionRecorder';

vi.mock('@/lib/api/speaking-sessions', () => ({ uploadSpeakingSessionRecording: vi.fn() }));

class FakeMediaRecorder {
  static isTypeSupported = () => false;
  state: 'inactive' | 'recording' = 'inactive';
  ondataavailable: ((event: { data: Blob }) => void) | null = null;
  onstop: (() => void) | null = null;
  constructor(public stream: MediaStream) {}
  start() {
    this.state = 'recording';
  }
  stop() {
    this.state = 'inactive';
  }
}

function fakeStream() {
  const track = { stop: vi.fn() };
  return { stream: { getTracks: () => [track] } as unknown as MediaStream, track };
}

describe('useSpeakingSessionRecorder microphone ownership', () => {
  const original = { mediaDevices: navigator.mediaDevices, recorder: globalThis.MediaRecorder };

  beforeEach(() => {
    vi.stubGlobal('MediaRecorder', FakeMediaRecorder);
  });

  afterEach(() => {
    Object.defineProperty(navigator, 'mediaDevices', { value: original.mediaDevices, configurable: true });
    vi.unstubAllGlobals();
  });

  it('releases a microphone granted after the panel has already unmounted', async () => {
    const { stream, track } = fakeStream();
    let grant!: (value: MediaStream) => void;
    Object.defineProperty(navigator, 'mediaDevices', {
      value: { getUserMedia: () => new Promise<MediaStream>((resolve) => { grant = resolve; }) },
      configurable: true,
    });

    const { result, unmount } = renderHook(() => useSpeakingSessionRecorder('session-1'));
    let started!: Promise<boolean>;
    act(() => {
      started = result.current.start();
    });

    // The learner leaves (Back, or the exam advances) while the permission prompt is still open.
    unmount();
    await act(async () => {
      grant(stream);
      await started;
    });

    expect(track.stop).toHaveBeenCalledTimes(1);
    await expect(started).resolves.toBe(false);
  });

  it('keeps a microphone granted while the panel is still mounted', async () => {
    const { stream, track } = fakeStream();
    Object.defineProperty(navigator, 'mediaDevices', {
      value: { getUserMedia: () => Promise.resolve(stream) },
      configurable: true,
    });

    const { result, unmount } = renderHook(() => useSpeakingSessionRecorder('session-1'));
    await act(async () => {
      await result.current.start();
    });

    expect(result.current.status).toBe('recording');
    expect(track.stop).not.toHaveBeenCalled();
    unmount();
    expect(track.stop).toHaveBeenCalledTimes(1);
  });
});
