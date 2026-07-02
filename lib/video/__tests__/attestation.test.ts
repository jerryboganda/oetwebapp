// @vitest-environment jsdom
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

const { mockGetAppRuntimeKind, mockIsAvailable, mockSignViaCapacitor, mockFetchChallenge, mockCreateSession, mockIsApiError } = vi.hoisted(() => ({
  mockGetAppRuntimeKind: vi.fn(),
  mockIsAvailable: vi.fn(),
  mockSignViaCapacitor: vi.fn(),
  mockFetchChallenge: vi.fn(),
  mockCreateSession: vi.fn(),
  mockIsApiError: vi.fn(),
}));

vi.mock('@/lib/runtime-signals', () => ({
  getAppRuntimeKind: mockGetAppRuntimeKind,
}));

vi.mock('@/lib/mobile/playback-attestation', () => ({
  isPlaybackAttestationAvailable: mockIsAvailable,
  signVideoChallenge: mockSignViaCapacitor,
  setSecureScreen: vi.fn(),
}));

vi.mock('@/lib/api/videos', () => ({
  fetchPlaybackChallenge: mockFetchChallenge,
  createPlaybackSession: mockCreateSession,
}));

vi.mock('@/lib/api', () => ({
  isApiError: mockIsApiError,
}));

import { getPlaybackAttestor, PlaybackGateError, requestPlaybackSession } from '../attestation';

type BridgeShape = { attestation?: { signVideoChallenge: ReturnType<typeof vi.fn> } };
type BridgeWindow = Window & { desktopBridge?: BridgeShape };

function installBridge(bridge: BridgeShape) {
  (window as unknown as { desktopBridge?: BridgeShape }).desktopBridge = bridge;
}

const SESSION = {
  sessionId: 'sess-1',
  playbackUrl: 'https://vz-test.b-cdn.net/bunny-1/playlist.m3u8?token=t&expires=1&token_path=%2Fbunny-1%2F',
  expiresAt: '2026-07-03T12:00:00Z',
  watermarkText: 'learner@example.com · sess-1',
  captions: [],
};

const SIGNATURE = { signature: 'aa'.repeat(32), platform: 'tauri', keyId: 'v1', appVersion: '0.4.0' };

function apiError(code: string, status: number) {
  return { code, status, userMessage: `err:${code}` };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockIsApiError.mockImplementation((error: unknown) => Boolean(error && typeof error === 'object' && 'code' in (error as object)));
  mockFetchChallenge.mockResolvedValue({ nonce: 'nonce-1', expiresAt: '2026-07-03T00:01:30Z' });
  mockCreateSession.mockResolvedValue(SESSION);
});

afterEach(() => {
  delete (window as BridgeWindow).desktopBridge;
});

describe('getPlaybackAttestor', () => {
  it('reports WEB_NOT_ALLOWED on web runtimes', () => {
    mockGetAppRuntimeKind.mockReturnValue('web');
    const attestor = getPlaybackAttestor();
    expect(attestor.available).toBe(false);
    if (!attestor.available) expect(attestor.reason).toBe('WEB_NOT_ALLOWED');
  });

  it('reports DESKTOP_UPDATE_REQUIRED when the v0.3 bridge lacks attestation', () => {
    mockGetAppRuntimeKind.mockReturnValue('desktop');
    installBridge({});
    const attestor = getPlaybackAttestor();
    expect(attestor.available).toBe(false);
    if (!attestor.available) expect(attestor.reason).toBe('DESKTOP_UPDATE_REQUIRED');
  });

  it('reports MOBILE_UPDATE_REQUIRED when the Capacitor plugin is absent', () => {
    mockGetAppRuntimeKind.mockReturnValue('capacitor-native');
    mockIsAvailable.mockReturnValue(false);
    const attestor = getPlaybackAttestor();
    expect(attestor.available).toBe(false);
    if (!attestor.available) expect(attestor.reason).toBe('MOBILE_UPDATE_REQUIRED');
  });
});

