import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import type { ReactNode } from 'react';

const api = vi.hoisted(() => ({
  fetchAdminVocabularyRecallSets: vi.fn(),
  push: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  useRouter: () => ({ push: api.push, replace: vi.fn(), prefetch: vi.fn() }),
}));

vi.mock('next/link', () => ({
  default: ({ children, href }: { children: ReactNode; href?: string }) => <a href={href}>{children}</a>,
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
  fetchAdminVocabularyRecallSets: api.fetchAdminVocabularyRecallSets,
}));

import { VocabularyForm, type VocabFormValues } from './_form';

describe('VocabularyForm free preview', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    api.fetchAdminVocabularyRecallSets.mockResolvedValue({ sets: [] });
  });

  it('submits the free-preview checkbox value', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async (_values: VocabFormValues) => {});

    render(<VocabularyForm mode="create" onSubmit={onSubmit} />);

    await user.type(screen.getByLabelText('Term'), 'Dyspnoea');
    await user.type(screen.getByLabelText('Definition'), 'Difficulty breathing.');
    await user.click(screen.getByRole('checkbox', { name: /Free preview word/i }));
    await user.click(screen.getByRole('button', { name: /Create draft/i }));

    await waitFor(() => {
      expect(onSubmit).toHaveBeenCalledWith(expect.objectContaining({ isFreePreview: true }));
    });
  });

  it('publishes with the current free-preview checkbox value', async () => {
    const user = userEvent.setup();
    const onSubmit = vi.fn(async (_values: VocabFormValues) => {});
    const onPublish = vi.fn(async (_values: VocabFormValues) => {});

    render(
      <VocabularyForm
        mode="edit"
        initial={{ term: 'Dyspnoea', definition: 'Difficulty breathing.', status: 'draft' }}
        onSubmit={onSubmit}
        onPublish={onPublish}
        itemId="vt-001"
      />
    );

    await user.click(screen.getByRole('checkbox', { name: /Free preview word/i }));
    await user.click(screen.getByRole('button', { name: /Publish/i }));

    await waitFor(() => {
      expect(onPublish).toHaveBeenCalledWith(expect.objectContaining({ isFreePreview: true, status: 'active' }));
    });
  });
});
