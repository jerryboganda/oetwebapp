import { apiRequest, type ApiRecord } from './client';
import { minutesToLabel, toSubTest } from './task-mappers';
import { scoreRangeDisplay, toConfidence } from './result-mappers';
import { type DiagnosticResult, DiagnosticSession, SubTest } from '../mock-data';

/**
 * Diagnostic session lifecycle + results.
 * Extracted verbatim from `lib/api.ts`; re-exported there.
 */
export async function fetchDiagnosticSession(): Promise<DiagnosticSession> {
  const overview = await apiRequest<ApiRecord>('/v1/diagnostic/overview');
  return {
    id: overview.diagnosticId,
    status: overview.state,
    startedAt: overview.startedAt,
    completedAt: overview.completedAt,
    subTests: (overview.subtests ?? []).map((item: ApiRecord) => ({
      subTest: toSubTest(item.subtest),
      status: item.state,
      estimatedDuration: minutesToLabel(item.estimatedDurationMinutes),
      completedAt: item.completedAt,
      contentId: item.contentId ?? item.taskId ?? undefined,
    })),
  };
}

export async function startDiagnostic(): Promise<DiagnosticSession> {
  const session = await apiRequest<ApiRecord>('/v1/diagnostic/attempts', { method: 'POST' });
  return {
    id: session.diagnosticId,
    status: session.state,
    startedAt: session.startedAt,
    completedAt: session.completedAt,
    subTests: (session.subtests ?? []).map((item: ApiRecord) => ({
      subTest: toSubTest(item.subtest),
      status: item.state,
      estimatedDuration: minutesToLabel(item.estimatedDurationMinutes),
      completedAt: item.completedAt,
      contentId: item.contentId ?? item.taskId ?? undefined,
    })),
  };
}

/**
 * Fetches the diagnostic task ID for a given sub-test.
 * Fails closed if the backend has not published a real diagnostic task.
 */
export async function fetchDiagnosticTaskId(subTest: SubTest): Promise<string> {
  try {
    const response = await apiRequest<ApiRecord>(`/v1/diagnostic/tasks?subtest=${encodeURIComponent(subTest)}`);
    if (response.diagnosticEligible !== true) {
      throw new Error('Diagnostic task is not marked eligible.');
    }
    const taskId = response.taskId ?? response.contentId ?? null;
    if (taskId) return String(taskId);
  } catch {
    // Fail closed below; learner-facing diagnostics must not use demo IDs.
  }

  throw new Error(`Diagnostic ${subTest} task is unavailable.`);
}

export async function fetchDiagnosticResults(): Promise<DiagnosticResult[]> {
  const session = await fetchDiagnosticSession();
  const response = await apiRequest<ApiRecord>(`/v1/diagnostic/attempts/${session.id}/results`);
  return (response.results ?? []).map((item: ApiRecord) => ({
    subTest: toSubTest(item.subTest),
    scoreRange: scoreRangeDisplay(item.scoreRange),
    confidence: toConfidence(item.confidence),
    strengths: item.strengths ?? [],
    issues: item.issues ?? [],
    readiness: item.readiness ?? 0,
    criterionBreakdown: (item.criterionBreakdown ?? []).map((criterion: ApiRecord) => ({
      name: criterion.name,
      score: criterion.score,
      maxScore: criterion.maxScore,
      grade: criterion.grade,
      explanation: criterion.explanation,
      anchoredComments: criterion.anchoredComments ?? [],
      omissions: criterion.omissions ?? [],
      unnecessaryDetails: criterion.unnecessaryDetails ?? [],
      revisionSuggestions: criterion.revisionSuggestions ?? [],
      strengths: criterion.strengths ?? [],
      issues: criterion.issues ?? [],
    })),
  }));
}

// ─── Expert Console API ───
