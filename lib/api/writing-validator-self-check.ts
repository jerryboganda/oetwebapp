import { apiClient } from '@/lib/api';

/**
 * On-demand Writing validator self-check (owner directive 2026-10-09). The server feeds golden
 * clinical-abbreviation cases (QD/QID/QDS/QOD/BD/TDS/PRN, scan and table forms, OD vs right eye,
 * completed vs pending actions) through the deployed validator and reports pass/fail per case.
 * Never runs in CI; no database write and no AI call.
 */
export interface WritingSelfCheckCase {
  group: string;
  name: string;
  checkId: string;
  expected: boolean;
  actual: boolean;
  ok: boolean;
  detail: string;
}

export interface WritingSelfCheckReport {
  ranAt: string;
  validatorVersion: string;
  total: number;
  passed: number;
  failed: number;
  cases: WritingSelfCheckCase[];
}

export function runWritingValidatorSelfCheck(): Promise<WritingSelfCheckReport> {
  return apiClient.post<WritingSelfCheckReport>('/v1/admin/writing/validator-self-check');
}
