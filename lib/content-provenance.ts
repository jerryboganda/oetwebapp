// ============================================================================
// Content Provenance & QA Tracking Types
// ============================================================================
//
// Content operations maturity requires structured authoring provenance,
// stale-content review, rubric coverage visibility, and performance analytics.
// These types are consumed by admin content surfaces and backend contracts.
//
// Document: docs/product-strategy/06_feature_strategy_and_blueprint.md §Slice F
// ============================================================================

// ---------------------------------------------------------------------------
// Provenance
// ---------------------------------------------------------------------------

/** Source of a content item or revision. */
export type ContentSource =
  | 'manual_expert'          // Written by a subject-matter expert
  | 'ai_draft'               // AI-generated first draft
  | 'ai_draft_expert_review' // AI draft + expert review and edit
  | 'import_bulk'            // Bulk import (ZIP, CSV, etc.)
  | 'import_oet_official'    // Derived from official OET materials
  | 'legacy_migration';      // Migrated from a legacy system

/** Lifecycle stage of a content item. */
export type ContentLifecycleStage =
  | 'draft'
  | 'under_review'
  | 'approved'
  | 'published'
  | 'deprecated'
  | 'archived';

/** Structured provenance record for a content item. */
export interface ContentProvenanceRecord {
  id: string;
  contentItemId: string;
  revisionId: string;
  source: ContentSource;
  authorId: string;
  authorName: string;
  authorRole: 'expert' | 'admin' | 'ai_system' | 'importer';
  createdAt: string;
  reviewedById?: string | null;
  reviewedByName?: string | null;
  reviewedAt?: string | null;
  reviewNotes?: string | null;
  aiModelId?: string | null;
  aiPromptVersion?: string | null;
}

// ---------------------------------------------------------------------------
// Stale-content review
// ---------------------------------------------------------------------------

/** Staleness assessment for a content item. */
export interface ContentStalenessAssessment {
  contentItemId: string;
  title: string;
  lastPublishedAt: string;
  daysSinceLastEdit: number;
  daysSinceLastUsage: number | null;
  usageCountLast90Days: number;
  isStale: boolean;
  stalenessReason: string;
  recommendedAction: 'no_action' | 'minor_refresh' | 'major_revision' | 'archive';
  rubricCoveragePercent: number;
  missingRubricCriteria: string[];
}

// ---------------------------------------------------------------------------
// Rubric coverage
// ---------------------------------------------------------------------------

/** Coverage of a rubric criterion within a content item. */
export interface RubricCriterionCoverage {
  criterionCode: string;
  criterionName: string;
  maxScore: number;
  covered: boolean;
  coverageDepth: 'none' | 'implicit' | 'explicit' | 'fully_illustrated';
  contentItemIds: string[];
}

/** Rubric coverage report for an exam family + subtest. */
export interface RubricCoverageReport {
  examFamilyCode: string;
  subtestCode: string;
  professionId: string | null;
  criteria: RubricCriterionCoverage[];
  overallCoveragePercent: number;
  gaps: string[]; // criterionCodes not covered
  excess: string[]; // criteria over-covered (optional audit)
}

// ---------------------------------------------------------------------------
// Content performance analytics
// ---------------------------------------------------------------------------

/** Performance metrics for a single content item. */
export interface ContentPerformanceMetrics {
  contentItemId: string;
  title: string;
  examFamilyCode: string;
  subtestCode: string;
  totalAttempts: number;
  averageScore: number | null;
  averageTimeSeconds: number | null;
  completionRate: number; // 0–1
  dropOffPoint?: string | null; // e.g. "question_3", "transcript_reveal"
  aiEvaluationAccuracy?: number | null; // correlation with expert review
  learnerFeedbackScore?: number | null; // 1–5 star rating
}

/** Aggregated content performance for an admin dashboard. */
export interface ContentPerformanceSummary {
  totalItems: number;
  itemsWithAttempts: number;
  itemsNeedingAttention: number; // low completion or high drop-off
  averageCompletionRate: number;
  topPerformers: ContentPerformanceMetrics[];
  underPerformers: ContentPerformanceMetrics[];
}
