import { createContext } from 'react';
import { screen } from '@testing-library/react';
import { Library, Sparkles } from 'lucide-react';
import { renderWithRouter } from '@/tests/test-utils';

const { mockSignOut, mockUseAuth } = vi.hoisted(() => ({
  mockSignOut: vi.fn(),
  mockUseAuth: vi.fn(),
}));

vi.mock('@/contexts/auth-context', () => ({
  AuthContext: createContext({ signOut: mockSignOut }),
  useAuth: () => mockUseAuth(),
}));

vi.mock('@/lib/mobile/haptics', () => ({
  triggerImpactHaptic: vi.fn(),
}));

import { Sidebar, type NavGroup } from '../sidebar';

const adminUser = {
  userId: 'admin-1',
  displayName: 'Admin User',
  email: 'admin@example.com',
  role: 'admin',
  isEmailVerified: true,
};

const contentGroups: NavGroup[] = [
  {
    label: 'Content authoring',
    items: [
      { href: '/admin/content', label: 'Content Library', icon: <Library className="h-4 w-4" />, matchPrefix: '/admin/content' },
      { href: '/admin/content/generation', label: 'Content Generation', icon: <Sparkles className="h-4 w-4" />, matchPrefix: '/admin/content/generation' },
    ],
  },
];

describe('Sidebar route matching', () => {
  beforeEach(() => {
    vi.clearAllMocks();
    mockUseAuth.mockReturnValue({ user: adminUser, signOut: mockSignOut });
  });

  it('prefers the most specific canonical content route over the generic content library route', () => {
    renderWithRouter(<Sidebar workspaceRole="admin" groups={contentGroups} />, { pathname: '/admin/content/generation' });

    expect(screen.getByRole('link', { name: /content generation/i })).toHaveAttribute('aria-current', 'page');
    expect(screen.getByRole('link', { name: /content library/i })).not.toHaveAttribute('aria-current');
  });
});
