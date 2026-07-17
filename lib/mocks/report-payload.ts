// MockReportPayloadV1 — formal contract mirroring backend MockReportPayloadV1 record.
// Source of truth: backend/src/OetWithDrHesham.Api/Services/Mocks/Results/MockReportPayloadV1.cs
//
// Consumers branch on `payloadSchemaVersion`. Reports written before the schema was
// formalised have version === '' or undefined and fall back to the legacy MockReport
// interface in lib/mock-data.ts. New reports always carry version 'v1'.
//
// Optional fields are populated incrementally:
//   - weaknessNarrative          → Phase 1.5 renderer + aggregator population
//   - passPrediction             → Phase 3 calibration
//   - trend                      → Phase 3 MockReadinessTrendService
//   - perQuestionTiming          → Phase 1 supporting service
//   - teacherFeedbackFragments   → populated as expert reviews complete

export const MOCK_REPORT_PAYLOAD_SCHEMA_VERSION = 'v1' as const;
export type MockReportPayloadSchemaVersion = typeof MOCK_REPORT_PAYLOAD_SCHEMA_VERSION;

export interface MockReportPayloadV1 {
  payloadSchemaVersion: MockReportPayloadSchemaVersion;
  id: string;
  reportId: string;
  mockAttemptId: string;
  title: string;
  date: string;
  profession?: string | null;
  targetCountry?: string | null;
  deliveryMode?: string | null;
  strictness?: string | null;
  releasePolicy?: string | null;
  overallScore: string;
  overallGrade?: string | null;
  summary: string;
  subTests: MockReportSubTestV1[];
  weakestCriterion: MockReportWeakestCriterionV1;
  reviewSummary: MockReportReviewSummaryV1;
  perModuleReadiness: MockReportPerModuleReadinessV1[];
  partScores: MockReportPartScoreV1[];
  timingAnalysis: MockReportTimingAnalysisV1[];
  errorCategories: MockReportErrorCategoryV1[];
  teacherReviewState: MockReportReviewSummaryV1;
  bookingAdvice: MockReportBookingAdviceV1;
  retakeAdvice: MockReportRetakeAdviceV1;
  proctoringSummary: MockReportProctoringSummaryV1;
  remediationPlan: MockReportRemediationActionV1[];
  priorComparison: MockReportPriorComparisonV1;
  weaknessNarrative?: MockReportWeaknessNarrativeV1 | null;
  passPrediction?: MockReportPassPredictionV1 | null;
  trend?: MockReportTrendV1 | null;
  perQuestionTiming?: MockReportPerQuestionTimingV1[] | null;
  teacherFeedbackFragments?: MockReportTeacherFeedbackFragmentV1[] | null;
}

export interface MockReportSubTestV1 {
  id: string;
  name: string;
  score: string;
  rawScore: string;
  scaledScore?: number | null;
  grade?: string | null;
  state: string;
  evidenceSource: string;
  contentPaperTitle?: string | null;
  reviewRequestId?: string | null;
  reviewState?: string | null;
}

export interface MockReportWeakestCriterionV1 {
  subtest: string;
  criterion: string;
  description: string;
}

export interface MockReportReviewSummaryV1 {
  queued: number;
  inReview: number;
  completed: number;
  pending: number;
}

export interface MockReportPerModuleReadinessV1 {
  subtest: string;
  scaledScore?: number | null;
  grade?: string | null;
  rag: string;
  message: string;
  passThreshold?: number | null;
}

export interface MockReportPartScoreV1 {
  subtest: string;
  rawScore?: string | null;
  scaledScore?: number | null;
  grade?: string | null;
  state?: string | null;
  evidenceSource?: string | null;
}

export interface MockReportTimingAnalysisV1 {
  sectionId: string;
  subtest: string;
  startedAt?: string | null;
  submittedAt?: string | null;
  completedAt?: string | null;
  deadlineAt?: string | null;
  secondsUsed?: number | null;
}

export interface MockReportErrorCategoryV1 {
  category: string;
  subtest: string;
  severity: string;
  description: string;
}

export interface MockReportBookingAdviceV1 {
  status: string;
  message: string;
  route?: string | null;
  score?: number | null;
}

export interface MockReportRetakeAdviceV1 {
  recommendedWindowDays: number;
  nextMockType: string;
  subtest: string;
  message: string;
}

export interface MockReportProctoringKindV1 {
  kind: string;
  count: number;
}

export interface MockReportProctoringSummaryV1 {
  totalEvents: number;
  advisoryOnly: boolean;
  criticalEvents: number;
  warningEvents: number;
  byKind: MockReportProctoringKindV1[];
  message: string;
}

export interface MockReportRemediationActionV1 {
  day: string;
  title: string;
  description: string;
  route: string;
}

export interface MockReportPriorComparisonV1 {
  exists: boolean;
  priorMockName: string;
  overallTrend: 'up' | 'down' | 'flat';
  details: string;
}

export interface MockReportWeaknessTagV1 {
  tag: string;
  subtest: string;
  description: string;
  drillId?: string | null;
  drillRouteHref?: string | null;
}

export interface MockReportWeaknessNarrativeV1 {
  headline: string;
  body: string;
  tags: MockReportWeaknessTagV1[];
}

export interface MockReportPassPredictionV1 {
  confidenceBand: 'high' | 'medium' | 'low' | string;
  verdict: string;
  rationale: string;
}

export interface MockReportTrendV1 {
  attemptsConsidered: number;
  overallTrend: 'up' | 'down' | 'flat' | string;
  consistentGreen: boolean;
  message: string;
}

export interface MockReportPerQuestionTimingV1 {
  sectionId: string;
  subtest: string;
  itemId: string;
  secondsSpent?: number | null;
  correct?: boolean | null;
}

export interface MockReportTeacherFeedbackFragmentV1 {
  subtest: string;
  reviewRequestId: string;
  criterion: string;
  comment: string;
  anchorRef?: string | null;
}

// Type guard for consumers to safely opt into the typed contract. Returns true
// when the payload carries the V1 schema marker. Pre-V1 reports return false
// and consumers should fall back to the legacy MockReport interface.
export function isMockReportPayloadV1(value: unknown): value is MockReportPayloadV1 {
  if (!value || typeof value !== 'object') return false;
  const candidate = value as { payloadSchemaVersion?: unknown };
  return candidate.payloadSchemaVersion === MOCK_REPORT_PAYLOAD_SCHEMA_VERSION;
}
