import { screen } from '@testing-library/react';
import { renderWithRouter } from '@/tests/test-utils';
import AdminSpeakingPage from './page';

describe('AdminSpeakingPage', () => {
  it('groups speaking work into operations, authoring, and assets sections', () => {
    renderWithRouter(<AdminSpeakingPage />);

    expect(screen.getByRole('heading', { name: 'Speaking' })).toBeInTheDocument();
    expect(screen.getByRole('heading', { name: 'Operations & quality' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Result visibility Open workspace/i })).toHaveAttribute('href', '/admin/speaking/result-visibility');
    expect(screen.getByRole('link', { name: /Speaking analytics Open workspace/i })).toHaveAttribute('href', '/admin/analytics/speaking');
    expect(screen.getByRole('link', { name: /Recording audit Open workspace/i })).toHaveAttribute('href', '/admin/speaking/recordings/audit');

    expect(screen.getByRole('heading', { name: 'Content authoring' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Speaking authoring Open workspace/i })).toHaveAttribute('href', '/admin/content/papers?subtest=speaking');
    expect(screen.getByRole('link', { name: /Mock sets Open workspace/i })).toHaveAttribute('href', '/admin/content/speaking/mock-sets');

    expect(screen.getByRole('heading', { name: 'Shared assets' })).toBeInTheDocument();
    expect(screen.getByRole('link', { name: /Shared resources Open workspace/i })).toHaveAttribute('href', '/admin/content/speaking/shared-resources');
  });
});