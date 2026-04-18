import { render, screen } from '@testing-library/react';
import { RulebookFindingsPanel } from '../rulebook-findings-panel';

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

describe('RulebookFindingsPanel', () => {
  it('renders empty success state when no findings exist', () => {
    render(
      <RulebookFindingsPanel
        title="Rulebook Review"
        subtitle="Live checks"
        findings={[]}
      />,
    );

    expect(screen.getByText(/No rulebook issues detected/i)).toBeInTheDocument();
  });

  it('renders findings with severity, rule links, quote and fix suggestion', () => {
    render(
      <RulebookFindingsPanel
        title="Rulebook Review"
        subtitle="Live checks"
        ruleHref={(id) => `/writing/rulebook/${id}`}
        findings={[
          {
            ruleId: 'R03.4',
            severity: 'critical',
            message: 'Smoking status must be mentioned.',
            quote: 'No smoking history documented',
            fixSuggestion: 'Add smoking status.',
          },
        ]}
      />,
    );

    expect(screen.getByText('R03.4')).toHaveAttribute('href', '/writing/rulebook/R03.4');
    expect(screen.getByText(/Smoking status must be mentioned/i)).toBeInTheDocument();
    expect(screen.getByText(/Add smoking status/i)).toBeInTheDocument();
    expect(screen.getAllByText(/Critical/i).length).toBeGreaterThan(0);
  });
});
