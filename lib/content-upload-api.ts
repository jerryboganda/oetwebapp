/**
 * Admin Content Upload API client. Matches backend endpoints in
 * `ContentPapersAdminEndpoints.cs`. See `docs/CONTENT-UPLOAD-PLAN.md`.
 */

import { apiClient } from './api';
import { env } from './env';
import { ensureFreshAccessToken } from './auth-client';
import { fetchWithTimeout } from './network/fetch-with-timeout';

// ── Types mirror the .NET contracts 1:1 ─────────────────────────────────

export type PaperAssetRole =
  | 'Audio'
  | 'QuestionPaper'
  | 'AudioScript'
  | 'AnswerKey'
  | 'CaseNotes'
  | 'ModelAnswer'
  | 'RoleCard'
  | 'AssessmentCriteria'
  | 'WarmUpQuestions'
  | 'Supplementary';

export type ContentStatus = 'Draft' | 'InReview' | 'Published' | 'Archived' | 'Rejected';

export interface ContentPaperDto {
  id: string;
  subtestCode: string;
  title: string;
  slug: string;
  professionId: string | null;
  appliesToAllProfessions: boolean;
  difficulty: string;
  estimatedDurationMinutes: number;
  status: ContentStatus;
  publishedRevisionId: string | null;
  cardType: string | null;
  letterType: string | null;
  priority: number;
  tagsCsv: string;
  sourceProvenance: string | null;
  createdAt: string;
  updatedAt: string;
  publishedAt: string | null;
  archivedAt: string | null;
  integrityAcknowledgedByAdminId: string | null;
  integrityAcknowledgedAt: string | null;
  assets?: ContentPaperAssetDto[];
}

export interface ContentPaperAssetDto {
  id: string;
  role: PaperAssetRole;
  part: string | null;
  mediaAssetId: string;
  title: string | null;
  displayOrder: number;
  isPrimary: boolean;
  createdAt: string;
  media: {
    id: string;
    originalFilename: string;
    mimeType: string;
    format: string;
    sizeBytes: number;
    durationSeconds: number | null;
    sha256: string | null;
    mediaKind: string | null;
    uploadedAt: string;
  } | null;
}

export interface ContentPaperCreateDto {
  subtestCode: string;
  title: string;
  slug?: string | null;
  professionId?: string | null;
  appliesToAllProfessions: boolean;
  difficulty?: string | null;
  estimatedDurationMinutes: number;
  cardType?: string | null;
  letterType?: string | null;
  priority: number;
  tagsCsv?: string | null;
  sourceProvenance?: string | null;
}

export interface ContentPaperUpdateDto {
  title?: string;
  professionId?: string | null;
  appliesToAllProfessions?: boolean;
  difficulty?: string | null;
  estimatedDurationMinutes?: number;
  cardType?: string | null;
  letterType?: string | null;
  priority?: number;
  tagsCsv?: string | null;
  sourceProvenance?: string | null;
}

export interface ContentPaperAssetAttachDto {
  role: PaperAssetRole;
  mediaAssetId: string;
  part?: string | null;
  title?: string | null;
  displayOrder: number;
  makePrimary: boolean;
}

export interface ChunkedUploadStartResponse {
  uploadId: string;
  chunkSizeBytes: number;
  expiresAt: string;
}

export interface ChunkedUploadCommitResult {
  mediaAssetId: string;
  sha256: string;
  sizeBytes: number;
  deduplicated: boolean;
}

export interface SpeakingStructureValidationIssue {
  code: string;
  severity: 'error' | 'warning' | string;
  message: string;
}

export interface SpeakingStructureValidationReport {
  isPublishReady: boolean;
  issues: SpeakingStructureValidationIssue[];
}

export interface SpeakingAuthoringStructure {
  candidateCard?: {
    candidateRole?: string;
    role?: string;
    setting?: string;
    patientRole?: string;
    patient?: string;
    task?: string;
    brief?: string;
    background?: string;
    tasks?: string[];
  };
  interlocutorCard?: {
    patientProfile?: string;
    background?: string;
    hiddenInformation?: string;
    cuePrompts?: string[];
    prompts?: string[];
    objectives?: string[];
    privateNotes?: string;
  };
  warmUpQuestions?: string[];
  prepTimeSeconds?: number;
  roleplayTimeSeconds?: number;
  patientEmotion?: string;
  communicationGoal?: string;
  clinicalTopic?: string;
  criteriaFocus?: string[];
  complianceNotes?: string;
}

