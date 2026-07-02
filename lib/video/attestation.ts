/**
 * Playback attestation orchestration — the client half of the
 * native-app-only video playback gate.
 *
 * Flow (see `VideoAttestationService.cs` server-side):
 *   1. Fetch a single-use challenge nonce from the API (90s TTL).
 *   2. Ask the NATIVE layer to sign `"{nonce}|{videoId}|{userId}|{platform}|{keyId}"`
 *      with a build-embedded HMAC-SHA256 secret:
 *        - Tauri desktop  → `window.desktopBridge.attestation.signVideoChallenge`
 *        - Capacitor      → `PlaybackAttestation` plugin (lib/mobile/playback-attestation)
 *   3. POST the signature to mint a playback session + signed CDN URL.
 *
 * The secret never exists in JS (both shells load this same remote web app),
 * so a browser cannot produce a valid signature no matter what it spoofs.
 * Runtime detection here is UX-only — the server rejects anything unsigned.
 */
import { isApiError } from '@/lib/api';
import {
  createPlaybackSession,
  fetchPlaybackChallenge,
} from '@/lib/api/videos';
import type { PlaybackSession } from '@/lib/types/videos';
import { getAppRuntimeKind, type AppRuntimeKind } from '@/lib/runtime-signals';
import {
  isPlaybackAttestationAvailable,
  signVideoChallenge as signViaCapacitor,
} from '@/lib/mobile/playback-attestation';

export type PlaybackGateErrorCode =
  | 'WEB_NOT_ALLOWED'
  | 'DESKTOP_UPDATE_REQUIRED'
  | 'MOBILE_UPDATE_REQUIRED'
  | 'ATTESTATION_REJECTED'
  | 'ATTESTATION_UNAVAILABLE'
  | 'CONTENT_LOCKED'
  | 'SUBSCRIPTION_FROZEN'
  | 'SUBSCRIPTION_EXPIRED'
  | 'SESSION_LIMIT'
  | 'NOT_CONFIGURED'
  | 'NETWORK';

export class PlaybackGateError extends Error {
  readonly code: PlaybackGateErrorCode;

  constructor(code: PlaybackGateErrorCode, message?: string) {
    super(message ?? code);
    this.name = 'PlaybackGateError';
    this.code = code;
  }
}

export interface NativeSignature {
  signature: string;
  platform: string;
  keyId: string;
  appVersion: string;
}

export interface PlaybackAttestor {
  available: true;
  runtimeKind: Exclude<AppRuntimeKind, 'web'>;
  sign(nonce: string, videoId: string, userId: string): Promise<NativeSignature>;
}

export interface UnavailableAttestor {
  available: false;
  reason: 'WEB_NOT_ALLOWED' | 'DESKTOP_UPDATE_REQUIRED' | 'MOBILE_UPDATE_REQUIRED';
}

type DesktopAttestationBridge = {
  desktopBridge?: {
    attestation?: {
      signVideoChallenge: (
        nonce: string,
        videoId: string,
        userId: string,
      ) => Promise<NativeSignature>;
    };
  };
};

export function getPlaybackAttestor(): PlaybackAttestor | UnavailableAttestor {
  const kind = getAppRuntimeKind();

  if (kind === 'desktop') {
    const bridge = (window as unknown as DesktopAttestationBridge).desktopBridge?.attestation;
    if (!bridge?.signVideoChallenge) {
      // v0.3.x desktop shells predate the attestation command.
      return { available: false, reason: 'DESKTOP_UPDATE_REQUIRED' };
    }
    return {
      available: true,
      runtimeKind: 'desktop',
      sign: (nonce, videoId, userId) => bridge.signVideoChallenge(nonce, videoId, userId),
    };
  }

  if (kind === 'capacitor-native') {
    if (!isPlaybackAttestationAvailable()) {
      // v1.1.x mobile builds predate the PlaybackAttestation plugin.
      return { available: false, reason: 'MOBILE_UPDATE_REQUIRED' };
    }
    return {
      available: true,
      runtimeKind: 'capacitor-native',
      sign: async (nonce, videoId, userId) => {
        // The wrapper is no-throw and returns null on any native failure
        // (old shell, missing release secret) — surface that as a throw so
        // attemptSession maps it to the update-required path.
        const result = await signViaCapacitor(nonce, videoId, userId);
        if (!result) {
          throw new Error('playback attestation unavailable on this build');
        }
        return result;
      },
    };
  }

  return { available: false, reason: 'WEB_NOT_ALLOWED' };
}

