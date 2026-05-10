const { mockRedirect } = vi.hoisted(() => ({
  mockRedirect: vi.fn(),
}));

vi.mock('next/navigation', () => ({
  redirect: mockRedirect,
}));

import DiagnosticReadingPage from './page';

describe('Diagnostic reading page', () => {
  beforeEach(() => {
    vi.clearAllMocks();
  });

  it('redirects to the structured Reading hub', () => {
    DiagnosticReadingPage();
    expect(mockRedirect).toHaveBeenCalledWith('/reading');
  });
});