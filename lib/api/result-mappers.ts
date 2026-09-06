/**
 * Shared evaluation-status/confidence normalizers — extracted from `lib/api.ts`.
 * Internal shared layer for ./api/* slices.
 */
import {
  titleCase,
  minutesToLabel,
  scoreRangeDisplay,
  parseCriterionScore,
  scoreToGrade,
  toExamFamilyCode,
} from '../domain/format';
import type { Confidence, EvalStatus } from '../mock-data';

export { titleCase, minutesToLabel, scoreRangeDisplay, parseCriterionScore, scoreToGrade, toExamFamilyCode };

export function toConfidence(value: string | null | undefined): Confidence {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'high') return 'High';
  if (normalized === 'low') return 'Low';
  return 'Medium';
}

export function toEvalStatus(value: string | null | undefined): EvalStatus {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'queued' || normalized === 'processing' || normalized === 'completed' || normalized === 'failed') {
    return normalized;
  }
  return 'processing';
}

export function toReviewStatus(value: string | null | undefined) {
  const normalized = (value ?? '').toLowerCase();
  if (normalized === 'completed' || normalized === 'reviewed') return 'reviewed';
  if (normalized === 'submitted' || normalized === 'queued' || normalized === 'in_review' || normalized === 'pending') return 'pending';
  return 'not_requested';
}
