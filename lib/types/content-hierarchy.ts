// ── Content Hierarchy Types ──

export type ContentStatus = 'Draft' | 'Published' | 'Archived';
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

export interface ContentReference {
  id: string;
  moduleId: string;
  title: string;
  referenceType: ReferenceType;
  mediaAssetId?: string;
  externalUrl?: string;
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

export interface PackageContentRule {
  id: string;
  packageId: string;
  ruleType: string;
  targetId: string;
  targetType: 'program' | 'track' | 'module' | 'content_item';
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
  uploadedAt: string;
  processedAt?: string;
}

// ── Testimonials & Marketing ──

export interface TestimonialAsset {
  id: string;
  learnerDisplayName?: string;
  profession?: string;
  testDate?: string;
  overallGrade?: string;
  subtestGrades?: Record<string, string>;
  testimonialText?: string;
  mediaAssetId?: string;
  consentStatus: 'pending' | 'granted' | 'revoked';
  displayApproved: boolean;
  displayOrder: number;
  createdAt: string;
}

export interface MarketingAsset {
  id: string;
  title: string;
  assetType: MarketingAssetType;
  mediaAssetId?: string;
  packageId?: string;
  status: ContentStatus;
  createdAt: string;
}

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

export interface FoundationResource {
  id: string;
  title: string;
  resourceType: FoundationResourceType;
  contentBody?: string;
  mediaAssetId?: string;
  difficulty: string;
  prerequisiteResourceId?: string;
  displayOrder: number;
  status: ContentStatus;
  createdAt: string;
  updatedAt: string;
}

// ── Cohort Overlay ──

export interface ContentCohortOverlay {
  id: string;
  programId: string;
  cohortCode: string;
  cohortTitle: string;
  startDate?: string;
  endDate?: string;
  releaseSchedule: Array<{ moduleId: string; releaseDate: string }>;
  status: ContentStatus;
  createdAt: string;
  updatedAt: string;
}

// ── Import Batch ──

export interface ContentImportBatch {
  id: string;
  title: string;
  status: 'pending' | 'processing' | 'completed' | 'failed' | 'rolled_back';
  totalItems: number;
  processedItems: number;
  failedItems: number;
  createdBy?: string;
  errorLog?: unknown[];
  createdAt: string;
  completedAt?: string;
}

// ── Extended ContentItem fields (additions to existing types) ──

export interface ContentItemMigrationFields {
  instructionLanguage: string;
  contentLanguage: string;
  professionIds: string[];
  packageEligibility: string[];
  cohortRelevance?: string;
  sourceProvenance: SourceProvenance;
  rightsStatus: RightsStatus;
  freshnessConfidence: FreshnessConfidence;
  supersededById?: string;
  duplicateGroupId?: string;
  mediaManifest: Array<{
    assetId: string;
    type: string;
    url: string;
    format: string;
    size: number;
    duration?: number;
    thumbnailUrl?: string;
  }>;
  canonicalSourcePath?: string;
  importBatchId?: string;
  isPreviewEligible: boolean;
  isDiagnosticEligible: boolean;
  isMockEligible: boolean;
  qualityScore: number;
}

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

export interface DedupScanResult {
  groupsFound: number;
  itemsTagged: number;
}

// ── Content Browser (access-aware) ──

export interface BrowsableContentItem {
  contentId: string;
  contentType: string;
  subtest: string;
  professionId?: string;
  title: string;
  difficulty: string;
  estimatedDurationMinutes: number;
  scenarioType?: string;
  instructionLanguage: string;
  sourceProvenance: SourceProvenance;
  qualityScore: number;
  isAccessible: boolean;
  isPreview: boolean;
  requiresUpgrade: boolean;
  noSubscription: boolean;
}

export interface ContentAccessCheck {
  isAccessible: boolean;
  reason: 'free_preview' | 'subscription' | 'locked' | 'not_found';
  requiredPackageId?: string;
}

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

export interface MockExamTemplate {
  id: string;
  title: string;
  professionId?: string;
  language: string;
  sections: MockExamSection[];
  totalTimeLimitMinutes: number;
  createdAt: string;
}

export interface MockExamSection {
  subtestCode: string;
  contentItemIds: string[];
  timeLimitMinutes: number;
}

export interface DiagnosticTemplate {
  id: string;
  professionId?: string;
  sections: DiagnosticSection[];
  estimatedDurationMinutes: number;
  createdAt: string;
}

export interface DiagnosticSection {
  subtestCode: string;
  contentItemIds: string[];
  calibrationConfidence: 'low' | 'medium' | 'high';
}

export interface ReadinessScore {
  userId: string;
  overallScore: number;
  overallConfidence: 'low' | 'medium' | 'high';
  subtestScores: Record<string, SubtestReadiness>;
  weakestSubtest: string;
  recommendation: string;
  calculatedAt: string;
}

export interface SubtestReadiness {
  subtestCode: string;
  score: number;
  attemptCount: number;
  averageScore: number;
  recentTrend: number;
  totalMinutesInvested: number;
  confidence: 'low' | 'medium' | 'high';
}

// ── Phase 7: Bulk Import ──

export interface ContentImportRow {
  rowIndex: number;
  title: string;
  subtestCode: string;
  contentType?: string;
  professionId?: string;
  difficulty?: string;
  estimatedDurationMinutes?: number;
  scenarioType?: string;
  detailJson?: string;
  modelAnswerJson?: string;
  instructionLanguage?: string;
  contentLanguage?: string;
  sourceProvenance?: string;
  rightsStatus?: string;
  freshnessConfidence?: string;
  canonicalSourcePath?: string;
  qualityScore?: number;
  criteriaFocusJson?: string;
  modeSupportJson?: string;
}

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

export interface ContentSearchQuery {
  text?: string;
  subtestCode?: string;
  professionId?: string;
  difficulty?: string;
  language?: string;
  provenance?: string;
  contentType?: string;
  minQuality?: number;
  mockEligibleOnly?: boolean;
  previewEligibleOnly?: boolean;
}

export interface SearchResultItem {
  id: string;
  title: string;
  subtestCode: string;
  contentType: string;
  professionId?: string;
  difficulty: string;
  difficultyRating: number;
  estimatedDurationMinutes: number;
  scenarioType?: string;
  instructionLanguage: string;
  sourceProvenance: string;
  qualityScore: number;
  isPreviewEligible: boolean;
  isMockEligible: boolean;
  isDiagnosticEligible: boolean;
  createdAt: string;
}

export interface SearchFacets {
  subtests: FacetCount[];
  difficulties: FacetCount[];
  professions: FacetCount[];
  languages: FacetCount[];
  provenances: FacetCount[];
  totalPublished: number;
}

export interface FacetCount {
  value: string;
  count: number;
}

export interface RecommendationResult {
  recommended: RecommendedItem[];
  weakestSubtest: string;
  quickAccess: {
    officialSamples: { id: string; title: string; subtestCode: string }[];
    recentRecalls: { id: string; title: string; subtestCode: string }[];
    freeWebinars: { id: string; title: string }[];
  };
}

export interface RecommendedItem {
  id: string;
  title: string;
  subtestCode: string;
  difficulty: string;
  professionId?: string;
  scenarioType?: string;
  estimatedDurationMinutes: number;
  qualityScore: number;
  reason: string;
}

// ── Phase 11: Media Normalization ──

export interface MediaAccessResult {
  isAccessible: boolean;
  url?: string;
  reason?: string;
}

export interface MediaNormalizationPlan {
  targetFormat: string;
  targetCodec?: string;
  shouldGenerateThumbnail: boolean;
  shouldExtractCaptions: boolean;
  shouldGenerateTranscript: boolean;
  qualityPresets: string[];
  notes: string;
}

export interface MediaAuditResult {
  totalAssets: number;
  readyCount: number;
  processingCount: number;
  failedCount: number;
  missingThumbnails: string[];
  missingTranscripts: string[];
  missingCaptions: string[];
}
