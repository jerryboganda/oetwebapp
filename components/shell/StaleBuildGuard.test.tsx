import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { StaleBuildGuard } from './StaleBuildGuard';

const { mockGetAppRuntimeKind, mockHardReload } = vi.hoisted(() => ({
  mockGetAppRuntimeKind: vi.fn(),
  mockHardReload: vi.fn(),
}));

vi.mock('@/lib/runtime-signals', () => ({
  getAppRuntimeKind: mockGetAppRuntimeKind,
}));

vi.mock('@/lib/shell/hard-reload', () => ({
  hardReload: mockHardReload,
}));

describe('StaleBuildGuard', () => {
  beforeEach(() => {
    mockGetAppRuntimeKind.mockReset();
    mockHardReload.mockReset();
    (window as unknown as { __NEXT_DATA__?: { buildId?: string } }).__NEXT_DATA__ = { buildId: 'build-current' };
    vi.stubGlobal('fetch', vi.fn());
  });

  afterEach(() => {
    vi.unstubAllGlobals();
    delete (window as unknown as { __NEXT_DATA__?: { buildId?: string } }).__NEXT_DATA__;
  });

  it('does nothing on the plain web runtime (no fetch, no banner)', async () => {
    mockGetAppRuntimeKind.mockReturnValue('web');

    render(<StaleBuildGuard />);

    expect(global.fetch).not.toHaveBeenCalled();
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('stays silent on the Capacitor shell when the live build id matches', async () => {
    mockGetAppRuntimeKind.mockReturnValue('capacitor-native');
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      text: () => Promise.resolve('<script>{"buildId":"build-current"}</script>'),
    });

    render(<StaleBuildGuard />);

    await waitFor(() => expect(global.fetch).toHaveBeenCalled());
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });

  it('shows a manual-refresh banner on the Capacitor shell when the live build id differs, and Refresh triggers hardReload', async () => {
    const user = userEvent.setup();
    mockGetAppRuntimeKind.mockReturnValue('capacitor-native');
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      text: () => Promise.resolve('<script>{"buildId":"build-newer"}</script>'),
    });

    render(<StaleBuildGuard />);

    expect(await screen.findByRole('status')).toBeInTheDocument();
    expect(screen.getByText(/newer version/i)).toBeInTheDocument();

    await user.click(screen.getByRole('button', { name: /refresh/i }));
    expect(mockHardReload).toHaveBeenCalledOnce();
  });

  it('does not reload automatically — staleness only ever shows a manual banner', async () => {
    mockGetAppRuntimeKind.mockReturnValue('capacitor-native');
    (global.fetch as ReturnType<typeof vi.fn>).mockResolvedValue({
      ok: true,
      text: () => Promise.resolve('<script>{"buildId":"build-newer"}</script>'),
    });

    render(<StaleBuildGuard />);

    await screen.findByRole('status');
    expect(mockHardReload).not.toHaveBeenCalled();
  });

  it('stays silent on a fetch failure instead of showing a false-positive banner', async () => {
    mockGetAppRuntimeKind.mockReturnValue('capacitor-native');
    (global.fetch as ReturnType<typeof vi.fn>).mockRejectedValue(new Error('offline'));

    render(<StaleBuildGuard />);

    await waitFor(() => expect(global.fetch).toHaveBeenCalled());
    expect(screen.queryByRole('status')).not.toBeInTheDocument();
  });
});
