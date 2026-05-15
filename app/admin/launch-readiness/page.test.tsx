import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdminLaunchReadinessSettings } from '@/lib/api';

const { mockFetch, mockUpdate } = vi.hoisted(() => ({
  mockFetch: vi.fn(),
  mockUpdate: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  fetchAdminLaunchReadinessSettings: mockFetch,
  updateAdminLaunchReadinessSettings: mockUpdate,
}));

import LaunchReadinessPage from './page';

const baseSettings: AdminLaunchReadinessSettings = {
  mobileMinSupportedVersion: '1.0.0',
  mobileLatestVersion: '1.1.0',
  mobileForceUpdate: false,
  iosAppStoreUrl: null,
  androidPlayStoreUrl: null,
  iosBundleId: null,
  appleTeamId: null,
  appleAssociatedDomainStatus: 'pending',
  appleUniversalLinksStatus: null,
  iosSigningProfileReference: null,
  iosIapStatus: null,
  iosPushStatus: null,
  androidPackageName: null,
  androidSha256Fingerprints: null,
  androidSigningKeyReference: null,
  androidAssetLinksStatus: 'pending',
  androidIapStatus: null,
  androidPushStatus: null,
  desktopMinSupportedVersion: '1.0.0',
  desktopLatestVersion: '1.0.0',
  desktopForceUpdate: false,
  desktopUpdateFeedUrl: null,
  desktopUpdateChannel: null,
  windowsSigningStatus: 'pending',
  macSigningStatus: null,
  linuxSigningStatus: null,
  deviceValidationEvidenceUrl: null,
  deviceValidationNotes: null,
  realtimeLegalApprovalStatus: 'pending',
  realtimePrivacyApprovalStatus: 'pending',
  realtimeProtectedSmokeStatus: 'pending',
  realtimeEvidenceUrl: null,
  realtimeSpendCapApproved: false,
  realtimeTopologyApproved: false,
  releaseOwnerApprovalStatus: 'pending',
  launchNotes: null,
  updatedAt: '2026-05-15T12:00:00Z',
  updatedByAdminId: 'admin-1',
  updatedByAdminName: 'Launch Admin',
};

describe('LaunchReadinessPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders launch readiness fields from the backend', async () => {
    mockFetch.mockResolvedValue(baseSettings);

    render(<LaunchReadinessPage />);

    expect(await screen.findByLabelText('Minimum supported mobile version')).toHaveValue('1.0.0');
    expect(screen.getByLabelText('Force mobile update')).not.toBeChecked();
    expect(screen.getByLabelText('Legal approval status')).toHaveValue('pending');
  });

  it('saves only edited launch readiness fields', async () => {
    mockFetch.mockResolvedValue(baseSettings);
    mockUpdate.mockImplementation(async (input: Partial<AdminLaunchReadinessSettings>) => ({
      ...baseSettings,
      ...input,
      updatedAt: '2026-05-15T13:00:00Z',
    }));
    const user = userEvent.setup();

    render(<LaunchReadinessPage />);

    const minVersion = await screen.findByLabelText('Minimum supported mobile version');
    fireEvent.change(minVersion, { target: { value: '1.2.0' } });
    await user.click(screen.getByLabelText('Force mobile update'));
    await user.click(screen.getByRole('button', { name: /save changes/i }));

    await waitFor(() => expect(mockUpdate).toHaveBeenCalledTimes(1));
    expect(mockUpdate).toHaveBeenCalledWith({
      mobileMinSupportedVersion: '1.2.0',
      mobileForceUpdate: true,
    });
    expect(await screen.findByText('Launch readiness settings saved.')).toBeInTheDocument();
  });

  it('shows backend errors without hiding the form', async () => {
    mockFetch.mockResolvedValue(baseSettings);
    mockUpdate.mockRejectedValue(new Error('Invalid release URL'));
    const user = userEvent.setup();

    render(<LaunchReadinessPage />);

    fireEvent.change(await screen.findByLabelText('iOS App Store URL'), { target: { value: 'https://localhost/app' } });
    await user.click(screen.getByRole('button', { name: /save changes/i }));

    expect(await screen.findByText('Invalid release URL')).toBeInTheDocument();
    expect(screen.getByLabelText('iOS App Store URL')).toBeInTheDocument();
  });
});
