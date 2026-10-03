import { Badge } from '@/components/ui/badge';

/**
 * Plain-language labels for the fixed vocabulary the backend stores in
 * `WritingTutorReviewAssignment.ReviewReason` (comma-separated). Tutor-facing only:
 * it says why a submission was flagged for review, never a verdict on the letter.
 */
const REASON_LABELS: Record<string, string> = {
  guard_block: 'Flagged submission',
  outcome_flip: 'Pass/fail disagreement',
  criteria_divergence: 'Criterion disagreement',
  verify_flag: 'Cited finding unsupported',
  finding_valid_alternative: 'Possible valid alternative',
};

const FALLBACK_LABEL = 'Flagged submission';

/** Distinct labels for a stored reason string, in stored order; empty when there is no reason. */
export function reviewReasonLabels(reviewReason: string | null | undefined): string[] {
  if (!reviewReason) return [];
  const labels: string[] = [];
  for (const code of reviewReason.split(',')) {
    const trimmed = code.trim();
    if (!trimmed) continue;
    const label = REASON_LABELS[trimmed] ?? FALLBACK_LABEL;
    if (!labels.includes(label)) labels.push(label);
  }
  return labels;
}

/** Neutral badges explaining why Jev asked for tutor review. Renders nothing for old rows (null). */
export function WritingReviewReasonBadges({ reviewReason }: { reviewReason?: string | null }) {
  const labels = reviewReasonLabels(reviewReason);
  if (labels.length === 0) return null;
  return (
    <div className="mt-1 flex flex-wrap gap-1" data-testid="writing-review-reasons">
      {labels.map((label) => (
        <Badge key={label} variant="muted" size="sm" title="Why this submission was flagged for review">
          {label}
        </Badge>
      ))}
    </div>
  );
}
