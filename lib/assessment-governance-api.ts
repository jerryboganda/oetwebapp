import { apiClient } from '@/lib/api';

export type AssessmentGovernanceStatus =
  | 'Draft'
  | 'InReview'
  | 'Approved'
  | 'Effective'
  | 'Locked'
  | 'Retired'
  | 'Completed';

export type AssessmentScoreConversionRowDto = {
  rawScore: number;
  convertedScore: number;
  grade: string | null;
  passed: boolean | null;
};

export type AssessmentScoreConversionTableDto = {
  id: string;
  assessment: 'listening' | 'reading';
  scopeKey: string;
  versionKey: string;
  status: AssessmentGovernanceStatus;
  effectiveFrom: string;
  approvedAt: string | null;
  approvedByUserId: string | null;
  lockedAt: string | null;
  hasBeenUsed: boolean;
  rows: AssessmentScoreConversionRowDto[];
};

export type AssessmentScoreConversionTableRequest = {
  assessment: 'listening' | 'reading';
  scopeKey: string;
  versionKey: string;
  effectiveFrom?: string;
  rows: AssessmentScoreConversionRowDto[];
};

export type AssessmentMarkingPolicyDto = {
  id: string;
  assessment: 'listening' | 'reading';
  scopeKey: string;
  versionKey: string;
  policyJson: string;
  status: AssessmentGovernanceStatus;
  effectiveFrom: string;
  approvedAt: string | null;
  approvedByUserId: string | null;
  hasBeenUsed: boolean;
};

export type AssessmentMarkingPolicyRequest = {
  assessment: 'listening' | 'reading';
  scopeKey: string;
  versionKey: string;
  policyJson: string;
  effectiveFrom?: string;
};

export type AssessmentRationaleDto = {
  id: string;
  assessment: 'listening' | 'reading';
  questionRevisionId: string;
  sourceSentence: string;
  rationaleText: string;
  evidenceCount: number;
  status: AssessmentGovernanceStatus;
  createdByUserId: string;
  approvedByUserId: string | null;
  createdAt: string;
  updatedAt: string;
};

export type AssessmentReMarkJobDto = {
  id: string;
  assessment: 'listening' | 'reading';
  attemptId: string;
  questionRevisionId: string;
  reason: string;
  status: AssessmentGovernanceStatus;
  requestedByUserId: string;
  approvedByUserId: string | null;
  createdAt: string;
  approvedAt: string | null;
  completedAt: string | null;
  affectedAttemptIds: string | null;
};

export function listAssessmentScoreTables(params?: {
  assessment?: 'listening' | 'reading';
  scopeKey?: string;
}) {
  const search = new URLSearchParams();
  if (params?.assessment) search.set('assessment', params.assessment);
  if (params?.scopeKey) search.set('scopeKey', params.scopeKey);
  const query = search.toString();
  return apiClient.get<AssessmentScoreConversionTableDto[]>(
    `/v1/admin/assessment-governance/score-tables${query ? `?${query}` : ''}`,
  );
}

export function createAssessmentScoreTable(request: AssessmentScoreConversionTableRequest) {
  return apiClient.post<AssessmentScoreConversionTableDto>(
    '/v1/admin/assessment-governance/score-tables',
    request,
  );
}

export function makeAssessmentScoreTableEffective(id: string) {
  return apiClient.post<AssessmentScoreConversionTableDto>(
    `/v1/admin/assessment-governance/score-tables/${encodeURIComponent(id)}/effective`,
    {},
  );
}

export function listAssessmentMarkingPolicies(params?: {
  assessment?: 'listening' | 'reading';
  scopeKey?: string;
}) {
  const search = new URLSearchParams();
  if (params?.assessment) search.set('assessment', params.assessment);
  if (params?.scopeKey) search.set('scopeKey', params.scopeKey);
  const query = search.toString();
  return apiClient.get<AssessmentMarkingPolicyDto[]>(
    `/v1/admin/assessment-governance/marking-policies${query ? `?${query}` : ''}`,
  );
}

export function createAssessmentMarkingPolicy(request: AssessmentMarkingPolicyRequest) {
  return apiClient.post<AssessmentMarkingPolicyDto>(
    '/v1/admin/assessment-governance/marking-policies',
    request,
  );
}

export function makeAssessmentMarkingPolicyEffective(id: string) {
  return apiClient.post<AssessmentMarkingPolicyDto>(
    `/v1/admin/assessment-governance/marking-policies/${encodeURIComponent(id)}/effective`,
    {},
  );
}
export function listAssessmentRationales(params?: { assessment?: 'listening' | 'reading' }) {
  const query = params?.assessment ? `?assessment=${encodeURIComponent(params.assessment)}` : '';
  return apiClient.get<AssessmentRationaleDto[]>(`/v1/admin/assessment-governance/rationales${query}`);
}

export function createAssessmentRationale(request: {
  assessment: 'listening' | 'reading';
  questionRevisionId: string;
  sourceSentence: string;
  rationaleText: string;
  evidenceCount: number;
}) {
  return apiClient.post<AssessmentRationaleDto>('/v1/admin/assessment-governance/rationales', request);
}

export function makeAssessmentRationaleEffective(id: string) {
  return apiClient.post<AssessmentRationaleDto>(
    `/v1/admin/assessment-governance/rationales/${encodeURIComponent(id)}/effective`,
    {},
  );
}

export function listAssessmentReMarkJobs(params?: { assessment?: 'listening' | 'reading' }) {
  const query = params?.assessment ? `?assessment=${encodeURIComponent(params.assessment)}` : '';
  return apiClient.get<AssessmentReMarkJobDto[]>(`/v1/admin/assessment-governance/re-mark-jobs${query}`);
}

export function createAssessmentReMarkJob(request: {
  assessment: 'listening' | 'reading';
  attemptId: string;
  questionRevisionId: string;
  reason: string;
  originalKeySnapshotJson: string;
  newKeySnapshotJson: string;
}) {
  return apiClient.post<AssessmentReMarkJobDto>('/v1/admin/assessment-governance/re-mark-jobs', request);
}

export function approveAssessmentReMarkJob(id: string) {
  return apiClient.post<AssessmentReMarkJobDto>(
    `/v1/admin/assessment-governance/re-mark-jobs/${encodeURIComponent(id)}/approve`,
    {},
  );
}

export function executeAssessmentReMarkJob(id: string) {
  return apiClient.post<AssessmentReMarkJobDto>(
    `/v1/admin/assessment-governance/re-mark-jobs/${encodeURIComponent(id)}/execute`,
    {},
  );
}