describe('requestPlaybackSession', () => {
  function installDesktopBridge() {
    const sign = vi.fn().mockResolvedValue(SIGNATURE);
    installBridge({ attestation: { signVideoChallenge: sign } });
    mockGetAppRuntimeKind.mockReturnValue('desktop');
    return sign;
  }

  it('rejects immediately on web without touching the API', async () => {
    mockGetAppRuntimeKind.mockReturnValue('web');
    await expect(requestPlaybackSession('video-1', 'user-1')).rejects.toMatchObject({ code: 'WEB_NOT_ALLOWED' });
    expect(mockFetchChallenge).not.toHaveBeenCalled();
    expect(mockCreateSession).not.toHaveBeenCalled();
  });

  it('runs challenge → native sign → session on desktop', async () => {
    const sign = installDesktopBridge();

    const session = await requestPlaybackSession('video-1', 'user-1');

    expect(session).toEqual(SESSION);
    expect(sign).toHaveBeenCalledWith('nonce-1', 'video-1', 'user-1');
    expect(mockCreateSession).toHaveBeenCalledWith('video-1', {
      nonce: 'nonce-1',
      platform: 'tauri',
      keyId: 'v1',
      signature: SIGNATURE.signature,
    });
  });

  it('signs through the Capacitor plugin on mobile', async () => {
    mockGetAppRuntimeKind.mockReturnValue('capacitor-native');
    mockIsAvailable.mockReturnValue(true);
    mockSignViaCapacitor.mockResolvedValue({ ...SIGNATURE, platform: 'capacitor-android' });

    await requestPlaybackSession('video-9', 'user-2');

    expect(mockSignViaCapacitor).toHaveBeenCalledWith('nonce-1', 'video-9', 'user-2');
    expect(mockCreateSession).toHaveBeenCalledWith('video-9', expect.objectContaining({ platform: 'capacitor-android' }));
  });

  it('retries exactly once with a fresh nonce when the nonce expired', async () => {
    installDesktopBridge();
    mockFetchChallenge
      .mockResolvedValueOnce({ nonce: 'stale', expiresAt: 'x' })
      .mockResolvedValueOnce({ nonce: 'fresh', expiresAt: 'x' });
    mockCreateSession
      .mockRejectedValueOnce(apiError('attestation_invalid', 403))
      .mockResolvedValueOnce(SESSION);

    const session = await requestPlaybackSession('video-1', 'user-1');

    expect(session).toEqual(SESSION);
    expect(mockCreateSession).toHaveBeenCalledTimes(2);
    expect(mockCreateSession).toHaveBeenLastCalledWith('video-1', expect.objectContaining({ nonce: 'fresh' }));
  });

  it('maps entitlement denials to CONTENT_LOCKED without retrying', async () => {
    installDesktopBridge();
    mockCreateSession.mockRejectedValue(apiError('content_locked', 402));

    await expect(requestPlaybackSession('video-1', 'user-1')).rejects.toMatchObject({ code: 'CONTENT_LOCKED' });
    expect(mockCreateSession).toHaveBeenCalledTimes(1);
  });

  it('maps concurrent-session refusals to SESSION_LIMIT', async () => {
    installDesktopBridge();
    mockCreateSession.mockRejectedValue(apiError('concurrent_session_limit', 409));

    await expect(requestPlaybackSession('video-1', 'user-1')).rejects.toMatchObject({ code: 'SESSION_LIMIT' });
  });

  it('treats a failing native signer as an app-update problem', async () => {
    mockGetAppRuntimeKind.mockReturnValue('desktop');
    const sign = vi.fn().mockRejectedValue(new Error('attestation secret not embedded'));
    installBridge({ attestation: { signVideoChallenge: sign } });

    await expect(requestPlaybackSession('video-1', 'user-1')).rejects.toBeInstanceOf(PlaybackGateError);
    await expect(requestPlaybackSession('video-1', 'user-1')).rejects.toMatchObject({ code: 'DESKTOP_UPDATE_REQUIRED' });
    expect(mockCreateSession).not.toHaveBeenCalled();
  });
});
