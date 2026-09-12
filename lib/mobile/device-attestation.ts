'use client';

import { Capacitor } from '@capacitor/core';
import { getIntegrityToken, isPlaybackAttestationAvailable } from './playback-attestation';

/**
 * Client wrapper for server-side hardware device-integrity verification
 * (security standard MOB-09 / MOB-10).
 *
 * Flow (server issues and consumes the nonce — see
 * `backend/.../Endpoints/DeviceAttestationEndpoints.cs`):
 *   1. POST /v1/device-integrity/challenge        → single-use nonce
 *   2. native `getIntegrityToken({ nonce })`       → Play Integrity / App Attest token
 *   3. POST /v1/device-integrity/verify            → sanitised verdict
 *
 * The returned verdict is a RISK SIGNAL, never an access control. Callers must
 * NOT gate sign-in, entitlement, or any other authn/authz decision on it.
 */

/** Sanitised verdict returned by `POST /v1/device-integrity/verify`. */
export interface DeviceAttestationVerdict {
  /** Server's affirmative attestation result. `false` for unknown/unsupported. */
  trusted: boolean;
  /** `play_integrity` | `app_attest` | `unavailable`. */
  provider: string;
  /** `trusted` | `not_trusted` | `unknown` | `not_implemented`. */
  verdict: string;
  /** Short machine-readable reason, safe to log. Null when trusted. */
  reason: string | null;
}

export interface RequestIntegrityTokenOptions {
  /**
   * API origin, e.g. `https://api.oetwithdrhesham.co.uk`. Empty/omitted →
   * same-origin relative URLs (the typical web / Capacitor proxy case).
   */
  apiBaseUrl?: string;
  /** Bearer access token for the authenticated learner. */
  accessToken?: string;
  /** Override the fetch implementation (tests / custom clients). */
  fetcher?: typeof fetch;
  /** AbortSignal to cancel the round-trip. */
  signal?: AbortSignal;
}

const CHALLENGE_PATH = '/v1/device-integrity/challenge';
const VERIFY_PATH = '/v1/device-integrity/verify';

function resolveUrl(apiBaseUrl: string | undefined, path: string): string {
  const base = (apiBaseUrl ?? '').replace(/\/+$/, '');
  return `${base}${path}`;
}

/** The two native platforms whose attestation the server understands. */
function currentPlatform(): 'capacitor-android' | 'capacitor-ios' | null {
  if (!Capacitor.isNativePlatform()) {
    return null;
  }
  const platform = Capacitor.getPlatform();
  if (platform === 'android') {
    return 'capacitor-android';
  }
  if (platform === 'ios') {
    return 'capacitor-ios';
  }
  return null;
}

/**
 * Obtains a hardware attestation token and gets it verified server-side.
 *
 * No-throw by design (integrity is a RISK SIGNAL — a failure here must never
 * break the caller's critical flow): returns `null` on web/desktop, on shells
 * without the native `getIntegrityToken` method, on any network/HTTP failure, or
 * when the server returns an unparseable verdict. Callers proceed without a
 * signal rather than blocking.
 */
export async function requestIntegrityToken(
  options: RequestIntegrityTokenOptions = {},
): Promise<DeviceAttestationVerdict | null> {
  const doFetch = options.fetcher ?? (typeof fetch !== 'undefined' ? fetch : undefined);
  if (!doFetch) {
    return null;
  }

  const platform = currentPlatform();
  if (!platform || !isPlaybackAttestationAvailable()) {
    return null;
  }

  const headers: Record<string, string> = { 'Content-Type': 'application/json' };
  if (options.accessToken) {
    headers.Authorization = `Bearer ${options.accessToken}`;
  }

  try {
    // 1. Server-issued, single-use, user-bound nonce. Feeding it into the
    //    native SDK binds the resulting token to this challenge so a captured
    //    token cannot be replayed.
    const challengeResponse = await doFetch(resolveUrl(options.apiBaseUrl, CHALLENGE_PATH), {
      method: 'POST',
      headers,
      body: JSON.stringify({ platform }),
      signal: options.signal,
    });
    if (!challengeResponse.ok) {
      return null;
    }
    const challenge = (await challengeResponse.json()) as { nonce?: string } | null;
    if (!challenge?.nonce) {
      return null;
    }

    // 2. Native hardware attestation token bound to the nonce.
    const attestation = await getIntegrityToken(challenge.nonce);
    if (!attestation) {
      return null;
    }

    // 3. Server-side verification.
    const verifyResponse = await doFetch(resolveUrl(options.apiBaseUrl, VERIFY_PATH), {
      method: 'POST',
      headers,
      body: JSON.stringify({
        platform,
        nonce: challenge.nonce,
        integrityToken: attestation.token,
        keyId: attestation.keyId ?? null,
      }),
      signal: options.signal,
    });
    if (!verifyResponse.ok) {
      return null;
    }

    const verdict = (await verifyResponse.json()) as DeviceAttestationVerdict | null;
    if (!verdict || typeof verdict.trusted !== 'boolean') {
      return null;
    }
    return verdict;
  } catch {
    return null;
  }
}
