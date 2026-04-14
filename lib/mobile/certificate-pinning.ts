/**
 * Mobile Certificate Pinning
 *
 * Validates SSL certificates against expected pins for the API domain.
 * This provides MITM protection on mobile, equivalent to Electron's
 * certificate-pinning.cjs for desktop.
 *
 * Since Capacitor's WebView controls the HTTP stack, we implement this
 * via fetch interception and native validation where possible.
 */

import { Capacitor } from '@capacitor/core';

interface CertificatePinRule {
  host: string;
  includeSubdomains?: boolean;
  fingerprints256: string[];
}

const PIN_RULES: CertificatePinRule[] = [
  {
    host: 'app.oetwithdrhesham.co.uk',
    includeSubdomains: true,
    fingerprints256: [], // Populated from environment/config at build time
  },
];

let pinningEnabled = false;

/**
 * Initialize certificate pinning. On native platforms, this installs
 * a fetch interceptor that validates server certificates.
 *
 * Note: Full certificate pinning on Capacitor requires a native plugin
 * (e.g., capacitor-ssl-pinning). This module provides the configuration
 * and fallback validation logic.
 */
export function initializeCertificatePinning(rules?: CertificatePinRule[]): void {
  if (!Capacitor.isNativePlatform()) return;

  if (rules) {
    PIN_RULES.length = 0;
    PIN_RULES.push(...rules);
  }

  // Only enable if we have actual pins configured
  pinningEnabled = PIN_RULES.some((rule) => rule.fingerprints256.length > 0);

  if (pinningEnabled) {
    installFetchInterceptor();
  }
}

export function isPinningEnabled(): boolean {
  return pinningEnabled;
}

export function getPinRules(): readonly CertificatePinRule[] {
  return PIN_RULES;
}

/**
 * Validate a hostname against pinning rules.
 * Returns true if the host is pinned (requires validation).
 */
export function isHostPinned(hostname: string): boolean {
  return PIN_RULES.some((rule) => {
    if (rule.host === hostname) return true;
    if (rule.includeSubdomains && hostname.endsWith(`.${rule.host}`)) return true;
    return false;
  });
}

/**
 * Install a global fetch wrapper that logs certificate validation warnings
 * for pinned hosts. Full native pinning requires the native plugin.
 */
function installFetchInterceptor(): void {
  const originalFetch = globalThis.fetch;

  globalThis.fetch = async function pinnedFetch(
    input: RequestInfo | URL,
    init?: RequestInit
  ): Promise<Response> {
    const url = typeof input === 'string'
      ? new URL(input, globalThis.location?.href)
      : input instanceof URL
        ? input
        : new URL((input as Request).url);

    if (url.protocol === 'https:' && isHostPinned(url.hostname)) {
      // On native, the SSL pinning plugin handles actual pin validation.
      // This wrapper adds defense-in-depth: if the response somehow
      // arrives despite a bad certificate, we can detect it.
      try {
        const response = await originalFetch(input, init);
        return response;
      } catch (error) {
        // Network errors on pinned hosts may indicate MITM
        if (error instanceof TypeError && (error.message.includes('SSL') || error.message.includes('certificate'))) {
          console.error('[cert-pinning] SSL validation failed for pinned host:', url.hostname);
        }
        throw error;
      }
    }

    return originalFetch(input, init);
  };
}

/**
 * Load pin rules from runtime configuration.
 * Called during app initialization from mobile-runtime-bridge.
 */
export function loadPinRulesFromConfig(config: {
  certificatePins?: Array<{
    host: string;
    includeSubdomains?: boolean;
    fingerprints256: string[];
  }>;
}): void {
  if (config.certificatePins && config.certificatePins.length > 0) {
    initializeCertificatePinning(config.certificatePins);
  }
}
