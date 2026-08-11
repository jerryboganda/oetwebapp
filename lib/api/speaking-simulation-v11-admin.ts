import { apiClient } from '@/lib/api';
import type { SpeakingSimulationV11Criterion } from '@/lib/api/speaking-simulation-v11';

export interface SpeakingSimulationV11AdminRubricCriterion {
  criterionCode: string;
  label: string;
  weight: number;
  enabledRuleIds: string[];
}

export interface SpeakingSimulationV11AdminGate {
  isReleased: boolean;
  blockingReasons: string[];
  enabledRuleIds: string[];
  specVersion: string;
  rubricVersion: string;
  rubricCriteria?: SpeakingSimulationV11AdminRubricCriterion[] | null;
}

export interface SpeakingSimulationV11AdminStatus {
  professionId: string;
  gate: SpeakingSimulationV11AdminGate;
  specReleases: Array<{
    id: string;
    specVersion: string;
    releaseVersion: string;
    status: string;
    createdAt: string;
    updatedAt: string;
  }>;
  rubricReleases: Array<{
    id: string;
    rubricVersion: string;
    calibrationVersion: string;
    status: string;
    criteria: string;
    createdAt: string;
    updatedAt: string;
  }>;
  approvals: Array<{
    id: string;
    approvalKey: string;
    scopeKey: string;
    specVersion: string | null;
    rubricVersion: string | null;
    status: string;
    numericValue: number | null;
    evidenceJson: string | null;
    approvedByUserId: string | null;
    approvedAt: string | null;
    createdAt: string;
    updatedAt: string;
  }>;
  rubricCriteria: SpeakingSimulationV11AdminRubricCriterion[];
}

export function getSpeakingSimulationV11AdminStatus(professionId = 'medicine') {
  return apiClient.get<SpeakingSimulationV11AdminStatus>(
    `/v1/admin/speaking/simulation-v1.1/status?professionId=${encodeURIComponent(professionId)}`,
  );
}

export function createSpeakingSimulationV11SpecRelease(input: {
  specVersion: string;
  releaseVersion: string;
}) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['specReleases'][number]>(
    '/v1/admin/speaking/simulation-v1.1/spec-releases',
    input,
  );
}

export function approveSpeakingSimulationV11SpecRelease(id: string) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['specReleases'][number]>(
    `/v1/admin/speaking/simulation-v1.1/spec-releases/${encodeURIComponent(id)}/approve`,
    {},
  );
}

export function createSpeakingSimulationV11RubricRelease(input: {
  rubricVersion: string;
  calibrationVersion: string;
  criteria: SpeakingSimulationV11AdminRubricCriterion[];
}) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['rubricReleases'][number]>(
    '/v1/admin/speaking/simulation-v1.1/rubric-releases',
    input,
  );
}

export function approveSpeakingSimulationV11RubricRelease(id: string) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['rubricReleases'][number]>(
    `/v1/admin/speaking/simulation-v1.1/rubric-releases/${encodeURIComponent(id)}/approve`,
    {},
  );
}

export function createSpeakingSimulationV11Approval(input: {
  approvalKey: string;
  scopeKey: string;
  specVersion: string;
  rubricVersion: string;
  numericValue?: number | null;
  evidenceJson?: string | null;
}) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['approvals'][number]>(
    '/v1/admin/speaking/simulation-v1.1/approvals',
    input,
  );
}

export function approveSpeakingSimulationV11Approval(id: string, note?: string) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['approvals'][number]>(
    `/v1/admin/speaking/simulation-v1.1/approvals/${encodeURIComponent(id)}/approve`,
    { note: note ?? null },
  );
}

export function rejectSpeakingSimulationV11Approval(id: string, note?: string) {
  return apiClient.post<SpeakingSimulationV11AdminStatus['approvals'][number]>(
    `/v1/admin/speaking/simulation-v1.1/approvals/${encodeURIComponent(id)}/reject`,
    { note: note ?? null },
  );
}

export type SpeakingSimulationV11AdminCriterionPreview = SpeakingSimulationV11Criterion;
