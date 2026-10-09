/**
 * AI Learning Companion learner surface.
 *
 * Kept as its own module rather than added to `lib/api.ts`: the companion is a
 * self-contained programme (docs/ai-learning-companion/) and this file is the
 * only place its wire shapes are described.
 */
import { apiRequest } from './client';

/** Why the companion is or is not usable right now. Server-decided. */
export type CompanionAccessReason =
  | 'ok'
  | 'companion_disabled'
  | 'ai_disabled'
  | 'kill_switch'
  | 'policy_unavailable'
  | 'package_required'
  | 'plan_excludes_companion'
  | 'monthly_cap_reached'
  | 'daily_cap_reached'
  /** An operator switched the companion off for this learner (SAMI §9). */
  | 'manually_disabled'
  /** The learner's per-user grant lapsed and nothing else grants access. */
  | 'expired';

/**
 * Provenance behind the decision (SAMI §9 per-user access layer). `reason` says
 * which rule decided; `source` says where the entitlement came from, so an
 * operator can see "this learner was enabled by hand" without reading a plan row.
 * Null only while the session endpoint never evaluated the gate (master flag off).
 */
export type CompanionAccessSource =
  | 'package_included'
  | 'admin_enabled'
  | 'promotional'
  | 'manually_disabled'
  | 'expired'
  | 'none';

export interface CompanionAccess {
  canChat: boolean;
  reason: CompanionAccessReason;
  source: CompanionAccessSource | null;
  planCode: string | null;
  planName: string | null;
  /** Where to go to gain access. Server-resolved; never composed on the client. */
  upgradeUrl: string | null;
}

export interface CompanionSession {
  enabled: boolean;
  persona: string;
  retrievalEnabled: boolean;
  actionsEnabled: boolean;
  creditConsumptionEnabled: boolean;
  /** TV-006 / TV-007. While false, never render a numeric band estimate. */
  scoreDisplayEnabled: boolean;
  tier: string;
  hasSubscription: boolean;
  professionId: string | null;
  examTypeCode: string | null;
  examDate: string | null;
  daysUntilExam: number | null;
  locale: string;
  aiCreditsRemaining: number;
  access: CompanionAccess;
  topUpUrl: string | null;
}

export interface CompanionMemoryNote {
  id: string;
  title: string;
  body: string;
  createdAt: string;
}

export interface CompanionMemoryBookmark {
  id: string;
  term: string;
  createdAt: string;
}

export interface CompanionMemory {
  notes: CompanionMemoryNote[];
  bookmarks: CompanionMemoryBookmark[];
}

/** F-011 / F-050 / F-052 — how the learner wants to be taught. */
export type CompanionTeachingStyle = 'Direct' | 'Socratic' | 'Coaching';
export type CompanionExplanationDepth = 'Brief' | 'Standard' | 'Deep';

export interface CompanionPreferences {
  teachingStyle: CompanionTeachingStyle;
  depth: CompanionExplanationDepth;
  /** F-055 — reply in English even to an Arabic message. Opt-in immersion. */
  englishOnly: boolean;
  preferWorkedExamples: boolean;
  updatedAt: string;
}

export function fetchCompanionPreferences(): Promise<CompanionPreferences> {
  return apiRequest<CompanionPreferences>('/v1/companion/preferences');
}

export function saveCompanionPreferences(
  preferences: Omit<CompanionPreferences, 'updatedAt'>,
): Promise<CompanionPreferences> {
  return apiRequest<CompanionPreferences>('/v1/companion/preferences', {
    method: 'PUT',
    body: JSON.stringify(preferences),
  });
}

/** Capability snapshot for the current learner. Drives the paywall and the chip. */
export function fetchCompanionSession(): Promise<CompanionSession> {
  return apiRequest<CompanionSession>('/v1/companion/session');
}

/** Everything the companion has saved on this learner's behalf (F-047). */
export function fetchCompanionMemory(): Promise<CompanionMemory> {
  return apiRequest<CompanionMemory>('/v1/companion/memory');
}

export function deleteCompanionNote(noteId: string): Promise<void> {
  return apiRequest<void>(`/v1/companion/memory/notes/${encodeURIComponent(noteId)}`, {
    method: 'DELETE',
  });
}

export function deleteCompanionBookmark(bookmarkId: string): Promise<void> {
  return apiRequest<void>(`/v1/companion/memory/bookmarks/${encodeURIComponent(bookmarkId)}`, {
    method: 'DELETE',
  });
}

/**
 * Downloads the learner's companion memory as a JSON file.
 *
 * Uses a blob URL rather than a plain link because the endpoint is bearer-authed
 * — a native anchor cannot send the token, which is the same reason the rest of
 * this app fetches protected media through `fetchAuthorizedObjectUrl`.
 */
export async function downloadCompanionMemory(): Promise<void> {
  // Reuses the repo's bearer-authed blob fetch — the same helper the app uses
  // for protected audio and images, for the same reason: a native anchor cannot
  // carry the access token.
  const { fetchAuthorizedObjectUrl } = await import('@/lib/api');
  const url = await fetchAuthorizedObjectUrl('/v1/companion/memory/export');
  try {
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = 'companion-memory.json';
    document.body.appendChild(anchor);
    anchor.click();
    anchor.remove();
  } finally {
    URL.revokeObjectURL(url);
  }
}

/**
 * Resets the learner's companion data and reports what was removed.
 *
 * The response covers derived data as well as notes and bookmarks: structured learning
 * memory and Error DNA are deleted too, and each count is returned so a caller can show the
 * learner exactly what was erased rather than just asserting success.
 */
export interface CompanionMemoryResetResult {
  notesDeleted: number;
  bookmarksDeleted: number;
  memoriesDeleted: number;
  errorDnaDeleted: number;
}

export function resetCompanionMemory(): Promise<CompanionMemoryResetResult> {
  return apiRequest<CompanionMemoryResetResult>('/v1/companion/memory', {
    method: 'DELETE',
  });
}
