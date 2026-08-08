import { cleanup, fireEvent, render, screen, waitFor } from '@testing-library/react';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';
import { VideoPlayer } from './video-player';

const mocks = vi.hoisted(() => ({
  requestPlaybackSession: vi.fn(),
  renewPlaybackSession: vi.fn(),
  postVideoEvent: vi.fn(),
  postVideoProgress: vi.fn(),
  createHlsEngine: vi.fn(),
  reportProtectionEvent: vi.fn(),
  setVideoScreenProtection: vi.fn(),
  addCaptureStateListener: vi.fn(),
  addScreenshotListener: vi.fn(),
}));

vi.mock('@/lib/runtime-signals', () => ({ getAppRuntimeKind: () => 'desktop' }));
vi.mock('@/lib/video/attestation', () => ({
  PlaybackGateError: class PlaybackGateError extends Error {
    code: string;
    constructor(code: string) {
      super(code);
      this.code = code;
    }
  },
  requestPlaybackSession: mocks.requestPlaybackSession,
}));
vi.mock('@/lib/api/videos', () => ({
  postVideoEvent: mocks.postVideoEvent,
  postVideoProgress: mocks.postVideoProgress,
  renewPlaybackSession: mocks.renewPlaybackSession,
}));
vi.mock('@/lib/video/hls-engine', () => ({ createHlsEngine: mocks.createHlsEngine }));
vi.mock('@/lib/api/video-protection', () => ({ reportProtectionEvent: mocks.reportProtectionEvent }));
vi.mock('@/lib/video/screen-protection', () => ({ setVideoScreenProtection: mocks.setVideoScreenProtection }));
vi.mock('@/lib/mobile/playback-attestation', () => ({
  addCaptureStateListener: mocks.addCaptureStateListener,
  addScreenshotListener: mocks.addScreenshotListener,
}));
vi.mock('@/components/videos/watermark-overlay', () => ({ WatermarkOverlay: () => null }));

const secureSession = {
  sessionId: 'session-1',
  playbackUrl: 'https://iframe.mediadelivery.net/embed/123/video-guid?token=signed',
  deliveryMode: 'secure_embed' as const,
  expiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
  sessionExpiresAt: new Date(Date.now() + 10 * 60 * 1000).toISOString(),
  watermarkText: 'Learner Â· session',
  watermark: null,
  captions: [],
};

const directSession = {
  ...secureSession,
  playbackUrl: 'https://cdn.example/video.m3u8',
  deliveryMode: 'direct_hls' as const,
};

const hlsHandle = {
  levels: [],
  onLevelsUpdated: vi.fn(),
  onFatalNetworkError: vi.fn(),
  setQuality: vi.fn(),
  recoverWithUrl: vi.fn(),
  destroy: vi.fn(),
};

function renderPlayer() {
  return render(
    <VideoPlayer
      videoId="video-1"
      userId="user-1"
      durationSeconds={600}
      initialProgress={null}
      chapters={[]}
    />,
  );
}

describe('VideoPlayer presentation controls', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mocks.setVideoScreenProtection.mockResolvedValue(true);
    mocks.addCaptureStateListener.mockResolvedValue(() => {});
    mocks.addScreenshotListener.mockResolvedValue(() => {});
    mocks.postVideoProgress.mockResolvedValue({ percentComplete: 0, completed: false, positionSeconds: 0 });
    mocks.requestPlaybackSession.mockResolvedValue(secureSession);
    mocks.createHlsEngine.mockResolvedValue(hlsHandle);
  });

  afterEach(() => {
    cleanup();
    vi.restoreAllMocks();
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
  });

  it('stretches a secure embed and uses the container fullscreen API', async () => {
    renderPlayer();

    await screen.findByRole('button', { name: 'Stretch video to fill player' });
    const iframe = screen.getByTitle('Protected course video');
    expect(iframe).toHaveClass('object-contain');

    fireEvent.click(screen.getByRole('button', { name: 'Stretch video to fill player' }));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Fit video to player' })).toHaveAttribute('aria-pressed', 'true'));
    await waitFor(() => expect(iframe).toHaveClass('object-cover'));

    const player = screen.getByRole('application', { name: 'Video player' });
    const requestFullscreen = vi.fn(() => {
      Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: player });
      document.dispatchEvent(new Event('fullscreenchange'));
      return Promise.resolve();
    });
    const exitFullscreen = vi.fn(() => {
      Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
      document.dispatchEvent(new Event('fullscreenchange'));
      return Promise.resolve();
    });
    Object.defineProperty(player, 'requestFullscreen', { configurable: true, value: requestFullscreen });
    Object.defineProperty(document, 'exitFullscreen', { configurable: true, value: exitFullscreen });

    fireEvent.click(screen.getByRole('button', { name: 'Fullscreen' }));
    await waitFor(() => expect(requestFullscreen).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Exit fullscreen' })).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Exit fullscreen' }));
    await waitFor(() => expect(exitFullscreen).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Fullscreen' })).toBeInTheDocument());
  });

  it('applies the same stretch toggle to legacy direct-HLS playback', async () => {
    mocks.requestPlaybackSession.mockResolvedValue(directSession);
    renderPlayer();

    const stretchButton = await screen.findByRole('button', { name: 'Stretch video to fill player' });
    const video = document.querySelector('video');
    expect(video).toHaveClass('object-contain');

    fireEvent.click(stretchButton);
    await waitFor(() => expect(video).toHaveClass('object-cover'));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Fit video to player' })).toHaveAttribute('aria-pressed', 'true'));
  });
});
