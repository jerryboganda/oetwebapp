import { render, screen, waitFor } from '@testing-library/react';

const {
  mockGetListeningSequence,
  mockDeriveListeningSequence,
  mockValidateListeningSequence,
  mockReplaceListeningSequence,
  mockUseAdminAuth,
} = vi.hoisted(() => ({
  mockGetListeningSequence: vi.fn(),
  mockDeriveListeningSequence: vi.fn(),
  mockValidateListeningSequence: vi.fn(),
  mockReplaceListeningSequence: vi.fn(),
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

vi.mock('@/components/admin/layout/admin-settings-layout', () => ({
  AdminSettingsLayout: ({ children, actions }: { children: React.ReactNode; actions?: React.ReactNode }) => (
    <main>{actions}{children}</main>
  ),
}));

vi.mock('@/components/admin/ui/kpi-tile', () => ({
  KpiTile: ({ label, value }: { label: string; value: React.ReactNode }) => <div>{label}: {value}</div>,
}));

vi.mock('@/lib/hooks/use-admin-auth', () => ({ useAdminAuth: () => mockUseAdminAuth() }));

vi.mock('@/lib/listening-authoring-api', () => ({
  getListeningSequence: (...args: unknown[]) => mockGetListeningSequence(...args),
  deriveListeningSequence: (...args: unknown[]) => mockDeriveListeningSequence(...args),
  validateListeningSequence: (...args: unknown[]) => mockValidateListeningSequence(...args),
  replaceListeningSequence: (...args: unknown[]) => mockReplaceListeningSequence(...args),
}));

import AdminListeningSequencePage from '../page';

type Item = {
  index: number;
  type: string;
  partCode: string | null;
  extractDisplayOrder: number | null;
  durationMs: number | null;
  label: string | null;
};

function canonicalItems(): Item[] {
  // A trimmed canonical phase list — enough to assert ordering + render.
  return [
    { index: 0, type: 'instruction', partCode: null, extractDisplayOrder: null, durationMs: 0, label: 'intro' },
    { index: 1, type: 'reading_time', partCode: 'A1', extractDisplayOrder: null, durationMs: 30000, label: 'a1_preview' },
    { index: 2, type: 'audio_extract', partCode: 'A1', extractDisplayOrder: null, durationMs: 0, label: 'a1_audio' },
    { index: 3, type: 'local_check_time', partCode: 'A1', extractDisplayOrder: null, durationMs: 75000, label: 'a1_review' },
  ];
}

const validReport = {
  isValid: true,
  issues: [],
  counts: { partACount: 24, partBCount: 6, partCCount: 12, totalItems: 42 },
};

describe('AdminListeningSequencePage', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: true, role: 'admin' });
    mockValidateListeningSequence.mockResolvedValue(validReport);
  });

  it('renders an authored sequence with coverage and a valid banner', async () => {
    mockGetListeningSequence.mockResolvedValue({
      sequence: { items: canonicalItems(), version: 1 },
      isAuthored: true,
    });

    render(<AdminListeningSequencePage />);

    // Phase count heading.
    expect(await screen.findByText('Phases (4)')).toBeInTheDocument();

    // Coverage tiles (mocked KpiTile renders "label: value").
    await waitFor(() => expect(screen.getByText('Total: 42 / 42')).toBeInTheDocument());
    expect(screen.getByText('Part A: 24 / 24')).toBeInTheDocument();

    // Live validation banner.
    await waitFor(() => expect(mockValidateListeningSequence).toHaveBeenCalled());
    expect(await screen.findByText(/Sequence is valid/i)).toBeInTheDocument();

    // Did not need to derive — the paper already had an authored sequence.
    expect(mockDeriveListeningSequence).not.toHaveBeenCalled();
  });

  it('seeds from the derived canonical sequence when none is authored', async () => {
    mockGetListeningSequence.mockResolvedValue({ sequence: null, isAuthored: false });
    mockDeriveListeningSequence.mockResolvedValue({
      sequence: { items: canonicalItems(), version: 1 },
      mode: 'Exam',
    });

    render(<AdminListeningSequencePage />);

    expect(await screen.findByText('Phases (4)')).toBeInTheDocument();
    expect(mockDeriveListeningSequence).toHaveBeenCalledWith('paper-1');
    expect(
      screen.getByText(/This paper has no authored sequence yet/i),
    ).toBeInTheDocument();
  });

  it('surfaces validation errors and blocks save', async () => {
    mockGetListeningSequence.mockResolvedValue({
      sequence: { items: canonicalItems(), version: 1 },
      isAuthored: true,
    });
    mockValidateListeningSequence.mockResolvedValue({
      isValid: false,
      issues: [{ code: 'listening_sequence_phase_order', severity: 'error', message: 'Phase 2 must be reading_time.' }],
      counts: { partACount: 20, partBCount: 6, partCCount: 12, totalItems: 38 },
    });

    render(<AdminListeningSequencePage />);

    await waitFor(() => expect(mockValidateListeningSequence).toHaveBeenCalled());
    expect(await screen.findByText(/validation error/i)).toBeInTheDocument();
    expect(screen.getByText('Phase 2 must be reading_time.')).toBeInTheDocument();
  });

  it('locks the page for non-admin sessions', () => {
    mockUseAdminAuth.mockReturnValue({ isAuthenticated: false, role: null });
    render(<AdminListeningSequencePage />);
    expect(screen.getByText('Admin access required.')).toBeInTheDocument();
    expect(mockGetListeningSequence).not.toHaveBeenCalled();
  });
});
