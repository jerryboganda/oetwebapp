import { fireEvent, render, screen, waitFor } from '@testing-library/react';

const { mockListLearnerCards, mockTrack } = vi.hoisted(() => ({
  mockListLearnerCards: vi.fn(),
  mockTrack: vi.fn(),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => <div data-testid="learner-dashboard-shell">{children}</div>,
}));

vi.mock('@/components/domain/task-card', () => ({
  TaskCard: ({ title }: { title: string }) => <div>{title}</div>,
}));

vi.mock('@/components/ui/filter-bar', () => ({
  FilterBar: ({
    groups,
    selected,
    onChange,
    onClear,
  }: {
    groups: { id: string; label: string; options: { id: string; label: string }[] }[];
    selected: Record<string, string[]>;
    onChange: (groupId: string, optionId: string) => void;
    onClear?: () => void;
  }) => (
    <div data-testid="filter-bar">
      {groups.map((group) => (
        <div key={group.id}>
          {group.options.map((option) => (
            <button
              key={option.id}
              type="button"
              data-testid={`filter-${group.id}-${option.id}`}
              data-selected={selected[group.id]?.includes(option.id) ? 'true' : 'false'}
              onClick={() => onChange(group.id, option.id)}
            >
              {option.label}
            </button>
          ))}
        </div>
      ))}
      {onClear ? <button type="button" onClick={onClear}>mock-clear</button> : null}
    </div>
  ),
}));

vi.mock('@/lib/api/speaking-role-play-cards', () => ({
  listLearnerRolePlayCards: mockListLearnerCards,
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: mockTrack } }));

import SpeakingTaskSelection from './page';

const CARD = {
  cardId: 'rpc-1',
  professionId: 'nursing',
  scenarioTitle: 'Discharge advice after appendectomy',
  setting: 'Surgical day-ward',
  candidateRole: 'Nurse',
  interlocutorRole: 'Patient',
  patientName: 'Mr Ortiz',
  patientAge: '32',
  background: 'Medically cleared for discharge.',
  tasks: ['Explain the plan'],
  allowedNotes: true,
  prepTimeSeconds: 180,
  rolePlayTimeSeconds: 300,
  patientEmotion: 'anxious',
  communicationGoal: 'Advise',
  clinicalTopic: 'post-operative recovery',
  primaryCategory: 'Already Known Patient',
  secondaryTags: [],
  criteriaFocus: ['informationGiving'],
  disclaimer: '',
};

