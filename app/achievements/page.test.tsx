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

vi.mock('@/components/ui/card', () => ({
  Card: ({ children, className, ...props }: any) => <div data-testid="card" className={className} {...props}>{children}</div>,
}));

vi.mock('@/components/ui/progress', () => ({
  ProgressBar: (props: any) => <div data-testid="progress-bar" role="progressbar" />,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));
vi.mock('@/lib/api', () => ({ fetchXP: mockFetchXP, fetchStreak: mockFetchStreak, fetchAchievements: mockFetchAchievements }));

import AchievementsPage from './page';

describe('Achievements page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockFetchXP.mockResolvedValue({ totalXP: 2450, weeklyXP: 100, monthlyXP: 600, level: 5, nextLevelXP: 1500, currentLevelXP: 1000 });
    mockFetchStreak.mockResolvedValue({ currentStreak: 7, longestStreak: 14, lastActiveDate: '2026-04-01', streakFreezesAvailable: 2 });
    mockFetchAchievements.mockResolvedValue([
      { id: 'ach-1', code: 'first_practice', label: 'First Practice', description: 'Complete your first task', category: 'practice', iconUrl: null, xpReward: 50, sortOrder: 1, unlocked: true, unlockedAt: '2026-03-20' },
      { id: 'ach-2', code: 'week_warrior', label: 'Week Warrior', description: 'Maintain a 7-day streak', category: 'streak', iconUrl: null, xpReward: 100, sortOrder: 2, unlocked: false, unlockedAt: null },
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
