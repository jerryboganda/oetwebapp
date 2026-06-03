import { screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { renderWithRouter } from '@/tests/test-utils';
import type { AdminSignupCatalogResponse, AdminUserDetail } from '@/lib/types/admin';

const {
  mockUseAdminAuth,
  mockGetAdminUserDetailData,
  mockFetchAdminSignupCatalog,
  mockUpdateAdminUserProfile,
  mockSetAdminUserPassword,
} = vi.hoisted(() => ({
  mockUseAdminAuth: vi.fn(),
  mockGetAdminUserDetailData: vi.fn(),
  mockFetchAdminSignupCatalog: vi.fn(),
  mockUpdateAdminUserProfile: vi.fn(),
  mockSetAdminUserPassword: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ id: 'learner-1' }),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));
vi.mock('@/lib/admin', () => ({ getAdminUserDetailData: (...args: unknown[]) => mockGetAdminUserDetailData(...args) }));
vi.mock('@/lib/api', () => ({
  adjustAdminUserCredits: vi.fn(),
  deleteAdminUser: vi.fn(),
  fetchAdminPermissions: vi.fn(),
  fetchAdminSignupCatalog: (...args: unknown[]) => mockFetchAdminSignupCatalog(...args),
  resendAdminUserInvite: vi.fn(),
  restoreAdminUser: vi.fn(),
  revokeAdminUserSessions: vi.fn(),
  setAdminUserPassword: (...args: unknown[]) => mockSetAdminUserPassword(...args),
  triggerAdminUserPasswordReset: vi.fn(),
  unlockAdminUser: vi.fn(),
  updateAdminUserProfile: (...args: unknown[]) => mockUpdateAdminUserProfile(...args),
  updateAdminUserStatus: vi.fn(),
}));

import UserDetailPage from './page';

function buildUser(overrides: Partial<AdminUserDetail> = {}): AdminUserDetail {
  return {
    id: 'learner-1',
    name: 'Learner One',
    email: 'learner@example.test',
    role: 'learner',
    status: 'active',
    lastLogin: null,
    createdAt: '2026-01-01T00:00:00Z',
    authAccountId: 'auth-learner-1',
    profession: 'nursing',
    tasksCompleted: 0,
    creditBalance: 0,
    displayName: 'Learner One',
    firstName: 'Learner',
    lastName: 'One',
    mobileNumber: '+441234567890',
    professionId: 'nursing',
    examTypeId: 'oet',
    countryTarget: 'Australia',
    timezone: 'UTC',
    locale: 'en-AU',
    marketingOptIn: false,
    agreeToTerms: true,
    agreeToPrivacy: true,
    attribution: null,
    security: null,
    subscription: null,
    recentActivity: [],
    availableActions: {
      canSuspend: false,
      canDelete: false,
      canRestore: false,
      canAdjustCredits: false,
      canTriggerPasswordReset: true,
      canForceSignOut: false,
      canUnlock: false,
      canResendInvite: false,
    },
    ...overrides,
  };
}

const baseCatalog: AdminSignupCatalogResponse = {
  examTypes: [
    { id: 'oet', code: 'OET', label: 'OET', description: 'Occupational English Test', sortOrder: 1, isActive: true },
  ],
  professions: [
    {
      id: 'nursing',
      label: 'Nursing',
      description: 'Nursing pathway',
      examTypeIds: ['oet'],
      countryTargets: [],
      sortOrder: 1,
      isActive: true,
    },
    {
      id: 'medicine',
      label: 'Medicine',
      description: 'Medicine pathway',
      examTypeIds: ['oet'],
      countryTargets: [],
      sortOrder: 2,
      isActive: true,
    },
  ],
};

