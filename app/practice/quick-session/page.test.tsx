import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';

vi.mock('@/components/layout', () => ({
  LearnerDashboardShell: ({ children }: { children: React.ReactNode }) => (
    <div data-testid="learner-dashboard-shell">{children}</div>
  ),
}));

vi.mock('@/lib/analytics', () => ({ analytics: { track: vi.fn() } }));
vi.mock('@/lib/auth-client', () => ({ ensureFreshAccessToken: vi.fn().mockResolvedValue('test-token') }));
vi.mock('@/lib/env', () => ({ env: { apiBaseUrl: 'http://localhost:5000' } }));

import MobileQuickSessionPage from './page';

describe('Quick session page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    global.fetch = vi.fn().mockResolvedValue(new Response(JSON.stringify({ code: 'not_found', message: 'No live quick session' }), {
      status: 404,
      headers: { 'content-type': 'application/json' },
    }));
  });

  it('renders through the shared learner dashboard shell', () => {
    render(<MobileQuickSessionPage />);
    expect(screen.getByTestId('learner-dashboard-shell')).toBeInTheDocument();
  });

  it('shows an unavailable state instead of generating fallback questions when the API fails', async () => {
    const user = userEvent.setup();
    render(<MobileQuickSessionPage />);

    await user.click(screen.getByText('Medical Vocabulary'));

    expect(await screen.findByText('Quick practice is temporarily unavailable because no live session could be loaded.')).toBeInTheDocument();
    expect(screen.queryByText(/The patient presented with acute/i)).not.toBeInTheDocument();
  });
});
