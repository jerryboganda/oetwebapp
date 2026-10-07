import { fireEvent, screen, waitFor } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import type {
  GraderCalibrationCriterion,
  GraderCalibrationMockSampleDetail,
} from '@/lib/api/speaking-grader-calibration';

const { mockDetail, mockLabel, mockExclude, mockOverview, mockObjectUrl } = vi.hoisted(() => ({
  mockDetail: vi.fn(),
  mockLabel: vi.fn(),
  mockExclude: vi.fn(),
  mockOverview: vi.fn(),
  mockObjectUrl: vi.fn(),
}));

vi.mock('@/lib/api/speaking-grader-calibration', () => ({
  adminGetGraderCalibrationMockSample: mockDetail,
  adminLabelGraderCalibrationMockSample: mockLabel,
  adminExcludeGraderCalibrationMockSample: mockExclude,
  adminGetGraderCalibrationMocks: mockOverview,
  graderCalibrationMockAudioPath: (sampleId: string, recordingId: string) => `/audio/${sampleId}/${recordingId}`,
}));

vi.mock('@/lib/api', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@/lib/api')>();
  return { ...actual, fetchAuthorizedObjectUrl: mockObjectUrl };
});

import SpeakingGraderCalibrationMockSamplePage from './page';

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

const card = (title: string, background: string) => ({
  title,
  professionId: 'medicine',
  setting: 'Outpatient clinic',
  candidateRole: 'Doctor',
  interlocutorRole: 'Patient',
  background,
  tasks: ['Establish rapport', 'Address the patient\'s concerns'],
});

const detail = (overrides: Partial<GraderCalibrationMockSampleDetail> = {}): GraderCalibrationMockSampleDetail => ({
  id: 'spgcm_1',
  status: 'pending',
  hasAudio: true,
  cardA: card('Asthma review', 'Card A background: the patient has had asthma for ten years.'),
  cardB: card('Chest pain', 'Card B background: the patient reports central chest pain.'),
  transcriptA: [{ speaker: 'candidate', startMs: 3000, endMs: 9000, text: 'Good morning, Doctor Lee speaking.' }],
  transcriptB: [{ speaker: 'candidate', startMs: 3000, endMs: 9000, text: 'Tell me more about the pain.' }],
  clipsA: [{ recordingId: 'rec-a1', durationSeconds: 65, mimeType: 'audio/webm' }],
  clipsB: [{ recordingId: 'rec-b1', durationSeconds: 70, mimeType: 'audio/webm' }],
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

describe('SpeakingGraderCalibrationMockSamplePage', () => {
  beforeAll(() => {
    if (!URL.revokeObjectURL) URL.revokeObjectURL = vi.fn();
  });

  beforeEach(() => {
    vi.clearAllMocks();
    mockDetail.mockResolvedValue(detail());
    mockObjectUrl.mockResolvedValue('blob:clip-1');
    mockOverview.mockResolvedValue({ coverage: {}, samples: [{ id: 'spgcm_2', status: 'pending' }] });
  });

  const renderPage = () => renderWithRouter(<SpeakingGraderCalibrationMockSamplePage />, { params: { id: 'spgcm_1' } });

  it('shows BOTH cards, both transcripts and both cards\' audio, and says marking is blind', async () => {
    renderPage();

    expect(await screen.findByText(/You are marking blind/)).toBeInTheDocument();
    expect(screen.getByText('Card A background: the patient has had asthma for ten years.')).toBeInTheDocument();
    expect(screen.getByText('Card B background: the patient reports central chest pain.')).toBeInTheDocument();
    expect(screen.getByText('Good morning, Doctor Lee speaking.')).toBeInTheDocument();
    expect(screen.getByText('Tell me more about the pain.')).toBeInTheDocument();
    expect(await screen.findByLabelText('Card A audio clip 1')).toHaveAttribute('src', 'blob:clip-1');
    expect(await screen.findByLabelText('Card B audio clip 1')).toHaveAttribute('src', 'blob:clip-1');
    expect(mockObjectUrl).toHaveBeenCalledWith('/audio/spgcm_1/rec-a1');
    expect(mockObjectUrl).toHaveBeenCalledWith('/audio/spgcm_1/rec-b1');
    // ONE set of marks for the whole test, not two.
    expect(screen.getAllByTestId('criteria-total')).toHaveLength(1);
  });

  it('saves ONE set of marks and ONE overall for the whole test', async () => {
    mockLabel.mockResolvedValue({ id: 'spgcm_1', status: 'labelled' });
    renderPage();
    const save = await screen.findByRole('button', { name: 'Save my marks' });

    markEverything(4, 2);
    expect(screen.getByTestId('criteria-total')).toHaveTextContent('Criteria total: 26 / 39');
    fireEvent.change(screen.getByLabelText(/Your overall result for the whole test/), { target: { value: '350' } });
    fireEvent.click(save);

    await waitFor(() => expect(mockLabel).toHaveBeenCalledTimes(1));
    expect(mockLabel).toHaveBeenCalledWith('spgcm_1', {
      scores: {
        intelligibility: 4, fluency: 4, appropriateness: 4, grammarExpression: 4,
        relationshipBuilding: 2, patientPerspective: 2, structure: 2, informationGathering: 2, informationGiving: 2,
      },
      overallScaled: 350,
      notes: '',
    });
    expect(await screen.findByText(/Marks saved/)).toBeInTheDocument();
    expect(await screen.findByRole('link', { name: 'Mark the next Full Mock' })).toHaveAttribute(
      'href',
      '/admin/speaking/grader-calibration/mock/spgcm_2',
    );
  });

  it('warns when a card has no stored audio, so the grader cannot judge Intelligibility from real audio', async () => {
    mockDetail.mockResolvedValue(detail({ hasAudio: false, clipsB: [] }));
    renderPage();

    expect(await screen.findByText(/At least one card has no stored audio/)).toBeInTheDocument();
  });

  it('excludes an unusable Full Mock only with a reason and a confirmation', async () => {
    const confirm = vi.spyOn(window, 'confirm').mockReturnValue(true);
    mockExclude.mockResolvedValue({ id: 'spgcm_1', status: 'excluded' });
    renderPage();
    const exclude = await screen.findByRole('button', { name: 'Exclude this Full Mock' });
    expect(exclude).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Why can't it be used?"), { target: { value: 'No speech on Card B' } });
    fireEvent.click(exclude);

    await waitFor(() => expect(mockExclude).toHaveBeenCalledWith('spgcm_1', 'No speech on Card B'));
    expect(confirm).toHaveBeenCalled();
    confirm.mockRestore();
  });
});
