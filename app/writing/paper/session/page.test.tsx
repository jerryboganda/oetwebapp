import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockPush, mockReplace, mockUseSearchParams } = vi.hoisted(() => ({
  mockPush: vi.fn(),
  mockReplace: vi.fn(),
  mockUseSearchParams: vi.fn(() => new URLSearchParams('')),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({
    push: mockPush,
    replace: mockReplace,
  }),
  useSearchParams: () => mockUseSearchParams(),
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));

import WritingPaperSessionIndexPage from './page';

describe('WritingPaperSessionIndexPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseSearchParams.mockReturnValue(new URLSearchParams(''));
  });

  it('redirects to the dynamic session route when id exists in search params', async () => {
    mockUseSearchParams.mockReturnValue(new URLSearchParams('id=mock-session-001'));

    render(<WritingPaperSessionIndexPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/writing/paper/session/mock-session-001');
    });
  });

  it('trims and URL-encodes query-param session ids before redirecting', async () => {
    mockUseSearchParams.mockReturnValue(new URLSearchParams('id=%20mock/session%20'));

    render(<WritingPaperSessionIndexPage />);

    await waitFor(() => {
      expect(mockReplace).toHaveBeenCalledWith('/writing/paper/session/mock%2Fsession');
    });
  });

  it('allows manual session-id navigation from the quick-open form', async () => {
    const user = userEvent.setup();
    render(<WritingPaperSessionIndexPage />);

    const input = screen.getByLabelText('Session ID');
    await user.type(input, 'paper-session-42');
    await user.click(screen.getByRole('button', { name: /open session/i }));

    expect(mockPush).toHaveBeenCalledWith('/writing/paper/session/paper-session-42');
  });

  it('keeps Open session disabled until a non-empty id is provided', () => {
    render(<WritingPaperSessionIndexPage />);

    expect(screen.getByRole('button', { name: /open session/i })).toBeDisabled();
  });

  it('does not allow whitespace-only ids to be submitted', async () => {
    const user = userEvent.setup();
    render(<WritingPaperSessionIndexPage />);

    const input = screen.getByLabelText('Session ID');
    await user.type(input, '   ');

    expect(screen.getByRole('button', { name: /open session/i })).toBeDisabled();
    expect(mockPush).not.toHaveBeenCalled();
  });
});