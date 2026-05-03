import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';

const api = vi.hoisted(() => ({
  previewAdminVocabularyImport: vi.fn(),
  bulkImportAdminVocabulary: vi.fn(),
  fetchAdminVocabularyImportBatch: vi.fn(),
  exportAdminVocabularyImportBatchCsv: vi.fn(),
  reconcileAdminVocabularyImportBatch: vi.fn(),
  rollbackAdminVocabularyImportBatch: vi.fn(),
}));

vi.mock('@/lib/api', () => ({
  previewAdminVocabularyImport: api.previewAdminVocabularyImport,
  bulkImportAdminVocabulary: api.bulkImportAdminVocabulary,
  fetchAdminVocabularyImportBatch: api.fetchAdminVocabularyImportBatch,
  exportAdminVocabularyImportBatchCsv: api.exportAdminVocabularyImportBatchCsv,
  reconcileAdminVocabularyImportBatch: api.reconcileAdminVocabularyImportBatch,
  rollbackAdminVocabularyImportBatch: api.rollbackAdminVocabularyImportBatch,
}));

vi.mock('@/components/domain/admin-route-surface', () => ({
  AdminRouteWorkspace: ({ children }: { children: ReactNode }) => <main>{children}</main>,
  AdminRoutePanel: ({ children }: { children: ReactNode }) => <section>{children}</section>,
  AdminRouteSectionHeader: ({ title, description, actions }: { title: string; description?: string; actions?: ReactNode }) => (
    <header>
      <h1>{title}</h1>
      {description ? <p>{description}</p> : null}
      {actions}
    </header>
  ),
  AdminRouteSummaryCard: ({ label, value }: { label: string; value: string | number }) => (
    <article>
      <span>{label}</span>
      <strong>{value}</strong>
    </article>
  ),
}));

import AdminVocabularyImportPage from './page';

function makeManifestFile() {
  return new File(
    ['Term,Definition,Category,Difficulty,ProfessionId,ExamTypeCode,SourceProvenance\ndyspnoea,Shortness of breath,symptoms,medium,medicine,oet,src=unit;p=1;row=1\n'],
    'approved-manifest.csv',
    { type: 'text/csv' },
  );
}

async function prepareReconciliation(batchId: string, manifest: File) {
  const user = userEvent.setup();
  render(<AdminVocabularyImportPage />);

  const batchInput = screen.getByLabelText(/import batch id/i);
  await user.clear(batchInput);
  await user.type(batchInput, batchId);
  await user.upload(screen.getByLabelText(/approved manifest csv/i), manifest);
  await user.click(screen.getByRole('button', { name: /reconcile manifest/i }));

  await waitFor(() => {
    expect(api.reconcileAdminVocabularyImportBatch).toHaveBeenCalledWith(batchId, manifest);
  });
}

