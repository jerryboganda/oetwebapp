import { screen, waitFor } from '@testing-library/react';
import AndroidInstallPage from './page';
import { renderWithRouter } from '@/tests/test-utils';
import type { InstallSourceKind } from '@/lib/mobile/install-source';

let mockInstallSourceKind: InstallSourceKind = 'unknown';

vi.mock('@/lib/mobile/install-source', () => ({
  getInstallSource: () => Promise.resolve({
    kind: mockInstallSourceKind,
    installerPackage: mockInstallSourceKind === 'play' ? 'com.android.vending' : null,
    versionCode: null,
  }),
  getPlayListingUrl: () => 'https://play.google.com/store/apps/details?id=com.oetwithdrhesham.app',
}));

describe('AndroidInstallPage installer routing', () => {
  beforeEach(() => {
    mockInstallSourceKind = 'unknown';
  });

  it('routes a Play-installed copy to Google Play and hides the direct APK', async () => {
    mockInstallSourceKind = 'play';

    renderWithRouter(<AndroidInstallPage />);

    expect(await screen.findByRole('button', { name: /Update in Google Play/i })).toBeInTheDocument();
    expect(screen.queryByRole('link', { name: /Download the update/i })).not.toBeInTheDocument();
    expect(screen.getByText(/App not installed/)).toBeInTheDocument();
  });

  it('keeps the direct APK flow for a sideloaded copy', async () => {
    mockInstallSourceKind = 'sideload';

    renderWithRouter(<AndroidInstallPage />);

    await waitFor(() => {
      expect(screen.queryByRole('button', { name: /Update in Google Play/i })).not.toBeInTheDocument();
    });
    expect(screen.getByRole('link', { name: /Download the update/i })).toBeInTheDocument();
  });

  it('offers both channels when the source is unknown (plain browser)', async () => {
    mockInstallSourceKind = 'unknown';

    renderWithRouter(<AndroidInstallPage />);

    expect(await screen.findByRole('link', { name: /Download the update/i })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /Update it there/i })).toBeInTheDocument();
  });
});
