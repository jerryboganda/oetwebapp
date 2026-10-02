import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

const { getVocabLists, subscribeToVocabList } = vi.hoisted(() => ({
  getVocabLists: vi.fn(),
  subscribeToVocabList: vi.fn(),
}));

vi.mock('@/lib/reading-pathway-api', () => ({ getVocabLists, subscribeToVocabList }));
vi.mock('@/contexts/auth-context', () => ({ useAuth: () => ({ isAuthenticated: true, loading: false }) }));
vi.mock('sonner', () => ({ toast: { success: vi.fn(), error: vi.fn() } }));

import VocabListsPage from './page';

describe('Vocabulary lists page', () => {
  beforeEach(() => {
    getVocabLists.mockReset();
  });

  it('shows the lists the API returns, including ones beyond the old curated four', async () => {
    getVocabLists.mockResolvedValue([
      { id: 'l1', slug: 'dentistry', name: 'Dentistry', description: 'Dental terms.', wordCount: 120, isSubscribed: false, previewWords: [] },
    ]);

    render(<VocabListsPage />);

    expect(await screen.findByRole('heading', { name: 'Dentistry' })).toBeInTheDocument();
    expect(screen.getByText('120 words')).toBeInTheDocument();
    expect(screen.queryByText('Top 200 OET Medical Terms')).not.toBeInTheDocument();
  });

  it('shows a retryable error instead of invented zero-word lists when the API fails', async () => {
    getVocabLists.mockRejectedValueOnce(new Error('Service unavailable')).mockResolvedValueOnce([]);

    render(<VocabListsPage />);

    expect(await screen.findByText('Service unavailable')).toBeInTheDocument();
    expect(screen.queryByRole('button', { name: 'Subscribe' })).not.toBeInTheDocument();
    await userEvent.click(screen.getByRole('button', { name: /retry/i }));
    expect(await screen.findByText('No vocabulary lists yet')).toBeInTheDocument();
  });
});