describe('Speaking selection page (FINAL 2026-09-06 catalogue)', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockListLearnerCards.mockResolvedValue({
      rolePlayCards: [CARD],
      activeProfessionId: 'nursing',
      totalCount: 1,
      appliedProfessionId: 'nursing',
      appliedPrimaryCategory: null,
    });
  });

  it('fetches unfiltered first and shows the server-derived count with the 2-credit rule', async () => {
    render(<SpeakingTaskSelection />);

    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledWith({
      professionId: undefined,
      primaryCategory: undefined,
    }));
    expect(await screen.findByTestId('speaking-available-count')).toHaveTextContent('1');
    expect(screen.getByText(/available Speaking card/)).toBeInTheDocument();
    expect(screen.getByText(/Each card uses 2 AI credits/)).toBeInTheDocument();
    expect(screen.getByText('Discharge advice after appendectomy')).toBeInTheDocument();
  });

  it('keeps the reference cards and exposes no Difficulty control', async () => {
    render(<SpeakingTaskSelection />);

    expect(await screen.findByText('Prepare for your OET Speaking')).toBeInTheDocument();
    expect(screen.getByText('Speaking Assessment Criteria')).toBeInTheDocument();
    expect(screen.getByText('Speaking Intro Questions')).toBeInTheDocument();
    expect(screen.getByText('Open Assessment Criteria').closest('a')).toHaveAttribute('href', '/speaking/assessment-criteria');
    expect(screen.getByText('Open Intro Questions').closest('a')).toHaveAttribute('href', '/speaking/intro-questions');
    expect(screen.queryByText('Difficulty')).not.toBeInTheDocument();
  });

  it('applies profession + category only after Apply, then shows the new server count', async () => {
    render(<SpeakingTaskSelection />);
    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByTestId('filter-profession-medicine'));
    fireEvent.click(screen.getByTestId('filter-category-First Visit'));
    // Draft only — no refetch before Apply.
    expect(mockListLearnerCards).toHaveBeenCalledTimes(1);

    mockListLearnerCards.mockResolvedValueOnce({
      rolePlayCards: [],
      activeProfessionId: 'nursing',
      totalCount: 0,
      appliedProfessionId: 'medicine',
      appliedPrimaryCategory: 'First Visit',
    });
    fireEvent.click(screen.getByRole('button', { name: /Apply filters/ }));

    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledWith({
      professionId: 'medicine',
      primaryCategory: 'First Visit',
    }));
    expect(await screen.findByText('No cards available for these filters')).toBeInTheDocument();
  });

  it('Clear all filters resets to the unfiltered server list', async () => {
    render(<SpeakingTaskSelection />);
    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledTimes(1));

    fireEvent.click(screen.getByTestId('filter-profession-medicine'));
    fireEvent.click(screen.getByRole('button', { name: /Apply filters/ }));
    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledTimes(2));

    fireEvent.click(screen.getByRole('button', { name: /Clear all filters/ }));
    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledWith({
      professionId: undefined,
      primaryCategory: undefined,
    }));
  });

  it('API failure shows a retryable error state only — never alongside "No cards available"', async () => {
    mockListLearnerCards.mockReset();
    mockListLearnerCards.mockRejectedValueOnce(new Error('network down'));

    render(<SpeakingTaskSelection />);

    expect(await screen.findByText('Could not load speaking cards. Please try again.')).toBeInTheDocument();
    // The contradictory double-render bug: an error banner must never be
    // paired with the "no cards" empty state for the same failed fetch.
    expect(screen.queryByText('No speaking cards available')).not.toBeInTheDocument();
    expect(screen.queryByText('No cards available for these filters')).not.toBeInTheDocument();

    mockListLearnerCards.mockResolvedValueOnce({
      rolePlayCards: [CARD],
      activeProfessionId: 'nursing',
      totalCount: 1,
      appliedProfessionId: 'nursing',
      appliedPrimaryCategory: null,
    });
    fireEvent.click(screen.getByRole('button', { name: 'Retry' }));

    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('Discharge advice after appendectomy')).toBeInTheDocument();
    expect(screen.queryByText('Could not load speaking cards. Please try again.')).not.toBeInTheDocument();
  });

  it('a malformed response (missing rolePlayCards array) is treated as a failure, not a crash', async () => {
    mockListLearnerCards.mockReset();
    // Deliberately malformed (no rolePlayCards array) to exercise the guard.
    mockListLearnerCards.mockResolvedValueOnce({ totalCount: 'oops' });

    render(<SpeakingTaskSelection />);

    expect(await screen.findByText('Could not load speaking cards. Please try again.')).toBeInTheDocument();
    expect(screen.queryByText('No speaking cards available')).not.toBeInTheDocument();
  });

  it('a stale in-flight response never overwrites a newer one (request race)', async () => {
    mockListLearnerCards.mockReset();
    let resolveFirst!: (value: unknown) => void;
    const first = new Promise((resolve) => { resolveFirst = resolve; });
    mockListLearnerCards.mockReturnValueOnce(first);

    render(<SpeakingTaskSelection />);
    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledTimes(1));

    // A second (newer) request resolves BEFORE the first, stale one.
    mockListLearnerCards.mockResolvedValueOnce({
      rolePlayCards: [CARD],
      activeProfessionId: 'nursing',
      totalCount: 1,
      appliedProfessionId: 'medicine',
      appliedPrimaryCategory: 'First Visit',
    });
    fireEvent.click(screen.getByTestId('filter-profession-medicine'));
    fireEvent.click(screen.getByTestId('filter-category-First Visit'));
    fireEvent.click(screen.getByRole('button', { name: /Apply filters/ }));
    await waitFor(() => expect(mockListLearnerCards).toHaveBeenCalledTimes(2));
    expect(await screen.findByText('Discharge advice after appendectomy')).toBeInTheDocument();

    // The stale first request now resolves with a DIFFERENT (empty) result —
    // it must be dropped, not clobber the newer render.
    resolveFirst({
      rolePlayCards: [],
      activeProfessionId: 'nursing',
      totalCount: 0,
      appliedProfessionId: 'nursing',
      appliedPrimaryCategory: null,
    });
    await Promise.resolve();
    expect(screen.getByText('Discharge advice after appendectomy')).toBeInTheDocument();
  });
});
