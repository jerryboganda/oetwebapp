import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import AdminWritingHubPage from './page';

describe('AdminWritingHubPage', () => {
  it('groups writing work into authoring, quality, and AI sections', () => {
    renderWithRouter(<AdminWritingHubPage />);

    expect(screen.getByRole('heading', { name: 'Writing' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Authoring workspace' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Create task Open workspace/i })).toHaveAttribute('href', '/admin/writing/tasks/new');
    expect(screen.getByRole('link', { name: /Task library Open workspace/i })).toHaveAttribute('href', '/admin/writing/tasks');

    expect(screen.getByRole('heading', { name: 'Quality & release' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Analytics Open workspace/i })).toHaveAttribute('href', '/admin/writing/analytics');
    expect(screen.getByRole('link', { name: /Result visibility Open workspace/i })).toHaveAttribute('href', '/admin/writing/result-visibility');

    expect(screen.getByRole('heading', { name: 'AI assistance' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /AI options Open workspace/i })).toHaveAttribute('href', '/admin/writing/options');
    expect(screen.getByRole('link', { name: /AI draft Open workspace/i })).toHaveAttribute('href', '/admin/writing/ai-draft');
  });
});