describe('UserDetailPage profile catalog fields', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetAdminUserDetailData.mockResolvedValue(buildUser());
    mockFetchAdminSignupCatalog.mockResolvedValue(baseCatalog);
    mockUpdateAdminUserProfile.mockResolvedValue({ updated: true });
    mockSetAdminUserPassword.mockResolvedValue({ userId: 'learner-1', email: 'learner@example.test', revoked: 0 });
  });

  it('treats an empty profession country list as all supported target countries', async () => {
    renderWithRouter(<UserDetailPage />);

    await userEvent.click(await screen.findByRole('button', { name: /edit profile/i }));

    const targetCountrySelect = await screen.findByLabelText('Target country') as HTMLSelectElement;
    const values = Array.from(targetCountrySelect.options).map((option) => option.value);

    expect(targetCountrySelect).toHaveValue('Australia');
    expect(values).toEqual(expect.arrayContaining(['United Kingdom', 'Australia', 'Qatar']));
  });

  it('preserves a stored profession value that is missing from the current signup catalog', async () => {
    const user = userEvent.setup();
    mockFetchAdminSignupCatalog.mockResolvedValue({
      ...baseCatalog,
      professions: baseCatalog.professions.filter((profession) => profession.id === 'medicine'),
    });

    renderWithRouter(<UserDetailPage />);

    await user.click(await screen.findByRole('button', { name: /edit profile/i }));

    const professionSelect = await screen.findByLabelText('Profession') as HTMLSelectElement;

    await waitFor(() => expect(professionSelect).toHaveValue('nursing'));
    expect(screen.getByRole('option', { name: /current value: nursing/i })).toBeInTheDocument();

    await user.clear(screen.getByLabelText('Mobile number'));
    await user.type(screen.getByLabelText('Mobile number'), '+441234567891');
    await user.click(screen.getByRole('button', { name: /save profile/i }));

    await waitFor(() => expect(mockUpdateAdminUserProfile).toHaveBeenCalled());
    expect(mockUpdateAdminUserProfile).toHaveBeenCalledWith('learner-1', {
      mobileNumber: '+441234567891',
    });
  });

  it('does not submit unchanged catalog fields after normalizing labels to ids', async () => {
    const user = userEvent.setup();
    mockGetAdminUserDetailData.mockResolvedValue(buildUser({
      profession: 'Other Allied health profession',
      professionId: 'Other Allied health profession',
      examTypeId: 'OET',
    }));
    mockFetchAdminSignupCatalog.mockResolvedValue({
      ...baseCatalog,
      professions: [
        ...baseCatalog.professions,
        {
          id: 'other-allied-health',
          label: 'Other Allied health profession',
          description: 'Other allied health pathway',
          examTypeIds: ['oet'],
          countryTargets: [],
          sortOrder: 3,
          isActive: true,
        },
      ],
    });

    renderWithRouter(<UserDetailPage />);

    await user.click(await screen.findByRole('button', { name: /edit profile/i }));
    await waitFor(() => expect(screen.getByLabelText('Profession')).toHaveValue('other-allied-health'));

    await user.clear(screen.getByLabelText('Mobile number'));
    await user.type(screen.getByLabelText('Mobile number'), '+441234567892');
    await user.click(screen.getByRole('button', { name: /save profile/i }));

    await waitFor(() => expect(mockUpdateAdminUserProfile).toHaveBeenCalled());
    expect(mockUpdateAdminUserProfile).toHaveBeenCalledWith('learner-1', {
      mobileNumber: '+441234567892',
    });
  });

  it('renders the manual set password action for eligible users', async () => {
    renderWithRouter(<UserDetailPage />);

    expect(await screen.findByRole('button', { name: /^set password$/i })).toBeInTheDocument();
  });

  it('submits a manual password update and revokes active sessions', async () => {
    const user = userEvent.setup();

    renderWithRouter(<UserDetailPage />);

    await user.click(await screen.findByRole('button', { name: /^set password$/i }));
    await user.type(await screen.findByLabelText('New password'), 'BetterPassword123!');
    await user.type(screen.getByLabelText('Confirm password'), 'BetterPassword123!');
    await user.click(screen.getByRole('button', { name: /^save password$/i }));

    await waitFor(() => expect(mockSetAdminUserPassword).toHaveBeenCalledWith('learner-1', { password: 'BetterPassword123!' }));
    expect(await screen.findByText(/password updated for learner@example\.test/i)).toBeInTheDocument();
  });

  it('blocks submission when the password fields do not match', async () => {
    const user = userEvent.setup();

    renderWithRouter(<UserDetailPage />);

    await user.click(await screen.findByRole('button', { name: /^set password$/i }));
    await user.type(await screen.findByLabelText('New password'), 'BetterPassword123!');
    await user.type(screen.getByLabelText('Confirm password'), 'DifferentPassword123!');
    await user.click(screen.getByRole('button', { name: /^save password$/i }));

    expect(mockSetAdminUserPassword).not.toHaveBeenCalled();
    expect(await screen.findByText(/passwords do not match/i)).toBeInTheDocument();
  });

  it('shows the backend password policy reason when the update is rejected', async () => {
    const user = userEvent.setup();
    mockSetAdminUserPassword.mockRejectedValueOnce(
      new Error('This password has appeared in a known public data breach. Please choose a different password.'),
    );

    renderWithRouter(<UserDetailPage />);

    await user.click(await screen.findByRole('button', { name: /^set password$/i }));
    expect(await screen.findByText(/avoid common\/leaked passwords/i)).toBeInTheDocument();
    await user.type(await screen.findByLabelText('New password'), 'BetterPassword123!');
    await user.type(screen.getByLabelText('Confirm password'), 'BetterPassword123!');
    await user.click(screen.getByRole('button', { name: /^save password$/i }));

    expect(await screen.findAllByText(/known public data breach/i)).not.toHaveLength(0);
  });
});
