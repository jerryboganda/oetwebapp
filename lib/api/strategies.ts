/**
 * Writing admin options, rule-violation analytics, strategy guides —
 * extracted from `lib/api.ts`. Re-exported there, so `@/lib/api`
 * imports keep working.
 */
import { apiRequest } from './client';
import type {
  StrategyGuideAdminItem,
  StrategyGuideBookmarkUpdateResponse,
  StrategyGuideDetail,
  StrategyGuideLibrary,
  StrategyGuideProgressUpdateResponse,
  StrategyGuidePublishResult,
  StrategyGuidePublishValidation,
  StrategyGuideUpsertPayload,
} from '../types/strategies';

export interface AdminWritingOptions {
  aiGradingEnabled: boolean;
  aiCoachEnabled: boolean;
  killSwitchReason: string | null;
  freeTierEnabled: boolean;
  freeTierLimit: number;
  freeTierWindowDays: number;
  updatedAt: string | null;
  updatedByAdminId: string | null;
}

export async function adminGetWritingOptions(): Promise<AdminWritingOptions> {
  return apiRequest<AdminWritingOptions>('/v1/admin/writing/options');
}

export async function adminUpdateWritingOptions(
  input: Omit<AdminWritingOptions, 'updatedAt' | 'updatedByAdminId'>,
): Promise<AdminWritingOptions> {
  return apiRequest<AdminWritingOptions>('/v1/admin/writing/options', {
    method: 'PUT',
    body: JSON.stringify(input),
  });
}

export interface AdminWritingRuleViolationSummary {
  totalViolations: number;
  distinctRules: number;
  distinctAttempts: number;
  distinctLearners: number;
  ruleEngineCount: number;
  aiCount: number;
}

export interface AdminWritingProfessionCount {
  profession: string;
  count: number;
}

export interface AdminWritingLetterTypeCount {
  letterType: string;
  count: number;
}

export interface AdminWritingRuleViolationGroup {
  ruleId: string;
  totalCount: number;
  distinctAttempts: number;
  distinctLearners: number;
  criticalCount: number;
  majorCount: number;
  minorCount: number;
  infoCount: number;
  professions: AdminWritingProfessionCount[];
}

export interface AdminWritingRuleViolationDashboard {
  generatedAt: string;
  windowDays: number;
  professionFilter: string | null;
  summary: AdminWritingRuleViolationSummary;
  topRules: AdminWritingRuleViolationGroup[];
  professionBreakdown: AdminWritingProfessionCount[];
  letterTypeBreakdown: AdminWritingLetterTypeCount[];
}

export interface AdminWritingRuleViolationRow {
  id: string;
  ruleId: string;
  severity: string;
  source: string;
  message: string;
  quote: string | null;
  generatedAt: string;
}

export interface AdminWritingAttemptViolations {
  attemptId: string;
  evaluationId: string | null;
  userId: string | null;
  profession: string | null;
  letterType: string | null;
  generatedAt: string | null;
  items: AdminWritingRuleViolationRow[];
}

export async function adminGetWritingRuleViolationDashboard(opts?: {
  days?: number;
  profession?: string;
}): Promise<AdminWritingRuleViolationDashboard> {
  const params = new URLSearchParams();
  if (opts?.days != null) params.set('days', String(opts.days));
  if (opts?.profession) params.set('profession', opts.profession);
  const qs = params.toString();
  return apiRequest<AdminWritingRuleViolationDashboard>(
    `/v1/admin/writing/analytics/rule-violations${qs ? `?${qs}` : ''}`,
  );
}

export async function adminGetWritingAttemptViolations(
  attemptId: string,
): Promise<AdminWritingAttemptViolations> {
  return apiRequest<AdminWritingAttemptViolations>(
    `/v1/admin/writing/analytics/rule-violations/${encodeURIComponent(attemptId)}`,
  );
}

