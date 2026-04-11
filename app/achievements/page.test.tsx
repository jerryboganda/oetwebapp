import { render, screen } from '@testing-library/react';
const { mockFetchXP, mockFetchStreak, mockFetchAchievements, mockTrack } = vi.hoisted(() => ({
  mockFetchXP: vi.fn(),
  mockFetchStreak: vi.fn(),
  mockFetchAchievements: vi.fn(),
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

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchXP: mockFetchXP, fetchStreak: mockFetchStreak, fetchAchievements: mockFetchAchievements }));

import AchievementsPage from './page';

describe('Achievements page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchXP.mockResolvedValue({ totalXp: 2450, level: 5, xpToNextLevel: 500, xpInCurrentLevel: 350 });
    mockFetchStreak.mockResolvedValue({ currentStreak: 7, longestStreak: 14, lastActivityDate: '2026-04-01' });
    mockFetchAchievements.mockResolvedValue([
      { achievementId: 'ach-1', title: 'First Practice', description: 'Complete your first task', category: 'practice', xpReward: 50, unlockedAt: '2026-03-20', earnedAt: '2026-03-20' },
      { achievementId: 'ach-2', title: 'Week Warrior', description: 'Maintain a 7-day streak', category: 'streak', xpReward: 100, unlockedAt: null, earnedAt: null },
    ]);
  });

  it('renders through the shared learner dashboard shell', async () => {
    render(<AchievementsPage />);
    expect(await screen.findByText('Achievements')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('displays XP level from the API', async () => {
    render(<AchievementsPage />);
    expect(await screen.findByText('5')).toBeInTheDocument();
  });

  it('shows unlocked and locked achievement sections', async () => {
    render(<AchievementsPage />);
    expect(await screen.findByText('First Practice')).toBeInTheDocument();
    expect(screen.getByText('Week Warrior')).toBeInTheDocument();
  });
});
