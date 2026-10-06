import { fireEvent, screen, waitFor, within } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import type {
  GraderCalibrationCandidate,
  GraderCalibrationMockCandidate,
  GraderCalibrationMockOverview,
  GraderCalibrationMockSampleRow,
  GraderCalibrationOverview,
  GraderCalibrationSampleRow,
} from '@/lib/api/speaking-grader-calibration';

const {
  mockOverview,
  mockCandidates,
  mockPromote,
  mockMockOverview,
  mockMockCandidatesList,
  mockPromoteMock,
} = vi.hoisted(() => ({
  mockOverview: vi.fn(),
  mockCandidates: vi.fn(),
  mockPromote: vi.fn(),
  mockMockOverview: vi.fn(),
  mockMockCandidatesList: vi.fn(),
  mockPromoteMock: vi.fn(),
}));

vi.mock('@/lib/api/speaking-grader-calibration', () => ({
  adminGetGraderCalibration: mockOverview,
  adminListGraderCalibrationCandidates: mockCandidates,
  adminPromoteGraderCalibrationSample: mockPromote,
  adminGetGraderCalibrationMocks: mockMockOverview,
  adminListGraderCalibrationMockCandidates: mockMockCandidatesList,
  adminPromoteGraderCalibrationMock: mockPromoteMock,
}));

import SpeakingGraderCalibrationPage from './page';

const row = (overrides: Partial<GraderCalibrationSampleRow>): GraderCalibrationSampleRow => ({
  id: 'spgc_1',
  sessionId: 's1',
  professionId: 'nursing',
  cardTitle: 'Asthma review',
  hasAudio: true,
  status: 'pending',
  expertOverallScaled: null,
  expertGrade: null,
  promotedAt: '2026-10-04T10:00:00Z',
  labelledAt: null,
  ...overrides,
});

const overview = (samples: GraderCalibrationSampleRow[]): GraderCalibrationOverview => ({
  coverage: {
    total: samples.length,
    labelled: 3,
    pending: 1,
    excluded: 0,
    labelledByGrade: { A: 1, B: 2, 'C+': 0, C: 0, D: 0, E: 0 },
    labelledNearPassLine: 2,
    audioShare: 0.667,
    requiredLabelled: 30,
    requiredPerGrade: 3,
    requiredNearPassLine: 10,
    labelledBelowPassLine: 1,
    labelledAtOrAbovePassLine: 1,
    requiredEachSideOfPassLine: 4,
    requiredAudioShare: 0.8,
    meetsCoverage: false,
    unmet: ['Mark 27 more performance(s): 3 of 30 marked.', 'Grade E: 0 of 3 marked.'],
  },
  samples,
});

const candidate = (overrides: Partial<GraderCalibrationCandidate> = {}): GraderCalibrationCandidate => ({
  sessionId: 's9',
  professionId: 'medicine',
  cardTitle: 'Chest pain follow-up',
  finishedAt: '2026-10-04T09:00:00Z',
  elapsedSeconds: 305,
  hasAudio: true,
  ...overrides,
});

const coverage = () => ({
  total: 3,
  labelled: 3,
  pending: 1,
  excluded: 0,
  labelledByGrade: { A: 1, B: 2, 'C+': 0, C: 0, D: 0, E: 0 },
  labelledNearPassLine: 2,
  audioShare: 0.667,
  requiredLabelled: 30,
  requiredPerGrade: 3,
  requiredNearPassLine: 10,
  labelledBelowPassLine: 1,
  labelledAtOrAbovePassLine: 1,
  requiredEachSideOfPassLine: 4,
  requiredAudioShare: 0.8,
  meetsCoverage: false,
  unmet: ['Mark 27 more performance(s): 3 of 30 marked.', 'Grade E: 0 of 3 marked.'],
});

const mockRow = (overrides: Partial<GraderCalibrationMockSampleRow>): GraderCalibrationMockSampleRow => ({
  id: 'spgcm_1',
  examId: 'exam-1',
  professionId: 'medicine',
  cardATitle: 'Asthma review',
  cardBTitle: 'Chest pain',
  hasAudio: true,
  status: 'pending',
  expertOverallScaled: null,
  expertGrade: null,
  promotedAt: '2026-10-07T10:00:00Z',
  labelledAt: null,
  ...overrides,
});

