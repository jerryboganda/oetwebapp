/**
 * Typed API client for the OET Speaking role-play card surface.
 *
 * Uses the shared `apiRequest` pipeline from `./client` (Bearer-token auth via
 * `ensureFreshAccessToken`, CSRF token via the `oet_csrf` cookie, retry
 * on 5xx/408/429) so auth handling stays consistent with the rest of the app.
 *
 * Backend endpoints targeted (see plan section B.1):
 *   POST    /v1/admin/speaking/role-play-cards
 *   GET     /v1/admin/speaking/role-play-cards
 *   GET     /v1/admin/speaking/role-play-cards/{id}
 *   PATCH   /v1/admin/speaking/role-play-cards/{id}
 *   POST    /v1/admin/speaking/role-play-cards/{id}/publish
 *   POST    /v1/admin/speaking/role-play-cards/{id}/archive
 *   POST    /v1/admin/speaking/role-play-cards/{id}/duplicate
 *   GET     /v1/admin/speaking/role-play-cards/{id}/interlocutor-script
 *   PUT     /v1/admin/speaking/role-play-cards/{id}/interlocutor-script
 *   GET     /v1/speaking/role-play-cards/{id}        (learner — no interlocutor)
 */

import { apiRequest } from './client';
import { fetchProfessionCatalog, professionCatalogOptions } from '@/lib/api/professions';
import { professions } from '@/lib/auth/enrollment';
import type { BulkActionResultDto } from '@/lib/types/admin';

// ─────────────────────────────────────────────────────────────────────────────
// Types — mirrors backend DTOs returned from AdminSpeakingContentEndpoints
// ─────────────────────────────────────────────────────────────────────────────

export type RolePlayCardStatus = 'Draft' | 'InReview' | 'Published' | 'Archived';
export type ResistanceLevelCode = 'low' | 'medium' | 'high';
export type RolePlayCardDifficulty = 'core' | 'extension' | 'exam';

export interface RolePlayCardSummary {
  cardId: string;
  contentItemId: string;
  title: string;
  professionId: string;
  setting: string;
  clinicalTopic: string;
  difficulty: RolePlayCardDifficulty | string;
  status: RolePlayCardStatus | string;
  isLiveTutorEligible: boolean;
  hasInterlocutorScript: boolean;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  // Speaking module rebuild (2026-06-11) — hidden card type (admin only).
  cardTypeId?: string | null;
  cardTypeName?: string | null;
  /** Rights/provenance notice from the printed source. Admin-only. */
  sourceAttribution?: string | null;
}

export interface RolePlayCardDetail {
  cardId: string;
  contentItemId: string;
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  patientName: string | null;
  patientAge: string | null;
  background: string;
  /** The full ordered task list, any length. Filtered to non-empty entries. */
  tasks: string[];
  allowedNotes: boolean;
  prepTimeSeconds: number;
  rolePlayTimeSeconds: number;
  patientEmotion: string;
  communicationGoal: string;
  clinicalTopic: string;
  difficulty: RolePlayCardDifficulty | string;
  criteriaFocus: string[];
  disclaimer: string;
  isLiveTutorEligible: boolean;
  status: RolePlayCardStatus | string;
  hasInterlocutorScript: boolean;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  archivedAt: string | null;
  // Speaking module rebuild (2026-06-11) — hidden card type + printed number.
  cardTypeId?: string | null;
  cardTypeName?: string | null;
  displayCardNumber?: number | null;
  /**
   * Verbatim rights/provenance notice printed on the source card this row was
   * transcribed from. ADMIN-ONLY — deliberately absent from
   * `RolePlayCardLearnerDetail`, so the learner's card face stays clean while
   * the record keeps honest provenance. Do not strip it on import.
   */
  sourceAttribution?: string | null;
}

export interface RolePlayCardLearnerDetail {
  cardId: string;
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole: string;
  patientName: string | null;
  patientAge: string | null;
  background: string;
  tasks: string[];
  allowedNotes: boolean;
  prepTimeSeconds: number;
  rolePlayTimeSeconds: number;
  patientEmotion: string;
  communicationGoal: string;
  clinicalTopic: string;
  difficulty: RolePlayCardDifficulty | string;
  criteriaFocus: string[];
  disclaimer: string;
}

export interface InterlocutorScriptDetail {
  cardId: string;
  openingResponse: string;
  prompt1: string | null;
  prompt2: string | null;
  prompt3: string | null;
  hiddenInformation: string;
  resistanceLevel: ResistanceLevelCode;
  closingCue: string;
  emotionalState: string;
  professionRoleNotes: string | null;
  layLanguageTriggers: string[];
  updatedAt: string;
  // Speaking module rebuild (2026-06-11) — printed roleplayer card face.
  patientBackground?: string;
  patientTasks?: (string | null)[];
  allowsSecondVisit?: boolean;
  secondVisitIndicator?: string | null;
  secondVisitCarryFacts?: string[];
}