export interface SpeakingStructureResponse {
  paperId: string;
  structure: SpeakingAuthoringStructure;
  validation: SpeakingStructureValidationReport;
  updatedAt?: string;
}

export interface WritingStructureValidationIssue {
  code: string;
  severity: 'error' | 'warning' | string;
  message: string;
}

export interface WritingStructureValidationReport {
  isPublishReady: boolean;
  issues: WritingStructureValidationIssue[];
}

export interface WritingAuthoringStructure {
  taskPrompt?: string;
  letterType?: string;
  taskDate?: string;
  writerRole?: string;
  recipient?: string;
  purpose?: string;
  caseNotes?: string;
  caseNoteSections?: Array<{
    heading?: string;
    items?: string[];
  }>;
  modelAnswerText?: string;
  modelAnswerParagraphs?: Array<{
    id?: string;
    text?: string;
    rationale?: string;
    criteria?: string[];
    included?: string[];
    excluded?: string[];
    languageNotes?: string;
  }>;
  criteriaFocus?: string[];
  authoringNotes?: string;
  // OET exam closure additions (read by the ContentPaper→Scenario bridge and
  // the enriched admin builder). All optional for backward compatibility.
  internalCode?: string;
  expectedAction?: string;
  wordGuideMin?: number;
  wordGuideMax?: number;
  fixedInstructions?: string[];
  simulationModes?: 'paper' | 'computer' | 'both';
  markingMode?: 'tutor' | 'ai_assisted' | 'double';
  retakePolicy?: { maxAttempts?: number; cooldownHours?: number };
  keyContentChecklist?: Array<{
    itemText?: string;
    category?: string;
    importance?: 'high' | 'medium' | 'low';
    requiredStatus?: 'required' | 'optional' | 'irrelevant';
    linkedCaseNoteSection?: string;
    expectedRepresentation?: string;
    commonError?: string;
  }>;
  irrelevantContentChecklist?: Array<{
    itemText?: string;
    category?: string;
    commonError?: string;
  }>;
}

export interface WritingStructureResponse {
  paperId: string;
  structure: WritingAuthoringStructure;
  validation: WritingStructureValidationReport;
  updatedAt?: string;
}

// ── Transport ───────────────────────────────────────────────────────────

function resolveUrl(path: string): string {
  if (path.startsWith('http')) return path;
  const base = env.apiBaseUrl || '';
  return base ? `${base.replace(/\/$/, '')}${path}` : path;
}

/**
 * Reads the double-submit CSRF cookie value (set by the auth flow). The
 * Next.js backend proxy at /api/backend/* requires this header for any
 * state-changing method when a refresh-token cookie is present, otherwise
 * it returns 403 before the request even reaches the .NET API.
 */
function readCsrfCookie(): string | null {
  if (typeof document === 'undefined') return null;
  const m = document.cookie.match(/(?:^|;\s*)oet_csrf=([^;]+)/);
  return m ? decodeURIComponent(m[1]) : null;
}

// ── Error surfacing ───────────────────────────────────────────────────────
// The .NET API emits problem payloads shaped like
//   { code, message, fieldErrors[], retryable, supportHint, correlationId }
// (see Program.cs UseExceptionHandler), and the same-origin /api/backend proxy
// relays the body unchanged. A few older endpoints use ASP.NET ProblemDetails
// ({ title, detail }). We lift the backend's own prose into Error.message so
// the upload UI shows a real reason (e.g. "Upload rejected: Unrecognised file
// format.") instead of a bare "HTTP 400", and we keep the structured fields on
// the error for programmatic callers (status / code / retryable).
interface ApiErrorPayload {
  code?: string;
  message?: string;
  retryable?: boolean;
  supportHint?: string | null;
  correlationId?: string;
  fieldErrors?: Array<{ field?: string; code?: string; message?: string }>;
  title?: string; // ProblemDetails fallback
  detail?: string; // ProblemDetails fallback
}