const mockOverviewData = (samples: GraderCalibrationMockSampleRow[]): GraderCalibrationMockOverview => ({
  coverage: coverage(),
  samples,
});

const mockCandidate = (overrides: Partial<GraderCalibrationMockCandidate> = {}): GraderCalibrationMockCandidate => ({
  examId: 'exam-9',
  professionId: 'medicine',
  cardATitle: 'Asthma review',
  cardBTitle: 'Chest pain',
  finishedAt: '2026-10-07T09:00:00Z',
  hasAudio: true,
  ...overrides,
});

describe('SpeakingGraderCalibrationPage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockOverview.mockResolvedValue(overview([
      row({ id: 'spgc_done', cardTitle: 'Marked one', status: 'labelled', expertOverallScaled: 350, expertGrade: 'B' }),
      row({ id: 'spgc_todo', cardTitle: 'Waiting one' }),
    ]));
    mockCandidates.mockResolvedValue([candidate()]);
    mockMockOverview.mockResolvedValue(mockOverviewData([
      mockRow({ id: 'spgcm_done', status: 'labelled', expertOverallScaled: 360, expertGrade: 'B' }),
      mockRow({ id: 'spgcm_todo' }),
    ]));
    mockMockCandidatesList.mockResolvedValue([mockCandidate()]);
  });

  it('shows how much expert marking exists against what is needed, in plain words, and says marking is blind', async () => {
    renderWithRouter(<SpeakingGraderCalibrationPage />);

    expect(await screen.findByText('3 of 30 performances marked')).toBeInTheDocument();
    expect(screen.getByText(/marking is blind: AI scores are never shown here/i)).toBeInTheDocument();
    expect(screen.getByTestId('coverage-grade-B')).toHaveTextContent('2 / 3');
    expect(screen.getByTestId('coverage-near')).toHaveTextContent('2 / 10');
    // The 320-380 block must straddle the 350 pass line.
    expect(screen.getByTestId('coverage-below')).toHaveTextContent('1 / 4');
    expect(screen.getByTestId('coverage-above')).toHaveTextContent('1 / 4');
    expect(screen.getByTestId('coverage-audio')).toHaveTextContent('67%');
    expect(screen.getByTestId('coverage-unmet')).toHaveTextContent('Grade E: 0 of 3 marked.');
    expect(screen.getByText('More marking needed')).toBeInTheDocument();
  });

  it('shows a performance that can no longer be graded as unavailable and never offers it as the next one to mark', async () => {
    mockOverview.mockResolvedValue(overview([
      row({ id: 'spgc_gone', cardTitle: 'Expired one', usable: false }),
      row({ id: 'spgc_todo', cardTitle: 'Waiting one' }),
    ]));
    renderWithRouter(<SpeakingGraderCalibrationPage />);

    const rows = await screen.findAllByTestId('calibration-sample-row');
    const gone = rows.find((r) => r.textContent?.includes('Expired one'))!;
    expect(gone).toHaveTextContent('Unavailable');
    expect(screen.getByRole('link', { name: 'Mark the next performance' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/spgc_todo');
  });

  it('lists the performances still to mark first, each with a way in, and offers the next one', async () => {
    renderWithRouter(<SpeakingGraderCalibrationPage />);

    const rows = await screen.findAllByTestId('calibration-sample-row');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Waiting one');
    expect(within(rows[0]).getByRole('link', { name: 'Mark' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/spgc_todo');
    expect(rows[1]).toHaveTextContent('350 · B');
    expect(within(rows[1]).getByRole('link', { name: 'Edit marks' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/spgc_done');
    expect(screen.getByRole('link', { name: 'Mark the next performance' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/spgc_todo');
  });

  it('adds a candidate to the calibration set only after the admin confirms the 365-day audio retention', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockPromote.mockResolvedValue(row({ id: 'spgc_new', sessionId: 's9' }));
    renderWithRouter(<SpeakingGraderCalibrationPage />);
    await screen.findByText('3 of 30 performances marked');

    fireEvent.click(screen.getByRole('button', { name: /Candidates/ }));
    const candidateRow = await screen.findByTestId('calibration-candidate-row');
    expect(candidateRow).toHaveTextContent('Chest pain follow-up');
    expect(candidateRow).toHaveTextContent('5:05');
    fireEvent.click(within(candidateRow).getByRole('button', { name: 'Add to calibration set' }));

    await waitFor(() => expect(mockPromote).toHaveBeenCalledWith('s9'));
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('365 days'));
    expect(await screen.findByText(/Added to the calibration set/)).toBeInTheDocument();
    // The lists are reloaded after promoting.
    expect(mockOverview.mock.calls.length).toBeGreaterThanOrEqual(2);
    confirm.mockRestore();
  });

  it('does nothing when the admin declines the confirmation', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    renderWithRouter(<SpeakingGraderCalibrationPage />);
    await screen.findByText('3 of 30 performances marked');

    fireEvent.click(screen.getByRole('button', { name: /Candidates/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'Add to calibration set' }));

    expect(mockPromote).not.toHaveBeenCalled();
    confirm.mockRestore();
  });

  it('says plainly when a candidate has no audio', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(false);
    mockCandidates.mockResolvedValue([candidate({ hasAudio: false })]);
    renderWithRouter(<SpeakingGraderCalibrationPage />);
    await screen.findByText('3 of 30 performances marked');

    fireEvent.click(screen.getByRole('button', { name: /Candidates/ }));
    fireEvent.click(await screen.findByRole('button', { name: 'Add to calibration set' }));

    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('no audio'));
    confirm.mockRestore();
  });

  it('shows an error instead of an empty page when loading fails', async () => {
    mockOverview.mockRejectedValue(new Error('Calibration is unavailable.'));
    renderWithRouter(<SpeakingGraderCalibrationPage />);

    expect(await screen.findByText('Calibration is unavailable.')).toBeInTheDocument();
  });

  it('switches to Full Mocks and lists the mock set with a way to mark each as one test', async () => {
    renderWithRouter(<SpeakingGraderCalibrationPage />);

    fireEvent.click(await screen.findByTestId('kind-switch-mocks'));

    const rows = await screen.findAllByTestId('calibration-mock-row');
    expect(rows).toHaveLength(2);
    expect(rows[0]).toHaveTextContent('Asthma review');
    expect(rows[0]).toHaveTextContent('Chest pain');
    expect(within(rows[0]).getByRole('link', { name: 'Mark' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/mock/spgcm_todo');
    expect(within(rows[1]).getByRole('link', { name: 'Edit marks' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/mock/spgcm_done');
    expect(screen.getByRole('link', { name: 'Mark the next Full Mock' })).toHaveAttribute('href', '/admin/speaking/grader-calibration/mock/spgcm_todo');
    // The coverage panel says a pilot needs none of it.
    expect(screen.getByTestId('coverage-pilot-note')).toHaveTextContent(/owner pilot/i);
  });

  it('adds a completed Full Mock as ONE performance only after the admin confirms the retention', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockPromoteMock.mockResolvedValue(mockRow({ id: 'spgcm_new', examId: 'exam-9' }));
    renderWithRouter(<SpeakingGraderCalibrationPage />);
    await screen.findByText('3 of 30 performances marked');

    fireEvent.click(await screen.findByTestId('kind-switch-mocks'));
    fireEvent.click(screen.getByRole('button', { name: /Candidates/ }));
    const candidateRow = await screen.findByTestId('calibration-mock-candidate-row');
    expect(candidateRow).toHaveTextContent('Asthma review');
    expect(candidateRow).toHaveTextContent('Chest pain');
    fireEvent.click(within(candidateRow).getByRole('button', { name: 'Add as one Full Mock' }));

    await waitFor(() => expect(mockPromoteMock).toHaveBeenCalledWith('exam-9'));
    expect(confirm).toHaveBeenCalledWith(expect.stringContaining('ONE performance'));
    expect(await screen.findByText(/Added to the Full Mock calibration set/)).toBeInTheDocument();
    confirm.mockRestore();
  });
});
