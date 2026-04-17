import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockCreateMockSession, mockTrack, mockPush } = vi.hoisted(() => ({
  mockCreateMockSession: vi.fn(),
  mockTrack: vi.fn(),
  mockPush: vi.fn(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ createMockSession: mockCreateMockSession }));

import MockSetup from './page';

describe('Mock setup page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockCreateMockSession.mockResolvedValue({ sessionId: 'mock-sess-1', redirectUrl: '/mocks/mock-sess-1' });
  });

  it('renders the mock setup form through the shared learner dashboard shell', () => {
    renderWithRouter(<MockSetup />, { router: { push: mockPush } });
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays mock type options', () => {
    renderWithRouter(<MockSetup />, { router: { push: mockPush } });
    expect(screen.getByText('Full Mock')).toBeInTheDocument();
  });
});