describe('Admin vocabulary import page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('reconciles an approved manifest and renders a clean result', async () => {
    api.reconcileAdminVocabularyImportBatch.mockResolvedValue({
      importBatchId: 'recalls-batch-001',
      manifestRows: 1,
      storedRows: 1,
      matchedRows: 1,
      missingRows: 0,
      extraRows: 0,
      mismatchedRows: 0,
      invalidManifestRows: 0,
      clean: true,
      rows: [
        { lineNumber: 2, key: 'dyspnoea|oet|medicine', status: 'matched', mismatches: [], error: null },
      ],
    });

    await prepareReconciliation('recalls-batch-001', makeManifestFile());

    expect((await screen.findAllByText(/reconciliation clean for batch recalls-batch-001/i)).length).toBeGreaterThan(0);
    expect(screen.getByText('Clean')).toBeInTheDocument();
    expect(screen.getByText('dyspnoea|oet|medicine')).toBeInTheDocument();
    expect(screen.getByText('matched')).toBeInTheDocument();
  });

  it('surfaces manifest mismatches for operator review', async () => {
    api.reconcileAdminVocabularyImportBatch.mockResolvedValue({
      importBatchId: 'recalls-batch-002',
      manifestRows: 1,
      storedRows: 1,
      matchedRows: 0,
      missingRows: 0,
      extraRows: 0,
      mismatchedRows: 1,
      invalidManifestRows: 0,
      clean: false,
      rows: [
        {
          lineNumber: 2,
          key: 'dyspnoea|oet|medicine',
          status: 'mismatched',
          mismatches: [{ field: 'definition', expected: 'Shortness of breath', actual: 'Laboured breathing' }],
          error: null,
        },
      ],
    });

    await prepareReconciliation('recalls-batch-002', makeManifestFile());

    expect(await screen.findByText(/reconciliation needs review for batch recalls-batch-002/i)).toBeInTheDocument();
    expect(screen.getAllByText(/needs review/i).length).toBeGreaterThan(0);
    expect(screen.getByText('mismatched')).toBeInTheDocument();
    expect(screen.getByText(/definition: expected Shortness of breath \/ stored Laboured breathing/i)).toBeInTheDocument();
  });

  it('shows issue rows even when they appear after the first 50 manifest rows', async () => {
    api.reconcileAdminVocabularyImportBatch.mockResolvedValue({
      importBatchId: 'recalls-batch-003',
      manifestRows: 51,
      storedRows: 51,
      matchedRows: 50,
      missingRows: 0,
      extraRows: 0,
      mismatchedRows: 1,
      invalidManifestRows: 0,
      clean: false,
      rows: [
        ...Array.from({ length: 50 }, (_, index) => ({
          lineNumber: index + 2,
          key: `matched-${index}|oet|medicine`,
          status: 'matched',
          mismatches: [],
          error: null,
        })),
        {
          lineNumber: 52,
          key: 'late-mismatch|oet|medicine',
          status: 'mismatched',
          mismatches: [{ field: 'definition', expected: 'Approved definition', actual: 'Stored definition' }],
          error: null,
        },
      ],
    });

    await prepareReconciliation('recalls-batch-003', makeManifestFile());

    expect(await screen.findByText('late-mismatch|oet|medicine')).toBeInTheDocument();
    expect(screen.getByText(/definition: expected Approved definition \/ stored Stored definition/i)).toBeInTheDocument();
    expect(screen.getByText(/showing all 1 issue row; 50 matched rows hidden/i)).toBeInTheDocument();
    expect(screen.queryByText('matched-0|oet|medicine')).not.toBeInTheDocument();
  });

  it('reconciles against the visible batch ID after a loaded batch ID is changed', async () => {
    const user = userEvent.setup();
    const manifest = makeManifestFile();

    api.fetchAdminVocabularyImportBatch.mockResolvedValue({
      importBatchId: 'recalls-batch-old',
      total: 1,
      draft: 1,
      active: 0,
      archived: 0,
      warnings: [],
    });
    api.reconcileAdminVocabularyImportBatch.mockResolvedValue({
      importBatchId: 'recalls-batch-new',
      manifestRows: 1,
      storedRows: 1,
      matchedRows: 1,
      missingRows: 0,
      extraRows: 0,
      mismatchedRows: 0,
      invalidManifestRows: 0,
      clean: true,
      rows: [],
    });

    render(<AdminVocabularyImportPage />);

    const batchInput = screen.getByLabelText(/import batch id/i);
    await user.clear(batchInput);
    await user.type(batchInput, 'recalls-batch-old');
    await user.click(screen.getByRole('button', { name: /batch summary/i }));

    await waitFor(() => {
      expect(api.fetchAdminVocabularyImportBatch).toHaveBeenCalledWith('recalls-batch-old');
    });

    await user.clear(batchInput);
    await user.type(batchInput, 'recalls-batch-new');
    await user.upload(screen.getByLabelText(/approved manifest csv/i), manifest);
    await user.click(screen.getByRole('button', { name: /reconcile manifest/i }));

    await waitFor(() => {
      expect(api.reconcileAdminVocabularyImportBatch).toHaveBeenCalledWith('recalls-batch-new', manifest);
    });
  });
});
