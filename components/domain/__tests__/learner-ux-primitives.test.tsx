import { render, screen } from '@testing-library/react';
import { BookOpen, Sparkles } from 'lucide-react';
import { renderWithRouter } from '@/tests/test-utils';
import { LearnerBreadcrumbs } from '../learner-breadcrumbs';
import { LearnerEmptyState } from '../learner-empty-state';
import { LearnerFreshnessIndicator } from '../learner-freshness-indicator';
import { LearnerSkillSwitcher } from '../learner-skill-switcher';
import { LearnerSkeleton } from '../learner-skeletons';

describe('LearnerEmptyState', () => {
  it('renders an accessible recovery state with primary and secondary actions', () => {
    render(
      <LearnerEmptyState
        icon={Sparkles}
        title="No live tasks yet"
        description="Create a plan or take a diagnostic to get your next action."
        primaryAction={{ label: 'Create Plan', href: '/study-plan' }}
        secondaryAction={{ label: 'Take Diagnostic', href: '/diagnostic' }}
      />,
    );

    expect(screen.getByRole('region', { name: 'No live tasks yet' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /create plan/i })).toHaveAttribute('href', '/study-plan');
    expect(screen.getByRole('link', { name: /take diagnostic/i })).toHaveAttribute('href', '/diagnostic');
  });
});

describe('LearnerFreshnessIndicator', () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(new Date('2026-05-07T00:00:00Z'));
  });

  afterEach(() => {
    vi.useRealTimers();
  });

  it('describes recent and unknown data freshness', () => {
    const { rerender } = render(<LearnerFreshnessIndicator updatedAt="2026-05-06T23:45:00Z" />);

    expect(screen.getByText('Updated 15 min ago')).toBeInTheDocument();

    rerender(<LearnerFreshnessIndicator updatedAt={null} source="loaded" />);

    expect(screen.getByText('Loaded time unknown')).toBeInTheDocument();
  });
});

describe('LearnerSkillSwitcher', () => {
  it('marks the current skill module and exposes cross-module links', () => {
    renderWithRouter(<LearnerSkillSwitcher compact />, { pathname: '/writing' });

    expect(screen.getByRole('navigation', { name: /skill modules/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /writing/i })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: /mocks/i })).toHaveAttribute('href', '/mocks');
    expect(screen.getByRole('link', { name: /conversation/i })).toHaveAttribute('href', '/conversation');
  });
});

describe('LearnerBreadcrumbs', () => {
  it('shows orientation for non-immersive learner routes', () => {
    renderWithRouter(<LearnerBreadcrumbs />, { pathname: '/writing/result' });

    expect(screen.getByRole('navigation', { name: /breadcrumb/i })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /dashboard/i })).toHaveAttribute('href', '/');
    expect(screen.getByRole('link', { name: 'Writing' })).toHaveAttribute('href', '/writing');
    expect(screen.getByText('Result')).toHaveAttribute('aria-current', 'page');
  });

  it('suppresses immersive learner routes', () => {
    const { container } = renderWithRouter(<LearnerBreadcrumbs />, { pathname: '/listening/player/attempt-1' });

    expect(container).toBeEmptyDOMElement();
  });
});

describe('LearnerSkeleton', () => {
  it('renders a named loading status for learner workspace variants', () => {
    render(<LearnerSkeleton variant="dashboard" />);

    expect(screen.getByRole('status', { name: /loading learner workspace/i })).toBeInTheDocument();
  });
});
