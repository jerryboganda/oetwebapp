import { screen } from '@testing-library/react';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
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

vi.mock('@/components/layout', () => ({
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

vi.mock('next/link', () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>{children}</a>
  ),
}));

import SettingsSectionPage from './page';
import { renderWithRouter } from '@/tests/test-utils';

describe('Settings section page', () => {
  function renderSection(section: string) {
    const client = new QueryClient({
      defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
    });
    return renderWithRouter(
      <QueryClientProvider client={client}>
        <SettingsSectionPage />
      </QueryClientProvider>,
      {
        params: { section },
        router: { push: mockPush },
      },
    );
  }

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

    const { container } = renderSection('profile');

    /* Wait for async data to load — the hero description is always rendered */
    expect(await screen.findByText('Review and update your profile settings.')).toBeInTheDocument();
    /* Helper card content */
    expect(await screen.findByText('Personal information')).toBeInTheDocument();
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

  it('renders the privacy section pointing to the real recordings/data controls, not disconnected toggles', async () => {
    // Privacy no longer fetches or renders preference toggles: recording
    // retention/consent/reviewer access are governed by the compliance system,
    // so this section points to the real My-Recordings + account-deletion controls.
    const { container } = renderSection('privacy');

    /* Hero description derived from the section title */
    expect(await screen.findByText('Review and update your privacy & data settings.')).toBeInTheDocument();
    /* Helper card content */
    expect(await screen.findByText('Recordings & data')).toBeInTheDocument();
    expect(screen.getAllByText('Your Data').length).toBeGreaterThan(0);
    /* Real controls replace the old disconnected toggles */
    expect(screen.getByText('Your recordings & data')).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Manage my recordings/i })).toHaveAttribute('href', '/speaking/recordings');
    expect(screen.getByRole('link', { name: /Delete my account & data/i })).toHaveAttribute('href', '/settings/danger-zone');
    /* The old fake toggles are gone */
    expect(screen.queryByRole('switch', { name: 'Toggle Store speaking recordings' })).toBeNull();
    /* No remote fetch for the informational privacy section */
    expect(mockFetchSettingsSection).not.toHaveBeenCalled();
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

    const { container } = renderSection('goals');

    /* Wait for async data to load */
    expect(await screen.findByText('Review and update your goals settings.')).toBeInTheDocument();
    /* Helper card badge */
    expect(screen.getAllByText('Target Scores').length).toBeGreaterThan(0);
    /* Sub-test target score controls */
    expect(await screen.findByLabelText('Writing target score')).toBeInTheDocument();
    expect(screen.getByLabelText('Speaking target score')).toBeInTheDocument();
    expect(screen.getByLabelText('Reading target score')).toBeInTheDocument();
    expect(screen.getByLabelText('Listening target score')).toBeInTheDocument();
    /* Study Pace tag */
    expect(screen.getByText('Study Pace')).toBeInTheDocument();
    /* Mixed states: 5 set (overallGoal, writing:350, speaking:360, listening:380, studyHoursPerWeek:10), 1 not set (reading:'') */
    expect(container).toHaveTextContent('5/6 configured');
    expect(screen.getByLabelText('Reading target score')).toHaveValue(null);
    expect(screen.getByLabelText('Writing target score')).toHaveValue(350);
    expect(screen.getByLabelText('Speaking target score')).toHaveValue(360);
    expect(screen.getByLabelText('Listening target score')).toHaveValue(380);
    /* Purple-themed accent for goals section */
    expect(container.querySelector('.bg-purple-50.text-purple-700')).toBeTruthy();
  });
});
