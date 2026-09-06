import { apiRequest, asArray, type ApiRecord } from './client';
import { mapReadinessResponse, parseScoreValue } from './mock-attempts';
import { type ProgressEvidenceSummary, ReadinessBlocker, ReadinessData, ReadinessForecast, ReadinessHistoryPoint, TrendPoint } from '../mock-data';

/**
 * Readiness, trends, completion, progress evidence + admin readiness views.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchReadiness(): Promise<ReadinessData> {
  const readiness = await apiRequest<ApiRecord>('/v1/readiness');
  return mapReadinessResponse(readiness);
}

export async function fetchReadinessHistory(weeks = 12): Promise<ReadinessHistoryPoint[]> {
  const rows = await apiRequest<ApiRecord[]>(`/v1/readiness/history?weeks=${weeks}`);
  return (Array.isArray(rows) ? rows : []).map((row) => ({
    weekStartDate: row.weekStartDate,
    overall: Number(row.overall ?? 0),
    writing: Number(row.writing ?? 0),
    speaking: Number(row.speaking ?? 0),
    reading: Number(row.reading ?? 0),
    listening: Number(row.listening ?? 0),
    vocabulary: Number(row.vocabulary ?? 0),
    risk: row.risk ?? 'Unknown',
    targetDateProbability: typeof row.targetDateProbability === 'number' ? row.targetDateProbability : null,
  }));
}

export async function fetchReadinessBlockers(): Promise<ReadinessBlocker[]> {
  const blockers = await apiRequest<ApiRecord[]>('/v1/readiness/blockers');
  return (Array.isArray(blockers) ? blockers : []).map((b) => ({
    id: b.id,
    title: b.title,
    description: b.description,
    actionLabel: typeof b.actionLabel === 'string' ? b.actionLabel : undefined,
    actionHref: typeof b.actionHref === 'string' ? b.actionHref : undefined,
    impactScore: typeof b.impactScore === 'number' ? b.impactScore : undefined,
    severity: (typeof b.severity === 'string' ? b.severity : undefined) as 'high' | 'medium' | 'low' | undefined,
  }));
}

export async function fetchReadinessForecast(hoursPerWeek?: number): Promise<ReadinessForecast> {
  const url = hoursPerWeek != null ? `/v1/readiness/forecast?hoursPerWeek=${hoursPerWeek}` : '/v1/readiness/forecast';
  const result = await apiRequest<ApiRecord>(url);
  return {
    probability: Number(result.probability ?? 0),
    weeksNeeded: Number(result.weeksNeeded ?? 0),
    weeksAvailable: Number(result.weeksAvailable ?? 0),
    requiredImprovement: Number(result.requiredImprovement ?? 0),
    slopePerWeek: Number(result.slopePerWeek ?? 0),
    scenarios: asArray(result.scenarios).map((s: ApiRecord) => ({
      label: String(s.label ?? ''),
      hoursPerWeek: Number(s.hoursPerWeek ?? 0),
      projectedReadinessAtTarget: Number(s.projectedReadinessAtTarget ?? 0),
      probability: Number(s.probability ?? 0),
    })),
  };
}

export async function refreshReadiness(): Promise<ReadinessData> {
  const readiness = await apiRequest<ApiRecord>('/v1/readiness/refresh', { method: 'POST' });
  return mapReadinessResponse(readiness);
}

export interface AdminReadinessLearnerRow {
  userId: string;
  displayName: string;
  targetExamDate: string | null;
  overallReadiness: number;
  overallRisk: string;
  weakestSubtest: string | null;
  targetDateProbability: number | null;
  computedAt: string;
  expiresAt: string;
}

export interface AdminReadinessLearnerList {
  page: number;
  pageSize: number;
  total: number;
  items: AdminReadinessLearnerRow[];
}

export interface AdminReadinessMetrics {
  learnersWithSnapshot: number;
  highRisk: number;
  moderateRisk: number;
  lowRisk: number;
  unknownRisk: number;
  interventionCandidates: number;
  staleSnapshots: number;
  avgWriting: number;
  avgSpeaking: number;
  avgReading: number;
  avgListening: number;
  avgVocabulary: number;
  avgOverall: number;
  generatedAt: string;
}

export async function fetchAdminReadinessLearners(params: { risk?: string; page?: number; pageSize?: number } = {}): Promise<AdminReadinessLearnerList> {
  const qs = new URLSearchParams();
  if (params.risk) qs.set('risk', params.risk);
  if (params.page) qs.set('page', String(params.page));
  if (params.pageSize) qs.set('pageSize', String(params.pageSize));
  const url = `/v1/admin/readiness/learners${qs.toString() ? `?${qs}` : ''}`;
  return apiRequest<AdminReadinessLearnerList>(url);
}

export async function fetchAdminReadinessLearner(userId: string): Promise<{ userId: string; displayName: string | null; targetExamDate: string | null; snapshot: ApiRecord; history: ReadinessHistoryPoint[]; reasoningTrace: string }> {
  const result = await apiRequest<ApiRecord>(`/v1/admin/readiness/learners/${encodeURIComponent(userId)}`);
  return {
    userId: result.userId,
    displayName: typeof result.displayName === 'string' ? result.displayName : null,
    targetExamDate: typeof result.targetExamDate === 'string' ? result.targetExamDate : null,
    snapshot: result.snapshot as ApiRecord,
    history: Array.isArray(result.history) ? result.history.map((row: ApiRecord) => ({
      weekStartDate: row.weekStartDate,
      overall: Number(row.overall ?? 0),
      writing: Number(row.writing ?? 0),
      speaking: Number(row.speaking ?? 0),
      reading: Number(row.reading ?? 0),
      listening: Number(row.listening ?? 0),
      vocabulary: Number(row.vocabulary ?? 0),
      risk: row.risk ?? 'Unknown',
      targetDateProbability: typeof row.targetDateProbability === 'number' ? row.targetDateProbability : null,
    })) : [],
    reasoningTrace: typeof result.reasoningTrace === 'string' ? result.reasoningTrace : '',
  };
}

export async function recomputeAdminReadiness(userId: string): Promise<void> {
  await apiRequest(`/v1/admin/readiness/learners/${encodeURIComponent(userId)}/recompute`, { method: 'POST' });
}

export async function fetchAdminReadinessMetrics(): Promise<AdminReadinessMetrics> {
  return apiRequest<AdminReadinessMetrics>('/v1/admin/readiness/metrics');
}

export async function fetchTrendData(): Promise<TrendPoint[]> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  const grouped = new Map<string, TrendPoint>();
  for (const point of progress.trend ?? []) {
    const label = point.week ?? new Date(point.generatedAt).toLocaleDateString();
    const existing: TrendPoint = grouped.get(label) ?? { date: label };
    existing[String(point.subtest).toLowerCase()] = parseScoreValue(point.scoreRange);
    grouped.set(label, existing);
  }
  return Array.from(grouped.values());
}

export async function fetchCompletionData(): Promise<{ day: string; completed: number }[]> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  return progress.completion ?? [];
}

export async function fetchSubmissionVolume(): Promise<{ week: string; submissions: number }[]> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  return progress.submissionVolume ?? [];
}

export async function fetchProgressEvidenceSummary(): Promise<ProgressEvidenceSummary> {
  const progress = await apiRequest<ApiRecord>('/v1/progress');
  return {
    reviewUsage: {
      totalRequests: Number(progress.reviewUsage?.totalRequests ?? 0),
      completedRequests: Number(progress.reviewUsage?.completedRequests ?? 0),
      averageTurnaroundHours: progress.reviewUsage?.averageTurnaroundHours ?? null,
      creditsConsumed: Number(progress.reviewUsage?.creditsConsumed ?? 0),
    },
    freshness: {
      generatedAt: progress.freshness?.generatedAt ?? new Date().toISOString(),
      usesFallbackSeries: Boolean(progress.freshness?.usesFallbackSeries),
    },
  };
}
