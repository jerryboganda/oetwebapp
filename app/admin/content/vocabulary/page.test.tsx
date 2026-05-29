import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';

const api = vi.hoisted(() => ({
  fetchAdminVocabularyItems: vi.fn(),
  deleteAdminVocabularyItem: vi.fn(),
  deleteAdminVocabularyItems: vi.fn(),
  fetchAdminVocabularyCategories: vi.fn(),
  fetchAdminVocabularyRecallSets: vi.fn(),
  push: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: api.push, replace: vi.fn(), prefetch: vi.fn() }),
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
}));

vi.mock('@/lib/api', () => ({
  fetchAdminVocabularyItems: api.fetchAdminVocabularyItems,
  deleteAdminVocabularyItem: api.deleteAdminVocabularyItem,
  deleteAdminVocabularyItems: api.deleteAdminVocabularyItems,
  fetchAdminVocabularyCategories: api.fetchAdminVocabularyCategories,
  fetchAdminVocabularyRecallSets: api.fetchAdminVocabularyRecallSets,
}));

import AdminVocabularyPage from './page';

const rows = [
  {
    id: 'VOC-1',
    term: 'Cusco speculum',
    definition: 'A bivalve vaginal speculum used for cervical visualisation.',
    professionId: 'nursing',
    category: 'equipment',
    exampleSentence: null,
    status: 'draft',
  },
  {
    id: 'VOC-2',
    term: 'Mannequin',
    definition: 'Training model.',
    professionId: null,
    category: 'recall_term',
    exampleSentence: null,
    status: 'draft',
  },
];

describe('AdminVocabularyPage bulk selection', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.fetchAdminVocabularyCategories.mockResolvedValue({ categories: [] });
    api.fetchAdminVocabularyRecallSets.mockResolvedValue({ sets: [] });
    api.fetchAdminVocabularyItems.mockResolvedValue({ total: rows.length, page: 1, pageSize: 25, items: rows });
    api.deleteAdminVocabularyItems.mockResolvedValue({ totalRequested: 1, deleted: 1, archived: 0, failed: 0, errors: [] });
  });

  it('selects vocabulary rows and bulk deletes selected terms', async () => {
    const user = userEvent.setup();

    render(<AdminVocabularyPage />);

    expect((await screen.findAllByText('Cusco speculum')).length).toBeGreaterThan(0);
    const table = screen.getByRole('table');

    await user.click(within(table).getByLabelText('Select row VOC-1'));

    expect(screen.getByTestId('bulk-action-bar')).toBeInTheDocument();
    expect(screen.getByText('1 item selected')).toBeInTheDocument();

    const bulkBar = screen.getByTestId('bulk-action-bar');
    await user.click(within(bulkBar).getByRole('button', { name: /Delete/i }));
    const dialog = screen.getByRole('dialog', { name: 'Delete selected vocabulary terms' });
    await user.click(within(dialog).getByRole('button', { name: /Delete selected/i }));

    await waitFor(() => {
      expect(api.deleteAdminVocabularyItems).toHaveBeenCalledWith(['VOC-1']);
    });
    expect(await screen.findByText(/Bulk delete complete: 1 deleted/i)).toBeInTheDocument();
    await waitFor(() => {
      expect(within(table).queryByText('Cusco speculum')).not.toBeInTheDocument();
    });
  });
});
