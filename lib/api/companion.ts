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
  | 'plan_excludes_companion'
  | 'monthly_cap_reached'
  | 'daily_cap_reached';

export interface CompanionAccess {
  canChat: boolean;
  reason: CompanionAccessReason;
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

export function resetCompanionMemory(): Promise<{ notesDeleted: number; bookmarksDeleted: number }> {
  return apiRequest<{ notesDeleted: number; bookmarksDeleted: number }>('/v1/companion/memory', {
    method: 'DELETE',
  });
}
