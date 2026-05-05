/**
 * UTM and acquisition-source attribution tracking.
 *
 * Captures UTM parameters from the URL on first visit, persists them in
 * sessionStorage, and surfaces them for inclusion in registration and
 * analytics payloads.
 */

const ATTRIBUTION_STORAGE_KEY = 'oet_attribution';

export interface AttributionSnapshot {
  utmSource: string | null;
  utmMedium: string | null;
  utmCampaign: string | null;
  utmTerm: string | null;
  utmContent: string | null;
  referrer: string | null;
  landingPath: string | null;
  capturedAt: string;
}

/**
 * Read UTM parameters from the current URL and persist them in
 * sessionStorage.  Should be called once on app mount (e.g. in the root
 * layout or auth gate).
 */
export function captureAttribution(): AttributionSnapshot {
  if (typeof window === 'undefined') {
    return emptyAttribution();
  }

  const existing = readAttribution();
  if (existing.utmSource || existing.utmMedium || existing.utmCampaign) {
    // Already captured — don't overwrite so the first-touch source wins.
    return existing;
  }

  const params = new URLSearchParams(window.location.search);
  const snapshot: AttributionSnapshot = {
    utmSource: params.get('utm_source'),
    utmMedium: params.get('utm_medium'),
    utmCampaign: params.get('utm_campaign'),
    utmTerm: params.get('utm_term'),
    utmContent: params.get('utm_content'),
    referrer: document.referrer || null,
    landingPath: window.location.pathname + window.location.search,
    capturedAt: new Date().toISOString(),
  };

  try {
    sessionStorage.setItem(ATTRIBUTION_STORAGE_KEY, JSON.stringify(snapshot));
  } catch {
    // Storage may be blocked in private mode — ignore silently.
  }

  return snapshot;
}

/** Read the currently stored attribution snapshot, if any. */
export function readAttribution(): AttributionSnapshot {
  if (typeof window === 'undefined') {
    return emptyAttribution();
  }

  try {
    const raw = sessionStorage.getItem(ATTRIBUTION_STORAGE_KEY);
    if (raw) {
      const parsed = JSON.parse(raw) as AttributionSnapshot;
      // Defensive: ensure all fields exist even on old serialised data.
      return { ...emptyAttribution(), ...parsed };
    }
  } catch {
    // Malformed storage — ignore.
  }

  return emptyAttribution();
}

/** Clear stored attribution (e.g. after successful registration). */
export function clearAttribution(): void {
  if (typeof window === 'undefined') return;
  try {
    sessionStorage.removeItem(ATTRIBUTION_STORAGE_KEY);
  } catch {
    // ignore
  }
}

function emptyAttribution(): AttributionSnapshot {
  return {
    utmSource: null,
    utmMedium: null,
    utmCampaign: null,
    utmTerm: null,
    utmContent: null,
    referrer: null,
    landingPath: null,
    capturedAt: new Date().toISOString(),
  };
}