export interface ApiError extends Error {
  status?: number;
  code?: string;
  retryable?: boolean;
  correlationId?: string;
  detail?: unknown;
}

async function toApiError(res: Response, fallbackPrefix?: string): Promise<ApiError> {
  let payload: ApiErrorPayload | null = null;
  try {
    payload = (await res.json()) as ApiErrorPayload;
  } catch {
    /* non-JSON or empty body — fall back to the status line */
  }

  const prose = [payload?.message, payload?.title, payload?.detail]
    .map((s) => (typeof s === 'string' ? s.trim() : ''))
    .find((s) => s.length > 0);
  const code = typeof payload?.code === 'string' ? payload.code : undefined;

  const base = prose || `HTTP ${res.status}`;
  // Append the machine code only when it adds signal the prose doesn't carry.
  const withCode = code && !base.toLowerCase().includes(code.toLowerCase())
    ? `${base} (${code})`
    : base;
  const message = fallbackPrefix ? `${fallbackPrefix} ${withCode}` : withCode;

  const err = new Error(message) as ApiError;
  err.status = res.status;
  err.code = code;
  err.retryable = typeof payload?.retryable === 'boolean' ? payload.retryable : undefined;
  err.correlationId = typeof payload?.correlationId === 'string' ? payload.correlationId : undefined;
  err.detail = payload;
  return err;
}

// JSON endpoints delegate to the shared API client (lib/api.ts) so they inherit
// auth (Bearer), CSRF, credentials, timeout, retry-on-5xx/408/429, and a
// normalized `ApiError` (status + code + retryable + fieldErrors, detectable via
// `isApiError`). The shared client already lifts the backend's `message`/`title`
// prose into `ApiError.message`, matching what `toApiError` did for the JSON
// path. The chunked binary upload (`uploadPart`) stays on the raw
// `fetchWithTimeout` path below because it streams an octet-stream `Blob` and
// must not be forced through JSON handling.
async function api<T>(path: string, init?: RequestInit): Promise<T> {
  return apiClient.request<T>(path, init);
}

// ── Papers ──────────────────────────────────────────────────────────────

export const listContentPapers = (query: Partial<{
  subtest: string; profession: string; status: string;
  cardType: string; letterType: string; search: string;
  page: number; pageSize: number;
}>) => {
  const qs = new URLSearchParams();
  Object.entries(query).forEach(([k, v]) => {
    if (v !== undefined && v !== null && v !== '') qs.append(k, String(v));
  });
  return api<ContentPaperDto[]>(`/v1/admin/papers?${qs.toString()}`);
};

export const getContentPaper = (id: string) => api<ContentPaperDto>(`/v1/admin/papers/${id}`);
export const createContentPaper = (body: ContentPaperCreateDto) =>
  api<ContentPaperDto>('/v1/admin/papers', { method: 'POST', body: JSON.stringify(body) });
export const updateContentPaper = (id: string, body: ContentPaperUpdateDto) =>
  api<ContentPaperDto>(`/v1/admin/papers/${id}`, { method: 'PUT', body: JSON.stringify(body) });
export const archiveContentPaper = (id: string) =>
  api<void>(`/v1/admin/papers/${id}`, { method: 'DELETE' });
export const publishContentPaper = (id: string) =>
  api<void>(`/v1/admin/papers/${id}/publish`, { method: 'POST' });
export const unpublishContentPaper = (id: string) =>
  api<void>(`/v1/admin/papers/${id}/unpublish`, { method: 'POST' });

// ── Writing-task authoring + lifecycle (spec §1C / §1E / §19) ───────────

export interface WritingTaskCreateDto {
  title: string;
  slug?: string | null;
  professionId: string;
  letterType: string;
  difficulty?: string | null;
  estimatedDurationMinutes: number;
  priority: number;
  tagsCsv?: string | null;
  sourceProvenance: string;
  integrityAcknowledged: true;
}

export const createWritingPaper = (body: WritingTaskCreateDto) =>
  api<ContentPaperDto>('/v1/admin/papers/writing-task', {
    method: 'POST',
    body: JSON.stringify(body),
  });

export const submitWritingPaperForReview = (id: string) =>
  api<void>(`/v1/admin/papers/${id}/submit-for-review`, { method: 'POST' });

