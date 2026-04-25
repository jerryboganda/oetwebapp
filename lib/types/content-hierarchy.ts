// ── Content Hierarchy Types ──

export type ContentStatus = 'Draft' | 'InReview' | 'EditorReview' | 'PublisherApproval' | 'Published' | 'Rejected' | 'Archived';
export type ProgramType = 'full_course' | 'crash_course' | 'foundation' | 'combo';
export type PackageType = 'full_course' | 'crash_course' | 'combo' | 'foundation' | 'standalone';
export type LessonType = 'video_lesson' | 'strategy_guide' | 'session_replay' | 'reading_material' | 'practice_task';
export type ReferenceType = 'pdf' | 'vocab_list' | 'grammar_sheet' | 'external_link';
export type SourceProvenance = 'original' | 'official_sample' | 'recall' | 'benchmark' | 'contributed';
export type RightsStatus = 'owned' | 'licensed' | 'recall_unverified' | 'official_attribution_required';
export type FreshnessConfidence = 'current' | 'likely_current' | 'aging' | 'superseded' | 'archived';
export type MediaAssetStatus = 'Processing' | 'Ready' | 'Failed';
export type PreviewType = 'webinar_replay' | 'sample_lesson' | 'sample_task';
export type MarketingAssetType = 'package_graphic' | 'promo_video' | 'poster' | 'schedule';
export type FoundationResourceType = 'basic_english' | 'grammar_foundation' | 'medical_vocabulary' | 'common_words';

// ── Program → Track → Module → Lesson ──

export interface ContentProgram {
  id: string;
  code: string;
  title: string;
  description?: string;
  professionId?: string;
  instructionLanguage: string;
  programType: ProgramType;
  status: ContentStatus;
  thumbnailUrl?: string;
  displayOrder: number;
  estimatedDurationMinutes: number;
  examFamilyCode: string;
  examTypeCode: string;
  createdBy?: string;
  createdAt: string;
  updatedAt: string;
  publishedAt?: string;
  archivedAt?: string;
}

export interface ContentTrack {
  id: string;
  programId: string;
  subtestCode?: string;
  title: string;
  description?: string;
  displayOrder: number;
  status: ContentStatus;
}

export interface ContentModule {
  id: string;
  trackId: string;
  title: string;
  description?: string;
  displayOrder: number;
  estimatedDurationMinutes: number;
  prerequisiteModuleId?: string;
  status: ContentStatus;
}

export interface ContentLesson {
  id: string;
  moduleId: string;
  contentItemId?: string;
  title: string;
  lessonType: LessonType;
  mediaAssetId?: string;
  displayOrder: number;
  status: ContentStatus;
}

// ── Packages & Entitlements ──

export interface ContentPackage {
  id: string;
  code: string;
  title: string;
  description?: string;
  packageType: PackageType;
  professionId?: string;
  instructionLanguage: string;
  billingPlanId?: string;
  status: ContentStatus;
  thumbnailUrl?: string;
  comparisonFeatures: string[];
  displayOrder: number;
  examFamilyCode: string;
  examTypeCode: string;
  createdAt: string;
  updatedAt: string;
  publishedAt?: string;
}

// ── Media Assets ──

export interface MediaAsset {
  id: string;
  originalFilename: string;
  mimeType: string;
  format: string;
  sizeBytes: number;
  durationSeconds?: number;
  storagePath: string;
  thumbnailPath?: string;
  captionPath?: string;
  transcriptPath?: string;
  status: MediaAssetStatus;
  uploadedBy?: string;
  uploadedAt: string;
  processedAt?: string;
  url?: string;
}

// ── Testimonials & Marketing ──

export interface FreePreviewAsset {
  id: string;
  title: string;
  previewType: PreviewType;
  contentItemId?: string;
  mediaAssetId?: string;
  conversionCtaText?: string;
  targetPackageId?: string;
  status: ContentStatus;
  displayOrder: number;
  createdAt: string;
}

// ── Foundation / Remediation ──

// ── Cohort Overlay ──

// ── Import Batch ──

// ── Extended ContentItem fields (additions to existing types) ──

// ── Paginated response wrapper ──

export interface PaginatedResponse<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

// ── Deduplication ──

export interface DuplicateGroupSummary {
  duplicateGroupId: string;
  count: number;
  items: DuplicateGroupItem[];
}

export interface DuplicateGroupItem {
  id: string;
  title: string;
  subtestCode: string;
  professionId?: string;
  scenarioType?: string;
  instructionLanguage: string;
  sourceProvenance: SourceProvenance;
  freshnessConfidence: FreshnessConfidence;
  qualityScore: number;
  status: ContentStatus;
  supersededById?: string;
  canonicalSourcePath?: string;
  createdAt: string;
  updatedAt: string;
}

// ── Content Browser (access-aware) ──

export interface BrowsableProgramItem {
  id: string;
  code: string;
  title: string;
  description?: string;
  professionId?: string;
  instructionLanguage: string;
  programType: ProgramType;
  thumbnailUrl?: string;
  displayOrder: number;
  estimatedDurationMinutes: number;
  isAccessible: boolean;
  trackCount: number;
}

// ── Phase 6: Mock / Diagnostic / Readiness ──

// ── Phase 7: Bulk Import ──

export interface ImportResult {
  batchId: string;
  created: number;
  failed: number;
  errors: ImportError[];
  createdIds: string[];
}

export interface ImportError {
  rowIndex: number;
  message: string;
}

export interface ContentInventoryItem {
  id: string;
  title: string;
  contentType: string;
  subtestCode: string;
  professionId?: string;
  difficulty: string;
  instructionLanguage: string;
  sourceProvenance: string;
  freshnessConfidence: string;
  qaStatus?: string;
  qualityScore: number;
  status: string;
  importBatchId?: string;
  duplicateGroupId?: string;
  isPreviewEligible: boolean;
  isMockEligible: boolean;
  isDiagnosticEligible: boolean;
  createdAt: string;
  updatedAt: string;
}

// ── Phase 9: Search ──

// ── Phase 11: Media Normalization ──

export interface MediaAuditResult {
  totalAssets: number;
  readyCount: number;
  processingCount: number;
  failedCount: number;
  missingThumbnails: string[];
  missingTranscripts: string[];
  missingCaptions: string[];
}
