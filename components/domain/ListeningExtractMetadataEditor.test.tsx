import { act, render, screen } from '@testing-library/react';
import { ListeningExtractMetadataEditor } from './ListeningExtractMetadataEditor';

const { mockBackfillListeningPaper, mockGetListeningExtracts, mockReplaceListeningExtracts } = vi.hoisted(() => ({
  mockBackfillListeningPaper: vi.fn(),
  mockGetListeningExtracts: vi.fn(),
  mockReplaceListeningExtracts: vi.fn(),
}));

vi.mock('@/lib/listening-authoring-api', () => ({
  backfillListeningPaper: mockBackfillListeningPaper,
  getListeningExtracts: mockGetListeningExtracts,
  replaceListeningExtracts: mockReplaceListeningExtracts,
}));

describe('ListeningExtractMetadataEditor', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('keeps relational backfill disabled while metadata is loading', async () => {
    let resolveLoad!: (value: { extracts: unknown[] }) => void;
    mockGetListeningExtracts.mockReturnValue(new Promise((resolve) => { resolveLoad = resolve; }));

    render(<ListeningExtractMetadataEditor paperId="paper-1" />);

    expect(screen.getByRole('button', { name: /backfill/i })).toBeDisabled();

    await act(async () => {
      resolveLoad({
        extracts: [{ partCode: 'A1', displayOrder: 0, kind: 'consultation', title: 'Consultation 1', accentCode: null, speakers: [], audioStartMs: null, audioEndMs: null }],
      });
    });
    expect(await screen.findByText('1 authored extract')).toBeInTheDocument();
  });

  it('requires scaffolded default metadata to be saved before backfill', async () => {
    mockGetListeningExtracts.mockResolvedValue({ extracts: [] });

    render(<ListeningExtractMetadataEditor paperId="paper-1" />);

    expect(await screen.findByText('5 authored extracts')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /save/i })).toBeEnabled();
    expect(screen.getByRole('button', { name: /backfill/i })).toBeDisabled();
  });

  it('keeps relational backfill disabled when metadata fails to load', async () => {
    mockGetListeningExtracts.mockRejectedValue(new Error('network down'));

    render(<ListeningExtractMetadataEditor paperId="paper-1" />);

    expect(await screen.findByText('Extract metadata unavailable')).toBeInTheDocument();
    expect(screen.getByRole('button', { name: /backfill/i })).toBeDisabled();
  });
});