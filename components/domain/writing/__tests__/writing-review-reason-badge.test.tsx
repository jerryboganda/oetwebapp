import { render, screen } from '@testing-library/react';
import {
  reviewReasonLabels,
  WritingReviewReasonBadges,
} from '../writing-review-reason-badge';

describe('reviewReasonLabels', () => {
  it('maps every stored reason code to plain tutor-facing language', () => {
    expect(reviewReasonLabels('outcome_flip')).toEqual(['Pass/fail disagreement']);
    expect(reviewReasonLabels('criteria_divergence')).toEqual(['Criterion disagreement']);
    expect(reviewReasonLabels('finding_valid_alternative')).toEqual(['Possible valid alternative']);
    expect(reviewReasonLabels('verify_flag')).toEqual(['Cited finding unsupported']);
    expect(reviewReasonLabels('guard_block')).toEqual(['Flagged submission']);
    expect(reviewReasonLabels('rv_override')).toEqual(['Reviewer overrode a critical finding']);
    expect(reviewReasonLabels('rv_unresolved')).toEqual(['High score could not be verified']);
  });

  it('keeps stored order for a merged list and drops duplicate labels', () => {
    expect(reviewReasonLabels('outcome_flip,verify_flag,guard_block,something_new')).toEqual([
      'Pass/fail disagreement',
      'Cited finding unsupported',
      'Flagged submission',
    ]);
  });

  it('returns nothing for old rows', () => {
    expect(reviewReasonLabels(null)).toEqual([]);
    expect(reviewReasonLabels(undefined)).toEqual([]);
    expect(reviewReasonLabels('')).toEqual([]);
    expect(reviewReasonLabels(' , ')).toEqual([]);
  });
});

describe('WritingReviewReasonBadges', () => {
  it('renders one badge per distinct reason', () => {
    render(<WritingReviewReasonBadges reviewReason="outcome_flip,criteria_divergence" />);
    expect(screen.getByText('Pass/fail disagreement')).toBeInTheDocument();
    expect(screen.getByText('Criterion disagreement')).toBeInTheDocument();
  });

  it('is hidden when there is no reason', () => {
    const { container } = render(<WritingReviewReasonBadges reviewReason={null} />);
    expect(container).toBeEmptyDOMElement();
    expect(screen.queryByTestId('writing-review-reasons')).not.toBeInTheDocument();
  });
});