function mapApiFailure(error: unknown): PlaybackGateError {
  if (isApiError(error)) {
    switch (error.code) {
      case 'attestation_invalid':
        return new PlaybackGateError('ATTESTATION_REJECTED', error.userMessage);
      case 'attestation_unavailable':
        return new PlaybackGateError('ATTESTATION_UNAVAILABLE', error.userMessage);
      case 'native_client_required':
        return new PlaybackGateError('WEB_NOT_ALLOWED', error.userMessage);
      case 'content_locked':
        return new PlaybackGateError('CONTENT_LOCKED', error.userMessage);
      case 'subscription_frozen':
        return new PlaybackGateError('SUBSCRIPTION_FROZEN', error.userMessage);
      case 'subscription_expired':
        return new PlaybackGateError('SUBSCRIPTION_EXPIRED', error.userMessage);
      case 'concurrent_session_limit':
        return new PlaybackGateError('SESSION_LIMIT', error.userMessage);
      case 'bunny_not_configured':
        return new PlaybackGateError('NOT_CONFIGURED', error.userMessage);
      default:
        if (error.status === 402) return new PlaybackGateError('CONTENT_LOCKED', error.userMessage);
        if (error.status === 403) return new PlaybackGateError('ATTESTATION_REJECTED', error.userMessage);
        if (error.status === 409) return new PlaybackGateError('SESSION_LIMIT', error.userMessage);
        if (error.status === 503) return new PlaybackGateError('NOT_CONFIGURED', error.userMessage);
        return new PlaybackGateError('NETWORK', error.userMessage);
    }
  }
  return new PlaybackGateError('NETWORK');
}

async function attemptSession(
  attestor: PlaybackAttestor,
  videoId: string,
  userId: string,
): Promise<PlaybackSession> {
  const challenge = await fetchPlaybackChallenge();

  let signed: NativeSignature;
  try {
    signed = await attestor.sign(challenge.nonce, videoId, userId);
  } catch {
    // Native command exists but failed (e.g. release build without an embedded
    // secret, or plugin-level rejection). Treat as an update/config problem
    // rather than a web denial so the message stays actionable.
    throw new PlaybackGateError(
      attestor.runtimeKind === 'desktop' ? 'DESKTOP_UPDATE_REQUIRED' : 'MOBILE_UPDATE_REQUIRED',
    );
  }

  return createPlaybackSession(videoId, {
    nonce: challenge.nonce,
    platform: signed.platform,
    keyId: signed.keyId,
    signature: signed.signature,
  });
}

/**
 * Full challenge → native-sign → session round trip.
 * Retries once on `attestation_invalid` (the nonce may have expired between
 * fetch and submit — e.g. app resumed from background).
 */
export async function requestPlaybackSession(videoId: string, userId: string): Promise<PlaybackSession> {
  const attestor = getPlaybackAttestor();
  if (!attestor.available) {
    throw new PlaybackGateError(attestor.reason);
  }

  try {
    return await attemptSession(attestor, videoId, userId);
  } catch (error) {
    if (error instanceof PlaybackGateError) {
      throw error;
    }
    const mapped = mapApiFailure(error);
    if (mapped.code !== 'ATTESTATION_REJECTED') {
      throw mapped;
    }
    // Single retry with a fresh nonce.
    try {
      return await attemptSession(attestor, videoId, userId);
    } catch (retryError) {
      throw retryError instanceof PlaybackGateError ? retryError : mapApiFailure(retryError);
    }
  }
}
