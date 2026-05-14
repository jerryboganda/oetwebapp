import { render, screen, waitFor, within } from '@testing-library/react';

const { mockListLeakReports, mockUpdateLeakReport } = vi.hoisted(() => ({
  mockListLeakReports: vi.fn(),
  mockUpdateLeakReport: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ href, children, ...props }: { href: string; children: React.ReactNode }) => (
    <a href={href} {...props}>{children}</a>
  ),
}));

vi.mock('@/lib/api', () => ({
  listAdminMockLeakReports: (...args: unknown[]) => mockListLeakReports(...args),
  updateAdminMockLeakReport: (...args: unknown[]) => mockUpdateLeakReport(...args),
}));

import AdminMockLeakReportsPage from './page';

describe('AdminMockLeakReportsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListLeakReports.mockResolvedValue({
      items: [
        {
          id: 'leak-1',
          bundleId: 'bundle-1',
          bundleTitle: 'Sample Mock',
          attemptId: 'attempt-1',
          severity: 'high',
          reasonCode: 'shared_publicly',
          pageOrQuestion: 'Q12',
          evidenceUrl: null,
          reportedByUserDisplayName: 'Learner One',
          reportedByUserId: 'learner-1',
          createdAt: '2026-05-12T00:00:00.000Z',
          status: 'open',
          resolvedAt: null,
          resolutionNote: null,
        },
      ],
    });
  });

  it('only announces counts for the currently loaded filter', async () => {
    render(<AdminMockLeakReportsPage />);

    await waitFor(() => expect(mockListLeakReports).toHaveBeenCalledWith({ status: 'open', limit: 50 }));

    expect(screen.getByRole('button', { name: 'Open reports (1 loaded)' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Investigating reports' })).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: /Investigating reports \(0/ })).not.toBeInTheDocument();

    const toolbar = screen.getByRole('toolbar', { name: 'Filter leak reports by status' });
    expect(within(toolbar).queryByText('0')).not.toBeInTheDocument();
  });
});