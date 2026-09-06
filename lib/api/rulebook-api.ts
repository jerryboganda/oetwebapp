import type { AiGroundingContext, LintFinding } from '../rulebook';

export interface WritingLintResponse {
  findings: LintFinding[];
  totals: { critical: number; major: number; minor: number; info: number };
}

export interface SpeakingAuditResponse {
  findings: LintFinding[];
}

export interface AiCompletionResponse {
  completion: string;
  rulebookVersion: string;
  appliedRuleIds: string[];
  metadata: {
    rulebookVersion: string;
    rulebookKind: 'writing' | 'speaking';
    profession: string;
    scoringPassMark: number;
    scoringGrade: 'B' | 'C+';
    appliedRulesCount: number;
  };
  promptHeadSnippet?: string;
}
import { apiRequest, isApiError } from './client';
import { type Rulebook, SpeakingAuditInput, WritingLintInput } from '../rulebook';

/**
 * Rulebook fetchers, writing weaknesses, dual assessment, grounded-AI lint/audit/complete.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchWritingRulebook(profession = 'medicine'): Promise<Rulebook> {
  return apiRequest<Rulebook>(`/v1/rulebooks/writing/${profession}`);
}

export async function fetchSpeakingRulebook(profession = 'medicine'): Promise<Rulebook> {
  return apiRequest<Rulebook>(`/v1/rulebooks/speaking/${profession}`);
}

export async function fetchRulebookRule(
  kind: 'writing' | 'speaking',
  profession: string,
  ruleId: string,
): Promise<Record<string, unknown>> {
  return apiRequest<Record<string, unknown>>(`/v1/rulebooks/${kind}/${profession}/rule/${encodeURIComponent(ruleId)}`);
}

export async function fetchRulebookAssessment(kind: 'writing' | 'speaking'): Promise<Record<string, unknown>> {
  return apiRequest<Record<string, unknown>>(`/v1/rulebooks/assessment/${kind}`);
}

// ═══════════════════ WRITING WEAKNESS ANALYTICS (spec §14) ═══════════════════

export interface WritingWeaknessTagRow {
  tag: string;
  label: string;
  count: number;
  share: number;
}

export interface WritingWeaknessCriterionRow {
  criterion: string;
  label: string;
  count: number;
  share: number;
}

export interface WritingWeaknessTrendBucket {
  date: string;
  count: number;
}

export interface WritingGradeTrendPoint {
  date: string;
  gradeRange: string;
  scoreRange: string;
}

export interface WritingPurposeTrendPoint {
  date: string;
  score: number;
  maxScore: number;
}

export interface WritingWeaknessSummary {
  totalObservations: number;
  topTags: WritingWeaknessTagRow[];
  byCriterion: WritingWeaknessCriterionRow[];
  trend: WritingWeaknessTrendBucket[];
  firstSeenAt: string;
  lastSeenAt: string;
  gradeTrend: WritingGradeTrendPoint[];
  purposeTrend: WritingPurposeTrendPoint[];
}

export async function fetchWritingWeaknesses(days = 14): Promise<WritingWeaknessSummary> {
  const clamped = Math.max(7, Math.min(90, Math.floor(days) || 14));
  return apiRequest<WritingWeaknessSummary>(`/v1/writing/analytics/weaknesses?days=${clamped}`);
}

// ═══════════════ EXPERT — ASSIGNED-TO-ME QUEUE (spec §4 Phase 4) ═══════════════

export interface ExpertAssignedItem {
  reviewRequestId: string;
  attemptId: string;
  subtestCode: 'writing';
  professionId: string | null;
  taskTitle: string;
  learnerDisplayName: string;
  letterType: string | null;
  assignedAt: string;
  slaDueAt: string;
  slaState: 'on_track' | 'at_risk' | 'overdue';
  turnaroundOption: string;
  reviewerCompensation: number;
  claimState: string;
}

export async function fetchExpertAssignedReviews(): Promise<ExpertAssignedItem[]> {
  return apiRequest<ExpertAssignedItem[]>('/v1/expert/queue/assigned-to-me');
}

// ═══════════════ WRITING DUAL AI + TUTOR ASSESSMENT (spec §12.E) ═══════════════
//
// Dual-assessment uses its own short-form criterion codes (purpose / content /
// conciseness / genre / organization / language) to match the backend
// payload at /v1/writing/evaluations/{id}/dual-assessment. These are
// intentionally distinct from `WritingCriterionCode` in lib/scoring.ts,
// which uses the long form (conciseness_clarity / genre_style /
// organisation_layout / language) for the rulebook scoring path.

export type WritingDualCriterionCode =
  | 'purpose' | 'content' | 'conciseness'
  | 'genre' | 'organization' | 'language';

export interface WritingCriterionScore {
  score: number;
  maxScore: number;
  rationale?: string | null;
  evidenceQuotes?: string[] | null;
}

export interface WritingAiTrack {
  assessmentId: string;
  generatedAt: string;
  confidenceBand: string;
  scoreRange: string;
  gradeRange: string | null;
  criterionScores: Record<WritingDualCriterionCode, WritingCriterionScore>;
  isAdvisory: boolean;
}

export interface WritingTutorTrack {
  assessmentId: string;
  tutorId: string;
  tutorName: string;
  criterionScores: Record<WritingDualCriterionCode, WritingCriterionScore>;
  overallFeedback?: string | null;
  isFinal: boolean;
  submittedAt: string;
}

export interface WritingDualDivergence {
  perCriterion: Record<WritingDualCriterionCode, number>;
  scaledDelta: number;
  agreementBand: 'close' | 'moderate' | 'wide';
}

export interface WritingDualAssessment {
  evaluationId: string;
  attemptId: string;
  subtestCode: 'writing';
  ai: WritingAiTrack;
  tutor: WritingTutorTrack | null;
  divergence: WritingDualDivergence | null;
}

export async function fetchWritingDualAssessment(evaluationId: string): Promise<WritingDualAssessment | null> {
  try {
    return await apiRequest<WritingDualAssessment>(`/v1/writing/evaluations/${encodeURIComponent(evaluationId)}/dual-assessment`);
  } catch (error) {
    if (isApiError(error) && error.status === 404) return null;
    throw error;
  }
}

export async function lintWritingViaApi(input: WritingLintInput): Promise<WritingLintResponse> {
  return apiRequest<WritingLintResponse>('/v1/writing/lint', {
    method: 'POST',
    body: JSON.stringify({
      letterText: input.letterText,
      attemptId: input.attemptId,
      contentId: input.contentId,
      letterType: input.letterType,
      recipientSpecialty: input.recipientSpecialty,
      recipientName: input.recipientName,
      patientAge: input.patientAge,
      patientIsMinor: input.patientIsMinor,
      caseNotesMarkers: input.caseNotesMarkers,
      profession: input.profession ?? 'medicine',
    }),
  });
}

export async function auditSpeakingViaApi(input: SpeakingAuditInput): Promise<SpeakingAuditResponse> {
  return apiRequest<SpeakingAuditResponse>('/v1/speaking/audit', {
    method: 'POST',
    body: JSON.stringify({
      transcript: input.transcript,
      cardType: input.cardType,
      profession: input.profession ?? 'medicine',
      silenceAfterDiagnosisMs: input.silenceAfterDiagnosisMs,
    }),
  });
}

export async function completeGroundedAi(
  context: AiGroundingContext,
  userInput: string,
  provider = '',
  model = '',
): Promise<AiCompletionResponse> {
  return apiRequest<AiCompletionResponse>('/v1/ai/complete', {
    method: 'POST',
    body: JSON.stringify({
      kind: context.kind,
      profession: context.profession,
      task: context.task,
      letterType: context.letterType,
      cardType: context.cardType,
      candidateCountry: context.candidateCountry,
      userInput,
      provider,
      model,
    }),
  });
}
