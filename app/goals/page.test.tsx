import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
const { mockFetchExamFamilies, mockFetchUserProfile, mockUpdateUserProfile, mockPush, mockTrack } = vi.hoisted(() => ({
  mockFetchExamFamilies: vi.fn(),
  mockFetchUserProfile: vi.fn(),
  mockUpdateUserProfile: vi.fn(),
  mockPush: vi.fn(),
  mockTrack: vi.fn(),
}));

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

vi.mock('@/hooks/use-analytics', () => ({ useAnalytics: () => ({ track: mockTrack }) }));
vi.mock('@/lib/api', () => ({ fetchExamFamilies: mockFetchExamFamilies, fetchUserProfile: mockFetchUserProfile, updateUserProfile: mockUpdateUserProfile }));

import GoalsPage from './page';

describe('Goals setup page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchExamFamilies.mockResolvedValue([{ code: 'oet', name: 'OET', isActive: true }]);
    mockFetchUserProfile.mockResolvedValue({ profession: 'medicine', examFamilyCode: 'oet' });
    mockUpdateUserProfile.mockResolvedValue({});
  });

  it('renders the goals form through the shared learner dashboard shell', () => {
    renderWithRouter(<GoalsPage />, { router: { push: mockPush } });
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays exam family selector', () => {
    renderWithRouter(<GoalsPage />, { router: { push: mockPush } });
    expect(screen.getByText('OET')).toBeInTheDocument();
  });
});
