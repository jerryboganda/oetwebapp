import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { mockValidate, mockStructure, mockGetPaper, mockPublish, mockUnpublish } = vi.hoisted(() => ({
  mockValidate: vi.fn(),
  mockStructure: vi.fn(),
  mockGetPaper: vi.fn(),
  mockPublish: vi.fn(),
  mockUnpublish: vi.fn(),
}));

vi.mock('next/navigation', () => ({ useParams: () => ({ paperId: 'paper-1' }) }));
vi.mock('next/link', () => ({
  default: ({ children, href }: { children: React.ReactNode; href?: string }) => <a href={href}>{children}</a>,
}));

vi.mock('@/components/admin/layout/admin-operations-layout', () => ({
  AdminOperationsLayout: ({ kpis, primaryGrid, actions }: { kpis?: React.ReactNode; primaryGrid?: React.ReactNode; actions?: React.ReactNode }) => (
    <main>{actions}{kpis}{primaryGrid}</main>
  ),
  KpiStrip: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
}));
vi.mock('@/components/admin/ui/kpi-tile', () => ({
  KpiTile: ({ label, value }: { label: string; value: React.ReactNode }) => (
    <div><span>{label}</span><span>{value}</span></div>
  ),
}));
vi.mock('@/components/admin/ui/button', () => ({
  Button: ({ children, disabled, onClick, asChild, startIcon }: { children: React.ReactNode; disabled?: boolean; onClick?: () => void; asChild?: boolean; startIcon?: React.ReactNode }) =>
    asChild ? <span>{children}</span> : <button disabled={disabled} onClick={onClick}>{startIcon}{children}</button>,
}));
vi.mock('@/components/admin/ui/badge', () => ({ Badge: ({ children }: { children: React.ReactNode }) => <span>{children}</span> }));
vi.mock('@/components/admin/ui/card', () => ({
  Card: ({ children }: { children: React.ReactNode }) => <section>{children}</section>,
  CardContent: ({ children }: { children: React.ReactNode }) => <div>{children}</div>,
  CardHeader: ({ children }: { children: React.ReactNode }) => <header>{children}</header>,
  CardTitle: ({ children }: { children: React.ReactNode }) => <h2>{children}</h2>,
}));
vi.mock('@/components/admin/ui/skeleton', () => ({ Skeleton: () => <div /> }));
vi.mock('@/components/ui/alert', () => ({ Toast: ({ message }: { message: string }) => <div role="alert">{message}</div> }));
vi.mock('@/components/domain/admin/reading/ReadingWizardSteps', () => ({ ReadingWizardSteps: () => <nav /> }));

vi.mock('@/lib/reading-authoring-api', () => ({
  validateReadingPaper: mockValidate,
  getReadingStructureAdmin: mockStructure,
}));
vi.mock('@/lib/content-upload-api', () => ({
  getContentPaper: mockGetPaper,
  publishContentPaper: mockPublish,
  unpublishContentPaper: mockUnpublish,
}));

import ReadingValidatePublishPage from './page';

function fullStructure() {
  const q = (n: number) => Array.from({ length: n }, (_, i) => ({ id: `q-${i}`, displayOrder: i + 1, questionType: 'ShortAnswer', points: 1 }));
  return {
    paperId: 'paper-1',
    parts: [
      { id: 'part-a', partCode: 'A', timeLimitMinutes: 15, maxRawScore: 20, instructions: null, texts: [], questions: q(20), sections: [] },
      { id: 'part-b', partCode: 'B', timeLimitMinutes: 45, maxRawScore: 6, instructions: null, texts: [], questions: q(6), sections: [] },
      { id: 'part-c', partCode: 'C', timeLimitMinutes: 45, maxRawScore: 16, instructions: null, texts: [], questions: q(16), sections: [] },
    ],
  };
}

beforeEach(() => {
  vi.clearAllMocks();
  mockGetPaper.mockResolvedValue({ status: 'Draft' });
});

describe('Reading Validate & Publish page', () => {
  it('shows real counts from structure and keeps publish usable when validation load fails (e.g. rate-limited 429)', async () => {
    mockValidate.mockRejectedValue(new Error('429 Too Many Requests'));
    mockStructure.mockResolvedValue(fullStructure());

    render(<ReadingValidatePublishPage />);

    // Counts come from the structure call — a validation failure must NOT blank them to 0.
    expect(await screen.findByText('20 / 20')).toBeInTheDocument();
    expect(screen.getByText('6 / 6')).toBeInTheDocument();
    expect(screen.getByText('16 / 16')).toBeInTheDocument();
    expect(screen.getByText('42 / 42')).toBeInTheDocument();

    // Publishing must remain possible even though validation could not load.
    expect(screen.getByRole('button', { name: /publish paper/i })).toBeEnabled();
  });

  it('allows publishing even when validation reports it is not ready (validation is advisory)', async () => {
    mockValidate.mockResolvedValue({
      isPublishReady: false,
      issues: [{ code: 'part_A_item_count', severity: 'error', message: 'Part A has 19 items, expected 20.', targetId: 'part-a' }],
      counts: { partACount: 20, partBCount: 6, partCCount: 16, totalPoints: 42 },
    });
    mockStructure.mockResolvedValue(fullStructure());

    render(<ReadingValidatePublishPage />);

    expect(await screen.findByRole('button', { name: /publish paper/i })).toBeEnabled();
  });

  it('surfaces the real backend reason when a publish attempt fails', async () => {
    const user = userEvent.setup();
    mockValidate.mockResolvedValue({ isPublishReady: true, issues: [], counts: { partACount: 20, partBCount: 6, partCCount: 16, totalPoints: 42 } });
    mockStructure.mockResolvedValue(fullStructure());
    mockPublish.mockRejectedValue(new Error('SourceProvenance is required before publishing.'));

    render(<ReadingValidatePublishPage />);

    await user.click(await screen.findByRole('button', { name: /publish paper/i }));
    await user.click(screen.getByRole('button', { name: /click again to confirm/i }));

    expect(await screen.findByRole('alert')).toHaveTextContent(/sourceprovenance is required/i);
  });
});
