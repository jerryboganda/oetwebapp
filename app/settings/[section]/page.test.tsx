import { render, screen } from '@testing-library/react';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const {
  mockUseParams,
  mockPush,
  mockFetchSettingsSection,
  mockUpdateSettingsSection,
  mockTrack,
} = vi.hoisted(() => ({
  mockUseParams: vi.fn(),
  mockPush: vi.fn(),
  mockFetchSettingsSection: vi.fn(),
  mockUpdateSettingsSection: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => mockUseParams(),
  useRouter: () => ({
    push: mockPush,
  }),
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

import SettingsSectionPage from './page';

describe('Settings section page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUpdateSettingsSection.mockResolvedValue({});
  });

  it('renders the profile section with a themed hero, helper card, tags, and field status pills', async () => {
    mockUseParams.mockReturnValue({ section: 'profile' });
    mockFetchSettingsSection.mockResolvedValue({
      section: 'profile',
      values: {
        displayName: 'Faisal Maqsood',
        email: 'learner@oet-prep.dev',
        professionId: 'nursing',
        deviceVisibility: '',
      },
    });

    const { container } = render(<SettingsSectionPage />);

    expect(await screen.findByText('Keep profile settings clear before you change them')).toBeInTheDocument();
    expect(screen.getByText('What changes here')).toBeInTheDocument();
    expect(screen.getAllByText('Identity').length).toBeGreaterThan(0);
    expect(screen.getByText('Account')).toBeInTheDocument();
    expect(screen.getByText('Study Content')).toBeInTheDocument();
    expect(screen.getByText('Device Visibility')).toBeInTheDocument();
    expect(screen.getAllByText('Set').length).toBeGreaterThan(0);
    expect(screen.getByText('Not set')).toBeInTheDocument();
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
    expect(container.querySelector('[class*="max-w-3xl"][class*="mx-auto"][class*="px-4"]')).not.toBeInTheDocument();
    expect(container.querySelector('.bg-blue-50.text-blue-700')).toBeTruthy();
  });

  it('renders the privacy section with toggle state pills, helper copy, and rose accents', async () => {
    mockUseParams.mockReturnValue({ section: 'privacy' });
    mockFetchSettingsSection.mockResolvedValue({
      section: 'privacy',
      values: {
        storeRecordings: true,
        storeTranscripts: false,
        allowExpertAccess: true,
        consentHistoryNote: '',
      },
    });

    const { container } = render(<SettingsSectionPage />);

    expect(await screen.findByText('Keep privacy settings clear before you change them')).toBeInTheDocument();
    expect(screen.getByText('OET evidence privacy')).toBeInTheDocument();
    expect(screen.getByText('Sensitive Data')).toBeInTheDocument();
    expect(screen.getByText('Storage')).toBeInTheDocument();
    expect(screen.getByText('Transcript')).toBeInTheDocument();
    expect(screen.getByText('Reviewer Access')).toBeInTheDocument();
    expect(screen.getByText('Consent')).toBeInTheDocument();
    expect(screen.getAllByText('On').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Off').length).toBeGreaterThan(0);
    expect(container.querySelector('.bg-rose-50.text-rose-700')).toBeTruthy();
  });

  it('renders goals subtest tags and mixed configured states with purple accents', async () => {
    mockUseParams.mockReturnValue({ section: 'goals' });
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

    const { container } = render(<SettingsSectionPage />);

    expect(await screen.findByText('Keep goals settings clear before you change them')).toBeInTheDocument();
    expect(screen.getAllByText('Target Scores').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Writing').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Speaking').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Reading').length).toBeGreaterThan(0);
    expect(screen.getAllByText('Listening').length).toBeGreaterThan(0);
    expect(screen.getByText('Study Pace')).toBeInTheDocument();
    expect(screen.getAllByText('Set').length).toBeGreaterThan(0);
    expect(screen.getByText('Not set')).toBeInTheDocument();
    expect(container.querySelector('.bg-purple-50.text-purple-700')).toBeTruthy();
  });
});
