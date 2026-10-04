/**
 * Speaking score label (owner spec 4 Oct 2026).
 *
 * The SERVER decides it: a Speaking score is `provisional` until the grader version that produced it
 * has passed calibration against expert-labelled performances, and only then an `ai_practice_estimate`.
 * A payload with no label (an older result) is treated as provisional — never the other way round, so
 * an uncalibrated number can never quietly read as a settled one.
 */
export type SpeakingScoreLabel = 'provisional' | 'ai_practice_estimate';

export const PROVISIONAL_SCORE_TITLE = 'Provisional score — calibration in progress';

export const PROVISIONAL_SCORE_BODY =
  'We are still checking this scoring against expert-marked performances, so treat the number as a guide rather than a prediction.';

export function isProvisionalScore(label: string | null | undefined): boolean {
  return label !== 'ai_practice_estimate';
}
