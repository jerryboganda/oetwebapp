import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const api = vi.hoisted(() => ({
  adminGenerateWritingAiDraft: vi.fn(),
  isApiError: (e: unknown): e is { status: number; code: string; message: string } =>
    typeof e === 'object' && e !== null && 'status' in e && 'code' in e,
}));

vi.mock('@/lib/api', () => ({
  adminGenerateWritingAiDraft: api.adminGenerateWritingAiDraft,
  isApiError: api.isApiError,
}));

import AdminWritingAiDraftPage from './page';

const VALID_PROMPT =
  '60-year-old male with chest pain admitted to A&E for assessment and ECG monitoring overnight.';

function makeApiError(status: number, code: string, message: string) {
  // Mimic shape of `ApiError` from lib/api so isApiError predicate matches.
  return Object.assign(new Error(message), { name: 'ApiError', status, code, retryable: false });
}

function setPrompt(value = VALID_PROMPT) {
  fireEvent.change(screen.getByLabelText(/^prompt$/i), { target: { value } });
}

describe('AdminWritingAiDraftPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('disables Submit while the prompt is empty', () => {
    render(<AdminWritingAiDraftPage />);
    const submit = screen.getByRole('button', { name: /generate draft/i });
    expect(submit).toBeDisabled();
  });

  it('submits the normalized payload to adminGenerateWritingAiDraft', async () => {
    api.adminGenerateWritingAiDraft.mockResolvedValue({
      contentId: 'wp-001',
      title: 'Mr Jones — Routine referral',
      caseNoteCount: 12,
      modelLetterWordCount: 195,
      rulebookVersion: '2.1.0',
      appliedRuleIds: ['W-PURPOSE-1', 'W-AUDIENCE-2'],
      warning: null,
    });

    const user = userEvent.setup();
    render(<AdminWritingAiDraftPage />);

    await user.selectOptions(screen.getByLabelText(/^profession$/i), 'nursing');
    await user.selectOptions(screen.getByLabelText(/^letter type$/i), 'discharge');
    fireEvent.change(screen.getByLabelText(/recipient specialty/i), { target: { value: 'Cardiology' } });
    setPrompt();
    await user.click(screen.getByRole('radio', { name: /hard/i }));

    await user.click(screen.getByRole('button', { name: /generate draft/i }));

    await waitFor(() => {
      expect(api.adminGenerateWritingAiDraft).toHaveBeenCalledTimes(1);
    });
    expect(api.adminGenerateWritingAiDraft).toHaveBeenCalledWith({
      prompt: VALID_PROMPT,
      profession: 'nursing',
      letterType: 'discharge',
      recipientSpecialty: 'Cardiology',
      difficulty: 'hard',
      targetCaseNoteCount: 12,
    });
  });

  it('renders title and applied rule badges on success', async () => {
    api.adminGenerateWritingAiDraft.mockResolvedValue({
      contentId: 'wp-002',
      title: 'Generated case-note paper',
      caseNoteCount: 14,
      modelLetterWordCount: 210,
      rulebookVersion: '2.1.0',
      appliedRuleIds: ['W-PURPOSE-1', 'W-CONCISE-3'],
      warning: null,
    });

    const user = userEvent.setup();
    render(<AdminWritingAiDraftPage />);

    setPrompt();
    await user.click(screen.getByRole('button', { name: /generate draft/i }));

    expect(await screen.findByText('Generated case-note paper')).toBeInTheDocument();
    expect(screen.getByText('W-PURPOSE-1')).toBeInTheDocument();
    expect(screen.getByText('W-CONCISE-3')).toBeInTheDocument();
  });

  it('shows the warning callout when the response includes a warning', async () => {
    api.adminGenerateWritingAiDraft.mockResolvedValue({
      contentId: 'wp-003',
      title: 'Fallback starter',
      caseNoteCount: 12,
      modelLetterWordCount: 0,
      rulebookVersion: '2.1.0',
      appliedRuleIds: [],
      warning: 'AI parse failed — deterministic starter template used.',
    });

    const user = userEvent.setup();
    render(<AdminWritingAiDraftPage />);

    setPrompt();
    await user.click(screen.getByRole('button', { name: /generate draft/i }));

    expect(
      await screen.findByText(/deterministic starter template used/i),
    ).toBeInTheDocument();
  });

  it('shows an error alert when the request fails', async () => {
    api.adminGenerateWritingAiDraft.mockRejectedValue(
      makeApiError(500, 'internal_server_error', 'Backend exploded'),
    );

    const user = userEvent.setup();
    render(<AdminWritingAiDraftPage />);

    setPrompt();
    await user.click(screen.getByRole('button', { name: /generate draft/i }));

    expect(await screen.findByText(/draft generation failed/i)).toBeInTheDocument();
    expect(screen.getByText(/backend exploded/i)).toBeInTheDocument();
  });

  it('shows the quota-exceeded alert on a 429 response', async () => {
    api.adminGenerateWritingAiDraft.mockRejectedValue(
      makeApiError(429, 'rate_limited', 'quota exceeded'),
    );

    const user = userEvent.setup();
    render(<AdminWritingAiDraftPage />);

    setPrompt();
    await user.click(screen.getByRole('button', { name: /generate draft/i }));

    expect(await screen.findByText(/ai quota exceeded — try again later/i)).toBeInTheDocument();
  });
});
