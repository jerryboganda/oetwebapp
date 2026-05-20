// Stub types for Bulk Import admin surface (implementation pending).
// These placeholders satisfy tsc until the full feature lands.

export interface BulkImportApprovalInput {
  sessionId: string;
  approved: boolean;
  notes?: string;
}

export interface BulkImportCommitResult {
  sessionId: string;
  papersCreated: number;
  papersUpdated: number;
  assetsUploaded: number;
  errors: string[];
}

export interface BulkImportSessionResponse {
  id: string;
  status: 'staged' | 'approved' | 'committed' | 'failed';
  manifest: Record<string, unknown>;
  createdAt: string;
  committedAt?: string;
}

export interface GenerationJobListResponse {
  jobs: GenerationJobSummary[];
  totalCount: number;
  page: number;
  pageSize: number;
}

export interface GenerationJobSummary {
  id: string;
  type: string;
  status: 'queued' | 'running' | 'completed' | 'failed';
  progress: number;
  createdAt: string;
  completedAt?: string;
  error?: string;
}

export interface QueueGenerationInput {
  type: string;
  paperId?: string;
  parameters?: Record<string, unknown>;
}
