import { render, screen } from '@testing-library/react';
import { vi } from 'vitest';

import { AiAssistantWidget } from './AiAssistantWidget';
import { AdminPermission } from '@/lib/admin-permissions';
import type { CurrentUser } from '@/lib/types/auth';

const mocks = vi.hoisted(() => ({
  user: null as CurrentUser | null,
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({ user: mocks.user }),
}));

function user(overrides: Partial<CurrentUser>): CurrentUser {
  return {
    userId: 'user-1',
    email: 'admin@example.test',
    role: 'admin',
    displayName: 'Admin User',
    isEmailVerified: true,
    isAuthenticatorEnabled: false,
    requiresEmailVerification: false,
    requiresMfa: false,
    emailVerifiedAt: '2026-05-20T00:00:00.000Z',
    authenticatorEnabledAt: null,
    adminPermissions: [AdminPermission.UseAiAssistant],
    ...overrides,
  };
}

describe('AiAssistantWidget', () => {
  afterEach(() => {
    mocks.user = null;
  });

  it('renders the launcher for an admin with AI Assistant access', () => {
    mocks.user = user({});

    render(<AiAssistantWidget />);

    expect(screen.getByRole('button', { name: 'Open AI Assistant' })).toBeInTheDocument();
  });

  it('renders nothing for non-admin users', () => {
    mocks.user = user({ role: 'learner' });

    render(<AiAssistantWidget />);

    expect(screen.queryByRole('button', { name: 'Open AI Assistant' })).not.toBeInTheDocument();
  });

  it('renders nothing for admins without the assistant permission', () => {
    mocks.user = user({ adminPermissions: [] });

    render(<AiAssistantWidget />);

    expect(screen.queryByRole('button', { name: 'Open AI Assistant' })).not.toBeInTheDocument();
  });
});