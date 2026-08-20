import { render, screen } from '@testing-library/react';
import { AiCreditSummary } from './ai-credit-summary';
import type { AiPackageCreditSnapshot } from '@/lib/billing-types';

const snapshot: AiPackageCreditSnapshot = {
  userId: 'learner-1',
  flexibleCredits: 3,
  writingOnlyCredits: 0,
  speakingOnlyCredits: 0,
  listeningTestsRemaining: 0,
  readingTestsRemaining: 0,
  mockExamsRemaining: 0,
  expiredBecausePassed: false,
  transactions: [],
  creditsGranted: 5,
  creditsUsed: 2,
  creditsRemaining: 3,
};

describe('AiCreditSummary', () => {
  it('shows granted, used, and remaining AI credits', () => {
    render(<AiCreditSummary snapshot={snapshot} />);

    expect(screen.getByTestId('ai-credit-summary')).toBeInTheDocument();
    expect(screen.getByText('Granted / purchased').parentElement).toHaveTextContent('5');
    expect(screen.getByText('Used').parentElement).toHaveTextContent('2');
    expect(screen.getByText('Remaining').parentElement).toHaveTextContent('3');
  });
});
