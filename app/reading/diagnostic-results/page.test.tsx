import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const {
  mockRouterPush,
  mockRouterReplace,
  mockUseAuth,
  mockUseSearchParams,
  mockGetDiagnosticResult,
} = vi.hoisted(() => ({
  mockRouterPush: vi.fn(),
  mockRouterReplace: vi.fn(),
  mockUseAuth: vi.fn(),
  mockUseSearchParams: vi.fn(),
  mockGetDiagnosticResult: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockRouterPush,
    replace: mockRouterReplace,
  }),
  useSearchParams: () => mockUseSearchParams(),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/reading-pathway-api', () => ({
  getDiagnosticResult: (...args: unknown[]) => mockGetDiagnosticResult(...args),
}));

import DiagnosticResultsPage from './page';
import type { DiagnosticResultDto } from '@/lib/reading-pathway-api';

function buildDiagnosticResult(overrides: Partial<DiagnosticResultDto> = {}): DiagnosticResultDto {
  return {
    sessionId: 'session-1',
    score: 18,
    totalQuestions: 22,
    skillScores: { S1: 5, S2: 6 },
    estimatedOetBand: 'B',
    estimatedScaledScore: 350,
    durationSeconds: 1800,
    roadmapWeeks: 12,
    completedAt: '2026-06-01T10:00:00Z',
    ...overrides,
  };
}

describe('DiagnosticResultsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ isAuthenticated: true, loading: false });
    mockUseSearchParams.mockReturnValue(new URLSearchParams('sessionId=session-1'));
    mockGetDiagnosticResult.mockResolvedValue(buildDiagnosticResult());
    window.sessionStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it('loads the cached result and still navigates when storage clearing fails', async () => {
    window.sessionStorage.setItem('diagnostic_result', JSON.stringify(buildDiagnosticResult()));
    vi.spyOn(Storage.prototype, 'removeItem').mockImplementation(() => {
      throw new DOMException('The operation is insecure.', 'SecurityError');
    });

    render(<DiagnosticResultsPage />);

    expect(await screen.findByText(/your estimated oet reading score/i)).toBeInTheDocument();
    fireEvent.click(screen.getByRole('button', { name: /start my learning plan/i }));

    expect(mockRouterPush).toHaveBeenCalledWith('/reading/pathway');
  });

  it('falls back to the API when no cached result is available', async () => {
    render(<DiagnosticResultsPage />);

    expect(await screen.findByText(/your estimated oet reading score/i)).toBeInTheDocument();
    expect(mockGetDiagnosticResult).toHaveBeenCalledWith('session-1');
  });
});