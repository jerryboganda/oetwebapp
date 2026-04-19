import { apiRequest } from './api';

export interface ProgressPolicyDto {
  id: string;
  examFamilyCode: string;
  defaultTimeRange: '14d' | '30d' | '90d' | 'all';
  smoothingWindow: number;
  minCohortSize: number;
  mockDistinctStyle: boolean;
  showScoreGuaranteeStrip: boolean;
  showCriterionConfidenceBand: boolean;
  minEvaluationsForTrend: number;
  exportPdfEnabled: boolean;
  updatedByAdminId: string | null;
  updatedAt: string;
}

export interface ProgressPolicyUpdate {
  defaultTimeRange?: '14d' | '30d' | '90d' | 'all';
  smoothingWindow?: number;
  minCohortSize?: number;
  mockDistinctStyle?: boolean;
  showScoreGuaranteeStrip?: boolean;
  showCriterionConfidenceBand?: boolean;
  minEvaluationsForTrend?: number;
  exportPdfEnabled?: boolean;
}

export function getProgressPolicy(examFamilyCode = 'oet'): Promise<ProgressPolicyDto> {
  return apiRequest(`/v1/admin/progress-policy/${examFamilyCode}`);
}

export function updateProgressPolicy(examFamilyCode: string, dto: ProgressPolicyUpdate): Promise<ProgressPolicyDto> {
  return apiRequest(`/v1/admin/progress-policy/${examFamilyCode}`, {
    method: 'PUT',
    body: JSON.stringify(dto),
  });
}
