import { render, screen } from '@testing-library/react';

const { mockTrack, mockListFreeSamples } = vi.hoisted(() => ({
  mockTrack: vi.fn(),
  mockListFreeSamples: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: vi.fn() }),
  usePathname: () => '/writing',
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: { activeProfessionId: 'medicine' } }),
}));

vi.mock('@/lib/api/free-samples', () => ({ listFreeSamples: mockListFreeSamples }));

vi.mock('@/components/domain/learner-skill-switcher', () => ({
  LearnerSkillSwitcher: () => <div data-testid="skill-switcher" />,
}));

import WritingHome from './page';

describe('Writing landing page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    // No free sample on offer by default: the launcher renders nothing.
    mockListFreeSamples.mockResolvedValue([]);
  });

  it('shows the Free Writing Mock as the FIRST action under Start Writing, above the practice library', async () => {
    mockListFreeSamples.mockResolvedValue([
      { professionId: 'medicine', contentId: 'w-med', state: 'available', route: '/writing/practice/session/w-med' },
    ]);
    render(<WritingHome />);

    const card = await screen.findByTestId('writing-free-mock-card');
    expect(card).toHaveTextContent('writing.hub.freeSample.title');
    expect(card).toHaveTextContent('writing.hub.freeSample.badge');
    expect(mockListFreeSamples).toHaveBeenCalledWith('writing');

    const practice = screen.getByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ });
    const submissions = screen.getByRole('link', { name: /writing\.hub\.cards\.submissions\.cta/ });
    expect(card.compareDocumentPosition(practice) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
    expect(card.compareDocumentPosition(submissions) & Node.DOCUMENT_POSITION_FOLLOWING).toBeTruthy();
  });

  it('hides the free card when the server offers no sample (Practice Library and Past Submissions unchanged)', async () => {
    render(<WritingHome />);

    await screen.findByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ });
    expect(screen.queryByTestId('writing-free-mock-card')).toBeNull();
  });

  it('routes to the V2 writing flows', () => {
    render(<WritingHome />);

    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ })).toHaveAttribute('href', '/writing/practice/library');
    // Past Submissions must never open the global all-subtest history — it opens
    // pre-filtered to Writing only (brief item 5).
    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.submissions\.cta/ })).toHaveAttribute('href', '/submissions?subtest=writing');
  });

  it('no longer surfaces mock exams or model answers', () => {
    render(<WritingHome />);

    expect(screen.queryByRole('link', { name: /writing\.hub\.cards\.mocks\.cta/ })).toBeNull();
    expect(screen.queryByRole('link', { name: /writing\.hub\.cards\.model\.cta/ })).toBeNull();
  });

  it('does not surface the internal rulebook to learners', () => {
    const { container } = render(<WritingHome />);

    const hrefs = Array.from(container.querySelectorAll('a')).map((a) => a.getAttribute('href') ?? '');
    expect(hrefs.some((h) => h.startsWith('/writing/rulebook'))).toBe(false);
  });

  it('does not link to any retired V1 writing surfaces', () => {
    const { container } = render(<WritingHome />);

    const hrefs = Array.from(container.querySelectorAll('a')).map((a) => a.getAttribute('href') ?? '');
    expect(hrefs.some((h) => h.startsWith('/writing/player'))).toBe(false);
    expect(hrefs.some((h) => h.startsWith('/writing/library'))).toBe(false);
    expect(hrefs.some((h) => h.startsWith('/writing/revision'))).toBe(false);
  });

  it('tracks module entry on mount', () => {
    render(<WritingHome />);
    expect(mockTrack).toHaveBeenCalledWith('module_entry', { module: 'writing' });
  });
});
