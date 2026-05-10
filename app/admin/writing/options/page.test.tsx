/**
 * Vitest spec for the Writing AI Options admin page.
 *
 * Mirrors the project pattern in `app/admin/ai-providers/page.test.tsx`:
 * `vi.hoisted` for shared mocks, mock `@/lib/api`, render the default page,
 * and assert form behaviour without spinning up the network layer.
 */
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { AdminWritingOptions } from '@/lib/api';

const { mockGet, mockUpdate } = vi.hoisted(() => ({
  mockGet: vi.fn(),
  mockUpdate: vi.fn(),
}));

vi.mock('@/lib/api', async () => {
  const actual = await vi.importActual<typeof import('@/lib/api')>('@/lib/api');
  return {
    ...actual,
    adminGetWritingOptions: mockGet,
    adminUpdateWritingOptions: mockUpdate,
  };
});

import WritingOptionsPage from './page';

const baseOptions: AdminWritingOptions = {
  aiGradingEnabled: true,
  aiCoachEnabled: true,
  killSwitchReason: null,
  freeTierEnabled: false,
  freeTierLimit: 0,
  freeTierWindowDays: 7,
  updatedAt: '2026-05-09T10:00:00Z',
  updatedByAdminId: 'admin-1',
};

describe('WritingOptionsPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('renders the loaded values into form fields', async () => {
    mockGet.mockResolvedValue(baseOptions);

    render(<WritingOptionsPage />);

    await waitFor(() => {
      expect(screen.getByLabelText('Free tier limit')).toBeInTheDocument();
    });

    expect(screen.getByLabelText('Free tier limit')).toHaveValue(0);
    expect(screen.getByLabelText('Free tier window days')).toHaveValue(7);
    // Both kill-switch toggles start ON, so the reason textarea should NOT
    // be rendered yet.
    expect(screen.queryByLabelText('Kill-switch reason')).not.toBeInTheDocument();
  });

  it('reveals the kill-switch reason textarea when AI Grading is toggled off', async () => {
    mockGet.mockResolvedValue(baseOptions);
    const user = userEvent.setup();

    render(<WritingOptionsPage />);

    const aiGradingSwitch = await screen.findByRole('switch', { name: 'AI Grading' });
    expect(aiGradingSwitch).toHaveAttribute('aria-checked', 'true');

    await user.click(aiGradingSwitch);

    await waitFor(() => {
      expect(
        screen.getByRole('switch', { name: 'AI Grading' }),
      ).toHaveAttribute('aria-checked', 'false');
    });
    expect(screen.getByLabelText('Kill-switch reason')).toBeInTheDocument();
  });

  it('calls adminUpdateWritingOptions with the edited payload on Save', async () => {
    mockGet.mockResolvedValue(baseOptions);
    mockUpdate.mockImplementation(async (input: Omit<AdminWritingOptions, 'updatedAt' | 'updatedByAdminId'>) => ({
      ...baseOptions,
      ...input,
      updatedAt: '2026-05-10T12:00:00Z',
      updatedByAdminId: 'admin-1',
    }));
    const user = userEvent.setup();

    render(<WritingOptionsPage />);

    const limitInput = (await screen.findByLabelText('Free tier limit')) as HTMLInputElement;
    // Number inputs in JSDOM are flaky with userEvent.clear+type — set the
    // controlled value directly via the React-instrumented change event.
    fireEvent.change(limitInput, { target: { value: '5' } });

    const freeTierSwitch = screen.getByRole('switch', { name: 'Free tier enabled' });
    await user.click(freeTierSwitch);

    await waitFor(() => {
      expect(
        screen.getByRole('switch', { name: 'Free tier enabled' }),
      ).toHaveAttribute('aria-checked', 'true');
    });

    const saveBtn = screen.getByRole('button', { name: /save changes/i });
    await user.click(saveBtn);

    await waitFor(() => {
      expect(mockUpdate).toHaveBeenCalledTimes(1);
    });

    const payload = mockUpdate.mock.calls[0][0] as Omit<
      AdminWritingOptions,
      'updatedAt' | 'updatedByAdminId'
    >;
    expect(payload.freeTierEnabled).toBe(true);
    expect(payload.freeTierLimit).toBe(5);
    expect(payload.aiGradingEnabled).toBe(true);
    // updatedAt / updatedByAdminId must NOT be sent in the request body.
    expect(payload).not.toHaveProperty('updatedAt');
    expect(payload).not.toHaveProperty('updatedByAdminId');

    expect(await screen.findByText('Writing options saved.')).toBeInTheDocument();
  });

  it('shows an error message when the initial fetch rejects', async () => {
    mockGet.mockRejectedValue(new Error('Boom — backend down'));

    render(<WritingOptionsPage />);

    expect(await screen.findByRole('alert')).toHaveTextContent('Boom — backend down');
  });
});
