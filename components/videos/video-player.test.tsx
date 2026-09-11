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
  getAppRuntimeKind: vi.fn(() => 'desktop'),
}));

vi.mock('@/lib/runtime-signals', () => ({ getAppRuntimeKind: mocks.getAppRuntimeKind }));
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
  watermarkText: 'Learner · session',
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
    mocks.getAppRuntimeKind.mockReturnValue('desktop');
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

  it('keeps accessible container fullscreen and removes stretch/fit controls from secure playback', async () => {
    renderPlayer();

    await screen.findByRole('button', { name: 'Fullscreen' });
    expect(screen.queryByRole('button', { name: /stretch|fit/i })).not.toBeInTheDocument();
    expect(screen.getByTitle('Protected course video')).toHaveClass('h-full', 'w-full');

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
    Object.defineProperty(document, 'fullscreenElement', { configurable: true, value: null });
    Object.defineProperty(document, 'webkitFullscreenElement', { configurable: true, value: null });
    Object.defineProperty(document, 'mozFullScreenElement', { configurable: true, value: null });
    Object.defineProperty(document, 'msFullscreenElement', { configurable: true, value: null });

    fireEvent.click(screen.getByRole('button', { name: 'Fullscreen' }));
    await waitFor(() => expect(requestFullscreen).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Exit fullscreen' })).toBeInTheDocument());

    fireEvent.click(screen.getByRole('button', { name: 'Exit fullscreen' }));
    await waitFor(() => expect(exitFullscreen).toHaveBeenCalledTimes(1));
    await waitFor(() => expect(screen.getByRole('button', { name: 'Fullscreen' })).toBeInTheDocument());
  });

  it('keeps legacy direct-HLS playback free of stretch/fit controls', async () => {
    mocks.requestPlaybackSession.mockResolvedValue(directSession);
    renderPlayer();

    await screen.findByRole('button', { name: 'Fullscreen' });
    expect(document.querySelector('video')).toHaveClass('object-contain');
    expect(screen.queryByRole('button', { name: /stretch|fit/i })).not.toBeInTheDocument();
  });

  // Mobile-gate regression (Final Developer Modification Brief item 2): the
  // WEB_NOT_ALLOWED gate itself must stay untouched by the layout/timing fix
  // — a web runtime still never attempts a playback session and still shows
  // the "app required" notice.
  it('shows the app-required notice on a web runtime without ever requesting a playback session', async () => {
    mocks.getAppRuntimeKind.mockReturnValue('web');
    renderPlayer();

    expect(await screen.findByRole('heading', { name: 'App Required for Video Playback' })).toBeInTheDocument();
    expect(mocks.requestPlaybackSession).not.toHaveBeenCalled();
  });
});