export interface CreateRolePlayCardInput {
  professionId: string;
  scenarioTitle: string;
  setting: string;
  candidateRole: string;
  interlocutorRole?: string;
  patientName?: string | null;
  patientAge?: string | null;
  background: string;
  /**
   * PREFERRED: the full ordered task list, any length. Wins over `task1..task5`
   * when supplied. A card printed with six or more bullets MUST use this — the
   * five positional fields below cannot represent it and content is lost.
   * On PATCH this replaces the whole list.
   */
  tasks?: string[];
  /** @deprecated Legacy 5-slot fields; use `tasks`. Kept for older callers. */
  task1?: string | null;
  task2?: string | null;
  task3?: string | null;
  task4?: string | null;
  task5?: string | null;
  allowedNotes?: boolean;
  prepTimeSeconds?: number;
  rolePlayTimeSeconds?: number;
  patientEmotion: string;
  communicationGoal: string;
  clinicalTopic: string;
  difficulty?: RolePlayCardDifficulty | string;
  criteriaFocus: string[];
  disclaimer?: string;
  isLiveTutorEligible?: boolean;
  // Speaking module rebuild (2026-06-11) — hidden card type + printed number.
  cardTypeId?: string | null;
  displayCardNumber?: number | null;
  /** Rights/provenance notice from the printed source. Admin-only. `""` clears it. */
  sourceAttribution?: string | null;
}

export type PatchRolePlayCardInput = Partial<CreateRolePlayCardInput>;

export interface UpsertInterlocutorScriptInput {
  openingResponse: string;
  prompt1?: string | null;
  prompt2?: string | null;
  prompt3?: string | null;
  hiddenInformation?: string;
  resistanceLevel?: ResistanceLevelCode;
  closingCue?: string;
  emotionalState?: string;
  professionRoleNotes?: string | null;
  layLanguageTriggers?: string[];
  // Speaking module rebuild (2026-06-11) — printed roleplayer card face.
  patientBackground?: string;
  /**
   * PREFERRED: the full ordered roleplayer task list, any length. Replaces the
   * whole list when supplied; `patientTask1..5` are read only when this is
   * omitted.
   */
  patientTasks?: string[];
  /** @deprecated Legacy 5-slot fields; use `patientTasks`. */
  patientTask1?: string | null;
  patientTask2?: string | null;
  patientTask3?: string | null;
  patientTask4?: string | null;
  patientTask5?: string | null;
  allowsSecondVisit?: boolean;
  secondVisitIndicator?: string | null;
  secondVisitCarryFacts?: string[];
}

// ─────────────────────────────────────────────────────────────────────────────
// Card types (2026-06-11 rebuild) — fully configurable admin taxonomy
// ─────────────────────────────────────────────────────────────────────────────

export interface SpeakingCardTypeDetail {
  id: string;
  name: string;
  description: string;
  sortOrder: number;
  isActive: boolean;
  cardCount: number;
  createdAt: string;
  updatedAt: string;
}

export interface SpeakingCardTypeUpsertInput {
  name: string;
  description?: string | null;
  sortOrder?: number | null;
  isActive?: boolean | null;
}

export async function adminListSpeakingCardTypes(includeInactive = true): Promise<SpeakingCardTypeDetail[]> {
  return apiRequest<SpeakingCardTypeDetail[]>(
    `/v1/admin/speaking/card-types?includeInactive=${includeInactive ? 'true' : 'false'}`,
  );
}

