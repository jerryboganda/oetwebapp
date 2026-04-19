/**
 * @vitest-environment jsdom
 */
import { screen, fireEvent, waitFor } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';

const { mockGetProgressPolicy, mockUpdateProgressPolicy, mockUseAdminAuth } = vi.hoisted(() => ({
  mockGetProgressPolicy: vi.fn(),
  mockUpdateProgressPolicy: vi.fn(),
  mockUseAdminAuth: vi.fn(),
}));

let consoleErrorSpy: ReturnType<typeof vi.spyOn>;

vi.mock('@/lib/progress-policy-admin-api', () => ({
  getProgressPolicy: mockGetProgressPolicy,
  updateProgressPolicy: mockUpdateProgressPolicy,
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: () => mockUseAdminAuth(),
}));

import ProgressPolicyAdminPage from './page';

const samplePolicy = {
  id: 'pp-oet',
  examFamilyCode: 'oet',
  defaultTimeRange: '90d' as const,
  smoothingWindow: 3,
  minCohortSize: 30,
  mockDistinctStyle: true,
  showScoreGuaranteeStrip: true,
  showCriterionConfidenceBand: true,
  minEvaluationsForTrend: 2,
  exportPdfEnabled: false,
  updatedByAdminId: 'admin-1',
  updatedAt: '2026-04-20T12:00:00Z',
};

describe('ProgressPolicyAdminPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    consoleErrorSpy = vi.spyOn(console, 'error').mockImplementation((message?: unknown, ...args: unknown[]) => {
      const first = typeof message === 'string' ? message : '';
      if (first.includes('ProgressPolicyAdminPage inside a test was not wrapped in act')) {
        return;
      }
      if (first.includes('This ensures that you\'re testing the behavior the user would see in the browser.')) {
        return;
      }
      return undefined;
    });
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetProgressPolicy.mockResolvedValue(samplePolicy);
    mockUpdateProgressPolicy.mockResolvedValue({ ...samplePolicy, exportPdfEnabled: true });
  });

  afterEach(() => {
    consoleErrorSpy.mockRestore();
  });

  it('renders after loading the policy', async () => {
    renderWithRouter(<ProgressPolicyAdminPage />);
    await waitFor(() => expect(mockGetProgressPolicy).toHaveBeenCalledWith('oet'));
    expect(await screen.findByRole('heading', { name: /progress policy/i })).toBeInTheDocument();
  });

  it('exposes the save button', async () => {
    renderWithRouter(<ProgressPolicyAdminPage />);
    await waitFor(() => expect(mockGetProgressPolicy).toHaveBeenCalledWith('oet'));
    expect(await screen.findByRole('button', { name: /save policy/i })).toBeInTheDocument();
  });

  it('saves the policy when the user clicks save', async () => {
    renderWithRouter(<ProgressPolicyAdminPage />);
    await waitFor(() => expect(mockGetProgressPolicy).toHaveBeenCalledWith('oet'));
    const saveBtn = await screen.findByRole('button', { name: /save policy/i });
    fireEvent.click(saveBtn);
    await waitFor(() => expect(mockUpdateProgressPolicy).toHaveBeenCalled());
    await waitFor(() => expect(mockGetProgressPolicy).toHaveBeenCalledTimes(2));
    const callArgs = mockUpdateProgressPolicy.mock.calls[0];
    expect(callArgs[0]).toBe('oet');
    expect(callArgs[1]).toMatchObject({
      defaultTimeRange: '90d',
      smoothingWindow: 3,
      minCohortSize: 30,
    });
  });

  it('blocks unauthenticated users', () => {
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: false, role: null });
    renderWithRouter(<ProgressPolicyAdminPage />);
    expect(screen.getByText(/admin access required/i)).toBeInTheDocument();
  });
});
