import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import type {
  GraderCalibrationCriterion,
  GraderCalibrationSampleDetail,
} from '@/lib/api/speaking-grader-calibration';

const { mockDetail, mockLabel, mockExclude, mockOverview, mockObjectUrl } = vi.hoisted(() => ({
  mockDetail: vi.fn(),
  mockLabel: vi.fn(),
  mockExclude: vi.fn(),
  mockOverview: vi.fn(),
  mockObjectUrl: vi.fn(),
}));

vi.mock('@/lib/api/speaking-grader-calibration', () => ({
  adminGetGraderCalibrationSample: mockDetail,
  adminLabelGraderCalibrationSample: mockLabel,
  adminExcludeGraderCalibrationSample: mockExclude,
  adminGetGraderCalibration: mockOverview,
  graderCalibrationAudioPath: (sampleId: string, recordingId: string) => `/audio/${sampleId}/${recordingId}`,
}));

vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>();
  return { ...actual, fetchAuthorizedObjectUrl: mockObjectUrl };
});

import SpeakingGraderCalibrationSamplePage from './page';

const CRITERIA: GraderCalibrationCriterion[] = [
  { code: 'intelligibility', label: 'Intelligibility', family: 'linguistic', max: 6 },
  { code: 'fluency', label: 'Fluency', family: 'linguistic', max: 6 },
  { code: 'appropriateness', label: 'Appropriateness of language', family: 'linguistic', max: 6 },
  { code: 'grammarExpression', label: 'Resources of grammar and expression', family: 'linguistic', max: 6 },
  { code: 'relationshipBuilding', label: 'Relationship building', family: 'clinical', max: 3 },
  { code: 'patientPerspective', label: "Understanding and incorporating the patient's perspective", family: 'clinical', max: 3 },
  { code: 'structure', label: 'Providing structure', family: 'clinical', max: 3 },
  { code: 'informationGathering', label: 'Information gathering', family: 'clinical', max: 3 },
  { code: 'informationGiving', label: 'Information giving', family: 'clinical', max: 3 },
];

const detail = (overrides: Partial<GraderCalibrationSampleDetail> = {}): GraderCalibrationSampleDetail => ({
  id: 'spgc_1',
  status: 'pending',
  hasAudio: true,
  card: {
    title: 'Asthma review',
    professionId: 'nursing',
    setting: 'Outpatient clinic',
    candidateRole: 'Nurse',
    interlocutorRole: 'Patient',
    background: 'The patient has had asthma for ten years.',
    tasks: ['Establish the patient\'s understanding', 'Explain the inhaler technique'],
  },
  transcript: [
    { speaker: 'candidate', startMs: 3000, endMs: 9000, text: 'Good morning, I am Nurse Lee.' },
    { speaker: 'patient', startMs: 9000, endMs: 11000, text: 'Hello nurse.' },
  ],
  clips: [{ recordingId: 'rec-1', durationSeconds: 65, mimeType: 'audio/webm' }],
  criteria: CRITERIA,
  label: null,
  excludedReason: '',
  ...overrides,
});

const markEverything = (linguistic: number, clinical: number) => {
  for (const criterion of CRITERIA) {
    const value = criterion.max === 6 ? linguistic : clinical;
    fireEvent.click(screen.getByRole('radio', { name: `${criterion.label} ${value}` }));
  }
};

