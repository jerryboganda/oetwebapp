import { render } from '@testing-library/react';

const { mockRedirect } = vi.hoisted(() => ({ mockRedirect: vi.fn() }));

vi.mock('next/navigation', () => ({
  redirect: mockRedirect,
}));

import AdminWalletTiersRedirectPage from './page';

describe('AdminWalletTiersRedirectPage', () => {
  it('redirects bookmarks to the unified pricing hub wallet tab', () => {
    render(<AdminWalletTiersRedirectPage />);
    expect(mockRedirect).toHaveBeenCalledWith('/admin/billing/pricing?tab=wallet');
  });
});
