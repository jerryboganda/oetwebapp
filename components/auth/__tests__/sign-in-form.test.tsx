import { screen } from '@testing-library/react';
const { mockSignIn } = vi.hoisted(() => ({
  mockSignIn: vi.fn(),
}));

const { mockUseSignupCatalog } = vi.hoisted(() => ({
  mockUseSignupCatalog: vi.fn(() => ({ externalAuthProviders: [] })),
}));

vi.mock('@/contexts/auth-context', () => ({
  useAuth: () => ({
    signIn: mockSignIn,
  }),
}));

vi.mock('@/lib/auth-client', () => ({
  buildExternalAuthStartHref: vi.fn((_provider: string) => '#'),
}));

vi.mock('@/lib/hooks/use-signup-catalog', () => ({
  useSignupCatalog: () => mockUseSignupCatalog(),
}));

import { SignInForm } from '../sign-in-form';
import { renderWithRouter } from '@/tests/test-utils';

describe('SignInForm', () => {
  beforeEach(() => {
    mockUseSignupCatalog.mockReturnValue({ externalAuthProviders: [] });
  });

  it('uses explicit auth copy and ergonomic field attributes', () => {
    renderWithRouter(<SignInForm nextHref="/study-plan" />);

    expect(screen.getByRole('button', { name: 'Sign In' })).toBeInTheDocument();

    const emailInput = screen.getByLabelText(/email address/i);
    expect(emailInput).toHaveAttribute('name', 'email');
    expect(emailInput).toHaveAttribute('autocomplete', 'email');
    expect(emailInput).toHaveAttribute('inputmode', 'email');
    expect(emailInput).toHaveAttribute('spellcheck', 'false');

    const passwordInput = screen.getByLabelText(/^password$/i);
    expect(passwordInput).toHaveAttribute('name', 'password');
    expect(passwordInput).toHaveAttribute('autocomplete', 'current-password');
  });

  it('renders when the signup catalog omits external auth providers', () => {
    mockUseSignupCatalog.mockReturnValue({ externalAuthProviders: undefined } as never);

    renderWithRouter(<SignInForm nextHref="/study-plan" />);

    expect(screen.getByRole('button', { name: 'Sign In' })).toBeInTheDocument();
  });
});