describe('SpeakingGraderCalibrationSamplePage', () => {
  beforeAll(() => {
    // jsdom has no object-URL support; the page revokes the clip URL when it goes away.
    if (!URL.revokeObjectURL) URL.revokeObjectURL = vi.fn();
  });

  beforeEach(() => {
    vi.clearAllMocks();
    mockDetail.mockResolvedValue(detail());
    mockObjectUrl.mockResolvedValue('blob:clip-1');
    mockOverview.mockResolvedValue({ coverage: {}, samples: [{ id: 'spgc_2', status: 'pending' }] });
  });

  const renderPage = () => renderWithRouter(<SpeakingGraderCalibrationSamplePage />, { params: { id: 'spgc_1' } });

  it('is blind: says so, shows the card, the transcript the grader reads and the audio', async () => {
    renderPage();

    expect(await screen.findByText(/You are marking blind/)).toBeInTheDocument();
    expect(screen.getByText('The patient has had asthma for ten years.')).toBeInTheDocument();
    expect(screen.getByText('Good morning, I am Nurse Lee.')).toBeInTheDocument();
    expect(screen.getByText('0:03')).toBeInTheDocument();
    expect(await screen.findByLabelText('Audio clip 1')).toHaveAttribute('src', 'blob:clip-1');
    expect(mockObjectUrl).toHaveBeenCalledWith('/audio/spgc_1/rec-1');
  });

  it('keeps Save disabled until all nine criteria and the overall are chosen, then sends exactly those marks', async () => {
    mockLabel.mockResolvedValue({ id: 'spgc_1', status: 'labelled' });
    mockDetail
      .mockResolvedValueOnce(detail())
      .mockResolvedValue(detail({ status: 'labelled', label: { scores: {}, overallScaled: 350, notes: '' } }));
    renderPage();
    const save = await screen.findByRole('button', { name: 'Save my marks' });
    expect(save).toBeDisabled();

    markEverything(4, 2);
    expect(screen.getByTestId('criteria-total')).toHaveTextContent('Criteria total: 26 / 39');
    expect(save).toBeDisabled(); // still no overall

    fireEvent.change(screen.getByLabelText(/Your overall result/), { target: { value: '350' } });
    expect(screen.getByTestId('overall-grade')).toHaveTextContent('Grade B');
    fireEvent.change(screen.getByLabelText('Notes (optional)'), { target: { value: 'Solid structure.' } });
    expect(save).toBeEnabled();
    fireEvent.click(save);

    await waitFor(() => expect(mockLabel).toHaveBeenCalledTimes(1));
    expect(mockLabel).toHaveBeenCalledWith('spgc_1', {
      scores: {
        intelligibility: 4, fluency: 4, appropriateness: 4, grammarExpression: 4,
        relationshipBuilding: 2, patientPerspective: 2, structure: 2, informationGathering: 2, informationGiving: 2,
      },
      overallScaled: 350,
      notes: 'Solid structure.',
    });
    expect(await screen.findByText(/Marks saved/)).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Mark the next performance' })).toHaveAttribute(
      'href',
      '/admin/speaking/grader-calibration/spgc_2',
    );
  });

  it('opens an already-marked performance with his marks filled in so he can correct them', async () => {
    mockDetail.mockResolvedValue(detail({
      status: 'labelled',
      label: {
        scores: {
          intelligibility: 5, fluency: 4, appropriateness: 4, grammarExpression: 4,
          relationshipBuilding: 3, patientPerspective: 2, structure: 2, informationGathering: 2, informationGiving: 3,
        },
        overallScaled: 400,
        notes: 'Very clear.',
      },
    }));
    renderPage();

    expect(await screen.findByRole('button', { name: 'Update my marks' })).toBeEnabled();
    expect(screen.getByRole('radio', { name: 'Intelligibility 5' })).toBeChecked();
    expect(screen.getByRole('radio', { name: 'Relationship building 3' })).toBeChecked();
    expect(screen.getByLabelText(/Your overall result/)).toHaveValue('400');
    expect(screen.getByLabelText('Notes (optional)')).toHaveValue('Very clear.');
  });

  it('excludes an unusable performance only with a reason and a confirmation', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockExclude.mockResolvedValue({ id: 'spgc_1', status: 'excluded' });
    renderPage();
    const exclude = await screen.findByRole('button', { name: 'Exclude this performance' });
    expect(exclude).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Why can't it be used?"), { target: { value: 'No speech on the recording' } });
    fireEvent.click(exclude);

    await waitFor(() => expect(mockExclude).toHaveBeenCalledWith('spgc_1', 'No speech on the recording'));
    expect(confirm).toHaveBeenCalled();
    confirm.mockRestore();
  });

  it('says so when a performance has no audio', async () => {
    mockDetail.mockResolvedValue(detail({ hasAudio: false, clips: [] }));
    renderPage();

    expect(await screen.findByText(/No audio was kept for this performance/)).toBeInTheDocument();
    expect(screen.getByText('No audio clips.')).toBeInTheDocument();
  });

  it('shows an error when the performance cannot be loaded', async () => {
    mockDetail.mockRejectedValue(new Error('That calibration sample does not exist.'));
    renderPage();

    expect(await screen.findByText('That calibration sample does not exist.')).toBeInTheDocument();
  });
});
