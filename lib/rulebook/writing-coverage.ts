/**
 * Writing rule coverage matrix.
 *
 * As of the conformance-framework refactor this is a THIN WRAPPER over the
 * kind-agnostic `coverage-matrix.ts`. It exists to (a) preserve the writing
 * public API and the 172-row baseline lock, and (b) add the writing-only
 * `requiresCaseNoteMarkers` column.
 *
 * NOTE (decision 1 / Phase 2): `MARKER_DEPENDENT_CHECK_IDS` lists detectors
 * that depend on case-note markers which were removed from the product. Phase 2
 * prunes those detectors and reclassifies their rules to `ai-grounded`; this
 * set (and the column) is removed at that point. It is retained here only so
 * the matrix keeps reporting the dependency until that reclassification lands.
 */

import {
  buildRuleCoverageMatrix,
  validateRuleCoverageMatrix,
  type CoverageMode,
  type RuleCoverageRow,
} from './coverage-matrix';
import type { ExamProfession } from './types';

export type WritingCoverageMode = CoverageMode;

const MARKER_DEPENDENT_CHECK_IDS = new Set([
  'content_requires_allergy_for_atopic',
  'closure_mentions_review_if_required',
  'enclosure_results_phrase',
  'closure_mentions_patient_request_if_flagged',
  'closure_mentions_consent_if_flagged',
]);

export interface WritingRuleCoverageRow extends RuleCoverageRow {
  /** True when this rule's detector depends on case-note markers (removed — see Phase 2). */
  requiresCaseNoteMarkers: boolean;
}

export function buildWritingRuleCoverageMatrix(
  profession: ExamProfession = 'medicine',
): WritingRuleCoverageRow[] {
  return buildRuleCoverageMatrix('writing', profession).map((row) => ({
    ...row,
    requiresCaseNoteMarkers: row.checkId ? MARKER_DEPENDENT_CHECK_IDS.has(row.checkId) : false,
  }));
}

export function validateWritingRuleCoverageMatrix(
  profession: ExamProfession = 'medicine',
): string[] {
  return validateRuleCoverageMatrix('writing', profession);
}