export async function adminFetchGrammarPublishGate(lessonId: string) {
  return apiRequest<{ canPublish: boolean; errors: string[] }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish-gate`);
}

export async function adminPublishGrammarLessonV2(lessonId: string) {
  return apiRequest<{ published: boolean; status: string; errors: string[] }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/publish`, { method: 'POST' });
}

export async function adminUnpublishGrammarLessonV2(lessonId: string) {
  return apiRequest<{ id: string; status: string }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/unpublish`, { method: 'POST' });
}

export async function adminFetchGrammarStats(lessonId: string) {
  return apiRequest<{
    lessonId: string;
    attempts: number;
    uniqueLearners: number;
    averageMasteryScore: number;
    reviewItemsCreated: number;
  }>(`/v1/admin/grammar/lessons/${encodeURIComponent(lessonId)}/stats`);
}

export async function fetchStrategyGuides(params?: { examTypeCode?: string; subtestCode?: string; category?: string; q?: string; recommended?: boolean }) {
  const p = new URLSearchParams();
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.subtestCode) p.set('subtestCode', params.subtestCode);
  if (params?.category) p.set('category', params.category);
  if (params?.q) p.set('q', params.q);
  if (params?.recommended) p.set('recommended', 'true');
  return apiRequest<StrategyGuideLibrary>(`/v1/strategies?${p}`);
}

export async function fetchStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuideDetail>(`/v1/strategies/${encodeURIComponent(guideId)}`);
}

export async function updateStrategyGuideProgress(guideId: string, readPercent: number) {
  return apiRequest<StrategyGuideProgressUpdateResponse>(`/v1/strategies/${encodeURIComponent(guideId)}/progress`, {
    method: 'POST',
    body: JSON.stringify({ readPercent }),
  });
}

export async function setStrategyGuideBookmark(guideId: string, bookmarked: boolean) {
  return apiRequest<StrategyGuideBookmarkUpdateResponse>(`/v1/strategies/${encodeURIComponent(guideId)}/bookmark`, {
    method: 'POST',
    body: JSON.stringify({ bookmarked }),
  });
}

export async function adminListStrategyGuides(params?: { status?: string; examTypeCode?: string; search?: string }) {
  const p = new URLSearchParams();
  if (params?.status) p.set('status', params.status);
  if (params?.examTypeCode) p.set('examTypeCode', params.examTypeCode);
  if (params?.search) p.set('search', params.search);
  return apiRequest<StrategyGuideAdminItem[]>(`/v1/admin/strategies?${p}`);
}

export async function adminGetStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuideAdminItem>(`/v1/admin/strategies/${encodeURIComponent(guideId)}`);
}

export async function adminCreateStrategyGuide(payload: StrategyGuideUpsertPayload) {
  return apiRequest<StrategyGuideAdminItem>('/v1/admin/strategies', {
    method: 'POST',
    body: JSON.stringify(payload),
  });
}

export async function adminUpdateStrategyGuide(guideId: string, payload: StrategyGuideUpsertPayload) {
  return apiRequest<StrategyGuideAdminItem>(`/v1/admin/strategies/${encodeURIComponent(guideId)}`, {
    method: 'PUT',
    body: JSON.stringify(payload),
  });
}

export async function adminValidateStrategyGuidePublish(guideId: string) {
  return apiRequest<StrategyGuidePublishValidation>(`/v1/admin/strategies/${encodeURIComponent(guideId)}/publish-gate`);
}

export async function adminPublishStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuidePublishResult>(`/v1/admin/strategies/${encodeURIComponent(guideId)}/publish`, {
    method: 'POST',
  });
}

export async function adminArchiveStrategyGuide(guideId: string) {
  return apiRequest<StrategyGuideAdminItem>(`/v1/admin/strategies/${encodeURIComponent(guideId)}/archive`, {
    method: 'POST',
  });
}

/** Permanently deletes an archived strategy guide + all learner progress. system_admin only. */
export async function adminForceDeleteStrategyGuide(guideId: string) {
  return apiRequest(`/v1/admin/strategies/${encodeURIComponent(guideId)}/force-delete`, {
    method: 'POST',
  });
}
