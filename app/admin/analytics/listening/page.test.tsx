import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockGetAnalytics, mockExportAttempt, mockUseAdminAuth } = vi.hoisted(() => ({
  mockGetAnalytics: vi.fn(),
  mockExportAttempt: vi.fn(),
  mockUseAdminAuth: vi.fn(),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => mockUseAdminAuth(),
}));

vi.mock('@/lib/listening-authoring-api', () => ({
  getListeningAdminAnalytics: (...args: unknown[]) => mockGetAnalytics(...args),
  exportListeningAdminAttempt: (id: string) => mockExportAttempt(id),
}));

import ListeningAnalyticsPage from './page';

const emptyAnalytics = {
  days: 30,
  completedAttempts: 0,
  averageScaledScore: null,
  percentLikelyPassing: 0,
  classPartAverages: [],
  hardestQuestions: [],
  distractorHeat: [],
  commonMisspellings: [],
};

describe('ListeningAnalyticsPage — Audit export', () => {
  let createObjectURL: ReturnType<typeof vi.fn>;
  let revokeObjectURL: ReturnType<typeof vi.fn>;
  let clickSpy: ReturnType<typeof vi.spyOn>;

  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetAnalytics.mockResolvedValue(emptyAnalytics);

    createObjectURL = vi.fn(() => 'blob:mock-url');
    revokeObjectURL = vi.fn();
    Object.defineProperty(URL, 'createObjectURL', { value: createObjectURL, configurable: true });
    Object.defineProperty(URL, 'revokeObjectURL', { value: revokeObjectURL, configurable: true });

    clickSpy = vi.spyOn(HTMLAnchorElement.prototype, 'click').mockImplementation(() => {});
  });

  afterEach(() => {
    clickSpy.mockRestore();
  });

  async function renderAndWaitForExportPanel(): Promise<HTMLInputElement> {
    render(<ListeningAnalyticsPage />);
    await waitFor(() => {
      expect(mockGetAnalytics).toHaveBeenCalled();
    });
    return waitFor(() => screen.getByLabelText('Listening attempt id') as HTMLInputElement);
  }

  it('exports a Listening attempt by id and triggers a JSON download', async () => {
    const user = userEvent.setup();
    const fixture = { attemptId: 'att-123', normalized: { foo: 1 }, legacy: { bar: 2 } };
    mockExportAttempt.mockResolvedValueOnce(fixture);

    const input = await renderAndWaitForExportPanel();

    fireEvent.change(input, { target: { value: 'att-123' } });
    await user.click(screen.getByRole('button', { name: /^Export$/ }));

    await waitFor(() => {
      expect(mockExportAttempt).toHaveBeenCalledWith('att-123');
    });
    await waitFor(() => {
      expect(createObjectURL).toHaveBeenCalledTimes(1);
    });
    expect(clickSpy).toHaveBeenCalledTimes(1);
    expect(revokeObjectURL).toHaveBeenCalledWith('blob:mock-url');
  });

  it('shows an inline error when the export fails with 404', async () => {
    const user = userEvent.setup();
    const err = Object.assign(new Error('HTTP 404'), { status: 404 });
    mockExportAttempt.mockRejectedValueOnce(err);

    const input = await renderAndWaitForExportPanel();

    fireEvent.change(input, { target: { value: 'missing-id' } });
    await user.click(screen.getByRole('button', { name: /^Export$/ }));

    await waitFor(() => {
      expect(mockExportAttempt).toHaveBeenCalledWith('missing-id');
    });
    expect(await screen.findByText('Attempt "missing-id" not found.')).toBeInTheDocument();
    expect(clickSpy).not.toHaveBeenCalled();
  });
});