export const approvePublishWritingPaper = (id: string) =>
  api<void>(`/v1/admin/papers/${id}/approve-publish`, { method: 'POST' });

export const rejectWritingPaper = (id: string, reason: string) =>
  api<void>(`/v1/admin/papers/${id}/reject`, {
    method: 'POST',
    body: JSON.stringify({ reason }),
  });

export const getSpeakingStructure = (paperId: string) =>
  api<SpeakingStructureResponse>(`/v1/admin/papers/${paperId}/speaking-structure`);

export const updateSpeakingStructure = (paperId: string, structure: SpeakingAuthoringStructure) =>
  api<SpeakingStructureResponse>(`/v1/admin/papers/${paperId}/speaking-structure`, {
    method: 'PUT',
    body: JSON.stringify({ structure }),
  });

export const getWritingStructure = (paperId: string) =>
  api<WritingStructureResponse>(`/v1/admin/papers/${paperId}/writing-structure`);

export const updateWritingStructure = (paperId: string, structure: WritingAuthoringStructure) =>
  api<WritingStructureResponse>(`/v1/admin/papers/${paperId}/writing-structure`, {
    method: 'PUT',
    body: JSON.stringify({ structure }),
  });

export const attachPaperAsset = (paperId: string, body: ContentPaperAssetAttachDto) =>
  api<ContentPaperAssetDto>(`/v1/admin/papers/${paperId}/assets`, {
    method: 'POST', body: JSON.stringify(body),
  });
export const removePaperAsset = (paperId: string, assetId: string) =>
  api<void>(`/v1/admin/papers/${paperId}/assets/${assetId}`, { method: 'DELETE' });

export const getRequiredRoles = (subtest: string) =>
  api<{ subtest: string; required: PaperAssetRole[] }>(
    `/v1/admin/papers/required-roles/${subtest}`,
  );

// ── Chunked uploads ─────────────────────────────────────────────────────

export const startUpload = (body: {
  originalFilename: string;
  declaredMimeType: string;
  declaredSizeBytes: number;
  intendedRole: PaperAssetRole;
}) => api<ChunkedUploadStartResponse>('/v1/admin/uploads', {
  method: 'POST', body: JSON.stringify(body),
});

export async function uploadPart(uploadId: string, partNumber: number, body: Blob): Promise<void> {
  const token = await ensureFreshAccessToken();
  const csrf = readCsrfCookie();
  const res = await fetchWithTimeout(
    resolveUrl(`/v1/admin/uploads/${uploadId}/parts/${partNumber}`),
    {
      method: 'PUT',
      body,
      credentials: 'include',
      headers: {
        ...(token ? { Authorization: `Bearer ${token}` } : {}),
        ...(csrf ? { 'x-csrf-token': csrf } : {}),
        'Content-Type': 'application/octet-stream',
      },
    },
  );
  if (!res.ok) throw await toApiError(res, 'Upload part failed:');
}

export const completeUpload = (uploadId: string) =>
  api<ChunkedUploadCommitResult>(`/v1/admin/uploads/${uploadId}/complete`, { method: 'POST' });

/**
 * High-level helper: take a File, run the whole chunked upload protocol,
 * return the resulting MediaAsset id. Reports progress 0..1 via callback.
 */
export async function uploadFileChunked(
  file: File,
  intendedRole: PaperAssetRole,
  onProgress?: (pct: number) => void,
): Promise<ChunkedUploadCommitResult> {
  const start = await startUpload({
    originalFilename: file.name,
    declaredMimeType: file.type || 'application/octet-stream',
    declaredSizeBytes: file.size,
    intendedRole,
  });
  const chunkSize = start.chunkSizeBytes;
  const totalParts = Math.max(1, Math.ceil(file.size / chunkSize));
  for (let i = 0; i < totalParts; i++) {
    const from = i * chunkSize;
    const to = Math.min(file.size, from + chunkSize);
    const chunk = file.slice(from, to);
    await uploadPart(start.uploadId, i + 1, chunk);
    onProgress?.((i + 1) / totalParts * 0.95); // leave 5% for complete
  }
  const result = await completeUpload(start.uploadId);
  onProgress?.(1);
  return result;
}
