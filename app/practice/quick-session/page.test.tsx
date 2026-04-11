import { render, screen } from '@testing-library/react';
vi.mock('motion/react', () => ({
  motion: {
    div: ({ children, ...props }: any) => <div {...props}>{children}</div>,
    button: ({ children, ...props }: any) => <button {...props}>{children}</button>,
    span: ({ children, ...props }: any) => <span {...props}>{children}</span>,
    section: ({ children, ...props }: any) => <section {...props}>{children}</section>,
    p: ({ children, ...props }: any) => <p {...props}>{children}</p>,
  },
  useReducedMotion: () => false,
  AnimatePresence: ({ children }: any) => <div>{children}</div>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('@/lib/auth-client', () => ({ ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token') }));
vi.mock('@/lib/env', () => ({ env: { apiBaseUrl: 'http://localhost:5000' } }));

import MobileQuickSessionPage from './page';

describe('Quick session page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    global.fetch = vi.fn().mockRejectedValue(new Error('API unavailable'));
  });

  it('renders through the shared learner dashboard shell', () => {
    render(<MobileQuickSessionPage />);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });
});
