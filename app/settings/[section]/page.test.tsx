import { screen } from '@testing-library/react';
const {
  mockPush,
  mockFetchSettingsSection,
  mockUpdateSettingsSection,
  mockTrack,
} = vi.hoisted(() => ({
  mockPush: vi.fn(),
  mockFetchSettingsSection: vi.fn(),
  mockUpdateSettingsSection: vi.fn(),
  mockTrack: vi.fn(),
}));
vi.mock('@/components/layout/app-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/admin-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/expert-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/sponsor-dashboard-shell', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/learner-workspace-container', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-center', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/notification-preferences-panel', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/top-nav', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/components/layout/sidebar', () => ({
  LearnerDashboardShell: ({ children, workspaceClassName }: { children: React.ReactNode; workspaceClassName?: string }) => (
    <div data-testid="learner-dashboard-shell" data-workspace-class={workspaceClassName}>{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({
  analytics: {
    track: mockTrack,
  },
}));

vi.mock('@/lib/api', () => ({
  fetchSettingsSection: mockFetchSettingsSection,
  updateSettingsSection: mockUpdateSettingsSection,
}));

vi.mock('@/lib/auth-client', () => ({
  deleteAccount: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: null, signOut: vi.fn() }),
}));

import SettingsSectionPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Settings section page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUpdateSettingsSection.mockResolvedValue({});
  });

  it('renders the profile section with a themed hero, helper card, tags, and field status pills', async () => {
    mockFetchSettingsSection.mockResolvedValue({
      section: 'profile',
      values: {
        displayName: 'Faisal Maqsood',
        email: 'learner@oet-prep.dev',
        professionId: 'nursing',
      },
    });

    const { container } = renderWithRouter(<SettingsSectionPage />, {
      params: { section: 'profile' },
      router: { push: mockPush },
    });

    /* Wait for async data to load — the hero description is always rendered */
    expect(await screen.findByText('Review and update your profile settings.')).toBeInTheDocument();
    /* Helper card content */
    expect(screen.getByText('Personal information')).toBeInTheDocument();
    expect(screen.getByText('Your identity details used across the platform.')).toBeInTheDocument();
    /* Identity badge from helper card */
    expect(screen.getAllByText('Identity').length).toBeGreaterThan(0);
    /* Field labels */
    expect(screen.getByText('Display Name')).toBeInTheDocument();
    expect(screen.getByText('Email')).toBeInTheDocument();
    expect(screen.getByText('Profession')).toBeInTheDocument();
    /* All 3 fields have values → all show 'Set' */
    expect(screen.getAllByText('Set').length).toBeGreaterThan(0);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    /* Blue-themed accent for profile section */
    expect(container.querySelector('.bg-blue-50.text-blue-700')).toBeTruthy();
  });

  it('renders the privacy section with toggle state pills, helper copy, and rose accents', async () => {
    mockFetchSettingsSection.mockResolvedValue({
      section: 'privacy',
      values: {
        storeRecordings: true,
        storeTranscripts: false,
        allowExpertAccess: true,
        consentHistoryNote: '',
      },
    });

    const { container } = renderWithRouter(<SettingsSectionPage />, {
      params: { section: 'privacy' },
      router: { push: mockPush },
    });

    /* Wait for async data to load */
    expect(await screen.findByText('Review and update your privacy settings.')).toBeInTheDocument();
    /* Helper card content */
    expect(screen.getByText('Evidence privacy')).toBeInTheDocument();
    expect(screen.getAllByText('Sensitive Data').length).toBeGreaterThan(0);
    /* Field tags */
    expect(screen.getByText('Storage')).toBeInTheDocument();
    expect(screen.getByText('Transcript')).toBeInTheDocument();
    expect(screen.getByText('Reviewer Access')).toBeInTheDocument();
    expect(screen.getByText('Consent')).toBeInTheDocument();
    /* Toggle states: storeRecordings=On, storeTranscripts=Off, allowExpertAccess=On */
    expect(screen.getAllByText('On').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Off').length).toBeGreaterThan(0);
    /* Rose-themed accent for privacy section */
    expect(container.querySelector('.bg-rose-50.text-rose-700')).toBeTruthy();
  });

  it('renders goals subtest tags and mixed configured states with purple accents', async () => {
    mockFetchSettingsSection.mockResolvedValue({
      section: 'goals',
      values: {
        overallGoal: 'Reach B in all modules.',
        targetScoresBySubtest: {
          writing: 350,
          speaking: 360,
          reading: '',
          listening: 380,
        },
        studyHoursPerWeek: 10,
      },
    });

    const { container } = renderWithRouter(<SettingsSectionPage />, {
      params: { section: 'goals' },
      router: { push: mockPush },
    });

    /* Wait for async data to load */
    expect(await screen.findByText('Review and update your goals settings.')).toBeInTheDocument();
    /* Helper card badge */
    expect(screen.getAllByText('Target Scores').length).toBeGreaterThan(0);
    /* Sub-test field tags */
    expect(screen.getAllByText('Writing').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Speaking').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Reading').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Listening').length).toBeGreaterThan(0);
    /* Study Pace tag */
    expect(screen.getByText('Study Pace')).toBeInTheDocument();
    /* Mixed states: 5 set (overallGoal, writing:350, speaking:360, listening:380, studyHoursPerWeek:10), 1 not set (reading:'') */
    expect(screen.getAllByText('Set').length).toBeGreaterThan(0);
    expect(screen.getByText('Not set')).toBeInTheDocument();
    /* Purple-themed accent for goals section */
    expect(container.querySelector('.bg-purple-50.text-purple-700')).toBeTruthy();
  });
});
