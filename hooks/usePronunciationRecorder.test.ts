import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest';
import { renderHook, act } from '@testing-library/react';
import { usePronunciationRecorder } from '@/hooks/usePronunciationRecorder';

type MediaRecorderHandlers = {
  ondataavailable: ((event: { data: Blob }) => void) | null;
  onstop: (() => void) | null;
  state: 'inactive' | 'recording' | 'paused';
  start: (slice: number) => void;
  stop: () => void;
};

class MockMediaRecorder implements MediaRecorderHandlers {
  static isTypeSupported = vi.fn((type: string) => type === 'audio/webm;codecs=opus' || type === 'audio/webm');

  ondataavailable: MediaRecorderHandlers['ondataavailable'] = null;
  onstop: MediaRecorderHandlers['onstop'] = null;
  state: MediaRecorderHandlers['state'] = 'inactive';
  private _mimeType: string;

  constructor(_stream: MediaStream, options?: { mimeType?: string }) {
    this._mimeType = options?.mimeType ?? 'audio/webm';

  }

  start() {
    this.state = 'recording';
  }

  stop() {
    this.state = 'inactive';
    // Fire a single chunk then stop
    this.ondataavailable?.({ data: new Blob(['chunk'], { type: this._mimeType }) });
    this.onstop?.();
  }
}



function createStreamMock(): MediaStream {
  return {
    getTracks: () => [{ stop: vi.fn() }],
  } as unknown as MediaStream;
}

describe('usePronunciationRecorder', () => {
  let originalMediaRecorder: typeof MediaRecorder | undefined;
  let originalAudioContext: typeof AudioContext | undefined;
  let originalCreateObjectURL: typeof URL.createObjectURL | undefined;
  let originalRevokeObjectURL: typeof URL.revokeObjectURL | undefined;
  let originalRaf: typeof requestAnimationFrame | undefined;
  let originalCancelRaf: typeof cancelAnimationFrame | undefined;

  beforeEach(() => {
    originalMediaRecorder = (globalThis as unknown as { MediaRecorder?: typeof MediaRecorder }).MediaRecorder;
    originalAudioContext = (globalThis as unknown as { AudioContext?: typeof AudioContext }).AudioContext;
    originalCreateObjectURL = URL.createObjectURL;
    originalRevokeObjectURL = URL.revokeObjectURL;
    originalRaf = globalThis.requestAnimationFrame;
    originalCancelRaf = globalThis.cancelAnimationFrame;

    (globalThis as unknown as { MediaRecorder: unknown }).MediaRecorder = MockMediaRecorder;
    (globalThis as unknown as { AudioContext: unknown }).AudioContext = class {
      createMediaStreamSource() {
        return { connect: vi.fn() };
      }
      createAnalyser() {
        return {
          fftSize: 512,
          getByteTimeDomainData(array: Uint8Array) {
            for (let i = 0; i < array.length; i++) array[i] = 128;
          },
          connect: vi.fn(),
        };
      }
      close() {
        return Promise.resolve();
      }
    };

    URL.createObjectURL = vi.fn(() => 'blob:mock-url');
    URL.revokeObjectURL = vi.fn();

    // Synchronous-ish raf so `start()` doesn't hang waiting for a frame.
    globalThis.requestAnimationFrame = ((cb: FrameRequestCallback) => {
      return setTimeout(() => cb(performance.now()), 16) as unknown as number;
    }) as typeof requestAnimationFrame;
    globalThis.cancelAnimationFrame = ((id: number) => clearTimeout(id)) as typeof cancelAnimationFrame;

    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: {
        getUserMedia: vi.fn(() => Promise.resolve(createStreamMock())),
      },
    });


  });

  afterEach(() => {
    if (originalMediaRecorder !== undefined) {
      (globalThis as unknown as { MediaRecorder: unknown }).MediaRecorder = originalMediaRecorder;
    }
    if (originalAudioContext !== undefined) {
      (globalThis as unknown as { AudioContext: unknown }).AudioContext = originalAudioContext;
    }
    if (originalCreateObjectURL !== undefined) URL.createObjectURL = originalCreateObjectURL;
    if (originalRevokeObjectURL !== undefined) URL.revokeObjectURL = originalRevokeObjectURL;
    if (originalRaf !== undefined) globalThis.requestAnimationFrame = originalRaf;
    if (originalCancelRaf !== undefined) globalThis.cancelAnimationFrame = originalCancelRaf;
    vi.restoreAllMocks();
  });

  it('starts in idle state', () => {
    const { result } = renderHook(() => usePronunciationRecorder());
    expect(result.current.status).toBe('idle');
    expect(result.current.permission).toBe('unknown');
    expect(result.current.result).toBeNull();
  });

  it('requests microphone permission and transitions to ready', async () => {
    const { result } = renderHook(() => usePronunciationRecorder());
    await act(async () => {
      await result.current.requestPermission();
    });
    expect(result.current.permission).toBe('granted');
    expect(result.current.status).toBe('ready');
    expect(result.current.errorMessage).toBeNull();
  });

  it('handles denied permission with a user-friendly message', async () => {
    (navigator.mediaDevices.getUserMedia as unknown as ReturnType<typeof vi.fn>).mockRejectedValueOnce(
      Object.assign(new Error('denied'), { name: 'NotAllowedError' }),
    );
    const { result } = renderHook(() => usePronunciationRecorder());
    await act(async () => {
      await result.current.requestPermission();
    });
    expect(result.current.permission).toBe('denied');
    expect(result.current.status).toBe('error');
    expect(result.current.errorMessage).toMatch(/microphone access/i);
  });

  it('produces a Blob result after start() then stop()', async () => {
    const { result } = renderHook(() => usePronunciationRecorder({ maxDurationMs: 2000 }));
    await act(async () => {
      const ok = await result.current.start();
      expect(ok).toBe(true);
    });
    expect(result.current.status).toBe('recording');
    await act(async () => {
      const stopped = await result.current.stop();
      expect(stopped).not.toBeNull();
      expect(stopped?.blob).toBeInstanceOf(Blob);
    });
    expect(result.current.status).toBe('stopped');
    expect(result.current.result?.mimeType).toContain('audio/');
  });

  it('reset() clears a stopped result and revokes the URL', async () => {
    const { result } = renderHook(() => usePronunciationRecorder());
    await act(async () => {
      await result.current.start();
      await result.current.stop();
    });
    expect(result.current.result).not.toBeNull();
    act(() => {
      result.current.reset();
    });
    expect(result.current.result).toBeNull();
    expect(URL.revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });

  it('gracefully fails when MediaRecorder is unavailable', async () => {
    (globalThis as unknown as { MediaRecorder: unknown }).MediaRecorder = undefined;
    Object.defineProperty(navigator, 'mediaDevices', {
      configurable: true,
      value: undefined,
    });

    const { result } = renderHook(() => usePronunciationRecorder());
    await act(async () => {
      const ok = await result.current.requestPermission();
      expect(ok).toBe(false);
    });
    expect(result.current.permission).toBe('denied');
    expect(result.current.status).toBe('error');
  });
});
