/**
 * Listening exam-mode presentation taxonomy. Mirrors the canonical OET
 * delivery channels:
 *
 *   - `computer` → OET on Computer (test centre). Default skin.
 *   - `home`     → OET@Home. Kiosk fullscreen, distraction-free, paste-blocked.
 *
 * The server already exposes `session.modePolicy.mode` (`practice` / `exam` /
 * `home`) and the more granular `presentationStyle`. The mapper
 * here collapses both into a single canonical skin code consumed by the
 * `<ListeningPlayerSkinShell />` wrapper.
 */

export type ListeningPresentationMode = 'computer' | 'home';

export type ServerListeningMode = 'practice' | 'exam' | 'home';

export type ServerPresentationStyle =
  | 'practice'
  | 'exam_standard'
  | 'kiosk_fullscreen';

export interface PresentationModeInput {
  mode?: ServerListeningMode | null;
  presentationStyle?: ServerPresentationStyle | null;
}

/**
 * Resolve the canonical skin code from the server-issued session DTO. Prefers
 * the explicit `presentationStyle` when present; falls back to the coarser
 * `mode` field. `practice` and `exam` both resolve to `computer` — the
 * difference between them is enforcement strictness, not visual chrome.
 */
export function presentationModeFromSession(input: PresentationModeInput): ListeningPresentationMode {
  switch (input.presentationStyle) {
    case 'kiosk_fullscreen': return 'home';
    case 'exam_standard':
    case 'practice':
      return 'computer';
    default:
      break;
  }
  switch (input.mode) {
    case 'home': return 'home';
    case 'exam':
    case 'practice':
    default:
      return 'computer';
  }
}

export function isStrictPresentation(mode: ListeningPresentationMode): boolean {
  return mode === 'home';
}
