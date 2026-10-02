import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockFetchMocksHome, mockSearchParams } = vi.hoisted(() => ({
  mockFetchMocksHome: vi.fn(),
  mockSearchParams: { current: new URLSearchParams() },
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>
      {children}
    </a>
  ),
}));

vi.mock('next/navigation', () => ({
  useSearchParams: () => mockSearchParams.current,
}));

vi.mock('@/lib/api', () => ({
  fetchMocksHome: mockFetchMocksHome,
}));

vi.mock('@/lib/analytics', () => ({
  analytics: { track: vi.fn() },
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-shell">{children}</div>
  ),
}));

vi.mock('@/components/ui/motion-primitives', () => ({
  MotionSection: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
  MotionItem: ({ children }: { children: React.ReactNode }) => <>{children}</>,
}));

vi.mock('@/components/ui/button', () => ({
  // `asChild` renders its child (a Link) itself, as the real Button does.
  Button: ({ children, asChild, ...rest }: { children: React.ReactNode; asChild?: boolean }) =>
    asChild ? <>{children}</> : <button {...rest}>{children}</button>,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children, title }: { children?: React.ReactNode; title?: string }) => (
    <div role="alert">
      {title}
      {children}
    </div>
  ),
}));

vi.mock('@/components/domain', () => ({
  LearnerPageHero: ({ title }: { title: string }) => <h1>{title}</h1>,
  LearnerSurfaceCard: ({ card }: { card: { title: string } }) => <article>{card.title}</article>,
  LearnerSurfaceSectionHeader: ({ title }: { title: string }) => <h2>{title}</h2>,
}));

type EmptyStateAction = { label: string; href?: string };
vi.mock('@/components/domain/learner-empty-state', () => ({
  LearnerEmptyState: ({ title, primaryAction, secondaryAction }: { title: string; primaryAction?: EmptyStateAction; secondaryAction?: EmptyStateAction }) => (
    <section>
      {title}
      {[primaryAction, secondaryAction].map((action) =>
        action?.href ? <a key={action.label} href={action.href}>{action.label}</a> : null,
      )}
    </section>
  ),
}));

vi.mock('@/components/domain/learner-skill-switcher', () => ({
  LearnerSkillSwitcher: () => <div data-testid="skill-switcher" />,
}));

vi.mock('@/components/domain/learner-skeletons', () => ({
  LearnerSkeleton: () => <div data-testid="skeleton" />,
}));

import MockCenter from './page';

function buildHome(overrides?: {
  subTestMocks?: Array<{ id: string; title: string; subtest: string }>;
  fullMocks?: Array<{ id: string; title: string; mockType: string; includedSubtests: string[] }>;
}) {
  return {
    reports: [],
    resumableAttempts: [],
    recommendedNextMock: null,
    purchasedMockReviews: {},
    collections: {
      subTestMocks: overrides?.subTestMocks ?? [
        { id: 'sub-listen', title: 'Listening Sub Mock', subtest: 'listening' },
        { id: 'sub-read', title: 'Reading Sub Mock', subtest: 'reading' },
      ],
      fullMocks: overrides?.fullMocks ?? [
        { id: 'full-all', title: 'Full Combined Mock A', mockType: 'full', includedSubtests: ['listening', 'reading', 'writing', 'speaking'] },
        { id: 'full-noL', title: 'Reading + Writing Mock', mockType: 'full', includedSubtests: ['reading', 'writing'] },
      ],
    },
    emptyState: null,
    learnerProfession: null,
    availableProfessions: [],
    scoreGuarantee: null,
    cohortPercentile: null,
  };
}

