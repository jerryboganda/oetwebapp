// SUBAGENT_C: Shared types for bulk-import + generation orchestration.
//
// Mirrors the .NET DTOs returned by:
//   POST   /v1/admin/imports/zip
//   POST   /v1/admin/imports/zip/{sessionId}/commit
//   POST   /v1/admin/rulebooks/import
//   GET    /v1/admin/rulebooks
//   POST   /v1/admin/content/generate
//   GET    /v1/admin/content/generation-jobs[/{jobId}]
//
// Keep this file dependency-free — both client components and lib/api.ts
// import from it.

/** Subset of `PaperAssetRole` enum strings the manifest can emit. */
export type BulkImportAssetRole =
  | 'Primary'
  | 'Audio'
  | 'Transcript'
  | 'AnswerKey'
  | 'Stimulus'
  | 'Brief'
  | 'Letter'
  | 'Notes'
  | 'Cue'
  | 'Supplementary'
  | string;

export interface BulkImportAssetProposal {
  sourceRelativePath: string;
  role: BulkImportAssetRole;
  part?: string | null;
  suggestedTitle?: string | null;
}

export interface BulkImportPaperProposal {
  proposalId: string;
  subtestCode: string;
  title: string;
  professionId?: string | null;
  appliesToAllProfessions: boolean;
  cardType?: string | null;
  letterType?: string | null;
  sourceProvenance?: string | null;
  assets: BulkImportAssetProposal[];
}

export interface BulkImportSessionResponse {
  sessionId: string;
  expiresAt: string;
  papers: BulkImportPaperProposal[];
  issues: string[];
}

export interface BulkImportApprovalInput {
  proposalId: string;
  approve: boolean;
  overrideTitle?: string | null;
  overrideProfessionId?: string | null;
  overrideAppliesToAllProfessions?: boolean | null;
  overrideCardType?: string | null;
  overrideLetterType?: string | null;
  overrideSourceProvenance?: string | null;
}

export interface BulkImportCommitResult {
  createdPaperCount: number;
  createdAssetCount: number;
  deduplicatedAssetCount: number;
  warnings: string[];
}

// ── Rulebook bulk import ────────────────────────────────────────────────

export type RulebookImportMode = 'create' | 'replace';

export interface RulebookImportPreview {
  /** Original file name (client-side only, used for UI). */
  filename: string;
  /** Parsed JSON, kept as `unknown` because schema varies per kind. */
  parsed: unknown;
  kind: string;
  profession: string;
  version: string;
  sectionsCount: number;
  rulesCount: number;
  /** Original text — submitted verbatim to the import endpoint. */
  rawJson: string;
  error?: string | null;
}

// ── Generation job runner ───────────────────────────────────────────────

export type GenerationJobState =
  | 'pending'
  | 'queued'
  | 'generating'
  | 'completed'
  | 'failed'
  | string;

export interface GenerationJobSummary {
  jobId: string;
  requestedBy?: string | null;
  examTypeCode: string;
  subtestCode: string;
  taskTypeId?: string | null;
  professionId?: string | null;
  difficulty: string;
  requestedCount: number;
  generatedCount: number;
  state: GenerationJobState;
  errorMessage?: string | null;
  createdAt: string;
  completedAt?: string | null;
}

export interface GenerationJobListResponse {
  total?: number;
  items?: GenerationJobSummary[];
}

export interface QueueGenerationInput {
  examTypeCode: string;
  subtestCode: string;
  taskTypeId?: string;
  professionId?: string;
  difficulty?: string;
  count: number;
  customInstructions?: string;
}
