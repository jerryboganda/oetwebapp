// Shared delivery-mode helper for mock section players.
//
// OET is delivered in three modes — paper-based (test centre), computer-based
// (test centre, on-screen), and OET@Home (remotely proctored). The CONTENT is
// identical across all three; only the answer interaction, proctoring and (for
// Speaking) the interlocutor channel differ. So content is authored once and
// the mode is selected at delivery time.
//
// The backend `MockService.BuildLaunchRoute` appends `&deliveryMode=<mode>` to
// every section launch URL (values: `computer` | `paper` | `oet_home`, see
// `Domain/MockTypes.cs` `MockDeliveryModes`). Each subtest player reads it via
// the helpers below so the launched experience matches the chosen mode.

export type DeliveryMode = 'computer' | 'paper' | 'oet_home';

export const DELIVERY_MODES: readonly DeliveryMode[] = ['computer', 'paper', 'oet_home'] as const;

/** Minimal read-only view of URLSearchParams (Next's useSearchParams result). */
export interface ReadonlyParams {
  get(key: string): string | null;
}

/** Normalize an arbitrary string to a known delivery mode. Defaults to
 * `computer` — the on-screen experience — for anything unrecognized. */
export function normalizeDeliveryMode(value: string | null | undefined): DeliveryMode {
  const v = (value ?? '').trim().toLowerCase();
  if (v === 'paper') return 'paper';
  if (v === 'oet_home' || v === 'oet-home' || v === 'home') return 'oet_home';
  return 'computer';
}

/**
 * Resolve the delivery mode a mock launch attached to a section player URL.
 * Falls back to the legacy `presentation=paper` alias the Reading paper player
 * historically accepted, so existing deep links keep working.
 */
export function deriveDeliveryMode(searchParams: ReadonlyParams | null | undefined): DeliveryMode {
  const raw = searchParams?.get('deliveryMode');
  if (raw && raw.trim()) return normalizeDeliveryMode(raw);
  // Legacy alias used by the Reading paper player before delivery modes existed.
  if ((searchParams?.get('presentation') ?? '').trim().toLowerCase() === 'paper') return 'paper';
  return 'computer';
}

/**
 * The Reading paper player renders either a paper-style booklet (single
 * scrollable view) or a computer-style per-part layout. OET@Home is
 * computer-delivered, so only `paper` maps to the booklet presentation.
 */
export function deliveryModeToReadingPresentation(mode: DeliveryMode): 'paper' | 'computer' {
  return mode === 'paper' ? 'paper' : 'computer';
}

/** Human label for badges/headers. */
export function deliveryModeLabel(mode: DeliveryMode): string {
  switch (mode) {
    case 'paper':
      return 'Paper-based';
    case 'oet_home':
      return 'OET@Home';
    default:
      return 'On-screen (computer)';
  }
}
