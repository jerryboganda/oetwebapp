import { render, screen } from '@testing-library/react';

const { mockTrack } = vi.hoisted(() => ({
  mockTrack: vi.fn(),
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

import WritingHome from './page';

describe('Writing landing page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('routes to the V2 writing flows', () => {
    render(<WritingHome />);

    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.mocks\.cta/ })).toHaveAttribute('href', '/writing/mocks');
    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.practice\.cta/ })).toHaveAttribute('href', '/writing/practice/library');
  });

  it('surfaces the supporting writing resources', () => {
    render(<WritingHome />);

    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.model\.cta/ })).toHaveAttribute('href', '/writing/model');
    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.rulebook\.cta/ })).toHaveAttribute('href', '/writing/rulebook');
    expect(screen.getByRole('link', { name: /writing\.hub\.cards\.submissions\.cta/ })).toHaveAttribute('href', '/submissions');
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