describe('Mock Center subtest/type scoping', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockSearchParams.current = new URLSearchParams();
    mockFetchMocksHome.mockResolvedValue(buildHome());
  });

  it('shows every mock and no scope banner when there is no deep-link param', async () => {
    render(<MockCenter />);

    expect(await screen.findByText('Listening Sub Mock')).toBeInTheDocument();
    expect(screen.getByText('Reading Sub Mock')).toBeInTheDocument();
    expect(screen.queryByTestId('mocks-scope-banner')).not.toBeInTheDocument();
  });

  it('scopes to listening: keeps listening sub-test mocks + listening-including full mocks, hides the rest', async () => {
    mockSearchParams.current = new URLSearchParams('subtest=listening');
    render(<MockCenter />);

    // Scope banner + active category.
    const banner = await screen.findByTestId('mocks-scope-banner');
    expect(banner).toHaveTextContent(/Full Listening Mock/i);
    expect(screen.getByTestId('mocks-cat-listening')).toHaveAttribute('aria-current', 'true');

    // Listening sub-test mock kept; reading sub-test mock filtered out.
    expect(screen.getByText('Listening Sub Mock')).toBeInTheDocument();
    expect(screen.queryByText('Reading Sub Mock')).not.toBeInTheDocument();

    // Full mock that includes listening kept; full mock without listening filtered out.
    expect(screen.getByText('Full Combined Mock A')).toBeInTheDocument();
    expect(screen.queryByText('Reading + Writing Mock')).not.toBeInTheDocument();

    // Escape hatch back to the full center.
    expect(screen.getByTestId('mocks-scope-clear')).toHaveAttribute('href', '/mocks');
  });

  it('scopes to full: shows full mocks and hides the sub-test section', async () => {
    mockSearchParams.current = new URLSearchParams('type=full');
    render(<MockCenter />);

    expect(await screen.findByTestId('mocks-scope-banner')).toHaveTextContent(/Full Combined Mock/i);
    expect(screen.getByText('Full Combined Mock A')).toBeInTheDocument();
    // Sub-test mocks are not surfaced in the combined view.
    expect(screen.queryByText('Listening Sub Mock')).not.toBeInTheDocument();
    expect(screen.queryByText('Reading Sub Mock')).not.toBeInTheDocument();
  });

  it('shows a scoped empty state with a part-by-part escape hatch when no listening bundles exist', async () => {
    mockFetchMocksHome.mockResolvedValue(
      buildHome({
        subTestMocks: [{ id: 'sub-read', title: 'Reading Sub Mock', subtest: 'reading' }],
        fullMocks: [{ id: 'full-noL', title: 'Reading + Writing Mock', mockType: 'full', includedSubtests: ['reading', 'writing'] }],
      }),
    );
    mockSearchParams.current = new URLSearchParams('subtest=listening');
    render(<MockCenter />);

    expect(await screen.findByText(/No Full Listening Mock bundles are published yet/i)).toBeInTheDocument();
    const practiseLink = screen.getByRole('link', { name: /Practise Listening part-by-part/i });
    expect(practiseLink).toHaveAttribute('href', '/listening');
  });

  it('shows a single no-bundles empty state and never the admin-facing backend copy', async () => {
    mockFetchMocksHome.mockResolvedValue({
      ...buildHome({ subTestMocks: [], fullMocks: [] }),
      emptyState: {
        title: 'No mock bundles are published yet',
        description: 'Ask an admin to publish a full or sub-test mock bundle from the content mock bundle console.',
        route: '/admin/content/mocks',
      },
    });
    render(<MockCenter />);

    expect(await screen.findAllByText('No mock bundles are published yet')).toHaveLength(1);
    expect(screen.queryByText(/ask an admin/i)).not.toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /go to dashboard/i })).not.toBeInTheDocument();
  });

  it('uses the backend empty-state copy when it points at a learner route', async () => {
    mockFetchMocksHome.mockResolvedValue({
      ...buildHome({ subTestMocks: [], fullMocks: [] }),
      emptyState: {
        title: 'Your learner profile is not fully initialised yet',
        description: 'Open your dashboard once to finish setup, then come back here to pick a mock.',
        route: '/dashboard',
      },
    });
    render(<MockCenter />);

    expect(await screen.findByText('Your learner profile is not fully initialised yet')).toBeInTheDocument();
    expect(screen.queryByText('No mock bundles are published yet')).not.toBeInTheDocument();
    expect(screen.getByRole('link', { name: /go to dashboard/i })).toHaveAttribute('href', '/dashboard');
  });

  it('clears the scope when "View all mocks" is followed', async () => {
    const user = userEvent.setup();
    mockSearchParams.current = new URLSearchParams('subtest=listening');
    render(<MockCenter />);

    const clear = await screen.findByTestId('mocks-scope-clear');
    expect(clear).toHaveAttribute('href', '/mocks');
    await user.click(clear); // link stub renders an anchor; assert href is the unscoped route
  });
});
