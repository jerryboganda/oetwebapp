import { render, screen } from '@testing-library/react';
import { AiCreditSummary } from './ai-credit-summary';
import type { AiPackageCreditSnapshot } from '@/lib/billing-types';

const snapshot: AiPackageCreditSnapshot = {
  userId: 'learner-1',
  sharedCredits: 0,
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

  it('shows writing and speaking AI credits separately', () => {
    render(
      <AiCreditSummary
        snapshot={{
          ...snapshot,
          flexibleCredits: 0,
          writingOnlyCredits: 6,
          speakingOnlyCredits: 3,
          creditsGranted: 9,
          creditsUsed: 0,
          creditsRemaining: 9,
        }}
      />,
    );

    expect(screen.getByTestId('writing-ai-credits')).toHaveTextContent('6');
    expect(screen.getByTestId('speaking-ai-credits')).toHaveTextContent('3');
    expect(screen.getByTestId('shared-ai-credits')).toHaveTextContent('0');
  });

  it('shows unlimited writing and speaking for OET Mastery and hides the stale generic remaining', () => {
    render(
      <AiCreditSummary
        snapshot={{
          ...snapshot,
          flexibleCredits: 5,
          writingOnlyCredits: 0,
          speakingOnlyCredits: 0,
          listeningTestsRemaining: null,
          readingTestsRemaining: null,
          writingUnlimited: true,
          speakingUnlimited: true,
          creditsRemaining: 5,
        }}
      />,
    );

    expect(screen.getByTestId('writing-ai-credits')).toHaveTextContent('Unlimited');
    expect(screen.getByTestId('speaking-ai-credits')).toHaveTextContent('Unlimited');
    expect(screen.getByText('Reading tests').parentElement).toHaveTextContent('Unlimited');
    expect(screen.getByText('Listening tests').parentElement).toHaveTextContent('Unlimited');
    expect(screen.queryByTestId('shared-ai-credits')).not.toBeInTheDocument();
    expect(screen.queryByTestId('finite-remaining')).not.toBeInTheDocument();
    expect(screen.queryByText('Granted / purchased')).not.toBeInTheDocument();
  });
});
