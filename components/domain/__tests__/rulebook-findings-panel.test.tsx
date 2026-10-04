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

  it('hides internal rule ids on candidate-facing surfaces but keeps the finding', () => {
    render(
      <RulebookFindingsPanel
        title="Rulebook Review"
        subtitle="Live checks"
        hideRuleIds
        ruleHref={(id) => `/writing/rulebook/${id}`}
        findings={[
          { ruleId: 'RULE_22', severity: 'critical', message: 'Confirm identity first.' },
        ]}
      />,
    );

    expect(screen.queryByText('RULE_22')).not.toBeInTheDocument();
    expect(document.querySelector('a[href^="/writing/rulebook"]')).toBeNull();
    expect(screen.getByText(/Confirm identity first/i)).toBeInTheDocument();
  });

  it('normalizes numeric severity payloads from older API responses', () => {
    render(
      <RulebookFindingsPanel
        title="Rulebook Review"
        subtitle="Live checks"
        findings={[
          {
            ruleId: 'R03.8',
            severity: 2 as never,
            message: 'Body length is outside the recommended range.',
          },
        ]}
      />,
    );

    expect(screen.getByText('R03.8')).toBeInTheDocument();
    expect(screen.getAllByText(/Minor/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Body length is outside/i)).toBeInTheDocument();
  });

  it('falls back to info for malformed severity payloads', () => {
    render(
      <RulebookFindingsPanel
        title="Rulebook Review"
        subtitle="Live checks"
        findings={[
          {
            ruleId: 'R99.1',
            severity: 'warning' as never,
            message: 'Unexpected payload still renders safely.',
          },
        ]}
      />,
    );

    expect(screen.getByText('R99.1')).toBeInTheDocument();
    expect(screen.getAllByText(/Info/i).length).toBeGreaterThan(0);
    expect(screen.getByText(/Unexpected payload/i)).toBeInTheDocument();
  });
});