export async function adminCreateSpeakingCardType(input: SpeakingCardTypeUpsertInput): Promise<SpeakingCardTypeDetail> {
  return apiRequest<SpeakingCardTypeDetail>('/v1/admin/speaking/card-types', {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function adminUpdateSpeakingCardType(
  id: string,
  input: SpeakingCardTypeUpsertInput,
): Promise<SpeakingCardTypeDetail> {
  return apiRequest<SpeakingCardTypeDetail>(`/v1/admin/speaking/card-types/${encodeURIComponent(id)}`, {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export async function adminDeleteSpeakingCardType(
  id: string,
): Promise<{ id: string; action: string; softDeleted: boolean }> {
  return apiRequest<{ id: string; action: string; softDeleted: boolean }>(
    `/v1/admin/speaking/card-types/${encodeURIComponent(id)}`,
    { method: 'DELETE' },
  );
}

export interface ListRolePlayCardsFilters {
  professionId?: string;
  difficulty?: string;
  status?: string;
}

// ─────────────────────────────────────────────────────────────────────────────
// Admin functions
// ─────────────────────────────────────────────────────────────────────────────

export async function adminListRolePlayCards(filters: ListRolePlayCardsFilters = {}): Promise<RolePlayCardSummary[]> {
  const qs = new URLSearchParams();
  if (filters.professionId) qs.set('professionId', filters.professionId);
  if (filters.difficulty) qs.set('difficulty', filters.difficulty);
  if (filters.status) qs.set('status', filters.status);
  const query = qs.toString();
  return apiRequest<RolePlayCardSummary[]>(`/v1/admin/speaking/role-play-cards${query ? `?${query}` : ''}`);
}

export async function adminCreateRolePlayCard(input: CreateRolePlayCardInput): Promise<RolePlayCardDetail> {
  return apiRequest<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards`, {
    method: 'POST',
    body: JSON.stringify(input),
  });
}

export async function adminGetRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return apiRequest<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}`);
}

export async function adminPatchRolePlayCard(cardId: string, input: PatchRolePlayCardInput): Promise<RolePlayCardDetail> {
  return apiRequest<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}`, {
    method: 'PATCH',
    body: JSON.stringify(input),
  });
}

export async function adminPublishRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return apiRequest<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/publish`, {
    method: 'POST',
    body: '{}',
  });
}

export async function adminArchiveRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return apiRequest<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/archive`, {
    method: 'POST',
    body: '{}',
  });
}

export type RolePlayCardBulkAction = 'publish' | 'archive';

/**
 * Bulk action over speaking role-play cards.
 * `POST /v1/admin/speaking/role-play-cards/bulk`. Backend record is PascalCase
 * `(Action, Ids)`; ASP.NET binds the camelCase JSON case-insensitively (matches
 * the other admin POSTs in this module).
 */
export async function bulkAdminRolePlayCards(
  action: RolePlayCardBulkAction,
  ids: string[],
): Promise<BulkActionResultDto> {
  return apiRequest<BulkActionResultDto>('/v1/admin/speaking/role-play-cards/bulk', {
    method: 'POST',
    body: JSON.stringify({ action, ids }),
  });
}

export async function adminDuplicateRolePlayCard(cardId: string): Promise<RolePlayCardDetail> {
  return apiRequest<RolePlayCardDetail>(`/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/duplicate`, {
    method: 'POST',
    body: '{}',
  });
}

export async function adminGetInterlocutorScript(cardId: string): Promise<InterlocutorScriptDetail | null> {
  return apiRequest<InterlocutorScriptDetail | null>(
    `/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/interlocutor-script`,
    undefined,
    { acceptedStatuses: [404] },
  );
}

export async function adminUpsertInterlocutorScript(
  cardId: string,
  input: UpsertInterlocutorScriptInput,
): Promise<InterlocutorScriptDetail> {
  return apiRequest<InterlocutorScriptDetail>(
    `/v1/admin/speaking/role-play-cards/${encodeURIComponent(cardId)}/interlocutor-script`,
    { method: 'PUT', body: JSON.stringify(input) },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Phase 11 (G.11) — AI-assisted draft
// ─────────────────────────────────────────────────────────────────────────────

/** Seed for `POST /v1/admin/speaking/role-play-cards/ai-draft`. */
export interface AdminRolePlayCardAiDraftInput {
  professionId: string;
  topic?: string | null;
  emotion?: string | null;
  difficulty?: string | null;
  setting?: string | null;
  candidateRole?: string | null;
  interlocutorRole?: string | null;
  communicationGoal?: string | null;
}

/** Response from the grounded-gateway-backed role-play card draft endpoint. */
export interface AdminRolePlayCardAiDraftResponse {
  cardId: string;
  card: RolePlayCardDetail;
  warning?: string | null;
}

/**
 * Calls the grounded gateway via the backend to persist a Draft
 * candidate role-play card + paired hidden interlocutor script.
 * The server enforces grounding (rulebook + scoring) and returns the
 * persisted card + script as `RolePlayCardDetail` for an inline
 * preview, plus an optional `warning` when the AI reply could not be
 * parsed and a deterministic fallback was used.
 */
export async function draftSpeakingRolePlayCard(
  input: AdminRolePlayCardAiDraftInput,
): Promise<AdminRolePlayCardAiDraftResponse> {
  return apiRequest<AdminRolePlayCardAiDraftResponse>(
    '/v1/admin/speaking/role-play-cards/ai-draft',
    { method: 'POST', body: JSON.stringify(input) },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// WS9 (SPK-007) — scanned/text PDF import → structured draft
// ─────────────────────────────────────────────────────────────────────────────

/** One field-presence check from the import builder-validation report. */
export interface SpeakingImportFieldCheck {
  field: string;
  detected: boolean;
  required: boolean;
  note?: string | null;
}

/** Builder-validation report for an imported source PDF. */
export interface SpeakingImportValidationReport {
  isPublishable: boolean;
  checks: SpeakingImportFieldCheck[];
  blockers: string[];
}

/** Result of `POST /v1/admin/speaking/role-play-cards/import`. */
export interface SpeakingContentImportResult {
  sourceAssetKey: string;
  sourceBytes: number;
  extractedChars: number;
  likelyScanned: boolean;
  validation: SpeakingImportValidationReport;
  draftCardId?: string | null;
  draft?: RolePlayCardDetail | null;
  warning?: string | null;
  /** Viewable MediaAsset id for the persisted source PDF (provenance). Lets the
   * admin render the source beside the form when structuring a scanned card by
   * hand. Served (authenticated) at `/v1/media/{id}/content`. */
  sourceMediaId?: string | null;
}

/**
 * Imports a source paper (scanned or text PDF). The source asset is always
 * persisted server-side for provenance; when `autoDraft` is true and usable
 * text is extracted, the grounded AI-draft path produces a reviewable Draft
 * card. A scanned PDF with no OCR provider returns the validation report and
 * the saved source asset for manual structuring.
 */
export async function importSpeakingRolePlayCard(input: {
  file: File;
  professionId: string;
  topic?: string | null;
  autoDraft?: boolean;
}): Promise<SpeakingContentImportResult> {
  const form = new FormData();
  form.append('file', input.file);
  form.append('professionId', input.professionId);
  if (input.topic) form.append('topic', input.topic);
  form.append('autoDraft', String(input.autoDraft ?? false));
  return apiRequest<SpeakingContentImportResult>(
    '/v1/admin/speaking/role-play-cards/import',
    { method: 'POST', body: form },
    { timeoutMs: 180_000, json: false },
  );
}

// ─────────────────────────────────────────────────────────────────────────────
// Learner function (no interlocutor data)
// ─────────────────────────────────────────────────────────────────────────────

export async function learnerGetRolePlayCard(cardId: string): Promise<RolePlayCardLearnerDetail> {
  return apiRequest<RolePlayCardLearnerDetail>(`/v1/speaking/role-play-cards/${encodeURIComponent(cardId)}`);
}

// ─────────────────────────────────────────────────────────────────────────────
// Constants for forms
// ─────────────────────────────────────────────────────────────────────────────

/**
 * Profession choices for card authoring, video access tagging and expert
 * queue filters.
 *
 * Derived from the canonical taxonomy (spec §3) rather than hand-listed: the
 * ids written here are persisted (`ProfessionIdsJson`, card `professionId`)
 * and joined against the learner's registered profession, so an id outside
 * `SignupProfessionCatalog` tags content nobody can ever match. Synchronous
 * because every consumer renders a `<Select>` during first paint; use
 * {@link fetchProfessionOptions} where a live catalog read is worth the await.
 */
export const PROFESSION_OPTIONS: { value: string; label: string }[] = professions.map((item) => ({
  value: item.id,
  label: item.label,
}));

/** Live canonical catalog as select options; falls back to {@link PROFESSION_OPTIONS} when the API is unreachable. */
export async function fetchProfessionOptions(signal?: AbortSignal): Promise<{ value: string; label: string }[]> {
  return professionCatalogOptions(await fetchProfessionCatalog({ signal }));
}

export const DIFFICULTY_OPTIONS: { value: RolePlayCardDifficulty; label: string }[] = [
  { value: 'core', label: 'Core' },
  { value: 'extension', label: 'Extension' },
  { value: 'exam', label: 'Exam' },
];

/** The 9 OET Speaking criteria (4 linguistic + 5 clinical communication). */
export const SPEAKING_CRITERIA_OPTIONS: { value: string; label: string; band: 'linguistic' | 'clinical' }[] = [
  { value: 'intelligibility', label: 'Intelligibility', band: 'linguistic' },
  { value: 'fluency', label: 'Fluency', band: 'linguistic' },
  { value: 'appropriateness', label: 'Appropriateness of language', band: 'linguistic' },
  { value: 'grammarExpression', label: 'Resources of grammar & expression', band: 'linguistic' },
  { value: 'relationshipBuilding', label: 'Relationship building', band: 'clinical' },
  { value: 'patientPerspective', label: 'Understanding patient perspective', band: 'clinical' },
  { value: 'structure', label: 'Providing structure', band: 'clinical' },
  { value: 'informationGathering', label: 'Information gathering', band: 'clinical' },
  { value: 'informationGiving', label: 'Information giving', band: 'clinical' },
];

export const RESISTANCE_LEVEL_OPTIONS: { value: ResistanceLevelCode; label: string; description: string }[] = [
  { value: 'low', label: 'Low', description: 'Patient cooperative; accepts advice with minimal probing.' },
  { value: 'medium', label: 'Medium', description: 'Patient questions advice; needs reassurance before accepting.' },
  { value: 'high', label: 'High', description: 'Patient strongly resists; requires sustained empathy and rationale.' },
];

export const DEFAULT_DISCLAIMER =
  'Practice estimate only. This is not an official OET score or result.';
