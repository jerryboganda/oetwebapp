import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const {
  mockGetListeningCandidatePreview,
  mockGetListeningStructure,
  mockUseAdminAuth,
} = vi.hoisted(() => ({
  mockGetListeningCandidatePreview: vi.fn(),
  mockGetListeningStructure: vi.fn(),
  mockUseAdminAuth: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useParams: () => ({ paperId: 'paper-1' }),
}));

vi.mock('next/link', () => ({
  default: ({ children, href, ...rest }: React.AnchorHTMLAttributes<HTMLAnchorElement> & { children: React.ReactNode; href?: string }) => (
    <a href={href} {...rest}>{children}</a>
  ),
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({
  useAdminAuth: mockUseAdminAuth,
}));

vi.mock('@/lib/listening-authoring-api', () => ({
  getListeningCandidatePreview: mockGetListeningCandidatePreview,
  getListeningStructure: mockGetListeningStructure,
}));

vi.mock('@/components/admin/layout/admin-settings-layout', () => ({
  AdminSettingsLayout: ({ children }: { children: React.ReactNode }) => <main>{children}</main>,
}));

vi.mock('@/components/admin/ui/card', () => ({
  Card: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
  CardContent: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  CardHeader: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  CardTitle: ({ children }: { children: React.ReactNode }) => <h2>{children}</h2>,
}));

vi.mock('@/components/admin/ui/button', () => ({
  Button: ({ children, onClick, type = 'button' }: { children: React.ReactNode; onClick?: () => void; type?: 'button' | 'submit' | 'reset' }) => (
    <button type={type} onClick={onClick}>{children}</button>
  ),
}));

vi.mock('@/components/admin/ui/skeleton', () => ({
  Skeleton: () => <div data-testid="skeleton" />,
}));

vi.mock('@/components/ui/badge', () => ({
  Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span>,
}));

vi.mock('@/components/ui/alert', () => ({
  InlineAlert: ({ children }: { children: React.ReactNode }) => <div role="alert">{children}</div>,
}));

vi.mock('@/components/domain/listening/ListeningQuestionPaperViewer', () => ({
  ListeningQuestionPaperViewer: ({ url, partLabel }: { url: string; partLabel?: string | null }) => (
    <div data-testid={`question-paper-${partLabel ?? 'all'}`}>{url}</div>
  ),
}));

vi.mock('@/components/domain/audio-player-waveform', () => ({
  AudioPlayerWaveform: ({ audioUrl }: { audioUrl: string }) => (
    <div data-testid="audio-preview">{audioUrl}</div>
  ),
}));

import ListeningPreviewPage from './page';

describe('Admin Listening preview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockGetListeningCandidatePreview.mockResolvedValue({
      paper: {
        id: 'paper-1',
        title: 'Listening Sample Paper',
        subtestCode: 'listening',
        estimatedDurationMinutes: 40,
        questionPaperAssets: [
          { id: 'asset-a', part: 'A', title: 'Part A question paper', downloadPath: '/v1/media/asset-a/content' },
          { id: 'asset-b', part: 'B', title: 'Part B question paper', downloadPath: '/v1/media/asset-b/content' },
        ],
        audioAssets: [
          { id: 'audio-a1', part: 'A1', title: 'Part A extract 1', durationSeconds: 45, downloadPath: '/v1/media/audio-a1/content' },
        ],
      },
      counts: { partACount: 24, partBCount: 6, partCCount: 12, totalItems: 42 },
      extracts: [{
        partCode: 'A1',
        displayOrder: 1,
        kind: 'note_completion',
        title: 'Clinical handover',
        accentCode: 'en-GB',
        speakers: [{ role: 'Nurse', gender: 'f', accent: 'en-GB' }],
        audioStartMs: 1000,
        audioEndMs: 45000,
        timeLimitSeconds: 30,
        contextIntro: 'You hear a nurse handing over a patient.',
      }],
      questions: [{
        id: 'candidate-q-1',
        number: 1,
        partCode: 'A',
        type: 'typed',
        stem: 'The patient reports ____ pain.',
        options: [],
        points: 1,
      }, {
        id: 'candidate-q-25',
        number: 25,
        partCode: 'B',
        type: 'mcq',
        stem: 'What does the speaker recommend?',
        options: ['Immediate review', 'Routine follow-up', 'No further action'],
        points: 1,
      }],
    });
    mockGetListeningStructure.mockResolvedValue({
      questions: [{
        id: 'marking-q-1',
        number: 1,
        partCode: 'A',
        type: 'typed',
        stem: 'The patient reports ____ pain.',
        options: [],
        correctAnswer: 'severe',
        acceptedAnswers: ['intense'],
        explanation: 'The transcript uses severe.',
        points: 1,
      }, {
        id: 'marking-q-25',
        number: 25,
        partCode: 'B',
        type: 'mcq',
        stem: 'What does the speaker recommend?',
        options: ['Immediate review', 'Routine follow-up', 'No further action'],
        correctAnswer: 'A',
        acceptedAnswers: [],
        explanation: 'The speaker recommends an immediate review.',
        transcriptExcerpt: 'The speaker says to arrange an immediate review.',
        distractorExplanation: 'Routine follow-up delays the required action.',
        optionDistractorWhy: [null, 'This is too slow for the stated risk.', 'The speaker did recommend further action.'],
        optionDistractorCategory: [null, 'too_weak', 'opposite_meaning'],
        speakerAttitude: 'concerned',
        points: 1,
      }],
      counts: { partACount: 24, partBCount: 6, partCCount: 12, totalItems: 42 },
    });
  });

  it('renders learner-safe extract context without answer-key fields', async () => {
    render(<ListeningPreviewPage />);

    expect(await screen.findByRole('heading', { name: 'Listening Sample Paper' })).toBeInTheDocument();
    expect(screen.getByText('You hear a nurse handing over a patient.')).toBeInTheDocument();
    expect(screen.getByText('Audio window: 1.0s–45.0s')).toBeInTheDocument();
    expect(screen.getByText('The patient reports ____ pain.')).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Candidate question paper' })).toBeInTheDocument();
    expect(screen.getByTestId('question-paper-A')).toHaveTextContent('/v1/media/asset-a/content');
    expect(screen.getByRole('heading', { name: 'Candidate audio preview' })).toBeInTheDocument();
    expect(screen.getByTestId('audio-preview')).toHaveTextContent('/v1/media/audio-a1/content');
    expect(screen.getByRole('heading', { name: 'Timed candidate preview' })).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Start local timer' })).toBeInTheDocument();
    expect(await screen.findByText('0:30')).toBeInTheDocument();
    const answer = screen.getByRole('textbox', { name: 'Answer for question 1' });
    await userEvent.setup().type(answer, 'severe');
    expect(answer).toHaveValue('severe');
    const option = screen.getByRole('radio', { name: 'A. Immediate review' });
    await userEvent.setup().click(option);
    expect(option).toBeChecked();
    expect(screen.queryByText('severe')).not.toBeInTheDocument();
    expect(screen.queryByText('The transcript uses severe.')).not.toBeInTheDocument();
    expect(screen.queryByText('The speaker says to arrange an immediate review.')).not.toBeInTheDocument();
    expect(screen.queryByText('Routine follow-up delays the required action.')).not.toBeInTheDocument();
    expect(screen.queryByText('This is too slow for the stated risk.')).not.toBeInTheDocument();
    expect(screen.queryByText('concerned')).not.toBeInTheDocument();
  });

  it('keeps candidate preview available when the protected marking request fails', async () => {
    mockGetListeningStructure.mockRejectedValue(new Error('Marking projection unavailable'));
    const user = userEvent.setup();
    render(<ListeningPreviewPage />);

    expect(await screen.findByRole('heading', { name: 'Listening Sample Paper' })).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Marking preview' }));

    expect(screen.getByText('Marking projection unavailable')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Candidate preview' }));
    expect(screen.getByText('The patient reports ____ pain.')).toBeInTheDocument();
    expect(screen.queryByText('severe')).not.toBeInTheDocument();
  });

  it('shows protected accepted variants and approved evidence only in marking mode', async () => {
    const user = userEvent.setup();
    render(<ListeningPreviewPage />);

    expect(await screen.findByRole('heading', { name: 'Listening Sample Paper' })).toBeInTheDocument();
    expect(screen.queryByText('Accepted variants')).not.toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Marking preview' }));

    expect(screen.getByText('Correct answer: severe')).toBeInTheDocument();
    expect(screen.getByText('Accepted variants')).toBeInTheDocument();
    expect(screen.getByText('intense')).toBeInTheDocument();
    // Both marking questions expose an approved rationale; assert per-question
    // instead of globally to avoid an ambiguous text match.
    const rationaleBlocks = screen.getAllByText('Approved rationale');
    expect(rationaleBlocks).toHaveLength(2);
    expect(rationaleBlocks[0].parentElement).toHaveTextContent('The transcript uses severe.');
    expect(rationaleBlocks[1].parentElement).toHaveTextContent('The speaker recommends an immediate review.');
    expect(screen.getByText('The transcript uses severe.')).toBeInTheDocument();
    expect(screen.getByText('Distractor authoring')).toBeInTheDocument();
    expect(screen.getByText('Distractor explanation')).toBeInTheDocument();
    expect(screen.getByText('Routine follow-up delays the required action.')).toBeInTheDocument();
    expect(screen.getByText('This is too slow for the stated risk.')).toBeInTheDocument();
    expect(screen.getByText('Speaker attitude')).toBeInTheDocument();
    expect(screen.getByText('concerned')).toBeInTheDocument();
  });

  it('keeps the protected marking preview available when the candidate request fails', async () => {
    mockGetListeningCandidatePreview.mockRejectedValue(new Error('Candidate projection unavailable'));
    const user = userEvent.setup();
    render(<ListeningPreviewPage />);

    expect(await screen.findByText('Candidate projection unavailable')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'View marking preview' }));

    expect(screen.getByText('Correct answer: severe')).toBeInTheDocument();
    expect(screen.getByText('Candidate preview is unavailable; this protected admin-only marking projection is shown separately.')).toBeInTheDocument();
  });
});
