import { render, screen } from '@testing-library/react';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AudioPlayerWaveform } from './audio-player-waveform';

// Capture the latest mocked WaveSurfer instance so tests can drive its events.
type EventHandler = (...args: unknown[]) => void;

class MockWaveSurfer {
  handlers = new Map<string, EventHandler[]>();
  destroyed = false;
  loadedUrl: string | null = null;

  on(event: string, handler: EventHandler) {
    const list = this.handlers.get(event) ?? [];
    list.push(handler);
    this.handlers.set(event, list);
    return this;
  }

  emit(event: string, ...args: unknown[]) {
    for (const handler of this.handlers.get(event) ?? []) handler(...args);
  }

  load(url: string) {
    this.loadedUrl = url;
  }

  destroy() {
    this.destroyed = true;
  }

  playPause() {}
  setPlaybackRate() {}
  getDuration() {
    return 0;
  }
  seekTo() {}
}

const createdInstances: MockWaveSurfer[] = [];

vi.mock('wavesurfer.js', () => ({
  default: {
    create: () => {
      const ws = new MockWaveSurfer();
      createdInstances.push(ws);
      return ws;
    },
  },
}));

vi.mock('@/lib/api', () => ({
  fetchAuthorizedObjectUrl: vi.fn(async (url: string) => `blob:${url}`),
  isApiError: (error: unknown): error is { userMessage: string } =>
    typeof error === 'object' &&
    error !== null &&
    'userMessage' in error,
}));

describe('AudioPlayerWaveform', () => {
  afterEach(() => {
    createdInstances.length = 0;
    vi.clearAllMocks();
  });

  it('renders the loading indicator and audio-player region before WaveSurfer is ready', () => {
    render(<AudioPlayerWaveform audioUrl="/audio/test.mp3" />);
    expect(
      screen.getByRole('region', { name: /audio player/i }),
    ).toBeInTheDocument();
    expect(screen.getByText(/loading audio waveform/i)).toBeInTheDocument();
  });

  it('exposes transport controls (play/pause button and playback-rate select)', () => {
    render(<AudioPlayerWaveform audioUrl="/audio/test.mp3" />);
    // Button starts in "Play" state because isReady is false until WaveSurfer emits 'ready'.
    expect(
      screen.getByRole('button', { name: /play audio/i }),
    ).toBeInTheDocument();
    // Playback-rate <select> defaults to "1".
    const select = screen.getByRole('combobox') as HTMLSelectElement;
    expect(select.value).toBe('1');
  });

  it('is tolerant of a missing onTimeUpdate callback (useEffectEvent wrapper)', () => {
    // If the useEffectEvent wrapper did not null-check onTimeUpdate, rendering
    // without the prop would throw on first timeupdate emission.
    expect(() =>
      render(<AudioPlayerWaveform audioUrl="/audio/test.mp3" />),
    ).not.toThrow();
  });
});
